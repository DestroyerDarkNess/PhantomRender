using System;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui.Backends.D3D11;
using Hexa.NET.ImGui.Backends.Win32;
using PhantomRender.Core;
using HexaImGui = Hexa.NET.ImGui.ImGui;

namespace PhantomRender.ImGui.Core.Renderers
{
    public sealed unsafe class DirectX11Renderer : DxgiRendererBase
    {
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
        private delegate void GetImmediateContextDelegate(nint device, out nint ppImmediateContext);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetBufferDelegate(nint swapChain, uint bufferIndex, ref Guid riid, out nint ppSurface);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateRenderTargetViewDelegate(nint device, nint resource, nint desc, out nint renderTargetView);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryInterfaceDelegate(nint instance, ref Guid riid, out nint ppvObject);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate void OMGetRenderTargetsDelegate(nint deviceContext, uint numViews, nint* renderTargetViews, out nint depthStencilView);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate void OMSetRenderTargetsDelegate(nint deviceContext, uint numViews, nint* renderTargetViews, nint depthStencilView);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetMultithreadProtectedDelegate(nint multithread, int bMultithreadProtected);

        private nint _deviceContext;
        private nint _renderTargetView;
        private nint _swapChainForRenderTarget;

        public DirectX11Renderer()
            : base(GraphicsApi.DirectX11)
        {
        }

        public override bool Initialize(nint device, nint windowHandle)
        {
            if (IsInitialized)
            {
                return true;
            }

            if (device == IntPtr.Zero || windowHandle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                RaiseRendererInitializing(device, windowHandle);

                nint deviceContext = GetImmediateContext(device);
                if (deviceContext == IntPtr.Zero)
                {
                    return false;
                }

                InitializeImGui(windowHandle);

                ImGuiImplD3D11.SetCurrentContext(Context);
                if (!ImGuiImplD3D11.Init((ID3D11Device*)device, (ID3D11DeviceContext*)deviceContext))
                {
                    ReleaseComObject(deviceContext);
                    ShutdownImGui();
                    return false;
                }

                _deviceContext = deviceContext;
                TryEnableDeviceContextMultithreadProtection(_deviceContext);
                IsInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                ReportRuntimeError("DirectX11.Initialize", ex);
                ReleaseComObject(_deviceContext);
                _deviceContext = IntPtr.Zero;

                try
                {
                    ShutdownImGui();
                }
                catch
                {
                }

                return false;
            }
        }

        public override void NewFrame()
        {
            if (!IsInitialized || FrameStarted)
            {
                return;
            }

            try
            {
                BeginFrameCore(() =>
                {
                    ImGuiImplD3D11.SetCurrentContext(Context);
                    ImGuiImplD3D11.NewFrame();
                    ImGuiImplWin32.SetCurrentContext(Context);
                    ImGuiImplWin32.NewFrame();
                });

                FrameStarted = true;
            }
            catch (Exception ex)
            {
                ReportRuntimeError("DirectX11.NewFrame", ex);
            }
        }

        public override unsafe void Render(nint swapChain)
        {
            if (!IsInitialized || !FrameStarted)
            {
                return;
            }

            if (swapChain != IntPtr.Zero)
            {
                SwapChainHandle = swapChain;
            }

            try
            {
                RenderFrameCore(() =>
                {
                    if (SwapChainHandle == IntPtr.Zero)
                    {
                        return;
                    }

                    if (!EnsureBackendContext(SwapChainHandle))
                    {
                        return;
                    }

                    if (!EnsureRenderTarget(SwapChainHandle))
                    {
                        return;
                    }

                    nint previousRenderTarget = IntPtr.Zero;
                    nint previousDepthStencil = IntPtr.Zero;
                    bool restoreState = false;

                    try
                    {
                        restoreState = TryBackupOutputMergerState(&previousRenderTarget, 1, out previousDepthStencil);
                        if (!BindOverlayRenderTarget())
                        {
                            return;
                        }

                        ImGuiImplD3D11.SetCurrentContext(Context);
                        ImGuiImplD3D11.RenderDrawData(HexaImGui.GetDrawData());
                    }
                    finally
                    {
                        if (restoreState)
                        {
                            RestoreOutputMergerState(&previousRenderTarget, 1, previousDepthStencil);
                        }

                        ReleaseComObject(previousRenderTarget);
                        ReleaseComObject(previousDepthStencil);
                    }
                });
            }
            catch (Exception ex)
            {
                ReportRuntimeError("DirectX11.Render", ex);
            }
            finally
            {
                FrameStarted = false;
            }
        }

        public override void OnBeforeResizeBuffers(nint swapChain)
        {
            base.OnBeforeResizeBuffers(swapChain);
            ReleaseRenderTarget();
        }

        public override void OnAfterResizeBuffers(nint swapChain)
        {
            base.OnAfterResizeBuffers(swapChain);
            ReleaseRenderTarget();
        }

        public override void OnLostDevice()
        {
            ReleaseRenderTarget();
        }

        public override void OnResetDevice()
        {
            ReleaseRenderTarget();
        }

        public override void Dispose()
        {
            try
            {
                ReleaseRenderTarget();

                ReleaseComObject(_deviceContext);
                _deviceContext = IntPtr.Zero;

                if (!Context.IsNull)
                {
                    ImGuiImplD3D11.SetCurrentContext(Context);
                }

                if (IsInitialized)
                {
                    ImGuiImplD3D11.Shutdown();
                }
            }
            catch (Exception ex)
            {
                ReportRuntimeError("DirectX11.Dispose", ex);
            }
            finally
            {
                ShutdownImGui();
                IsInitialized = false;
                FrameStarted = false;
                SwapChainHandle = IntPtr.Zero;
            }
        }

        protected override bool TryGetDeviceFromSwapChain(nint swapChain, out nint device)
        {
            return TryGetSwapChainDevice(swapChain, IID_ID3D11Device, out device);
        }

        private static nint GetImmediateContext(nint device)
        {
            nint getImmediateContextAddress = GetVTableFunctionAddress(device, VTABLE_ID3D11Device_GetImmediateContext);
            if (getImmediateContextAddress == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var getImmediateContext = Marshal.GetDelegateForFunctionPointer<GetImmediateContextDelegate>(getImmediateContextAddress);
            getImmediateContext(device, out nint context);
            return context;
        }

        private static bool TryEnableDeviceContextMultithreadProtection(nint deviceContext)
        {
            if (deviceContext == IntPtr.Zero)
            {
                return false;
            }

            nint multithread = IntPtr.Zero;
            try
            {
                nint queryInterfaceAddress = GetVTableFunctionAddress(deviceContext, VTABLE_IUnknown_QueryInterface);
                if (queryInterfaceAddress == IntPtr.Zero)
                {
                    return false;
                }

                var queryInterface = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(queryInterfaceAddress);
                Guid iid = IID_ID3D11Multithread;
                int hr = queryInterface(deviceContext, ref iid, out multithread);
                if (hr < 0 || multithread == IntPtr.Zero)
                {
                    return false;
                }

                nint setMultithreadProtectedAddress = GetVTableFunctionAddress(multithread, VTABLE_ID3D11Multithread_SetMultithreadProtected);
                if (setMultithreadProtectedAddress == IntPtr.Zero)
                {
                    return false;
                }

                var setMultithreadProtected = Marshal.GetDelegateForFunctionPointer<SetMultithreadProtectedDelegate>(setMultithreadProtectedAddress);
                setMultithreadProtected(multithread, 1);
                return true;
            }
            finally
            {
                ReleaseComObject(multithread);
            }
        }

        private bool EnsureBackendContext(nint swapChain)
        {
            nint device = IntPtr.Zero;
            nint newContext = IntPtr.Zero;

            try
            {
                if (!TryGetDeviceFromSwapChain(swapChain, out device))
                {
                    return false;
                }

                newContext = GetImmediateContext(device);
                if (newContext == IntPtr.Zero)
                {
                    return false;
                }

                if (_deviceContext != IntPtr.Zero && newContext == _deviceContext)
                {
                    ReleaseComObject(newContext);
                    return true;
                }

                ReleaseComObject(_deviceContext);
                _deviceContext = IntPtr.Zero;

                try
                {
                    ImGuiImplD3D11.SetCurrentContext(Context);
                    ImGuiImplD3D11.Shutdown();
                }
                catch
                {
                }

                ImGuiImplD3D11.SetCurrentContext(Context);
                if (!ImGuiImplD3D11.Init((ID3D11Device*)device, (ID3D11DeviceContext*)newContext))
                {
                    return false;
                }

                _deviceContext = newContext;
                newContext = IntPtr.Zero;
                TryEnableDeviceContextMultithreadProtection(_deviceContext);
                ReleaseRenderTarget();
                return true;
            }
            finally
            {
                ReleaseComObject(newContext);
                ReleaseComObject(device);
            }
        }

        private void ReleaseRenderTarget()
        {
            ReleaseComObject(_renderTargetView);
            _renderTargetView = IntPtr.Zero;
            _swapChainForRenderTarget = IntPtr.Zero;
        }

        private bool EnsureRenderTarget(nint swapChain)
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

            nint backBuffer = IntPtr.Zero;
            nint device = IntPtr.Zero;

            try
            {
                if (!TryGetSwapChainBuffer(swapChain, out backBuffer))
                {
                    return false;
                }

                if (!TryGetDeviceFromSwapChain(swapChain, out device))
                {
                    return false;
                }

                if (!TryCreateRenderTargetView(device, backBuffer, out _renderTargetView))
                {
                    return false;
                }

                _swapChainForRenderTarget = swapChain;
                return true;
            }
            finally
            {
                ReleaseComObject(backBuffer);
                ReleaseComObject(device);
            }
        }

        private bool TryGetSwapChainBuffer(nint swapChain, out nint buffer)
        {
            buffer = IntPtr.Zero;
            nint getBufferAddress = GetVTableFunctionAddress(swapChain, VTABLE_IDXGISwapChain_GetBuffer);
            if (getBufferAddress == IntPtr.Zero)
            {
                return false;
            }

            var getBuffer = Marshal.GetDelegateForFunctionPointer<GetBufferDelegate>(getBufferAddress);
            Guid iid = IID_ID3D11Texture2D;
            return getBuffer(swapChain, 0, ref iid, out buffer) >= 0 && buffer != IntPtr.Zero;
        }

        private static bool TryCreateRenderTargetView(nint device, nint backBuffer, out nint renderTargetView)
        {
            renderTargetView = IntPtr.Zero;
            nint createRenderTargetViewAddress = GetVTableFunctionAddress(device, VTABLE_ID3D11Device_CreateRenderTargetView);
            if (createRenderTargetViewAddress == IntPtr.Zero)
            {
                return false;
            }

            var createRenderTargetView = Marshal.GetDelegateForFunctionPointer<CreateRenderTargetViewDelegate>(createRenderTargetViewAddress);
            return createRenderTargetView(device, backBuffer, IntPtr.Zero, out renderTargetView) >= 0 && renderTargetView != IntPtr.Zero;
        }

        private unsafe bool TryBackupOutputMergerState(nint* renderTargetViews, uint numViews, out nint depthStencilView)
        {
            depthStencilView = IntPtr.Zero;
            if (_deviceContext == IntPtr.Zero)
            {
                return false;
            }

            nint omGetRenderTargetsAddress = GetVTableFunctionAddress(_deviceContext, VTABLE_ID3D11DeviceContext_OMGetRenderTargets);
            if (omGetRenderTargetsAddress == IntPtr.Zero)
            {
                return false;
            }

            var omGetRenderTargets = Marshal.GetDelegateForFunctionPointer<OMGetRenderTargetsDelegate>(omGetRenderTargetsAddress);
            omGetRenderTargets(_deviceContext, numViews, renderTargetViews, out depthStencilView);
            return true;
        }

        private unsafe bool BindOverlayRenderTarget()
        {
            if (_deviceContext == IntPtr.Zero || _renderTargetView == IntPtr.Zero)
            {
                return false;
            }

            nint omSetRenderTargetsAddress = GetVTableFunctionAddress(_deviceContext, VTABLE_ID3D11DeviceContext_OMSetRenderTargets);
            if (omSetRenderTargetsAddress == IntPtr.Zero)
            {
                return false;
            }

            var omSetRenderTargets = Marshal.GetDelegateForFunctionPointer<OMSetRenderTargetsDelegate>(omSetRenderTargetsAddress);
            nint renderTargetView = _renderTargetView;
            omSetRenderTargets(_deviceContext, 1, &renderTargetView, IntPtr.Zero);
            return true;
        }

        private unsafe void RestoreOutputMergerState(nint* renderTargetViews, uint numViews, nint depthStencilView)
        {
            if (_deviceContext == IntPtr.Zero)
            {
                return;
            }

            nint omSetRenderTargetsAddress = GetVTableFunctionAddress(_deviceContext, VTABLE_ID3D11DeviceContext_OMSetRenderTargets);
            if (omSetRenderTargetsAddress == IntPtr.Zero)
            {
                return;
            }

            var omSetRenderTargets = Marshal.GetDelegateForFunctionPointer<OMSetRenderTargetsDelegate>(omSetRenderTargetsAddress);
            omSetRenderTargets(_deviceContext, numViews, renderTargetViews, depthStencilView);
        }
    }
}
