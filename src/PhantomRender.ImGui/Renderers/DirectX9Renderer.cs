using System;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.D3D9;
using Hexa.NET.ImGui.Backends.Win32;

namespace PhantomRender.ImGui.Renderers
{
    public sealed class DirectX9Renderer : RendererBase
    {
        public override unsafe bool Initialize(IntPtr device, IntPtr windowHandle)
        {
            if (IsInitialized) return true;

            try
            {
                InitializeImGui(windowHandle);

                // Initialize D3D9 Backend
                // Type cast to correct pointer type
                if (!ImGuiImplD3D9.Init((IDirect3DDevice9*)device))
                {
                    ShutdownImGui();
                    return false;
                }

                IsInitialized = true;
                return true;
            }
            catch
            {
                // Log?
                return false;
            }
        }

        public override void NewFrame()
        {
            if (!IsInitialized) return;

            ImGuiImplD3D9.NewFrame();
            ImGuiImplWin32.NewFrame();
            Hexa.NET.ImGui.ImGui.NewFrame();
        }

        public override void Render()
        {
            if (!IsInitialized) return;

            RaiseOverlayRender();
            Hexa.NET.ImGui.ImGui.Render();
            ImGuiImplD3D9.RenderDrawData(Hexa.NET.ImGui.ImGui.GetDrawData());
        }

        public override void OnLostDevice()
        {
            if (IsInitialized)
            {
                ImGuiImplD3D9.InvalidateDeviceObjects();
            }
        }

        public override void OnResetDevice()
        {
            if (IsInitialized)
            {
                ImGuiImplD3D9.CreateDeviceObjects();
            }
        }

        public override void Dispose()
        {
            if (IsInitialized)
            {
                ImGuiImplD3D9.Shutdown();
                ShutdownImGui();
                IsInitialized = false;
            }
        }
    }
}
