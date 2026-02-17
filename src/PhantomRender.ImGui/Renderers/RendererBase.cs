using System;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.Win32;
using PhantomRender.ImGui;

namespace PhantomRender.ImGui.Renderers
{
    public abstract class RendererBase : IOverlayRenderer
    {
        private readonly OverlayMenu _overlayMenu;

        protected RendererBase(OverlayMenu overlayMenu, GraphicsApi graphicsApi)
        {
            _overlayMenu = overlayMenu ?? OverlayMenu.Default;
            GraphicsApi = graphicsApi;
        }

        public GraphicsApi GraphicsApi { get; }
        public ImGuiContextPtr Context { get; protected set; }
        public ImGuiIOPtr IO { get; protected set; }
        public bool IsInitialized { get; protected set; }

        public event Action OnOverlayRender;

        protected IntPtr _windowHandle;

        public abstract bool Initialize(IntPtr device, IntPtr windowHandle);

        public abstract void NewFrame();

        public abstract void Render();

        public abstract void OnLostDevice();

        public abstract void OnResetDevice();

        public abstract void Dispose();

        protected void RaiseOverlayRender()
        {
            Action handlers = OnOverlayRender;
            if (handlers == null)
            {
                return;
            }

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action)handler)();
                }
                catch (Exception ex)
                {
                    if (!_overlayMenu.Options.CatchUserCallbackExceptions)
                    {
                        throw;
                    }

                    try { _overlayMenu.ReportRuntimeError("OnOverlayRender", ex); } catch { }
                }
            }
        }

        protected void RaiseRendererInitializing(IntPtr device, IntPtr windowHandle)
        {
            try { _overlayMenu.RaiseRendererInitializing(this, device, windowHandle); }
            catch (Exception ex)
            {
                if (!_overlayMenu.Options.CatchUserCallbackExceptions)
                {
                    throw;
                }

                try { _overlayMenu.ReportRuntimeError("InitializeRenderer", ex); } catch { }
            }
        }

        protected void RaiseImGuiInitialized()
        {
            try { _overlayMenu.RaiseImGuiInitialized(this); }
            catch (Exception ex)
            {
                if (!_overlayMenu.Options.CatchUserCallbackExceptions)
                {
                    throw;
                }

                try { _overlayMenu.ReportRuntimeError("InitializeImGui", ex); } catch { }
            }
        }

        protected void RenderMenuFrame()
        {
            try { _overlayMenu.RenderFrame(this, GraphicsApi, _windowHandle); }
            catch (Exception ex)
            {
                if (!_overlayMenu.Options.CatchUserCallbackExceptions)
                {
                    throw;
                }

                try { _overlayMenu.ReportRuntimeError("RenderFrame", ex); } catch { }
            }
        }

        protected void RaiseNewFrame()
        {
            try { _overlayMenu.RaiseNewFrame(this, GraphicsApi, _windowHandle); }
            catch (Exception ex)
            {
                if (!_overlayMenu.Options.CatchUserCallbackExceptions)
                {
                    throw;
                }

                try { _overlayMenu.ReportRuntimeError("NewFrame", ex); } catch { }
            }
        }

        protected unsafe void InitializeImGui(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;

            Console.WriteLine("[PhantomRender] Step 1: Creating ImGui context...");
            Console.Out.Flush();
            Context = Hexa.NET.ImGui.ImGui.CreateContext();

            Console.WriteLine("[PhantomRender] Step 2: Setting current context...");
            Console.Out.Flush();
            Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);

            Console.WriteLine("[PhantomRender] Step 3: Getting IO...");
            Console.Out.Flush();
            IO = Hexa.NET.ImGui.ImGui.GetIO();

            Console.WriteLine("[PhantomRender] Step 4: Setting ConfigFlags...");
            Console.Out.Flush();
            IO.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;

            // Initialize Win32 Backend
            Console.WriteLine("[PhantomRender] Step 5: ImGuiImplWin32.SetCurrentContext...");
            Console.Out.Flush();
            ImGuiImplWin32.SetCurrentContext(Context);

            Console.WriteLine("[PhantomRender] Step 6: ImGuiImplWin32.Init...");
            Console.Out.Flush();
            ImGuiImplWin32.Init((void*)windowHandle);

            RaiseImGuiInitialized();

            Console.WriteLine("[PhantomRender] InitializeImGui completed successfully.");
            Console.Out.Flush();
        }

        protected void ShutdownImGui()
        {
            // Cleanup must also work when initialization fails part-way through (IsInitialized may still be false).
            try
            {
                if (!Context.IsNull)
                {
                    Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
                    ImGuiImplWin32.SetCurrentContext(Context);
                }
            }
            catch { }

            try { ImGuiImplWin32.Shutdown(); } catch { }

            try
            {
                if (!Context.IsNull)
                {
                    Hexa.NET.ImGui.ImGui.DestroyContext(Context);
                }
            }
            catch { }

            Context = ImGuiContextPtr.Null;
            IO = default;
        }

        // --- OpenGL Version Detection ---

        private const uint GL_MAJOR_VERSION = 0x821B;
        private const uint GL_MINOR_VERSION = 0x821C;
        private const uint GL_VERSION = 0x1F02;

        [DllImport("opengl32.dll")]
        private static extern IntPtr glGetString(uint name);

        [DllImport("opengl32.dll")]
        private static extern IntPtr wglGetProcAddress(string procName);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void glGetIntegervDelegate(uint pname, out int data);

        /// <summary>
        /// Detects the OpenGL version and returns the appropriate GLSL version string.
        /// Returns null if OpenGL is too old (< 2.0) to use with ImGui OpenGL3 backend.
        /// </summary>
        protected static string DetectGLSLVersion()
        {
            int major = 0, minor = 0;

            try
            {
                // Try modern way first (GL 3.0+)
                IntPtr hModule = GetModuleHandleW("opengl32.dll");
                if (hModule != IntPtr.Zero)
                {
                    IntPtr glGetIntegervPtr = GetProcAddress(hModule, "glGetIntegerv");
                    if (glGetIntegervPtr != IntPtr.Zero)
                    {
                        var glGetIntegerv = Marshal.GetDelegateForFunctionPointer<glGetIntegervDelegate>(glGetIntegervPtr);
                        glGetIntegerv(GL_MAJOR_VERSION, out major);
                        glGetIntegerv(GL_MINOR_VERSION, out minor);
                    }
                }
            }
            catch { }

            // If major is 0, try parsing the version string (works for all GL versions)
            if (major == 0)
            {
                try
                {
                    IntPtr versionPtr = glGetString(GL_VERSION);
                    if (versionPtr != IntPtr.Zero)
                    {
                        string versionString = Marshal.PtrToStringAnsi(versionPtr);
                        Console.WriteLine($"[PhantomRender] GL_VERSION string: {versionString}");

                        // Parse "X.Y.Z ..." format
                        if (!string.IsNullOrEmpty(versionString))
                        {
                            string[] parts = versionString.Split(' ')[0].Split('.');
                            if (parts.Length >= 2)
                            {
                                int.TryParse(parts[0], out major);
                                int.TryParse(parts[1], out minor);
                            }
                        }
                    }
                }
                catch { }
            }

            Console.WriteLine($"[PhantomRender] Detected OpenGL version: {major}.{minor}");

            // ImGui OpenGL3 backend requires at least OpenGL 2.0
            if (major < 2) return null;

            // Map GL version to GLSL version
            if (major == 2 && minor == 0) return "#version 110";
            if (major == 2 && minor == 1) return "#version 120";
            if (major == 3 && minor == 0) return "#version 130";
            if (major == 3 && minor == 1) return "#version 140";
            if (major == 3 && minor == 2) return "#version 150";
            if (major >= 3 && minor >= 3) return $"#version {major}{minor}0";
            if (major >= 4) return $"#version {major}{minor}0";

            return "#version 130"; // Safe default for GL 3.0+
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
    }
}