using System;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.ImGui.Backends.Win32;

namespace PhantomRender.ImGui.Renderers
{
    public sealed class OpenGLRenderer : RendererBase
    {
        private string _glslVersion = "#version 130";

        public void SetGLSLVersion(string version)
        {
            _glslVersion = version;
        }

        public override bool Initialize(IntPtr device, IntPtr windowHandle)
        {
            // Device is ignored for OpenGL (context is thread-local)
            if (IsInitialized) return true;

            try
            {
                InitializeImGui(windowHandle);

                // Initialize OpenGL3 Backend
                if (!ImGuiImplOpenGL3.Init(_glslVersion))
                {
                    ShutdownImGui();
                    return false;
                }

                IsInitialized = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override void NewFrame()
        {
            if (!IsInitialized) return;

            ImGuiImplOpenGL3.NewFrame();
            ImGuiImplWin32.NewFrame();
            Hexa.NET.ImGui.ImGui.NewFrame();
        }

        public override void Render()
        {
            if (!IsInitialized) return;

            RaiseOverlayRender();
            Hexa.NET.ImGui.ImGui.Render();
            ImGuiImplOpenGL3.RenderDrawData(Hexa.NET.ImGui.ImGui.GetDrawData());
        }

        public override void OnLostDevice()
        {
            // Not applicable for OpenGL usually, but keeping consistent
        }

        public override void OnResetDevice()
        {
            // Not applicable
        }

        public override void Dispose()
        {
             if (IsInitialized)
             {
                 ImGuiImplOpenGL3.Shutdown();
                 ShutdownImGui();
                 IsInitialized = false;
             }
        }
    }
}
