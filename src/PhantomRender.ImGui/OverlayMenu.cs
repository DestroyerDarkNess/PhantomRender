using System;
using System.Numerics;
using Hexa.NET.ImGui;
using ImGuiApi = Hexa.NET.ImGui.ImGui;
using PhantomRender.ImGui.Renderers;

namespace PhantomRender.ImGui
{
    /// <summary>
    /// Shared overlay UI that is backend-agnostic.
    /// Renderers call this to keep the overlay menu consistent across APIs.
    /// </summary>
    public static class OverlayMenu
    {
        private static bool _showStatusWindow = true;
        private static bool _showDemoWindow = true;
        private static bool _showMetricsWindow;
        private static bool _showStyleEditor;

        public static bool ShowMainMenuBar { get; set; } = true;

        public static bool ShowStatusWindow
        {
            get => _showStatusWindow;
            set => _showStatusWindow = value;
        }

        public static bool ShowDemoWindow
        {
            get => _showDemoWindow;
            set => _showDemoWindow = value;
        }

        public static bool ShowMetricsWindow
        {
            get => _showMetricsWindow;
            set => _showMetricsWindow = value;
        }

        public static bool ShowStyleEditor
        {
            get => _showStyleEditor;
            set => _showStyleEditor = value;
        }

        public static void Draw(GraphicsApi api, IntPtr windowHandle, ulong frameCounter)
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

        private static void DrawMainMenuBar(GraphicsApi api, IntPtr windowHandle)
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

        private static void DrawStatusWindow(GraphicsApi api, IntPtr windowHandle, ulong frameCounter)
        {
            // Keep it below the main menu bar by default.
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
