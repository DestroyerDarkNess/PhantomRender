using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PhantomRender.Core.Hooks.Graphics;
using PhantomRender.Core.Hooks.Graphics.OpenGL;
using PhantomRender.ImGui.Renderers;

namespace PhantomRender.ImGui
{
    public static class OverlayManager
    {
        private static DirectX9Hook _dx9Hook;
        private static DirectX9Renderer _dx9Renderer;

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

        private static DirectX10Hook _dx10Hook;
        private static DirectX10Renderer _dx10Renderer;

        public static void Initialize()
        {
            try { InitializeDX9(); }
            catch (Exception ex) { Console.WriteLine($"[PhantomRender] DX9 Init Failed: {ex}"); }

            try { InitializeDXGI(); }
            catch (Exception ex) { Console.WriteLine($"[PhantomRender] DXGI Init Failed: {ex}"); }

            try { InitializeOpenGL(); }
            catch (Exception ex) { Console.WriteLine($"[PhantomRender] OpenGL Init Failed: {ex}"); }
        }

        private static object _dx9Lock = new object();

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

        private static void InitializeDX9()
        {
            var deviceAddr = DirectX9Hook.GetDeviceAddress();
            if (deviceAddr != IntPtr.Zero)
            {
                _dx9Hook = new DirectX9Hook(deviceAddr);
                _dx9Renderer = new DirectX9Renderer();
                
                _dx9Hook.OnPresent += (device, sourceRect, destRect, hDestWindowOverride, dirtyRegion) =>
                {
                    if (!_dx9Renderer.IsInitialized)
                    {
                        lock (_dx9Lock)
                        {
                            if (!_dx9Renderer.IsInitialized)
                            {
                                Console.WriteLine("[PhantomRender] DX9 OnPresent - Initializing Renderer... (Lock Acquired)");
                                IntPtr hWnd = hDestWindowOverride != IntPtr.Zero ? hDestWindowOverride : GetWindowHandleFailSafe();
                                Console.WriteLine($"[PhantomRender] Target Window: {hWnd}");
                                _dx9Renderer.Initialize(device, hWnd);
                            }
                        }
                    }

                    if (_dx9Renderer.IsInitialized)
                    {
                        bool beganScene = false;
                        try
                        {
                            EnsureDX9SceneDelegates(device);
                            if (_dx9BeginScene != null && _dx9EndScene != null)
                            {
                                int hrBegin = _dx9BeginScene(device);
                                beganScene = hrBegin >= 0;
                                if (!beganScene)
                                {
                                    // If we can't begin a scene, trying to draw may fail anyway; skip EndScene in that case.
                                    // We'll still attempt to render below as a best-effort fallback.
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
                };

                _dx9Hook.OnBeforeReset += (device, pp) =>
                {
                    lock (_dx9Lock)
                    {
                        if (_dx9Renderer.IsInitialized)
                        {
                            Console.WriteLine("[PhantomRender] DX9 Pre-Reset: Invalidating device objects...");
                            _dx9Renderer.OnLostDevice();
                        }
                    }
                };

                _dx9Hook.OnAfterReset += (device, pp) =>
                {
                    lock (_dx9Lock)
                    {
                        if (_dx9Renderer.IsInitialized)
                        {
                            Console.WriteLine("[PhantomRender] DX9 Post-Reset: Recreating device objects...");
                            _dx9Renderer.OnResetDevice();
                        }
                    }
                };

                _dx9Hook.Enable();
                Console.WriteLine("[PhantomRender] DX9 Hook Enabled.");
            }
            else
            {
                 Console.WriteLine("[PhantomRender] DX9 Device Not Found (Dummy creation failed).");
            }
        }

        private static object _dxgiLock = new object();
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
        private static void InitializeDXGI()
        {
            var swapChainAddr = DirectX10Hook.GetSwapChainAddress();
            if (swapChainAddr != IntPtr.Zero)
            {
                _dx10Hook = new DirectX10Hook(swapChainAddr);
                // Don't create renderers yet — we'll detect which one is needed

                _dx10Hook.OnBeforeResizeBuffers += (swapChain, bufferCount, width, height, newFormat, swapChainFlags) =>
                {
                    lock (_dxgiLock)
                    {
                        try { _dx11Renderer?.OnLostDevice(); } catch { }
                        try { _dx10Renderer?.OnLostDevice(); } catch { }
#if NET5_0_OR_GREATER
                        try { _dx12Renderer?.OnBeforeResizeBuffers(swapChain); } catch { }
#endif
                    }
                };

                _dx10Hook.OnAfterResizeBuffers += (swapChain, bufferCount, width, height, newFormat, swapChainFlags, hr) =>
                {
                    lock (_dxgiLock)
                    {
                        try { _dx11Renderer?.OnResetDevice(); } catch { }
                        try { _dx10Renderer?.OnResetDevice(); } catch { }
#if NET5_0_OR_GREATER
                        try { _dx12Renderer?.OnAfterResizeBuffers(swapChain); } catch { }
#endif
                    }
                };

                _dx10Hook.OnPresent += (swapChain, syncInterval, flags) =>
                {
                    // Check if either renderer is already initialized
                    bool hasRenderer = (_dx11Renderer != null && _dx11Renderer.IsInitialized)
                                    || (_dx10Renderer != null && _dx10Renderer.IsInitialized)
#if NET5_0_OR_GREATER
                                    || (_dx12Renderer != null && _dx12Renderer.IsInitialized)
#endif
                                    ;

                    if (!hasRenderer)
                    {
                        lock (_dxgiLock)
                        {
                            // Double-check after acquiring lock
                            hasRenderer = (_dx11Renderer != null && _dx11Renderer.IsInitialized)
                                       || (_dx10Renderer != null && _dx10Renderer.IsInitialized)
#if NET5_0_OR_GREATER
                                       || (_dx12Renderer != null && _dx12Renderer.IsInitialized)
#endif
                                       ;
                            if (!hasRenderer)
                            {
                                Console.WriteLine("[PhantomRender] DXGI OnPresent - Detecting device type...");
                                IntPtr hWnd = ResolveWindowHandleFromSwapChain(swapChain);

                                // 1. Try DX11 first (most modern games use DX11)
                                IntPtr dx11Device = _dx10Hook.GetDeviceAs11(swapChain);
                                if (dx11Device != IntPtr.Zero)
                                {
                                    Console.WriteLine($"[PhantomRender] DXGI: Detected DX11 device: {dx11Device}. Window: {hWnd}");
                                    _dx11Renderer = new DirectX11Renderer();
                                    _dx11Renderer.Initialize(dx11Device, hWnd);
                                    Marshal.Release(dx11Device); // Release extra ref from GetDeviceAs11
                                }
                                else
                                {
#if NET5_0_OR_GREATER
                                    // 2. Try DX12
                                    IntPtr dx12Device = _dx10Hook.GetDeviceAs12(swapChain);
                                    if (dx12Device != IntPtr.Zero)
                                    {
                                        Console.WriteLine($"[PhantomRender] DXGI: Detected DX12 device: {dx12Device}. Window: {hWnd}");
                                        _dx12Renderer = new DirectX12Renderer();
                                        _dx12Renderer.Initialize(dx12Device, hWnd);
                                        Marshal.Release(dx12Device); // Release extra ref from GetDeviceAs12
                                    }
                                    else
#endif
                                    {
                                        // 3. Fall back to DX10
                                        IntPtr dx10Device = _dx10Hook.GetDevice(swapChain);
                                        if (dx10Device != IntPtr.Zero)
                                        {
                                            Console.WriteLine($"[PhantomRender] DXGI: Detected DX10 device: {dx10Device}. Window: {hWnd}");
                                            _dx10Renderer = new DirectX10Renderer();
                                            _dx10Renderer.Initialize(dx10Device, hWnd);
                                            Marshal.Release(dx10Device); // Release extra ref from GetDevice
                                        }
                                        else
                                        {
                                            Console.WriteLine("[PhantomRender] DXGI: Could NOT detect any device from SwapChain!");
                                        }
                                    }
                                }
                            }
                        }
                    }

                    try
                    {
                        // Render with whichever renderer was initialized
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
                };
                
                _dx10Hook.Enable();
                Console.WriteLine("[PhantomRender] DXGI Present Hook Enabled (auto-detects DX10/DX11/DX12).");
            }
            else
            {
                Console.WriteLine("[PhantomRender] DXGI Hook NOT enabled (dummy SwapChain creation failed).");
            }
        }

        private static void InitializeOpenGL()
        {
            _glHook = new OpenGLHook();
            _glRenderer = new OpenGLRenderer();

            _glHook.OnSwapBuffers += (hdc) =>
            {
                 if (!_glRenderer.IsInitialized && !_glInitFailed)
                 {
                     Console.WriteLine("[PhantomRender] OpenGL OnSwapBuffers - Initializing Renderer...");
                     IntPtr hWnd = WindowFromDC(hdc);
                     if (hWnd == IntPtr.Zero) hWnd = GetWindowHandleFailSafe();
                     Console.WriteLine($"[PhantomRender] Target Window: {hWnd}");
                     
                     if (!_glRenderer.Initialize(IntPtr.Zero, hWnd))
                     {
                         Console.WriteLine("[PhantomRender] OpenGL Renderer init failed, will not retry.");
                         _glInitFailed = true;
                     }
                 }

                 if (_glRenderer.IsInitialized)
                 {
                     _glRenderer.NewFrame();
                     _glRenderer.Render();
                 }
            };
            
            _glHook.Enable();
             Console.WriteLine("[PhantomRender] OpenGL Hook Enabled.");
        }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromDC(IntPtr hdc);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private static IntPtr GetWindowHandleFailSafe()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                if (process.MainWindowHandle != IntPtr.Zero)
                    return process.MainWindowHandle;
                
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero)
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
    }
}
