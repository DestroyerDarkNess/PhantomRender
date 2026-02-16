using System;
using System.Numerics;
using Hexa.NET.ImGui;
using ImGuiApi = Hexa.NET.ImGui.ImGui;
using PhantomRender.ImGui;
using PhantomRender.ImGui.Renderers;

namespace PhantomRender.ImGui.Native
{
    internal sealed class NativeDefaultOverlayUi : IDisposable
    {
        private readonly OverlayMenu _menu;
        private bool _disposed;

        public NativeDefaultOverlayUi(OverlayMenu menu)
        {
            _menu = menu ?? throw new ArgumentNullException(nameof(menu));
            _menu.Render += OnRender;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _menu.Render -= OnRender;
        }

        private void OnRender(object sender, OverlayRenderEventArgs e)
        {
            if (_disposed || !_menu.Options.EnableDefaultUi)
            {
                return;
            }

            // Let OverlayMenu.DispatchSafe handle callback exceptions and route them to OnError.
            DrawDefaultUi(e.Api, e.WindowHandle, e.FrameCounter);
        }

        private void DrawDefaultUi(GraphicsApi api, IntPtr windowHandle, ulong frameCounter)
        {
            if (_menu.ShowMainMenuBar)
            {
                DrawMainMenuBar(api, windowHandle);
            }

            bool showStatusWindow = _menu.ShowStatusWindow;
            if (showStatusWindow)
            {
                DrawStatusWindow(api, windowHandle, frameCounter, ref showStatusWindow);
                _menu.ShowStatusWindow = showStatusWindow;
            }

            bool showDemo = _menu.ShowDemoWindow;
            if (showDemo)
            {
                ImGuiApi.ShowDemoWindow(ref showDemo);
                _menu.ShowDemoWindow = showDemo;
            }

            bool showMetrics = _menu.ShowMetricsWindow;
            if (showMetrics)
            {
                ImGuiApi.ShowMetricsWindow(ref showMetrics);
                _menu.ShowMetricsWindow = showMetrics;
            }

            bool showStyleEditor = _menu.ShowStyleEditor;
            if (showStyleEditor)
            {
                ImGuiApi.Begin("ImGui Style Editor", ref showStyleEditor);
                ImGuiApi.ShowStyleEditor();
                ImGuiApi.End();
                _menu.ShowStyleEditor = showStyleEditor;
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

                        bool showStatusWindow = _menu.ShowStatusWindow;
                        bool showDemo = _menu.ShowDemoWindow;
                        bool showMetrics = _menu.ShowMetricsWindow;
                        bool showStyleEditor = _menu.ShowStyleEditor;

                        if (ImGuiApi.MenuItem("Status Window", "", showStatusWindow, true)) showStatusWindow = !showStatusWindow;
                        if (ImGuiApi.MenuItem("ImGui Demo", "", showDemo, true)) showDemo = !showDemo;
                        if (ImGuiApi.MenuItem("ImGui Metrics", "", showMetrics, true)) showMetrics = !showMetrics;
                        if (ImGuiApi.MenuItem("ImGui Style Editor", "", showStyleEditor, true)) showStyleEditor = !showStyleEditor;

                        _menu.ShowStatusWindow = showStatusWindow;
                        _menu.ShowDemoWindow = showDemo;
                        _menu.ShowMetricsWindow = showMetrics;
                        _menu.ShowStyleEditor = showStyleEditor;
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

        private void DrawStatusWindow(GraphicsApi api, IntPtr windowHandle, ulong frameCounter, ref bool showStatusWindow)
        {
            ImGuiApi.SetNextWindowPos(new Vector2(10, 40), ImGuiCond.FirstUseEver);
            ImGuiApi.SetNextWindowBgAlpha(0.85f);

            ImGuiWindowFlags flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings;

            if (ImGuiApi.Begin("PhantomRender", ref showStatusWindow, flags))
            {
                ImGuiApi.Text($"Backend: {api.ToDisplayName()} ({api.ToShortName()})");
                if (windowHandle != IntPtr.Zero)
                {
                    ImGuiApi.Text($"Window: 0x{windowHandle.ToInt64():X}");
                }

                ImGuiApi.Text($"Frame: {frameCounter}");

                var io = ImGuiApi.GetIO();
                ImGuiApi.Text($"FPS: {io.Framerate:0.0}");

                bool showDemo = _menu.ShowDemoWindow;
                bool showMetrics = _menu.ShowMetricsWindow;
                bool showStyleEditor = _menu.ShowStyleEditor;

                ImGuiApi.Separator();
                ImGuiApi.Checkbox("ImGui Demo", ref showDemo);
                ImGuiApi.Checkbox("ImGui Metrics", ref showMetrics);
                ImGuiApi.Checkbox("ImGui Style Editor", ref showStyleEditor);

                _menu.ShowDemoWindow = showDemo;
                _menu.ShowMetricsWindow = showMetrics;
                _menu.ShowStyleEditor = showStyleEditor;
            }

            ImGuiApi.End();
        }
    }
}
