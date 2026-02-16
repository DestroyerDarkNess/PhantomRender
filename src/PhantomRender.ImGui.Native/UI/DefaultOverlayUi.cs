using System;
using System.Numerics;
using Hexa.NET.ImGui;
using ImGuiApi = Hexa.NET.ImGui.ImGui;
using PhantomRender.ImGui.Renderers;

namespace PhantomRender.ImGui.Native.UI
{
    internal sealed class DefaultOverlayUi : IDisposable
    {
        private readonly OverlayMenu _menu;
        private bool _showMainMenuBar = true;
        private bool _showStatusWindow = true;
        private bool _showDemoWindow = true;
        private bool _showMetricsWindow;
        private bool _showStyleEditor;
        private bool _disposed;

        public DefaultOverlayUi(OverlayMenu menu)
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
            if (_disposed)
            {
                return;
            }

            // Let OverlayMenu.DispatchSafe handle callback exceptions and route them to OnError.
            DrawDefaultUi(e.Api, e.WindowHandle, e.FrameCounter);
        }

        private void DrawDefaultUi(GraphicsApi api, nint windowHandle, ulong frameCounter)
        {
            if (_showMainMenuBar)
            {
                DrawMainMenuBar(api, windowHandle);
            }

            bool showStatusWindow = _showStatusWindow;
            if (showStatusWindow)
            {
                DrawStatusWindow(api, windowHandle, frameCounter, ref showStatusWindow);
                _showStatusWindow = showStatusWindow;
            }

            bool showDemo = _showDemoWindow;
            if (showDemo)
            {
                ImGuiApi.ShowDemoWindow(ref showDemo);
                _showDemoWindow = showDemo;
            }

            bool showMetrics = _showMetricsWindow;
            if (showMetrics)
            {
                ImGuiApi.ShowMetricsWindow(ref showMetrics);
                _showMetricsWindow = showMetrics;
            }

            bool showStyleEditor = _showStyleEditor;
            if (showStyleEditor)
            {
                ImGuiApi.Begin("ImGui Style Editor", ref showStyleEditor);
                ImGuiApi.ShowStyleEditor();
                ImGuiApi.End();
                _showStyleEditor = showStyleEditor;
            }
        }

        private void DrawMainMenuBar(GraphicsApi api, nint windowHandle)
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
                        if (windowHandle != nint.Zero)
                        {
                            ImGuiApi.TextDisabled($"Window: 0x{windowHandle.ToInt64():X}");
                        }

                        ImGuiApi.Separator();

                        bool showStatusWindow = _showStatusWindow;
                        bool showDemo = _showDemoWindow;
                        bool showMetrics = _showMetricsWindow;
                        bool showStyleEditor = _showStyleEditor;

                        if (ImGuiApi.MenuItem("Status Window", "", showStatusWindow, true)) showStatusWindow = !showStatusWindow;
                        if (ImGuiApi.MenuItem("ImGui Demo", "", showDemo, true)) showDemo = !showDemo;
                        if (ImGuiApi.MenuItem("ImGui Metrics", "", showMetrics, true)) showMetrics = !showMetrics;
                        if (ImGuiApi.MenuItem("ImGui Style Editor", "", showStyleEditor, true)) showStyleEditor = !showStyleEditor;

                        _showStatusWindow = showStatusWindow;
                        _showDemoWindow = showDemo;
                        _showMetricsWindow = showMetrics;
                        _showStyleEditor = showStyleEditor;
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

        private void DrawStatusWindow(GraphicsApi api, nint windowHandle, ulong frameCounter, ref bool showStatusWindow)
        {
            ImGuiApi.SetNextWindowPos(new Vector2(10, 40), ImGuiCond.FirstUseEver);
            ImGuiApi.SetNextWindowBgAlpha(0.85f);

            ImGuiWindowFlags flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings;

            if (ImGuiApi.Begin("PhantomRender", ref showStatusWindow, flags))
            {
                ImGuiApi.Text($"Backend: {api.ToDisplayName()} ({api.ToShortName()})");
                if (windowHandle != nint.Zero)
                {
                    ImGuiApi.Text($"Window: 0x{windowHandle.ToInt64():X}");
                }

                ImGuiApi.Text($"Frame: {frameCounter}");

                var io = ImGuiApi.GetIO();
                ImGuiApi.Text($"FPS: {io.Framerate:0.0}");

                bool showDemo = _showDemoWindow;
                bool showMetrics = _showMetricsWindow;
                bool showStyleEditor = _showStyleEditor;

                ImGuiApi.Separator();
                ImGuiApi.Checkbox("ImGui Demo", ref showDemo);
                ImGuiApi.Checkbox("ImGui Metrics", ref showMetrics);
                ImGuiApi.Checkbox("ImGui Style Editor", ref showStyleEditor);

                _showDemoWindow = showDemo;
                _showMetricsWindow = showMetrics;
                _showStyleEditor = showStyleEditor;
            }

            ImGuiApi.End();
        }
    }
}