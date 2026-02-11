using System;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.ImGui.Backends.Win32;

namespace PhantomRender.ImGui.Renderers
{
    public sealed class OpenGLRenderer : RendererBase
    {
        private bool _frameStarted = false;

        public override bool Initialize(IntPtr device, IntPtr windowHandle)
        {
            if (IsInitialized) return true;

            try
            {
                Console.WriteLine($"[PhantomRender] Initalizing ImGui for Window: {windowHandle}");
                Console.Out.Flush();
                InitializeImGui(windowHandle);

                // Detect the GL version and appropriate GLSL version
                string glslVersion = DetectGLSLVersion();
                if (glslVersion == null)
                {
                    Console.WriteLine("[PhantomRender] OpenGL version too old (< 2.0), cannot use ImGui OpenGL3 backend!");
                    ShutdownImGui();
                    return false;
                }

                // Initialize OpenGL3 Backend (with SetCurrentContext like the working project)
                Console.WriteLine($"[PhantomRender] ImGuiImplOpenGL3.SetCurrentContext...");
                Console.Out.Flush();
                ImGuiImplOpenGL3.SetCurrentContext(Context);

                Console.WriteLine($"[PhantomRender] Initializing OpenGL3 Backend with version: {glslVersion}");
                Console.Out.Flush();
                if (!ImGuiImplOpenGL3.Init(glslVersion))
                {
                    Console.WriteLine("[PhantomRender] ImGuiImplOpenGL3.Init returned false!");
                    ShutdownImGui();
                    return false;
                }

                IsInitialized = true;
                Console.WriteLine("[PhantomRender] OpenGLRenderer Initialized Successfully!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] OpenGLRenderer Initialization Error: {ex}");
                return false;
            }
        }

        public override void NewFrame()
        {
            if (!IsInitialized) return;
            if (_frameStarted) return;

            ImGuiImplOpenGL3.NewFrame();
            ImGuiImplWin32.NewFrame();
            _inputEmulator?.Update();
            Hexa.NET.ImGui.ImGui.NewFrame();

            _frameStarted = true;
        }

        public override void Render()
        {
            if (!IsInitialized) return;
            if (!_frameStarted) return;

            // Demo window for testing
            Hexa.NET.ImGui.ImGui.ShowDemoWindow();

            RaiseOverlayRender();
            Hexa.NET.ImGui.ImGui.Render();
            ImGuiImplOpenGL3.RenderDrawData(Hexa.NET.ImGui.ImGui.GetDrawData());

            _frameStarted = false;
        }

        public override void OnLostDevice() { }
        public override void OnResetDevice() { }

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
