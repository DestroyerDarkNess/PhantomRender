using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using PhantomRender.Core.Hooks.Graphics;
using PhantomRender.Core.Hooks.Graphics.OpenGL;
using PhantomRender.ImGui.Renderers;

namespace PhantomRender.ImGui
{
    public static class OverlayManager
    {
        private enum BackendKind
        {
            None = 0,
            DXGI = 1,
            DX9 = 2,
            OpenGL = 3,
        }

        // Hooks can be called from different threads (DXGI Present, D3D9 Present, wglSwapBuffers).
        // ImGui and its backends are not thread-safe, so serialize all hook callbacks that touch ImGui/device state.
        private static readonly object _renderLock = new object();
        private static readonly object _initLock = new object();

        // Only one backend should be active at a time in sensitive engines (Frostbite, etc).
        // 0=None, else BackendKind enum.
        private static int _activeBackend = (int)BackendKind.None;
        private static BackendKind _probingBackend = BackendKind.None;
        private static Thread _probeThread;
        private static volatile bool _probeStopRequested;
        private static volatile bool _probeStarted;

        // Default probe timeouts (ms). A custom timeout can be provided through OverlayMenuOptions.
        private const int PROBE_DXGI_TIMEOUT_MS = 12_000;
        private const int PROBE_DX9_TIMEOUT_MS = 10_000;
        private const int PROBE_OPENGL_TIMEOUT_MS = 10_000;

        private static OverlayMenu _overlayMenu = OverlayMenu.Default;
        private static OverlayMenuOptions _options = _overlayMenu.Options;
        private static OverlayHookKind _preferredHook = OverlayHookKind.Auto;

        private static DirectX9Hook _dx9Hook;
        private static DirectX9Renderer _dx9Renderer;
        private static IntPtr _dx9TargetWindow;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DX9BeginSceneDelegate(IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DX9EndSceneDelegate(IntPtr device);

        // IDirect3DDevice9 vtable indices.
        private const int DX9_VTABLE_BeginScene = 41;
        private const int DX9_VTABLE_EndScene = 42;

        private static DX9BeginSceneDelegate _dx9BeginScene;
        private static DX9EndSceneDelegate _dx9EndScene;
        private static IntPtr _dx9BeginScenePtr;
        private static IntPtr _dx9EndScenePtr;

        private static OpenGLHook _glHook;
        private static OpenGLRenderer _glRenderer;
        private static bool _glInitFailed;
        private static IntPtr _glTargetWindow;

        private static DirectX10Hook _dx10Hook;
        private static DirectX10Renderer _dx10Renderer;
        private static IntPtr _dxgiTargetWindow;
        private static long _dxgiPresentCounter;

        public static void Initialize()
        {
            Initialize(OverlayMenu.Default);
        }

        public static void Initialize(OverlayMenu overlayMenu)
        {
            if (overlayMenu != null)
            {
                _overlayMenu = overlayMenu;
                _options = overlayMenu.Options ?? new OverlayMenuOptions();
                _preferredHook = _options.PreferredHook;
            }

            lock (_initLock)
            {
                if (_probeStarted) return;
                _probeStarted = true;
            }

            Console.WriteLine($"[PhantomRender] OverlayManager starting with hook: {_preferredHook}");
            Console.Out.Flush();

            // Probe backends and enable ONLY the one that actually triggers for this process.
            // This avoids multi-backend ImGui usage and reduces the hook surface (important for some games/anti-cheat).
            _probeThread = new Thread(ProbeThreadMain)
            {
                IsBackground = true,
                Name = "PhantomRender.BackendProbe"
            };
            _probeThread.Start();
        }

        private static void ProbeThreadMain()
        {
            try
            {
                while (!_probeStopRequested && GetActiveBackend() == BackendKind.None)
                {
                    var order = GetProbeOrder();
                    bool startedAny = false;

                    for (int i = 0; i < order.Length; i++)
                    {
                        if (_probeStopRequested || GetActiveBackend() != BackendKind.None)
                            break;

                        BackendKind kind = order[i];
                        if (!IsBackendCandidate(kind))
                            continue;

                        startedAny = true;
                        if (!TryStartProbe(kind))
                            continue;

                        int timeoutMs = GetProbeTimeoutMs(kind);
                        long startTicks = Stopwatch.GetTimestamp();

                        while (!_probeStopRequested && GetActiveBackend() == BackendKind.None && !HasElapsed(startTicks, timeoutMs))
                        {
                            Thread.Sleep(200);
                        }

                        if (GetActiveBackend() != BackendKind.None)
                            break;

                        StopProbe(kind);
                    }

                    if (!startedAny)
                    {
                        // No known graphics runtime loaded yet. Wait a bit and try again.
                        Thread.Sleep(250);
                    }
                    else
                    {
                        // Avoid rapid enable/disable loops in processes that never present.
                        Thread.Sleep(500);
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    Console.WriteLine($"[PhantomRender] Backend probe thread error: {ex}");
                    Console.Out.Flush();
                }
                catch { }
            }
        }

        private static BackendKind GetActiveBackend()
        {
            return (BackendKind)Volatile.Read(ref _activeBackend);
        }

        private static BackendKind[] GetProbeOrder()
        {
            switch (_preferredHook)
            {
                case OverlayHookKind.DxgiPresent:
                    return new[] { BackendKind.DXGI };
                case OverlayHookKind.Dx9Present:
                case OverlayHookKind.Dx9EndScene:
                    return new[] { BackendKind.DX9 };
                case OverlayHookKind.OpenGLSwapBuffers:
                    return new[] { BackendKind.OpenGL };
            }

            // Heuristic:
            // - If D3D9 is loaded and D3D11 isn't, prefer D3D9 first (old games).
            // - Otherwise, prefer DXGI first (covers DX10/DX11/DX12).
            bool d3d9Loaded = GetModuleHandleW("d3d9.dll") != IntPtr.Zero;
            bool d3d11Loaded = GetModuleHandleW("d3d11.dll") != IntPtr.Zero;

            if (d3d9Loaded && !d3d11Loaded)
            {
                return new[] { BackendKind.DX9, BackendKind.DXGI, BackendKind.OpenGL };
            }

            return new[] { BackendKind.DXGI, BackendKind.DX9, BackendKind.OpenGL };
        }

        private static bool IsBackendCandidate(BackendKind kind)
        {
            switch (kind)
            {
                case BackendKind.DXGI:
                    return GetModuleHandleW("dxgi.dll") != IntPtr.Zero;
                case BackendKind.DX9:
                    return GetModuleHandleW("d3d9.dll") != IntPtr.Zero;
                case BackendKind.OpenGL:
                    return GetModuleHandleW("opengl32.dll") != IntPtr.Zero;
                default:
                    return false;
            }
        }

        private static int GetProbeTimeoutMs(BackendKind kind)
        {
            int customTimeout = _options != null ? _options.ProbeTimeoutMs : 0;
            if (customTimeout > 0)
            {
                return customTimeout;
            }

            switch (kind)
            {
                case BackendKind.DXGI: return PROBE_DXGI_TIMEOUT_MS;
                case BackendKind.DX9: return PROBE_DX9_TIMEOUT_MS;
                case BackendKind.OpenGL: return PROBE_OPENGL_TIMEOUT_MS;
                default: return 5_000;
            }
        }

        private static bool HasElapsed(long startTicks, int timeoutMs)
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
            double elapsedMs = (double)elapsedTicks * 1000.0 / Stopwatch.Frequency;
            return elapsedMs >= timeoutMs;
        }

        private static bool ShouldRunBackendCallback(BackendKind kind)
        {
            BackendKind active = GetActiveBackend();
            if (active == BackendKind.None)
            {
                return _probingBackend == kind;
            }

            return active == kind;
        }

        private static bool EnsureActiveBackendSelected(BackendKind kind)
        {
            int current = Volatile.Read(ref _activeBackend);
            if (current == (int)kind)
                return true;

            if (current != (int)BackendKind.None)
                return false;

            if (Interlocked.CompareExchange(ref _activeBackend, (int)kind, (int)BackendKind.None) != (int)BackendKind.None)
                return false;

            _probeStopRequested = true;
            DisableHooksExcept(kind);

            Console.WriteLine($"[PhantomRender] Active backend selected: {kind}");
            Console.Out.Flush();
            return true;
        }

        private static void DisableHooksExcept(BackendKind keep)
        {
            lock (_renderLock)
            {
                if (keep != BackendKind.DXGI)
                {
                    StopProbe(BackendKind.DXGI);
                }

                if (keep != BackendKind.DX9)
                {
                    StopProbe(BackendKind.DX9);
                }

                if (keep != BackendKind.OpenGL)
                {
                    StopProbe(BackendKind.OpenGL);
                }
            }
        }

        private static bool TryStartProbe(BackendKind kind)
        {
            lock (_renderLock)
            {
                if (GetActiveBackend() != BackendKind.None)
                    return false;

                // Only one probing hook at a time.
                if (_probingBackend != BackendKind.None && _probingBackend != kind)
                {
                    StopProbe(_probingBackend);
                }

                _probingBackend = kind;

                try
                {
                    switch (kind)
                    {
                        case BackendKind.DXGI:
                            return TryInitializeDXGI();
                        case BackendKind.DX9:
                            return TryInitializeDX9();
                        case BackendKind.OpenGL:
                            return TryInitializeOpenGL();
                        default:
                            return false;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PhantomRender] Probe start failed for {kind}: {ex}");
                    Console.Out.Flush();
                    return false;
                }
            }
        }

        private static void StopProbe(BackendKind kind)
        {
            lock (_renderLock)
            {
                try
                {
                    switch (kind)
                    {
                        case BackendKind.DXGI:
                            if (_dx10Hook != null)
                            {
                                try { _dx10Hook.Disable(); } catch { }
                                try { _dx10Hook.Dispose(); } catch { }
                                _dx10Hook = null;
                            }

                            _dxgiTargetWindow = IntPtr.Zero;
                            try { _dx11Renderer?.Dispose(); } catch { }
                            _dx11Renderer = null;
#if NET5_0_OR_GREATER
                            try { _dx12Renderer?.Dispose(); } catch { }
                            _dx12Renderer = null;
#endif
                            try { _dx10Renderer?.Dispose(); } catch { }
                            _dx10Renderer = null;
                            break;

                        case BackendKind.DX9:
                            if (_dx9Hook != null)
                            {
                                try { _dx9Hook.Disable(); } catch { }
                                try { _dx9Hook.Dispose(); } catch { }
                                _dx9Hook = null;
                            }

                            _dx9TargetWindow = IntPtr.Zero;
                            _dx9BeginScene = null;
                            _dx9EndScene = null;
                            _dx9BeginScenePtr = IntPtr.Zero;
                            _dx9EndScenePtr = IntPtr.Zero;
                            try { _dx9Renderer?.Dispose(); } catch { }
                            _dx9Renderer = null;
                            break;

                        case BackendKind.OpenGL:
                            if (_glHook != null)
                            {
                                try { _glHook.Disable(); } catch { }
                                try { _glHook.Dispose(); } catch { }
                                _glHook = null;
                            }

                            _glTargetWindow = IntPtr.Zero;
                            _glInitFailed = false;
                            try { _glRenderer?.Dispose(); } catch { }
                            _glRenderer = null;
                            break;
                    }
                }
                catch
                {
                    // Best-effort cleanup only.
                }
                finally
                {
                    if (_probingBackend == kind)
                    {
                        _probingBackend = BackendKind.None;
                    }
                }
            }
        }

        private static void EnsureDX9SceneDelegates(IntPtr device)
        {
            if (device == IntPtr.Zero) return;

            try
            {
                IntPtr vTable = Marshal.ReadIntPtr(device);

                IntPtr beginScenePtr = Marshal.ReadIntPtr(vTable + DX9_VTABLE_BeginScene * IntPtr.Size);
                IntPtr endScenePtr = Marshal.ReadIntPtr(vTable + DX9_VTABLE_EndScene * IntPtr.Size);

                if (_dx9BeginScene == null || beginScenePtr != _dx9BeginScenePtr)
                {
                    _dx9BeginScenePtr = beginScenePtr;
                    _dx9BeginScene = Marshal.GetDelegateForFunctionPointer<DX9BeginSceneDelegate>(beginScenePtr);
                }

                if (_dx9EndScene == null || endScenePtr != _dx9EndScenePtr)
                {
                    _dx9EndScenePtr = endScenePtr;
                    _dx9EndScene = Marshal.GetDelegateForFunctionPointer<DX9EndSceneDelegate>(endScenePtr);
                }
            }
            catch
            {
                // If we fail to resolve scene methods, we'll just render without them.
                _dx9BeginScene = null;
                _dx9EndScene = null;
            }
        }

        private static bool UseDx9EndSceneHook()
        {
            return _preferredHook == OverlayHookKind.Dx9EndScene;
        }

        private static void RenderDx9Overlay(IntPtr device, IntPtr hWnd, bool wrapBeginEndScene)
        {
            if (hWnd == IntPtr.Zero || _dx9Renderer == null)
            {
                return;
            }

            // Some processes have multiple D3D9 devices/windows; draw only on the first one we lock onto.
            if (_dx9TargetWindow != IntPtr.Zero && hWnd != _dx9TargetWindow)
            {
                return;
            }

            if (!_dx9Renderer.IsInitialized)
            {
                Console.WriteLine($"[PhantomRender] DX9 {(wrapBeginEndScene ? "OnPresent" : "OnEndScene")} - Initializing Renderer... (Lock Acquired)");
                Console.WriteLine($"[PhantomRender] Target Window: {hWnd}");
                _dx9TargetWindow = hWnd;
                _dx9Renderer.Initialize(device, hWnd);
                if (_dx9Renderer.IsInitialized)
                {
                    EnsureActiveBackendSelected(BackendKind.DX9);
                }
            }

            if (!_dx9Renderer.IsInitialized)
            {
                return;
            }

            bool beganScene = false;
            try
            {
                if (wrapBeginEndScene)
                {
                    EnsureDX9SceneDelegates(device);
                    if (_dx9BeginScene != null && _dx9EndScene != null)
                    {
                        int hrBegin = _dx9BeginScene(device);
                        beganScene = hrBegin >= 0;
                    }
                }

                _dx9Renderer.NewFrame();
                _dx9Renderer.Render();
            }
            finally
            {
                if (beganScene)
                {
                    try { _dx9EndScene(device); } catch { }
                }
            }
        }

        private static bool TryInitializeDX9()
        {
            // Don't create a dummy D3D9 device if the game hasn't loaded D3D9.
            // This prevents us from loading d3d9.dll into DX10/11/12 games.
            if (GetModuleHandleW("d3d9.dll") == IntPtr.Zero)
            {
                return false;
            }

            if (_dx9Hook != null)
            {
                return true;
            }

            var deviceAddr = DirectX9Hook.GetDeviceAddress();
            if (deviceAddr != IntPtr.Zero)
            {
                DX9HookFlags flags = DX9HookFlags.Reset | (UseDx9EndSceneHook() ? DX9HookFlags.EndScene : DX9HookFlags.Present);
                _dx9Hook = new DirectX9Hook(deviceAddr, flags);
                _dx9Renderer = new DirectX9Renderer(_overlayMenu);

                if (UseDx9EndSceneHook())
                {
                    _dx9Hook.OnEndScene += (device) =>
                    {
                        lock (_renderLock)
                        {
                            if (!ShouldRunBackendCallback(BackendKind.DX9))
                            {
                                return;
                            }

                            IntPtr hWnd = GetWindowHandleFailSafe();
                            RenderDx9Overlay(device, hWnd, wrapBeginEndScene: false);
                        }
                    };
                }
                else
                {
                    _dx9Hook.OnPresent += (device, sourceRect, destRect, hDestWindowOverride, dirtyRegion) =>
                    {
                        lock (_renderLock)
                        {
                            if (!ShouldRunBackendCallback(BackendKind.DX9))
                            {
                                return;
                            }

                            IntPtr hWnd = hDestWindowOverride != IntPtr.Zero ? hDestWindowOverride : GetWindowHandleFailSafe();
                            RenderDx9Overlay(device, hWnd, wrapBeginEndScene: true);
                        }
                    };
                }

                _dx9Hook.OnBeforeReset += (device, pp) =>
                {
                    lock (_renderLock)
                    {
                        if (GetActiveBackend() != BackendKind.DX9)
                        {
                            return;
                        }

                        if (_dx9Renderer.IsInitialized)
                        {
                            Console.WriteLine("[PhantomRender] DX9 Pre-Reset: Invalidating device objects...");
                            _dx9Renderer.OnLostDevice();
                        }
                    }
                };

                _dx9Hook.OnAfterReset += (device, pp) =>
                {
                    lock (_renderLock)
                    {
                        if (GetActiveBackend() != BackendKind.DX9)
                        {
                            return;
                        }

                        if (_dx9Renderer.IsInitialized)
                        {
                            Console.WriteLine("[PhantomRender] DX9 Post-Reset: Recreating device objects...");
                            _dx9Renderer.OnResetDevice();
                        }
                    }
                };

                _dx9Hook.Enable();
                Console.WriteLine($"[PhantomRender] DX9 Hook Enabled ({(UseDx9EndSceneHook() ? "EndScene" : "Present")}).");
                return true;
            }
            else
            {
                 Console.WriteLine("[PhantomRender] DX9 Device Not Found (Dummy creation failed).");
                return false;
            }
        }

        private static DirectX11Renderer _dx11Renderer;
#if NET5_0_OR_GREATER
        private static DirectX12Renderer _dx12Renderer;
#endif

        /// <summary>
        /// Unified DXGI initialization.
        /// Both DX10 and DX11 share IDXGISwapChain::Present, so we only need ONE hook.
        /// In the OnPresent callback, we auto-detect the actual device type:
        ///   - Try IID_ID3D11Device first → use DX11 renderer
        ///   - Fall back to IID_ID3D10Device → use DX10 renderer
        /// </summary>
        private static bool TryInitializeDXGI()
        {
            if (GetModuleHandleW("dxgi.dll") == IntPtr.Zero)
            {
                return false;
            }

            if (_dx10Hook != null)
            {
                return true;
            }

            var swapChainAddr = DirectX10Hook.GetSwapChainAddress();
            if (swapChainAddr != IntPtr.Zero)
            {
                _dx10Hook = new DirectX10Hook(swapChainAddr);
                // Don't create renderers yet — we'll detect which one is needed

                _dx10Hook.OnBeforeResizeBuffers += (swapChain, bufferCount, width, height, newFormat, swapChainFlags) =>
                {
                    lock (_renderLock)
                    {
                        if (!ShouldRunBackendCallback(BackendKind.DXGI))
                        {
                            return;
                        }

                        if (!IsDxgiSwapChainRelevant(swapChain))
                        {
                            return;
                        }

                        Console.WriteLine($"[PhantomRender] DXGI Before ResizeBuffers size={width}x{height} buffers={bufferCount} fmt={newFormat} flags=0x{swapChainFlags:X}");
                        Console.Out.Flush();

                        try { _dx11Renderer?.OnLostDevice(); } catch { }
                        try { _dx10Renderer?.OnLostDevice(); } catch { }
#if NET5_0_OR_GREATER
                        try { _dx12Renderer?.OnBeforeResizeBuffers(swapChain); } catch { }
#endif
                    }
                };

                _dx10Hook.OnAfterResizeBuffers += (swapChain, bufferCount, width, height, newFormat, swapChainFlags, hr) =>
                {
                    lock (_renderLock)
                    {
                        if (!ShouldRunBackendCallback(BackendKind.DXGI))
                        {
                            return;
                        }

                        if (!IsDxgiSwapChainRelevant(swapChain))
                        {
                            return;
                        }

                        if (hr < 0)
                        {
                            Console.WriteLine($"[PhantomRender] DXGI ResizeBuffers failed hr=0x{hr:X8} size={width}x{height} buffers={bufferCount} fmt={newFormat} flags=0x{swapChainFlags:X}");
                            Console.Out.Flush();
                        }
                        else
                        {
                            Console.WriteLine($"[PhantomRender] DXGI After ResizeBuffers hr=0x{hr:X8} size={width}x{height} buffers={bufferCount}");
                            Console.Out.Flush();
                        }

                        try { _dx11Renderer?.OnResetDevice(); } catch { }
                        try { _dx10Renderer?.OnResetDevice(); } catch { }
#if NET5_0_OR_GREATER
                        try { _dx12Renderer?.OnAfterResizeBuffers(swapChain); } catch { }
#endif
                    }
                };

                _dx10Hook.OnPresent += (swapChain, syncInterval, flags) =>
                {
                    bool lockTaken = false;
                    try
                    {
                        if (!Monitor.TryEnter(_renderLock))
                        {
                            // Never block the game's Present path due to overlay contention.
                            return;
                        }

                        lockTaken = true;

                        if (!ShouldRunBackendCallback(BackendKind.DXGI))
                        {
                            return;
                        }

                        long presentCount = Interlocked.Increment(ref _dxgiPresentCounter);
                        if ((presentCount % 1800) == 0)
                        {
                            Console.WriteLine($"[PhantomRender] DXGI Present heartbeat #{presentCount}");
                            Console.Out.Flush();
                        }

                        // Filter out secondary swapchains (common in engines that host overlays/webviews inside the process).
                        IntPtr swapChainWindow = IntPtr.Zero;
                        try { _dx10Hook.TryGetOutputWindow(swapChain, out swapChainWindow); } catch { }

                        if (_dxgiTargetWindow == IntPtr.Zero)
                        {
                            IntPtr expectedWindow = GetWindowHandleFailSafe();
                            if (swapChainWindow == IntPtr.Zero)
                            {
                                swapChainWindow = expectedWindow;
                            }

                            // If we know the main window, only lock onto swapchains that present to it.
                            if (expectedWindow != IntPtr.Zero && swapChainWindow != IntPtr.Zero && swapChainWindow != expectedWindow)
                            {
                                return;
                            }

                            _dxgiTargetWindow = swapChainWindow != IntPtr.Zero ? swapChainWindow : expectedWindow;
                            if (_dxgiTargetWindow == IntPtr.Zero)
                            {
                                // Delay renderer init until we have a reliable target window.
                                return;
                            }
                        }
                        else if (_dxgiTargetWindow != IntPtr.Zero && swapChainWindow != IntPtr.Zero && swapChainWindow != _dxgiTargetWindow)
                        {
                            return;
                        }
                        else if (swapChainWindow == IntPtr.Zero)
                        {
                            swapChainWindow = _dxgiTargetWindow;
                        }

                        // Check if either renderer is already initialized
                        bool hasRenderer = (_dx11Renderer != null && _dx11Renderer.IsInitialized)
                                        || (_dx10Renderer != null && _dx10Renderer.IsInitialized)
#if NET5_0_OR_GREATER
                                        || (_dx12Renderer != null && _dx12Renderer.IsInitialized)
#endif
                                        ;

                        if (!hasRenderer)
                        {
                            Console.WriteLine("[PhantomRender] DXGI OnPresent - Detecting device type...");
                            IntPtr hWnd = swapChainWindow != IntPtr.Zero ? swapChainWindow : _dxgiTargetWindow;
                            if (hWnd == IntPtr.Zero)
                            {
                                hWnd = ResolveWindowHandleFromSwapChain(swapChain);
                            }

                            // 1. Try DX11 first (most modern games use DX11)
                            IntPtr dx11Device = _dx10Hook.GetDeviceAs11(swapChain);
                            if (dx11Device != IntPtr.Zero)
                            {
                                Console.WriteLine($"[PhantomRender] DXGI: Detected DX11 device: {dx11Device}. Window: {hWnd}");
                                _dx11Renderer = new DirectX11Renderer(_overlayMenu);
                                _dx11Renderer.Initialize(dx11Device, hWnd);
                                Marshal.Release(dx11Device); // Release extra ref from GetDeviceAs11

                                if (_dx11Renderer.IsInitialized)
                                    EnsureActiveBackendSelected(BackendKind.DXGI);
                            }
                            else
                            {
#if NET5_0_OR_GREATER
                                // 2. Try DX12
                                IntPtr dx12Device = _dx10Hook.GetDeviceAs12(swapChain);
                                if (dx12Device != IntPtr.Zero)
                                {
                                    Console.WriteLine($"[PhantomRender] DXGI: Detected DX12 device: {dx12Device}. Window: {hWnd}");
                                    _dx12Renderer = new DirectX12Renderer(_overlayMenu);
                                    _dx12Renderer.Initialize(dx12Device, hWnd);
                                    Marshal.Release(dx12Device); // Release extra ref from GetDeviceAs12

                                    if (_dx12Renderer.IsInitialized)
                                        EnsureActiveBackendSelected(BackendKind.DXGI);
                                }
                                else
#endif
                                {
                                    // 3. Fall back to DX10
                                    IntPtr dx10Device = _dx10Hook.GetDevice(swapChain);
                                    if (dx10Device != IntPtr.Zero)
                                    {
                                        Console.WriteLine($"[PhantomRender] DXGI: Detected DX10 device: {dx10Device}. Window: {hWnd}");
                                        _dx10Renderer = new DirectX10Renderer(_overlayMenu);
                                        _dx10Renderer.Initialize(dx10Device, hWnd);
                                        Marshal.Release(dx10Device); // Release extra ref from GetDevice

                                        if (_dx10Renderer.IsInitialized)
                                            EnsureActiveBackendSelected(BackendKind.DXGI);
                                    }
                                    else
                                    {
                                        Console.WriteLine("[PhantomRender] DXGI: Could NOT detect any device from SwapChain!");
                                    }
                                }
                            }
                        }

                        try
                        {
                            // Render with whichever renderer was initialized.
                            // This lock also serializes rendering vs. ResizeBuffers (device/RTV recreation) to avoid races/crashes.
                            if (_dx11Renderer != null && _dx11Renderer.IsInitialized)
                            {
                                _dx11Renderer.NewFrame();
                                _dx11Renderer.Render(swapChain);
                            }
#if NET5_0_OR_GREATER
                            else if (_dx12Renderer != null && _dx12Renderer.IsInitialized)
                            {
                                _dx12Renderer.NewFrame();
                                _dx12Renderer.Render(swapChain);
                            }
#endif
                            else if (_dx10Renderer != null && _dx10Renderer.IsInitialized)
                            {
                                _dx10Renderer.NewFrame();
                                _dx10Renderer.Render();
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[PhantomRender] DXGI OnPresent Lambda Error: {ex}");
                            Console.Out.Flush();
                        }
                    }
                    finally
                    {
                        if (lockTaken)
                        {
                            Monitor.Exit(_renderLock);
                        }
                    }
                };
                
                _dx10Hook.Enable();
                Console.WriteLine("[PhantomRender] DXGI Present Hook Enabled (auto-detects DX10/DX11/DX12).");
                return true;
            }
            else
            {
                Console.WriteLine("[PhantomRender] DXGI Hook NOT enabled (dummy SwapChain creation failed).");
                return false;
            }
        }

        private static bool TryInitializeOpenGL()
        {
            // Don't force-load OpenGL into non-OpenGL games.
            if (GetModuleHandleW("opengl32.dll") == IntPtr.Zero)
            {
                return false;
            }

            if (_glHook != null)
            {
                return true;
            }

            _glHook = new OpenGLHook();
            _glRenderer = new OpenGLRenderer(_overlayMenu);

            _glHook.OnSwapBuffers += (hdc) =>
            {
                lock (_renderLock)
                {
                    if (!ShouldRunBackendCallback(BackendKind.OpenGL))
                    {
                        return;
                    }

                    IntPtr hWnd = WindowFromDC(hdc);
                    if (hWnd == IntPtr.Zero) hWnd = GetWindowHandleFailSafe();
                    if (hWnd == IntPtr.Zero) return;

                    // Some processes have multiple OpenGL swapbuffers calls (CEF/overlays). Draw only on the first window we lock onto.
                    if (_glTargetWindow != IntPtr.Zero && hWnd != _glTargetWindow)
                    {
                        return;
                    }

                    if (!_glRenderer.IsInitialized && !_glInitFailed)
                    {
                        Console.WriteLine("[PhantomRender] OpenGL OnSwapBuffers - Initializing Renderer...");
                        Console.WriteLine($"[PhantomRender] Target Window: {hWnd}");

                        _glTargetWindow = hWnd;
                        if (!_glRenderer.Initialize(IntPtr.Zero, hWnd))
                        {
                            Console.WriteLine("[PhantomRender] OpenGL Renderer init failed, will not retry.");
                            _glInitFailed = true;
                            _glTargetWindow = IntPtr.Zero;
                        }
                        else
                        {
                            EnsureActiveBackendSelected(BackendKind.OpenGL);
                        }
                    }

                    if (_glRenderer.IsInitialized)
                    {
                        _glRenderer.NewFrame();
                        _glRenderer.Render();
                    }
                }
            };
            
            _glHook.Enable();
             Console.WriteLine("[PhantomRender] OpenGL Hook Enabled.");
            return true;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromDC(IntPtr hdc);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        private const uint GW_OWNER = 4;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private static IntPtr FindBestTopLevelWindowForCurrentProcess(IntPtr consoleWindow)
        {
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;

            uint pid = 0;
            try { pid = (uint)Process.GetCurrentProcess().Id; } catch { }
            if (pid == 0) return IntPtr.Zero;

            try
            {
                EnumWindows((hWnd, lParam) =>
                {
                    try
                    {
                        if (hWnd == IntPtr.Zero) return true;
                        if (consoleWindow != IntPtr.Zero && hWnd == consoleWindow) return true;

                        GetWindowThreadProcessId(hWnd, out uint windowPid);
                        if (windowPid != pid) return true;

                        // Skip owned (tool/child) windows; prefer the main game window.
                        if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return true;

                        if (!IsWindowVisible(hWnd)) return true;
                        if (IsIconic(hWnd)) return true;

                        if (!GetClientRect(hWnd, out RECT rc)) return true;
                        int w = rc.Right - rc.Left;
                        int h = rc.Bottom - rc.Top;
                        if (w <= 0 || h <= 0) return true;

                        long area = (long)w * h;
                        if (area > bestArea)
                        {
                            bestArea = area;
                            best = hWnd;
                        }
                    }
                    catch
                    {
                        // Keep enumerating.
                    }

                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
                return IntPtr.Zero;
            }

            return best;
        }

        private static IntPtr GetWindowHandleFailSafe()
        {
            try
            {
                IntPtr consoleWindow = IntPtr.Zero;
                try { consoleWindow = GetConsoleWindow(); } catch { }

                var process = Process.GetCurrentProcess();
                if (process.MainWindowHandle != IntPtr.Zero && process.MainWindowHandle != consoleWindow)
                    return process.MainWindowHandle;

                IntPtr best = FindBestTopLevelWindowForCurrentProcess(consoleWindow);
                if (best != IntPtr.Zero)
                    return best;
                
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero && fg != consoleWindow)
                {
                    GetWindowThreadProcessId(fg, out uint processId);
                    if (processId == process.Id)
                        return fg;
                }
            }
            catch { }
            
            return IntPtr.Zero;
        }

        private static IntPtr ResolveWindowHandleFromSwapChain(IntPtr swapChain)
        {
            try
            {
                if (_dx10Hook != null && _dx10Hook.TryGetOutputWindow(swapChain, out IntPtr swapChainWindow) && swapChainWindow != IntPtr.Zero)
                {
                    Console.WriteLine($"[PhantomRender] DXGI: Using swapchain OutputWindow: {swapChainWindow}");
                    return swapChainWindow;
                }
            }
            catch { }

            IntPtr fallback = GetWindowHandleFailSafe();
            Console.WriteLine($"[PhantomRender] DXGI: Using fail-safe window: {fallback}");
            return fallback;
        }

        private static bool IsDxgiSwapChainRelevant(IntPtr swapChain)
        {
            if (swapChain == IntPtr.Zero)
            {
                return true;
            }

            IntPtr targetWindow = _dxgiTargetWindow;
            if (targetWindow == IntPtr.Zero || _dx10Hook == null)
            {
                return true;
            }

            try
            {
                if (_dx10Hook.TryGetOutputWindow(swapChain, out IntPtr swapChainWindow) && swapChainWindow != IntPtr.Zero)
                {
                    return swapChainWindow == targetWindow;
                }
            }
            catch
            {
                // Unknown swapchain metadata: don't block lifecycle callbacks.
            }

            return true;
        }
    }
}
