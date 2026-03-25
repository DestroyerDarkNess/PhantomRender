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
        private bool _frameStarted;
        private IDirect3DDevice9Ptr _device;

        public DirectX9Renderer()
            : base(GraphicsApi.DirectX9)
        {
        }

        public DirectX9InitializationEndpoint InitializationEndpoint { get; set; } = DirectX9InitializationEndpoint.Present;

        public override bool Initialize(nint device, nint windowHandle)
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

        public override void NewFrame()
        {
            if (!IsInitialized || _frameStarted)
            {
                return;
            }

            try
            {
                BeginFrameCore(() =>
                {
                    ImGuiImplD3D9.SetCurrentContext(Context);
                    ImGuiImplD3D9.NewFrame();
                    ImGuiImplWin32.SetCurrentContext(Context);
                    ImGuiImplWin32.NewFrame();
                });

                _frameStarted = true;
            }
            catch (Exception ex)
            {
                ReportRuntimeError("DirectX9.NewFrame", ex);
            }
        }

        public override void Render()
        {
            if (!IsInitialized || !_frameStarted)
            {
                return;
            }

            try
            {
                RenderFrameCore(() =>
                {
                    ImGuiImplD3D9.SetCurrentContext(Context);
                    ImGuiImplD3D9.RenderDrawData(HexaImGui.GetDrawData());
                });
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

        public override void OnLostDevice()
        {
            if (!IsInitialized)
            {
                return;
            }

            ImGuiImplD3D9.SetCurrentContext(Context);
            ImGuiImplD3D9.InvalidateDeviceObjects();
        }

        public override void OnResetDevice()
        {
            if (!IsInitialized)
            {
                return;
            }

            ImGuiImplD3D9.SetCurrentContext(Context);
            ImGuiImplD3D9.CreateDeviceObjects();
        }

        public override void Dispose()
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
}
