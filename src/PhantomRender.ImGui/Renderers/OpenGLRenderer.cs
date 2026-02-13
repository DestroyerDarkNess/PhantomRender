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
                Console.WriteLine($"[PhantomRender] OpenGLRenderer: Entering Initialize (RE-PUBLISHED V2). Window: {windowHandle}");
                Console.Out.Flush();
                InitializeImGui(windowHandle);

                // Detect the GL version and appropriate GLSL version
                string glslVersion = DetectGLSLVersion();
                if (glslVersion == null)
                {
                    Console.WriteLine("[PhantomRender] OpenGLRenderer: Version too old (< 2.0)!");
                    Console.Out.Flush();
                    ShutdownImGui();
                    return false;
                }

                // Initialize OpenGL3 Backend
                Console.WriteLine("[PhantomRender] OpenGLRenderer: Setting context for OpenGL3 backend...");
                Console.Out.Flush();
                ImGuiImplOpenGL3.SetCurrentContext(Context);

                Console.WriteLine($"[PhantomRender] OpenGLRenderer: Calling ImGuiImplOpenGL3.Init with {glslVersion}...");
                Console.Out.Flush();
                
                if (!ImGuiImplOpenGL3.Init(glslVersion))
                {
                    Console.WriteLine("[PhantomRender] OpenGLRenderer: ImGuiImplOpenGL3.Init returned FALSE!");
                    Console.Out.Flush();
                    ShutdownImGui();
                    return false;
                }

                IsInitialized = true;
                Console.WriteLine("[PhantomRender] OpenGLRenderer: Initialized Successfully! (V2)");
                Console.Out.Flush();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] OpenGLRenderer: Init Error (V2): {ex}");
                Console.Out.Flush();
                return false;
            }
        }

        public override void NewFrame()
        {
            if (!IsInitialized) return;
            if (_frameStarted) return;

            Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
            ImGuiImplOpenGL3.SetCurrentContext(Context);
            ImGuiImplWin32.SetCurrentContext(Context);

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

            Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);

            // Test window for testing
            Hexa.NET.ImGui.ImGui.SetNextWindowPos(new System.Numerics.Vector2(100, 100), ImGuiCond.FirstUseEver);
            bool showWindow = Hexa.NET.ImGui.ImGui.Begin("PhantomRender OpenGL");
            if (showWindow)
            {
                Hexa.NET.ImGui.ImGui.Text("Status: Active (OpenGL)");
                Hexa.NET.ImGui.ImGui.Text($"Window: {_windowHandle}");
            }
            Hexa.NET.ImGui.ImGui.End();

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
