using System;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui.Backends.D3D10;
using Hexa.NET.ImGui.Backends.Win32;
using PhantomRender.Core;
using HexaImGui = Hexa.NET.ImGui.ImGui;

namespace PhantomRender.ImGui.Core.Renderers
{
    public sealed unsafe class DirectX10Renderer : DxgiRendererBase
    {
        private const int VTABLE_IDXGISwapChain_GetBuffer = 9;
        private const int VTABLE_ID3D10Device_OMSetRenderTargets = 24;
        private const int VTABLE_ID3D10Device_OMGetRenderTargets = 56;
        private const int VTABLE_ID3D10Device_CreateRenderTargetView = 76;

        private static readonly Guid IID_ID3D10Device = new Guid("9b7e4c0f-342c-4106-a19f-4f2704f689f0");
        private static readonly Guid IID_ID3D10Texture2D = new Guid("9b7e4c04-342c-4106-a19f-4f2704f689f0");

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetBufferDelegate(nint swapChain, uint bufferIndex, ref Guid riid, out nint ppSurface);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateRenderTargetViewDelegate(nint device, nint resource, nint desc, out nint renderTargetView);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate void OMGetRenderTargetsDelegate(nint device, uint numViews, nint* renderTargetViews, out nint depthStencilView);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate void OMSetRenderTargetsDelegate(nint device, uint numViews, nint* renderTargetViews, nint depthStencilView);

        private ID3D10DevicePtr _device;
        private nint _renderTargetView;
        private nint _swapChainForRenderTarget;
        private nint _getBufferSwapChain;
        private readonly object _sync = new object();
        private readonly Action _backendNewFrameAction;
        private readonly Action _backendRenderAction;
        private GetBufferDelegate _getBuffer;
        private CreateRenderTargetViewDelegate _createRenderTargetView;
        private OMGetRenderTargetsDelegate _omGetRenderTargets;
        private OMSetRenderTargetsDelegate _omSetRenderTargets;

        public DirectX10Renderer()
            : base(GraphicsApi.DirectX10)
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

                try
                {
                    RaiseRendererInitializing(device, windowHandle);
                    _device = new ID3D10DevicePtr((ID3D10Device*)device);

                    if (!CacheDeviceFunctions())
                    {
                        _device = default;
                        ResetCachedDelegates();
                        return false;
                    }

                    InitializeImGui(windowHandle);

                    ImGuiImplD3D10.SetCurrentContext(Context);
                    if (!ImGuiImplD3D10.Init(_device))
                    {
                        _device = default;
                        ResetCachedDelegates();
                        ShutdownImGui();
                        return false;
                    }

                    IsInitialized = true;
                    return true;
                }
                catch (Exception ex)
                {
                    ReportRuntimeError("DirectX10.Initialize", ex);
                    _device = default;
                    ResetCachedDelegates();

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
        }

        public override void NewFrame()
        {
            lock (_sync)
            {
                if (!IsInitialized || FrameStarted || _device.Handle == null || Context.IsNull)
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
                    ReportRuntimeError("DirectX10.NewFrame", ex);
                }
            }
        }

        public override unsafe void Render(nint swapChain)
        {
            lock (_sync)
            {
                if (!IsInitialized || !FrameStarted || _device.Handle == null || Context.IsNull)
                {
                    return;
                }

                if (swapChain != IntPtr.Zero)
                {
                    SwapChainHandle = swapChain;
                }

                try
                {
                    RenderFrameCore(_backendRenderAction);
                }
                catch (Exception ex)
                {
                    ReportRuntimeError("DirectX10.Render", ex);
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

                if (!IsInitialized)
                {
                    return;
                }

                ImGuiImplD3D10.SetCurrentContext(Context);
                ImGuiImplD3D10.InvalidateDeviceObjects();
            }
        }

        public override void OnAfterResizeBuffers(nint swapChain)
        {
            lock (_sync)
            {
                base.OnAfterResizeBuffers(swapChain);

                if (!IsInitialized)
                {
                    return;
                }

                ImGuiImplD3D10.SetCurrentContext(Context);
                ImGuiImplD3D10.CreateDeviceObjects();
            }
        }

        public override void OnLostDevice()
        {
            lock (_sync)
            {
                OnBeforeResizeBuffers(SwapChainHandle);
            }
        }

        public override void OnResetDevice()
        {
            lock (_sync)
            {
                OnAfterResizeBuffers(SwapChainHandle);
            }
        }

        public override void Dispose()
        {
            lock (_sync)
            {
                if (!IsInitialized)
                {
                    return;
                }

                try
                {
                    ReleaseRenderTarget();

                    if (!Context.IsNull)
                    {
                        ImGuiImplD3D10.SetCurrentContext(Context);
                    }

                    ImGuiImplD3D10.Shutdown();
                }
                catch (Exception ex)
                {
                    ReportRuntimeError("DirectX10.Dispose", ex);
                }
                finally
                {
                    ShutdownImGui();
                    _device = default;
                    ResetCachedDelegates();
                    IsInitialized = false;
                    FrameStarted = false;
                    SwapChainHandle = IntPtr.Zero;
                }
            }
        }

        protected override bool TryGetDeviceFromSwapChain(nint swapChain, out nint device)
        {
            return TryGetSwapChainDevice(swapChain, IID_ID3D10Device, out device);
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
            try
            {
                if (!TryGetSwapChainBuffer(swapChain, out backBuffer))
                {
                    return false;
                }

                if (!TryCreateRenderTargetView(backBuffer, out _renderTargetView))
                {
                    return false;
                }

                _swapChainForRenderTarget = swapChain;
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

            Guid iid = IID_ID3D10Texture2D;
            return _getBuffer(swapChain, 0, ref iid, out buffer) >= 0 && buffer != IntPtr.Zero;
        }

        private bool TryCreateRenderTargetView(nint backBuffer, out nint renderTargetView)
        {
            renderTargetView = IntPtr.Zero;
            if (_device.Handle == null || backBuffer == IntPtr.Zero || _createRenderTargetView == null)
            {
                return false;
            }

            return _createRenderTargetView((nint)_device.Handle, backBuffer, IntPtr.Zero, out renderTargetView) >= 0 && renderTargetView != IntPtr.Zero;
        }

        private unsafe bool TryBackupOutputMergerState(nint* renderTargetViews, uint numViews, out nint depthStencilView)
        {
            depthStencilView = IntPtr.Zero;
            if (_device.Handle == null || _omGetRenderTargets == null)
            {
                return false;
            }

            _omGetRenderTargets((nint)_device.Handle, numViews, renderTargetViews, out depthStencilView);
            return true;
        }

        private unsafe bool BindOverlayRenderTarget()
        {
            if (_device.Handle == null || _renderTargetView == IntPtr.Zero || _omSetRenderTargets == null)
            {
                return false;
            }

            nint renderTargetView = _renderTargetView;
            _omSetRenderTargets((nint)_device.Handle, 1, &renderTargetView, IntPtr.Zero);
            return true;
        }

        private unsafe void RestoreOutputMergerState(nint* renderTargetViews, uint numViews, nint depthStencilView)
        {
            if (_device.Handle == null || _omSetRenderTargets == null)
            {
                return;
            }

            _omSetRenderTargets((nint)_device.Handle, numViews, renderTargetViews, depthStencilView);
        }

        private void BackendNewFrame()
        {
            ImGuiImplD3D10.SetCurrentContext(Context);
            ImGuiImplD3D10.NewFrame();
            ImGuiImplWin32.SetCurrentContext(Context);
            ImGuiImplWin32.NewFrame();
        }

        private unsafe void BackendRender()
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
            bool restoreState = false;

            try
            {
                restoreState = TryBackupOutputMergerState(&previousRenderTarget, 1, out previousDepthStencil);
                if (!BindOverlayRenderTarget())
                {
                    return;
                }

                ImGuiImplD3D10.SetCurrentContext(Context);
                ImGuiImplD3D10.RenderDrawData(HexaImGui.GetDrawData());
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
        }

        private bool CacheDeviceFunctions()
        {
            if (_device.Handle == null)
            {
                return false;
            }

            _createRenderTargetView = GetVTableDelegate<CreateRenderTargetViewDelegate>((nint)_device.Handle, VTABLE_ID3D10Device_CreateRenderTargetView);
            _omGetRenderTargets = GetVTableDelegate<OMGetRenderTargetsDelegate>((nint)_device.Handle, VTABLE_ID3D10Device_OMGetRenderTargets);
            _omSetRenderTargets = GetVTableDelegate<OMSetRenderTargetsDelegate>((nint)_device.Handle, VTABLE_ID3D10Device_OMSetRenderTargets);

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

            _getBuffer = GetVTableDelegate<GetBufferDelegate>(swapChain, VTABLE_IDXGISwapChain_GetBuffer);
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
