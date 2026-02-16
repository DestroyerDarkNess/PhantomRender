using System;
using System.Numerics;
using System.Threading;
using Hexa.NET.ImGui;
using ImGuiApi = Hexa.NET.ImGui.ImGui;
using PhantomRender.ImGui.Renderers;

namespace PhantomRender.ImGui
{
    public sealed class OverlayMenu
    {
        private static OverlayMenu _default = new OverlayMenu();

        private bool _showStatusWindow = true;
        private bool _showDemoWindow = true;
        private bool _showMetricsWindow;
        private bool _showStyleEditor;
        private int _raisingError;

        public OverlayMenu()
            : this(new OverlayMenuOptions())
        {
        }

        public OverlayMenu(OverlayHookKind preferredHook)
            : this(new OverlayMenuOptions { PreferredHook = preferredHook })
        {
        }

        public OverlayMenu(OverlayMenuOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public static OverlayMenu Default
        {
            get => Volatile.Read(ref _default);
            set => Volatile.Write(ref _default, value ?? throw new ArgumentNullException(nameof(value)));
        }

        public OverlayMenuOptions Options { get; }

        public bool ShowMainMenuBar { get; set; } = true;

        public bool ShowStatusWindow
        {
            get => _showStatusWindow;
            set => _showStatusWindow = value;
        }

        public bool ShowDemoWindow
        {
            get => _showDemoWindow;
            set => _showDemoWindow = value;
        }

        public bool ShowMetricsWindow
        {
            get => _showMetricsWindow;
            set => _showMetricsWindow = value;
        }

        public bool ShowStyleEditor
        {
            get => _showStyleEditor;
            set => _showStyleEditor = value;
        }

        public event EventHandler<OverlayRendererInitializingEventArgs> InitializeRenderer;
        public event EventHandler<OverlayImGuiInitializedEventArgs> InitializeImGui;
        public event EventHandler<OverlayRenderEventArgs> Render;
        public event EventHandler<OverlayErrorEventArgs> OnError;

        internal void RaiseRendererInitializing(IOverlayRenderer renderer, IntPtr device, IntPtr windowHandle)
        {
            DispatchSafe(
                InitializeRenderer,
                new OverlayRendererInitializingEventArgs(renderer, device, windowHandle),
                "InitializeRenderer");
        }

        internal void RaiseImGuiInitialized(IOverlayRenderer renderer)
        {
            DispatchSafe(
                InitializeImGui,
                new OverlayImGuiInitializedEventArgs(renderer, renderer.Context, renderer.IO),
                "InitializeImGui");
        }

        internal void RenderFrame(IOverlayRenderer renderer, GraphicsApi api, IntPtr windowHandle, ulong frameCounter)
        {
            if (Options.EnableDefaultUi)
            {
                try
                {
                    DrawDefaultUi(api, windowHandle, frameCounter);
                }
                catch (Exception ex)
                {
                    ReportError("DrawDefaultUi", ex);
                }
            }

            DispatchSafe(
                Render,
                new OverlayRenderEventArgs(renderer, api, windowHandle, frameCounter),
                "Render");
        }

        internal void ReportRuntimeError(string stage, Exception exception)
        {
            ReportError(stage, exception);
        }

        private void DispatchSafe<T>(EventHandler<T> handlers, T args, string stage)
            where T : EventArgs
        {
            if (handlers == null)
            {
                return;
            }

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((EventHandler<T>)handler)(this, args);
                }
                catch (Exception ex)
                {
                    if (!Options.CatchUserCallbackExceptions)
                    {
                        throw;
                    }

                    ReportError(stage, ex);
                }
            }
        }

        private void ReportError(string stage, Exception exception)
        {
            try
            {
                Console.WriteLine($"[PhantomRender] OverlayMenu error ({stage}): {exception}");
                Console.Out.Flush();
            }
            catch
            {
                // Ignore logging failures.
            }

            EventHandler<OverlayErrorEventArgs> handlers = OnError;
            if (handlers == null)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _raisingError, 1, 0) != 0)
            {
                return;
            }

            try
            {
                var args = new OverlayErrorEventArgs(stage, exception);
                foreach (Delegate handler in handlers.GetInvocationList())
                {
                    try
                    {
                        ((EventHandler<OverlayErrorEventArgs>)handler)(this, args);
                    }
                    catch
                    {
                        // Avoid recursive error event loops.
                    }
                }
            }
            finally
            {
                Volatile.Write(ref _raisingError, 0);
            }
        }

        private void DrawDefaultUi(GraphicsApi api, IntPtr windowHandle, ulong frameCounter)
        {
            if (ShowMainMenuBar)
            {
                DrawMainMenuBar(api, windowHandle);
            }

            if (_showStatusWindow)
            {
                DrawStatusWindow(api, windowHandle, frameCounter);
            }

            if (_showDemoWindow)
            {
                ImGuiApi.ShowDemoWindow(ref _showDemoWindow);
            }

            if (_showMetricsWindow)
            {
                ImGuiApi.ShowMetricsWindow(ref _showMetricsWindow);
            }

            if (_showStyleEditor)
            {
                ImGuiApi.Begin("ImGui Style Editor", ref _showStyleEditor);
                ImGuiApi.ShowStyleEditor();
                ImGuiApi.End();
            }
        }

        private void DrawMainMenuBar(GraphicsApi api, IntPtr windowHandle)
        {
            if (!ImGuiApi.BeginMainMenuBar())
            {
                return;
            }

            try
            {
                if (ImGuiApi.BeginMenu("PhantomRender"))
                {
                    try
                    {
                        ImGuiApi.TextDisabled($"Backend: {api.ToDisplayName()} ({api.ToShortName()})");
                        if (windowHandle != IntPtr.Zero)
                        {
                            ImGuiApi.TextDisabled($"Window: 0x{windowHandle.ToInt64():X}");
                        }

                        ImGuiApi.Separator();

                        if (ImGuiApi.MenuItem("Status Window", "", _showStatusWindow, true)) _showStatusWindow = !_showStatusWindow;
                        if (ImGuiApi.MenuItem("ImGui Demo", "", _showDemoWindow, true)) _showDemoWindow = !_showDemoWindow;
                        if (ImGuiApi.MenuItem("ImGui Metrics", "", _showMetricsWindow, true)) _showMetricsWindow = !_showMetricsWindow;
                        if (ImGuiApi.MenuItem("ImGui Style Editor", "", _showStyleEditor, true)) _showStyleEditor = !_showStyleEditor;
                    }
                    finally
                    {
                        ImGuiApi.EndMenu();
                    }
                }
            }
            finally
            {
                ImGuiApi.EndMainMenuBar();
            }
        }

        private void DrawStatusWindow(GraphicsApi api, IntPtr windowHandle, ulong frameCounter)
        {
            ImGuiApi.SetNextWindowPos(new Vector2(10, 40), ImGuiCond.FirstUseEver);
            ImGuiApi.SetNextWindowBgAlpha(0.85f);

            ImGuiWindowFlags flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings;

            if (ImGuiApi.Begin("PhantomRender", ref _showStatusWindow, flags))
            {
                ImGuiApi.Text($"Backend: {api.ToDisplayName()} ({api.ToShortName()})");
                if (windowHandle != IntPtr.Zero)
                {
                    ImGuiApi.Text($"Window: 0x{windowHandle.ToInt64():X}");
                }

                ImGuiApi.Text($"Frame: {frameCounter}");

                var io = ImGuiApi.GetIO();
                ImGuiApi.Text($"FPS: {io.Framerate:0.0}");

                ImGuiApi.Separator();
                ImGuiApi.Checkbox("ImGui Demo", ref _showDemoWindow);
                ImGuiApi.Checkbox("ImGui Metrics", ref _showMetricsWindow);
                ImGuiApi.Checkbox("ImGui Style Editor", ref _showStyleEditor);
            }

            ImGuiApi.End();
        }
    }
}
