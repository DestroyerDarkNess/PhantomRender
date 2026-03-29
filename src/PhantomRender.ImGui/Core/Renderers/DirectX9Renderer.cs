using System;
using Hexa.NET.ImGui.Backends.D3D9;
using Hexa.NET.ImGui.Backends.Win32;
using PhantomRender.Core;
using PhantomRender.ImGui.Core;
using HexaImGui = Hexa.NET.ImGui.ImGui;

namespace PhantomRender.ImGui.Core.Renderers
{
    public enum DirectX9InitializationEndpoint
    {
        Present = 0,
        EndScene = 1,
    }

    public sealed unsafe class DirectX9Renderer : RendererBase
    {
        private readonly object _sync = new object();
        private readonly Action _backendNewFrameAction;
        private readonly Action _backendRenderAction;
        private bool _frameStarted;
        private IDirect3DDevice9Ptr _device;

        public DirectX9Renderer()
            : base(GraphicsApi.DirectX9)
        {
            _backendNewFrameAction = BackendNewFrame;
            _backendRenderAction = BackendRender;
        }

        public DirectX9InitializationEndpoint InitializationEndpoint { get; set; } = DirectX9InitializationEndpoint.Present;

        public override bool Initialize(nint device, nint windowHandle)
        {
            lock (_sync)
            {
                if (IsInitialized)
                {
                    return true;
                }

                if (device == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    RaiseRendererInitializing(device, windowHandle);
                    InitializeImGui(windowHandle);

                    _device = new IDirect3DDevice9Ptr((IDirect3DDevice9*)device);

                    ImGuiImplD3D9.SetCurrentContext(Context);
                    if (!ImGuiImplD3D9.Init(_device))
                    {
                        _device = default;
                        ShutdownImGui();
                        return false;
                    }

                    IsInitialized = true;
                    return true;
                }
                catch (Exception ex)
                {
                    ReportRuntimeError("DirectX9.Initialize", ex);
                    _device = default;

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
                if (!IsInitialized || _frameStarted || Context.IsNull)
                {
                    return;
                }

                try
                {
                    BeginFrameCore(_backendNewFrameAction);
                    _frameStarted = true;
                }
                catch (Exception ex)
                {
                    ReportRuntimeError("DirectX9.NewFrame", ex);
                }
            }
        }

        public override void Render()
        {
            lock (_sync)
            {
                if (!IsInitialized || !_frameStarted || Context.IsNull)
                {
                    return;
                }

                try
                {
                    RenderFrameCore(_backendRenderAction);
                }
                catch (Exception ex)
                {
                    ReportRuntimeError("DirectX9.Render", ex);
                }
                finally
                {
                    _frameStarted = false;
                }
            }
        }

        public override void OnLostDevice()
        {
            lock (_sync)
            {
                if (!IsInitialized)
                {
                    return;
                }

                _frameStarted = false;
                ImGuiImplD3D9.SetCurrentContext(Context);
                ImGuiImplD3D9.InvalidateDeviceObjects();
            }
        }

        public override void OnResetDevice()
        {
            lock (_sync)
            {
                if (!IsInitialized)
                {
                    return;
                }

                _frameStarted = false;
                ImGuiImplD3D9.SetCurrentContext(Context);
                ImGuiImplD3D9.CreateDeviceObjects();
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
                    if (!Context.IsNull)
                    {
                        ImGuiImplD3D9.SetCurrentContext(Context);
                    }

                    ImGuiImplD3D9.Shutdown();
                }
                catch (Exception ex)
                {
                    ReportRuntimeError("DirectX9.Dispose", ex);
                }
                finally
                {
                    ShutdownImGui();
                    _device = default;
                    _frameStarted = false;
                    IsInitialized = false;
                }
            }
        }

        private void BackendNewFrame()
        {
            ImGuiImplD3D9.SetCurrentContext(Context);
            ImGuiImplD3D9.NewFrame();
            ImGuiImplWin32.SetCurrentContext(Context);
            ImGuiImplWin32.NewFrame();
        }

        private void BackendRender()
        {
            ImGuiImplD3D9.SetCurrentContext(Context);
            ImGuiImplD3D9.RenderDrawData(HexaImGui.GetDrawData());
        }
    }
}
