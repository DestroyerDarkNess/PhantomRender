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
        private const int VTABLE_IUNKNOWN_QUERY_INTERFACE = 0;
        private const int VTABLE_IDXGI_SWAPCHAIN_GET_BUFFER = 9;
        private const int VTABLE_ID3D11DEVICE_CREATE_RENDER_TARGET_VIEW = 9;
        private const int VTABLE_ID3D11DEVICE_GET_IMMEDIATE_CONTEXT = 40;
        private const int VTABLE_ID3D11DEVICECONTEXT_OM_SET_RENDER_TARGETS = 33;
        private const int VTABLE_ID3D11DEVICECONTEXT_OM_GET_RENDER_TARGETS = 89;
        private const int VTABLE_ID3D11MULTITHREAD_SET_MULTITHREAD_PROTECTED = 5;

        private static readonly Guid IID_ID3D11Device = new Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140");
        private static readonly Guid IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        private static readonly Guid IID_ID3D11Multithread = new Guid("9b7e4e00-342c-4106-a19f-4f2704f689f0");

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetImmediateContextDelegate(nint device, out nint immediateContext);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetBufferDelegate(nint swapChain, uint bufferIndex, ref Guid riid, out nint surface);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateRenderTargetViewDelegate(nint device, nint resource, nint desc, out nint renderTargetView);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryInterfaceDelegate(nint instance, ref Guid riid, out nint objectPointer);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate void OMGetRenderTargetsDelegate(nint deviceContext, uint numViews, nint* renderTargetViews, out nint depthStencilView);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate void OMSetRenderTargetsDelegate(nint deviceContext, uint numViews, nint* renderTargetViews, nint depthStencilView);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetMultithreadProtectedDelegate(nint multithread, int multithreadProtected);

        private nint _device;
        private nint _deviceContext;
        private nint _renderTargetView;
        private nint _renderTargetSwapChain;
        private bool _renderTargetDirty;

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

            nint immediateContext = IntPtr.Zero;
            try
            {
                RaiseRendererInitializing(device, windowHandle);

                immediateContext = GetImmediateContext(device);
                if (immediateContext == IntPtr.Zero)
                {
                    return false;
                }

                InitializeImGui(windowHandle);

                ImGuiImplD3D11.SetCurrentContext(Context);
                if (!ImGuiImplD3D11.Init((ID3D11Device*)device, (ID3D11DeviceContext*)immediateContext))
                {
                    ShutdownImGui();
                    return false;
                }

                Marshal.AddRef(device);
                _device = device;
                _deviceContext = immediateContext;
                immediateContext = IntPtr.Zero;

                TryEnableMultithreadProtection(_deviceContext);

                _renderTargetDirty = true;
                IsInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                ReportRuntimeError("DirectX11.Initialize", ex);

                try
                {
                    if (!Context.IsNull)
                    {
                        ImGuiImplD3D11.SetCurrentContext(Context);
                        ImGuiImplD3D11.Shutdown();
                    }
                }
                catch
                {
                }

                try
                {
                    ShutdownImGui();
                }
                catch
                {
                }

                ReleaseComObject(immediateContext);
                ReleaseDeviceResources();
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

                    if (!EnsureDeviceBinding(SwapChainHandle))
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
            _renderTargetDirty = true;
        }

        public override void OnAfterResizeBuffers(nint swapChain)
        {
            base.OnAfterResizeBuffers(swapChain);
            SwapChainHandle = swapChain != IntPtr.Zero ? swapChain : SwapChainHandle;
            _renderTargetDirty = true;
        }

        public override void OnLostDevice()
        {
            ReleaseRenderTarget();
            _renderTargetDirty = true;
        }

        public override void OnResetDevice()
        {
            _renderTargetDirty = true;
        }

        public override void Dispose()
        {
            try
            {
                ReleaseRenderTarget();

                if (IsInitialized)
                {
                    if (!Context.IsNull)
                    {
                        ImGuiImplD3D11.SetCurrentContext(Context);
                    }

                    ImGuiImplD3D11.Shutdown();
                }
            }
            catch (Exception ex)
            {
                ReportRuntimeError("DirectX11.Dispose", ex);
            }
            finally
            {
                ReleaseDeviceResources();
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
            nint address = GetVTableFunctionAddress(device, VTABLE_ID3D11DEVICE_GET_IMMEDIATE_CONTEXT);
            if (address == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var getImmediateContext = Marshal.GetDelegateForFunctionPointer<GetImmediateContextDelegate>(address);
            getImmediateContext(device, out nint immediateContext);
            return immediateContext;
        }

        private static bool TryEnableMultithreadProtection(nint deviceContext)
        {
            if (deviceContext == IntPtr.Zero)
            {
                return false;
            }

            nint multithread = IntPtr.Zero;
            try
            {
                nint queryInterfaceAddress = GetVTableFunctionAddress(deviceContext, VTABLE_IUNKNOWN_QUERY_INTERFACE);
                if (queryInterfaceAddress == IntPtr.Zero)
                {
                    return false;
                }

                var queryInterface = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(queryInterfaceAddress);
                Guid iid = IID_ID3D11Multithread;
                if (queryInterface(deviceContext, ref iid, out multithread) < 0 || multithread == IntPtr.Zero)
                {
                    return false;
                }

                nint setMultithreadProtectedAddress = GetVTableFunctionAddress(multithread, VTABLE_ID3D11MULTITHREAD_SET_MULTITHREAD_PROTECTED);
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

        private bool EnsureDeviceBinding(nint swapChain)
        {
            nint swapChainDevice = IntPtr.Zero;
            nint newContext = IntPtr.Zero;

            try
            {
                if (!TryGetDeviceFromSwapChain(swapChain, out swapChainDevice) || swapChainDevice == IntPtr.Zero)
                {
                    return false;
                }

                if (swapChainDevice == _device)
                {
                    return true;
                }

                newContext = GetImmediateContext(swapChainDevice);
                if (newContext == IntPtr.Zero)
                {
                    return false;
                }

                ImGuiImplD3D11.SetCurrentContext(Context);
                ImGuiImplD3D11.Shutdown();

                if (!ImGuiImplD3D11.Init((ID3D11Device*)swapChainDevice, (ID3D11DeviceContext*)newContext))
                {
                    return false;
                }

                ReleaseRenderTarget();
                ReleaseComObject(_deviceContext);
                ReleaseComObject(_device);

                _device = swapChainDevice;
                _deviceContext = newContext;
                swapChainDevice = IntPtr.Zero;
                newContext = IntPtr.Zero;

                TryEnableMultithreadProtection(_deviceContext);
                _renderTargetDirty = true;
                return true;
            }
            catch (Exception ex)
            {
                ReportRuntimeError("DirectX11.EnsureDeviceBinding", ex);
                return false;
            }
            finally
            {
                ReleaseComObject(newContext);
                ReleaseComObject(swapChainDevice);
            }
        }

        private bool EnsureRenderTarget(nint swapChain)
        {
            if (swapChain == IntPtr.Zero || _device == IntPtr.Zero)
            {
                return false;
            }

            if (!_renderTargetDirty && _renderTargetView != IntPtr.Zero && _renderTargetSwapChain == swapChain)
            {
                return true;
            }

            ReleaseRenderTarget();

            nint backBuffer = IntPtr.Zero;
            try
            {
                if (!TryGetSwapChainBuffer(swapChain, out backBuffer))
                {
                    return false;
                }

                if (!TryCreateRenderTargetView(_device, backBuffer, out _renderTargetView))
                {
                    return false;
                }

                _renderTargetSwapChain = swapChain;
                _renderTargetDirty = false;
                return true;
            }
            catch (Exception ex)
            {
                ReportRuntimeError("DirectX11.EnsureRenderTarget", ex);
                ReleaseRenderTarget();
                return false;
            }
            finally
            {
                ReleaseComObject(backBuffer);
            }
        }

        private bool TryGetSwapChainBuffer(nint swapChain, out nint buffer)
        {
            buffer = IntPtr.Zero;

            nint getBufferAddress = GetVTableFunctionAddress(swapChain, VTABLE_IDXGI_SWAPCHAIN_GET_BUFFER);
            if (getBufferAddress == IntPtr.Zero)
            {
                return false;
            }

            var getBuffer = Marshal.GetDelegateForFunctionPointer<GetBufferDelegate>(getBufferAddress);
            Guid iid = IID_ID3D11Texture2D;
            return getBuffer(swapChain, 0, ref iid, out buffer) >= 0 && buffer != IntPtr.Zero;
        }

        private static bool TryCreateRenderTargetView(nint device, nint resource, out nint renderTargetView)
        {
            renderTargetView = IntPtr.Zero;
            if (device == IntPtr.Zero || resource == IntPtr.Zero)
            {
                return false;
            }

            nint createRenderTargetViewAddress = GetVTableFunctionAddress(device, VTABLE_ID3D11DEVICE_CREATE_RENDER_TARGET_VIEW);
            if (createRenderTargetViewAddress == IntPtr.Zero)
            {
                return false;
            }

            var createRenderTargetView = Marshal.GetDelegateForFunctionPointer<CreateRenderTargetViewDelegate>(createRenderTargetViewAddress);
            return createRenderTargetView(device, resource, IntPtr.Zero, out renderTargetView) >= 0 && renderTargetView != IntPtr.Zero;
        }

        private unsafe bool TryBackupOutputMergerState(nint* renderTargetViews, uint numViews, out nint depthStencilView)
        {
            depthStencilView = IntPtr.Zero;
            if (_deviceContext == IntPtr.Zero)
            {
                return false;
            }

            nint omGetRenderTargetsAddress = GetVTableFunctionAddress(_deviceContext, VTABLE_ID3D11DEVICECONTEXT_OM_GET_RENDER_TARGETS);
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

            nint omSetRenderTargetsAddress = GetVTableFunctionAddress(_deviceContext, VTABLE_ID3D11DEVICECONTEXT_OM_SET_RENDER_TARGETS);
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

            nint omSetRenderTargetsAddress = GetVTableFunctionAddress(_deviceContext, VTABLE_ID3D11DEVICECONTEXT_OM_SET_RENDER_TARGETS);
            if (omSetRenderTargetsAddress == IntPtr.Zero)
            {
                return;
            }

            var omSetRenderTargets = Marshal.GetDelegateForFunctionPointer<OMSetRenderTargetsDelegate>(omSetRenderTargetsAddress);
            omSetRenderTargets(_deviceContext, numViews, renderTargetViews, depthStencilView);
        }

        private void ReleaseRenderTarget()
        {
            ReleaseComObject(_renderTargetView);
            _renderTargetView = IntPtr.Zero;
            _renderTargetSwapChain = IntPtr.Zero;
        }

        private void ReleaseDeviceResources()
        {
            ReleaseComObject(_deviceContext);
            _deviceContext = IntPtr.Zero;

            ReleaseComObject(_device);
            _device = IntPtr.Zero;
        }
    }
}
