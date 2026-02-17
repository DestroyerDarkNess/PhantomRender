using System;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.D3D11;
using Hexa.NET.ImGui.Backends.Win32;
using PhantomRender.ImGui;

namespace PhantomRender.ImGui.Renderers
{
    public sealed class DirectX11Renderer : RendererBase
    {
        private const int VTABLE_IDXGISwapChain_GetDevice = 7;
        private const int VTABLE_IDXGISwapChain_GetBuffer = 9;
        private const int VTABLE_ID3D11Device_CreateRenderTargetView = 9;
        private const int VTABLE_ID3D11Device_GetImmediateContext = 40;
        private const int VTABLE_IUnknown_QueryInterface = 0;
        private const int VTABLE_ID3D11DeviceContext_OMSetRenderTargets = 33;
        private const int VTABLE_ID3D11DeviceContext_OMGetRenderTargets = 89;
        private const int VTABLE_ID3D11Multithread_SetMultithreadProtected = 5;

        private static readonly Guid IID_ID3D11Device = new Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140");
        private static readonly Guid IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        private static readonly Guid IID_ID3D11Multithread = new Guid("9b7e4e00-342c-4106-a19f-4f2704f689f0");

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetImmediateContextDelegate(IntPtr device, out IntPtr ppImmediateContext);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDeviceDelegate(IntPtr swapChain, ref Guid riid, out IntPtr ppDevice);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetBufferDelegate(IntPtr swapChain, uint bufferIndex, ref Guid riid, out IntPtr ppSurface);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateRenderTargetViewDelegate(IntPtr device, IntPtr resource, IntPtr desc, out IntPtr renderTargetView);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryInterfaceDelegate(IntPtr instance, ref Guid riid, out IntPtr ppvObject);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate void OMGetRenderTargetsDelegate(IntPtr deviceContext, uint numViews, IntPtr* renderTargetViews, out IntPtr depthStencilView);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate void OMSetRenderTargetsDelegate(IntPtr deviceContext, uint numViews, IntPtr* renderTargetViews, IntPtr depthStencilView);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetMultithreadProtectedDelegate(IntPtr multithread, int bMultithreadProtected);

        private IntPtr _deviceContext;
        private IntPtr _renderTargetView;
        private IntPtr _swapChainForRenderTarget;
        private IntPtr _lastSwapChain;
        private bool _loggedRenderTargetReady;
        private bool _loggedBackendReinit;
        private bool _loggedMultithreadProtectEnabled;
        private bool _loggedMultithreadProtectUnavailable;

        public DirectX11Renderer(OverlayMenu overlayMenu)
            : base(overlayMenu, GraphicsApi.DirectX11)
        {
        }

        public override unsafe bool Initialize(IntPtr device, IntPtr windowHandle)
        {
            if (IsInitialized) return true;

            try
            {
                Console.WriteLine($"[PhantomRender] DirectX11Renderer: Entering Initialize. Device: {device}, Window: {windowHandle}");
                Console.Out.Flush();

                RaiseRendererInitializing(device, windowHandle);
                IntPtr deviceContext = GetImmediateContext(device);
                if (deviceContext == IntPtr.Zero)
                {
                    Console.WriteLine("[PhantomRender] DirectX11Renderer: Failed to get ImmediateContext!");
                    Console.Out.Flush();
                    return false;
                }

                Console.WriteLine($"[PhantomRender] DirectX11Renderer: Got ImmediateContext: {deviceContext}");
                Console.Out.Flush();

                InitializeImGui(windowHandle);

                ImGuiImplD3D11.SetCurrentContext(Context);
                if (!ImGuiImplD3D11.Init((ID3D11Device*)device, (ID3D11DeviceContext*)deviceContext))
                {
                    Console.WriteLine("[PhantomRender] DirectX11Renderer: ImGuiImplD3D11.Init returned FALSE!");
                    Console.Out.Flush();
                    Marshal.Release(deviceContext);
                    ShutdownImGui();
                    return false;
                }

                _deviceContext = deviceContext;

                if (TryEnableDeviceContextMultithreadProtection(_deviceContext))
                {
                    if (!_loggedMultithreadProtectEnabled)
                    {
                        Console.WriteLine("[PhantomRender] DirectX11Renderer: Enabled ID3D11Multithread protection.");
                        Console.Out.Flush();
                        _loggedMultithreadProtectEnabled = true;
                    }
                }
                else if (!_loggedMultithreadProtectUnavailable)
                {
                    Console.WriteLine("[PhantomRender] DirectX11Renderer: ID3D11Multithread unavailable; continuing without protection.");
                    Console.Out.Flush();
                    _loggedMultithreadProtectUnavailable = true;
                }

                IsInitialized = true;
                Console.WriteLine("[PhantomRender] DirectX11Renderer: Initialized Successfully!");
                Console.Out.Flush();
                return true;
            }
            catch (Exception ex)
            {
                if (_deviceContext != IntPtr.Zero)
                {
                    Marshal.Release(_deviceContext);
                    _deviceContext = IntPtr.Zero;
                }

                Console.WriteLine($"[PhantomRender] DirectX11Renderer: Init Error: {ex}");
                Console.Out.Flush();
                return false;
            }
        }

        public override void NewFrame()
        {
            if (!IsInitialized) return;

            Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
            ImGuiImplD3D11.SetCurrentContext(Context);
            ImGuiImplWin32.SetCurrentContext(Context);

            ImGuiImplD3D11.NewFrame();
            ImGuiImplWin32.NewFrame();
            RaiseNewFrame();
            Hexa.NET.ImGui.ImGui.NewFrame();
        }

        public override void Render()
        {
            Render(_lastSwapChain);
        }

        public unsafe void Render(IntPtr swapChain)
        {
            if (!IsInitialized || _deviceContext == IntPtr.Zero) return;

            if (swapChain != IntPtr.Zero)
            {
                _lastSwapChain = swapChain;
            }

            if (_lastSwapChain == IntPtr.Zero)
            {
                return;
            }

            Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);

            RenderMenuFrame();

            RaiseOverlayRender();
            Hexa.NET.ImGui.ImGui.Render();
            var drawData = Hexa.NET.ImGui.ImGui.GetDrawData();
            if (drawData.CmdListsCount <= 0 || drawData.TotalVtxCount <= 0)
            {
                return;
            }

            if (!EnsureRenderTarget(_lastSwapChain))
            {
                return;
            }

            if (!BindOverlayRenderTarget())
            {
                return;
            }

            ImGuiImplD3D11.RenderDrawData(drawData);
        }

        private unsafe bool EnsureBackendContext(IntPtr swapChain)
        {
            if (swapChain == IntPtr.Zero)
            {
                return false;
            }

            IntPtr device = IntPtr.Zero;
            IntPtr newContext = IntPtr.Zero;

            try
            {
                if (!TryGetSwapChainDevice(swapChain, out device))
                {
                    return false;
                }

                newContext = GetImmediateContext(device);
                if (newContext == IntPtr.Zero)
                {
                    return false;
                }

                // Most of the time the context stays stable. When games recreate the device/context (scene/world load, etc),
                // continuing to use the old context can crash. Detect and re-init ImGui DX11 backend.
                if (_deviceContext != IntPtr.Zero && newContext == _deviceContext)
                {
                    Marshal.Release(newContext);
                    newContext = IntPtr.Zero;
                    return true;
                }

                if (_deviceContext != IntPtr.Zero)
                {
                    Marshal.Release(_deviceContext);
                    _deviceContext = IntPtr.Zero;
                }

                // Fully re-init the DX11 backend with the new device/context.
                try { ImGuiImplD3D11.SetCurrentContext(Context); } catch { }
                try { ImGuiImplD3D11.Shutdown(); } catch { }

                ImGuiImplD3D11.SetCurrentContext(Context);
                if (!ImGuiImplD3D11.Init((ID3D11Device*)device, (ID3D11DeviceContext*)newContext))
                {
                    // Keep the renderer alive; we'll retry on future frames.
                    Marshal.Release(newContext);
                    newContext = IntPtr.Zero;
                    return false;
                }

                _deviceContext = newContext;
                newContext = IntPtr.Zero; // now owned by _deviceContext

                if (TryEnableDeviceContextMultithreadProtection(_deviceContext))
                {
                    if (!_loggedMultithreadProtectEnabled)
                    {
                        Console.WriteLine("[PhantomRender] DirectX11Renderer: Enabled ID3D11Multithread protection.");
                        Console.Out.Flush();
                        _loggedMultithreadProtectEnabled = true;
                    }
                }
                else if (!_loggedMultithreadProtectUnavailable)
                {
                    Console.WriteLine("[PhantomRender] DirectX11Renderer: ID3D11Multithread unavailable; continuing without protection.");
                    Console.Out.Flush();
                    _loggedMultithreadProtectUnavailable = true;
                }

                // The render target (and OM restore behavior) is tied to the old context/device state.
                ReleaseRenderTarget();

                if (!_loggedBackendReinit)
                {
                    Console.WriteLine("[PhantomRender] DirectX11Renderer: D3D11 backend reinitialized (device/context change).");
                    Console.Out.Flush();
                    _loggedBackendReinit = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DirectX11Renderer: EnsureBackendContext error: {ex.Message}");
                Console.Out.Flush();
                return false;
            }
            finally
            {
                if (newContext != IntPtr.Zero)
                {
                    Marshal.Release(newContext);
                }

                if (device != IntPtr.Zero)
                {
                    Marshal.Release(device);
                }
            }
        }

        public override void OnLostDevice()
        {
            if (IsInitialized)
            {
                // For DX11 resize, releasing and recreating RTV is enough.
                ReleaseRenderTarget();
            }
        }

        public override void OnResetDevice()
        {
            if (IsInitialized)
            {
                // Device objects are retained; RTV will be recreated lazily on next render.
                ReleaseRenderTarget();
            }
        }

        public override void Dispose()
        {
            ReleaseRenderTarget();

            if (_deviceContext != IntPtr.Zero)
            {
                Marshal.Release(_deviceContext);
                _deviceContext = IntPtr.Zero;
            }

            if (IsInitialized)
            {
                ImGuiImplD3D11.Shutdown();
                ShutdownImGui();
                IsInitialized = false;
            }

            _lastSwapChain = IntPtr.Zero;
        }

        private static IntPtr GetImmediateContext(IntPtr device)
        {
            if (device == IntPtr.Zero) return IntPtr.Zero;

            try
            {
                IntPtr getImmediateContextAddr = GetVTableFunctionAddress(device, VTABLE_ID3D11Device_GetImmediateContext);
                if (getImmediateContextAddr == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }

                var getImmediateContext = Marshal.GetDelegateForFunctionPointer<GetImmediateContextDelegate>(getImmediateContextAddr);
                IntPtr context;
                getImmediateContext(device, out context);
                return context;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DirectX11Renderer: GetImmediateContext error: {ex}");
                return IntPtr.Zero;
            }
        }

        private static IntPtr GetVTableFunctionAddress(IntPtr instance, int functionIndex)
        {
            if (instance == IntPtr.Zero) return IntPtr.Zero;

            IntPtr vTable = Marshal.ReadIntPtr(instance);
            if (vTable == IntPtr.Zero) return IntPtr.Zero;

            return Marshal.ReadIntPtr(vTable + functionIndex * IntPtr.Size);
        }

        private static void ReleaseComObject(IntPtr pointer)
        {
            if (pointer != IntPtr.Zero)
            {
                Marshal.Release(pointer);
            }
        }

        private static bool TryEnableDeviceContextMultithreadProtection(IntPtr deviceContext)
        {
            if (deviceContext == IntPtr.Zero)
            {
                return false;
            }

            IntPtr multithread = IntPtr.Zero;
            try
            {
                IntPtr queryInterfaceAddr = GetVTableFunctionAddress(deviceContext, VTABLE_IUnknown_QueryInterface);
                if (queryInterfaceAddr == IntPtr.Zero)
                {
                    return false;
                }

                var queryInterface = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(queryInterfaceAddr);
                Guid iid = IID_ID3D11Multithread;
                int hr = queryInterface(deviceContext, ref iid, out multithread);
                if (hr < 0 || multithread == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr setMultithreadProtectedAddr = GetVTableFunctionAddress(multithread, VTABLE_ID3D11Multithread_SetMultithreadProtected);
                if (setMultithreadProtectedAddr == IntPtr.Zero)
                {
                    return false;
                }

                var setMultithreadProtected = Marshal.GetDelegateForFunctionPointer<SetMultithreadProtectedDelegate>(setMultithreadProtectedAddr);
                setMultithreadProtected(multithread, 1);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                ReleaseComObject(multithread);
            }
        }

        private void ReleaseRenderTarget()
        {
            if (_renderTargetView != IntPtr.Zero)
            {
                Marshal.Release(_renderTargetView);
                _renderTargetView = IntPtr.Zero;
            }

            _swapChainForRenderTarget = IntPtr.Zero;
            _loggedRenderTargetReady = false;
        }

        private bool EnsureRenderTarget(IntPtr swapChain)
        {
            if (swapChain == IntPtr.Zero)
            {
                return false;
            }

            if (_renderTargetView != IntPtr.Zero && _swapChainForRenderTarget == swapChain)
            {
                return true;
            }

            ReleaseRenderTarget();

            IntPtr device = IntPtr.Zero;
            IntPtr backBuffer = IntPtr.Zero;

            try
            {
                if (!TryGetSwapChainBuffer(swapChain, out backBuffer))
                {
                    Console.WriteLine("[PhantomRender] DirectX11Renderer: Failed to get DX11 backbuffer.");
                    Console.Out.Flush();
                    return false;
                }

                if (!TryGetSwapChainDevice(swapChain, out device))
                {
                    Console.WriteLine("[PhantomRender] DirectX11Renderer: Failed to get DX11 device from swap chain.");
                    Console.Out.Flush();
                    return false;
                }

                if (!TryCreateRenderTargetView(device, backBuffer, out _renderTargetView))
                {
                    Console.WriteLine("[PhantomRender] DirectX11Renderer: Failed to create RenderTargetView.");
                    Console.Out.Flush();
                    return false;
                }

                _swapChainForRenderTarget = swapChain;
                if (!_loggedRenderTargetReady)
                {
                    Console.WriteLine($"[PhantomRender] DirectX11Renderer: RenderTargetView ready: {_renderTargetView}");
                    Console.Out.Flush();
                    _loggedRenderTargetReady = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DirectX11Renderer: EnsureRenderTarget error: {ex.Message}");
                Console.Out.Flush();
                return false;
            }
            finally
            {
                ReleaseComObject(backBuffer);
                ReleaseComObject(device);
            }
        }

        private static bool TryGetSwapChainDevice(IntPtr swapChain, out IntPtr device)
        {
            device = IntPtr.Zero;
            if (swapChain == IntPtr.Zero) return false;

            IntPtr getDeviceAddr = GetVTableFunctionAddress(swapChain, VTABLE_IDXGISwapChain_GetDevice);
            if (getDeviceAddr == IntPtr.Zero) return false;

            var getDevice = Marshal.GetDelegateForFunctionPointer<GetDeviceDelegate>(getDeviceAddr);
            Guid iid = IID_ID3D11Device;
            int hr = getDevice(swapChain, ref iid, out device);
            return hr >= 0 && device != IntPtr.Zero;
        }

        private static bool TryGetSwapChainBuffer(IntPtr swapChain, out IntPtr buffer)
        {
            buffer = IntPtr.Zero;
            if (swapChain == IntPtr.Zero) return false;

            IntPtr getBufferAddr = GetVTableFunctionAddress(swapChain, VTABLE_IDXGISwapChain_GetBuffer);
            if (getBufferAddr == IntPtr.Zero) return false;

            var getBuffer = Marshal.GetDelegateForFunctionPointer<GetBufferDelegate>(getBufferAddr);
            Guid iid = IID_ID3D11Texture2D;
            int hr = getBuffer(swapChain, 0, ref iid, out buffer);
            return hr >= 0 && buffer != IntPtr.Zero;
        }

        private static bool TryCreateRenderTargetView(IntPtr device, IntPtr backBuffer, out IntPtr renderTargetView)
        {
            renderTargetView = IntPtr.Zero;
            if (device == IntPtr.Zero || backBuffer == IntPtr.Zero) return false;

            IntPtr createRenderTargetViewAddr = GetVTableFunctionAddress(device, VTABLE_ID3D11Device_CreateRenderTargetView);
            if (createRenderTargetViewAddr == IntPtr.Zero) return false;

            var createRenderTargetView = Marshal.GetDelegateForFunctionPointer<CreateRenderTargetViewDelegate>(createRenderTargetViewAddr);
            int hr = createRenderTargetView(device, backBuffer, IntPtr.Zero, out renderTargetView);
            return hr >= 0 && renderTargetView != IntPtr.Zero;
        }

        private unsafe bool TryBackupOutputMergerState(IntPtr* renderTargetViews, uint numViews, out IntPtr depthStencilView)
        {
            depthStencilView = IntPtr.Zero;

            if (_deviceContext == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                IntPtr omGetRenderTargetsAddr = GetVTableFunctionAddress(_deviceContext, VTABLE_ID3D11DeviceContext_OMGetRenderTargets);
                if (omGetRenderTargetsAddr == IntPtr.Zero)
                {
                    return false;
                }

                var omGetRenderTargets = Marshal.GetDelegateForFunctionPointer<OMGetRenderTargetsDelegate>(omGetRenderTargetsAddr);
                omGetRenderTargets(_deviceContext, numViews, renderTargetViews, out depthStencilView);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DirectX11Renderer: OMGetRenderTargets error: {ex.Message}");
                Console.Out.Flush();
                return false;
            }
        }

        private unsafe bool BindOverlayRenderTarget()
        {
            if (_deviceContext == IntPtr.Zero || _renderTargetView == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                IntPtr rtv = _renderTargetView;

                IntPtr omSetRenderTargetsAddr = GetVTableFunctionAddress(_deviceContext, VTABLE_ID3D11DeviceContext_OMSetRenderTargets);
                if (omSetRenderTargetsAddr == IntPtr.Zero)
                {
                    return false;
                }

                var omSetRenderTargets = Marshal.GetDelegateForFunctionPointer<OMSetRenderTargetsDelegate>(omSetRenderTargetsAddr);
                omSetRenderTargets(_deviceContext, 1, &rtv, IntPtr.Zero);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DirectX11Renderer: OMSetRenderTargets error: {ex.Message}");
                Console.Out.Flush();
                return false;
            }
        }

        private unsafe void RestoreOutputMergerState(IntPtr* renderTargetViews, uint numViews, IntPtr depthStencilView)
        {
            if (_deviceContext == IntPtr.Zero)
            {
                return;
            }

            try
            {
                IntPtr omSetRenderTargetsAddr = GetVTableFunctionAddress(_deviceContext, VTABLE_ID3D11DeviceContext_OMSetRenderTargets);
                if (omSetRenderTargetsAddr == IntPtr.Zero)
                {
                    return;
                }

                var omSetRenderTargets = Marshal.GetDelegateForFunctionPointer<OMSetRenderTargetsDelegate>(omSetRenderTargetsAddr);
                omSetRenderTargets(_deviceContext, numViews, renderTargetViews, depthStencilView);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DirectX11Renderer: Restore OM state error: {ex.Message}");
                Console.Out.Flush();
            }
        }
    }
}