using System;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui.Backends.OpenGL3;
using PhantomRender.Core;
using PhantomRender.Core.Hooks.Graphics.OpenGL;
using PhantomRender.ImGui.Core;
using HexaImGui = Hexa.NET.ImGui.ImGui;

namespace PhantomRender.ImGui.Core.Renderers
{
    public sealed class OpenGLRenderer : RendererBase
    {
        private const uint GL_VERSION = 0x1F02;

        private readonly object _sync = new object();
        private readonly Action _backendNewFrameAction;
        private readonly Action _backendRenderAction;
        private bool _frameStarted;

        public OpenGLRenderer()
            : base(GraphicsApi.OpenGL)
        {
            _backendNewFrameAction = BackendNewFrame;
            _backendRenderAction = BackendRender;
        }

        public OpenGLSwapBuffersHookTarget SwapBuffersHookTarget { get; set; } = OpenGLSwapBuffersHookTarget.Auto;

        public override bool Initialize(nint device, nint windowHandle)
        {
            lock (_sync)
            {
                if (IsInitialized)
                {
                    return true;
                }

                try
                {
                    RaiseRendererInitializing(device, windowHandle);
                    InitializeImGui(windowHandle);

                    string glslVersion = DetectGlslVersion();
                    if (string.IsNullOrWhiteSpace(glslVersion))
                    {
                        ShutdownImGui();
                        return false;
                    }

                    ImGuiImplOpenGL3.SetCurrentContext(Context);
                    if (!ImGuiImplOpenGL3.Init(glslVersion))
                    {
                        ShutdownImGui();
                        return false;
                    }

                    IsInitialized = true;
                    return true;
                }
                catch (Exception ex)
                {
                    ReportRuntimeError("OpenGL.Initialize", ex);
                    ShutdownImGui();

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
                    ReportRuntimeError("OpenGL.NewFrame", ex);
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
                    ReportRuntimeError("OpenGL.Render", ex);
                }
                finally
                {
                    _frameStarted = false;
                }
            }
        }

        public override void OnLostDevice()
        {
        }

        public override void OnResetDevice()
        {
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
                        ImGuiImplOpenGL3.SetCurrentContext(Context);
                    }

                    ImGuiImplOpenGL3.Shutdown();
                }
                catch (Exception ex)
                {
                    ReportRuntimeError("OpenGL.Dispose", ex);
                }
                finally
                {
                    ShutdownImGui();
                    IsInitialized = false;
                    _frameStarted = false;
                }
            }
        }

        private void BackendNewFrame()
        {
            ImGuiImplOpenGL3.SetCurrentContext(Context);
            ImGuiImplOpenGL3.NewFrame();
            Hexa.NET.ImGui.Backends.Win32.ImGuiImplWin32.NewFrame();
        }

        private void BackendRender()
        {
            ImGuiImplOpenGL3.SetCurrentContext(Context);
            ImGuiImplOpenGL3.RenderDrawData(HexaImGui.GetDrawData());
        }

        private static string DetectGlslVersion()
        {
            nint versionPointer = glGetString(GL_VERSION);
            if (versionPointer == IntPtr.Zero)
            {
                return "#version 130";
            }

            string versionString = Marshal.PtrToStringAnsi(versionPointer);
            if (string.IsNullOrWhiteSpace(versionString))
            {
                return "#version 130";
            }

            string[] versionParts = versionString.Split(' ')[0].Split('.');
            if (versionParts.Length < 2 ||
                !int.TryParse(versionParts[0], out int major) ||
                !int.TryParse(versionParts[1], out int minor))
            {
                return "#version 130";
            }

            if (major < 2)
            {
                return null;
            }

            if (major == 2 && minor == 0) return "#version 110";
            if (major == 2 && minor == 1) return "#version 120";
            if (major == 3 && minor == 0) return "#version 130";
            if (major == 3 && minor == 1) return "#version 140";
            if (major == 3 && minor == 2) return "#version 150";
            if (major >= 4) return $"#version {major}{minor}0";
            if (major == 3 && minor >= 3) return $"#version {major}{minor}0";

            return "#version 130";
        }

        [DllImport("opengl32.dll")]
        private static extern nint glGetString(uint name);
    }
}
