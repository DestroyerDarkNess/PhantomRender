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
        private const int VTABLE_IDXGI_SWAPCHAIN_GET_BUFFER = 9;
        private const int VTABLE_ID3D11DEVICE_CREATE_RENDER_TARGET_VIEW = 9;
        private const int VTABLE_ID3D11DEVICE_GET_IMMEDIATE_CONTEXT = 40;
        private const int VTABLE_ID3D11DEVICECONTEXT_OM_GET_RENDER_TARGETS = 89;
        private const int VTABLE_ID3D11DEVICECONTEXT_OM_SET_RENDER_TARGETS = 33;

        private static readonly Guid IID_ID3D11Device = new Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140");
        private static readonly Guid IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetImmediateContextDelegate(nint device, out nint immediateContext);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetBufferDelegate(nint swapChain, uint bufferIndex, ref Guid riid, out nint surface);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateRenderTargetViewDelegate(nint device, nint resource, nint desc, out nint renderTargetView);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate void OMGetRenderTargetsDelegate(nint deviceContext, uint numViews, nint* renderTargetViews, out nint depthStencilView);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate void OMSetRenderTargetsDelegate(nint deviceContext, uint numViews, nint* renderTargetViews, nint depthStencilView);

        private nint _device;
        private nint _deviceContext;
        private nint _renderTargetView;
        private nint _renderTargetSwapChain;
        private nint _getBufferSwapChain;
        private readonly object _sync = new object();
        private readonly Action _backendNewFrameAction;
        private readonly Action _backendRenderAction;
        private GetBufferDelegate _getBuffer;
        private CreateRenderTargetViewDelegate _createRenderTargetView;
        private OMGetRenderTargetsDelegate _omGetRenderTargets;
        private OMSetRenderTargetsDelegate _omSetRenderTargets;

        public DirectX11Renderer()
            : base(GraphicsApi.DirectX11)
        {
            _backendNewFrameAction = BackendNewFrame;
            _backendRenderAction = BackendRender;
        }

        public override bool Initialize(nint device, nint windowHandle)
        {
            lock (_sync)
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

                    if (!CacheDeviceFunctions(device, immediateContext))
                    {
                        ResetCachedDelegates();
                        ReleaseComObject(immediateContext);
                        immediateContext = IntPtr.Zero;
                        return false;
                    }

                    InitializeImGui(windowHandle);

                    ImGuiImplD3D11.SetCurrentContext(Context);
                    if (!ImGuiImplD3D11.Init((ID3D11Device*)device, (ID3D11DeviceContext*)immediateContext))
                    {
                        ShutdownImGui();
                        ResetCachedDelegates();
                        ReleaseComObject(immediateContext);
                        immediateContext = IntPtr.Zero;
                        return false;
                    }

                    Marshal.AddRef(device);
                    _device = device;
                    _deviceContext = immediateContext;
                    immediateContext = IntPtr.Zero;
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
                    ResetCachedDelegates();
                    ReleaseDeviceResources();
                    return false;
                }
            }
        }

        public override void NewFrame()
        {
            lock (_sync)
            {
                if (!IsInitialized || FrameStarted || _deviceContext == IntPtr.Zero || Context.IsNull)
                {
                    return;
                }

                try
                {
                    BeginFrameCore(_backendNewFrameAction);
                    FrameStarted = true;
                }
                catch (Exception ex)
                {
                    ReportRuntimeError("DirectX11.NewFrame", ex);
                }
            }
        }

        public override unsafe void Render(nint swapChain)
        {
            lock (_sync)
            {
                if (!IsInitialized || !FrameStarted || _deviceContext == IntPtr.Zero || Context.IsNull)
                {
                    return;
                }

                if (swapChain != IntPtr.Zero)
                {
                    if (SwapChainHandle == IntPtr.Zero)
                    {
                        SwapChainHandle = swapChain;
                    }
                    else if (swapChain != SwapChainHandle)
                    {
                        FrameStarted = false;
                        return;
                    }
                }

                try
                {
                    RenderFrameCore(_backendRenderAction);
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
        }

        public override void OnBeforeResizeBuffers(nint swapChain)
        {
            lock (_sync)
            {
                base.OnBeforeResizeBuffers(swapChain);
                ReleaseRenderTarget();
            }
        }

        public override void OnAfterResizeBuffers(nint swapChain)
        {
            lock (_sync)
            {
                base.OnAfterResizeBuffers(swapChain);
                if (swapChain != IntPtr.Zero)
                {
                    SwapChainHandle = swapChain;
                }

                ReleaseRenderTarget();
            }
        }

        public override void OnLostDevice()
        {
            lock (_sync)
            {
                ReleaseRenderTarget();
            }
        }

        public override void OnResetDevice()
        {
        }

        public override void Dispose()
        {
            lock (_sync)
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
                    ResetCachedDelegates();
                    ShutdownImGui();
                    IsInitialized = false;
                    FrameStarted = false;
                    SwapChainHandle = IntPtr.Zero;
                }
            }
        }

        protected override bool TryGetDeviceFromSwapChain(nint swapChain, out nint device)
        {
            return TryGetSwapChainDevice(swapChain, IID_ID3D11Device, out device);
        }

        private static nint GetImmediateContext(nint device)
        {
            GetImmediateContextDelegate getImmediateContext = GetVTableDelegate<GetImmediateContextDelegate>(device, VTABLE_ID3D11DEVICE_GET_IMMEDIATE_CONTEXT);
            if (getImmediateContext == null)
            {
                return IntPtr.Zero;
            }

            getImmediateContext(device, out nint immediateContext);
            return immediateContext;
        }

        private bool EnsureRenderTarget(nint swapChain)
        {
            if (swapChain == IntPtr.Zero || _device == IntPtr.Zero)
            {
                return false;
            }

            if (_renderTargetView != IntPtr.Zero && _renderTargetSwapChain == swapChain)
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
                return true;
            }
            finally
            {
                ReleaseComObject(backBuffer);
            }
        }

        private bool TryGetSwapChainBuffer(nint swapChain, out nint buffer)
        {
            buffer = IntPtr.Zero;
            if (!CacheSwapChainFunctions(swapChain) || _getBuffer == null)
            {
                return false;
            }

            Guid iid = IID_ID3D11Texture2D;
            return _getBuffer(swapChain, 0, ref iid, out buffer) >= 0 && buffer != IntPtr.Zero;
        }

        private bool TryCreateRenderTargetView(nint device, nint resource, out nint renderTargetView)
        {
            renderTargetView = IntPtr.Zero;
            if (device == IntPtr.Zero || resource == IntPtr.Zero || _createRenderTargetView == null)
            {
                return false;
            }

            return _createRenderTargetView(device, resource, IntPtr.Zero, out renderTargetView) >= 0 && renderTargetView != IntPtr.Zero;
        }

        private unsafe bool TryBackupOutputMergerState(out nint renderTargetView, out nint depthStencilView)
        {
            renderTargetView = IntPtr.Zero;
            depthStencilView = IntPtr.Zero;
            if (_deviceContext == IntPtr.Zero || _omGetRenderTargets == null)
            {
                return false;
            }

            nint currentRenderTargetView = IntPtr.Zero;
            _omGetRenderTargets(_deviceContext, 1, &currentRenderTargetView, out depthStencilView);
            renderTargetView = currentRenderTargetView;
            return true;
        }

        private unsafe bool BindRenderTarget(nint renderTargetView)
        {
            if (_deviceContext == IntPtr.Zero || renderTargetView == IntPtr.Zero || _omSetRenderTargets == null)
            {
                return false;
            }

            nint currentRenderTargetView = renderTargetView;
            _omSetRenderTargets(_deviceContext, 1, &currentRenderTargetView, IntPtr.Zero);
            return true;
        }

        private unsafe void RestoreOutputMergerState(nint renderTargetView, nint depthStencilView)
        {
            if (_deviceContext == IntPtr.Zero || _omSetRenderTargets == null)
            {
                return;
            }

            nint currentRenderTargetView = renderTargetView;
            _omSetRenderTargets(_deviceContext, 1, &currentRenderTargetView, depthStencilView);
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

        private void BackendNewFrame()
        {
            ImGuiImplD3D11.SetCurrentContext(Context);
            ImGuiImplD3D11.NewFrame();
            ImGuiImplWin32.SetCurrentContext(Context);
            ImGuiImplWin32.NewFrame();
        }

        private void BackendRender()
        {
            if (SwapChainHandle == IntPtr.Zero)
            {
                return;
            }

            if (!EnsureRenderTarget(SwapChainHandle))
            {
                return;
            }

            nint previousRenderTarget = IntPtr.Zero;
            nint previousDepthStencil = IntPtr.Zero;

            try
            {
                if (!TryBackupOutputMergerState(out previousRenderTarget, out previousDepthStencil))
                {
                    return;
                }

                if (!BindRenderTarget(_renderTargetView))
                {
                    return;
                }

                ImGuiImplD3D11.SetCurrentContext(Context);
                ImGuiImplD3D11.RenderDrawData(HexaImGui.GetDrawData());
            }
            finally
            {
                RestoreOutputMergerState(previousRenderTarget, previousDepthStencil);
                ReleaseComObject(previousRenderTarget);
                ReleaseComObject(previousDepthStencil);
            }
        }

        private bool CacheDeviceFunctions(nint device, nint deviceContext)
        {
            _createRenderTargetView = GetVTableDelegate<CreateRenderTargetViewDelegate>(device, VTABLE_ID3D11DEVICE_CREATE_RENDER_TARGET_VIEW);
            _omGetRenderTargets = GetVTableDelegate<OMGetRenderTargetsDelegate>(deviceContext, VTABLE_ID3D11DEVICECONTEXT_OM_GET_RENDER_TARGETS);
            _omSetRenderTargets = GetVTableDelegate<OMSetRenderTargetsDelegate>(deviceContext, VTABLE_ID3D11DEVICECONTEXT_OM_SET_RENDER_TARGETS);

            return _createRenderTargetView != null &&
                   _omGetRenderTargets != null &&
                   _omSetRenderTargets != null;
        }

        private bool CacheSwapChainFunctions(nint swapChain)
        {
            if (swapChain == IntPtr.Zero)
            {
                return false;
            }

            if (_getBuffer != null && _getBufferSwapChain == swapChain)
            {
                return true;
            }

            _getBuffer = GetVTableDelegate<GetBufferDelegate>(swapChain, VTABLE_IDXGI_SWAPCHAIN_GET_BUFFER);
            _getBufferSwapChain = _getBuffer != null ? swapChain : IntPtr.Zero;
            return _getBuffer != null;
        }

        private void ResetCachedDelegates()
        {
            _getBuffer = null;
            _createRenderTargetView = null;
            _omGetRenderTargets = null;
            _omSetRenderTargets = null;
            _getBufferSwapChain = IntPtr.Zero;
        }

        private static TDelegate GetVTableDelegate<TDelegate>(nint instance, int functionIndex)
            where TDelegate : class
        {
            nint address = GetVTableFunctionAddress(instance, functionIndex);
            if (address == IntPtr.Zero)
            {
                return null;
            }

#if NET5_0_OR_GREATER
            return Marshal.GetDelegateForFunctionPointer<TDelegate>(address);
#else
            return Marshal.GetDelegateForFunctionPointer(address, typeof(TDelegate)) as TDelegate;
#endif
        }
    }
}
