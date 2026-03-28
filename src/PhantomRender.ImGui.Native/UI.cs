using System;
using System.Numerics;
using Hexa.NET.ImGui;
using PhantomRender.Core;
using PhantomRender.Overlays;
using PhantomRender.ImGui.Core;
using PhantomRender.ImGui.Core.Inputs;
using PhantomRender.ImGui.Core.Renderers;
using HexaImGui = Hexa.NET.ImGui.ImGui;

namespace PhantomRender.ImGui.Native
{
    internal sealed class UI : IDisposable
    {
        private readonly Overlay _overlay;
        private readonly ExternalOverlayWindow _externalWindow;
        private readonly RendererBase _renderer;
        private InputImguiEmu _input;
        private bool _disposed;
        private bool _visible = true;
        private bool _showDemoWindow;
        private bool _shutdownRequested;
        private readonly string _modeText;
        private readonly string _rendererText;

        public UI(Overlay overlay, ExternalOverlayWindow externalWindow = null)
        {
            _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
            _externalWindow = externalWindow;
            _renderer = (overlay as InternalOverlay)?.Renderer;
            _modeText = $"Mode: {(externalWindow != null ? "External" : "Internal")}";
            _rendererText = $"Renderer: {_overlay.GraphicsApi.ToDisplayName()}";
            _overlay.ImGuiInitialized += OnImGuiInitialized;
            if (_renderer != null)
            {
                _renderer.OnOverlayNewFrame += OnNewFrame;
                _renderer.OnOverlayRender += OnRender;
            }
        }

        public bool ShutdownRequested => _shutdownRequested;

        public bool Visible => _visible;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _overlay.ImGuiInitialized -= OnImGuiInitialized;
            if (_renderer != null)
            {
                _renderer.OnOverlayNewFrame -= OnNewFrame;
                _renderer.OnOverlayRender -= OnRender;
            }

            if (_input != null)
            {
                _input.ClearEvents();
                _input = null;
            }
        }

        private void OnImGuiInitialized(object sender, OverlayImGuiInitializedEventArgs e)
        {
            ApplyStyle();

            _input = new InputImguiEmu(e.IO, e.Renderer.WindowHandle)
            {
                KeyRepeatDelay = TimeSpan.FromMilliseconds(150),
            };

            _input.AddEvent(Keys.Insert, ToggleVisibility);
            _input.AddEvent(Keys.Delete, RequestShutdown);
            SyncExternalWindow();
        }

        private void OnNewFrame()
        {
            if (_input == null)
            {
                return;
            }

            HexaImGui.GetIO().MouseDrawCursor = _visible;

            if (_visible)
            {
                _input.Enabled = true;
                _input.Update();
            }
            else
            {
                _input.Enabled = false;
                _input.UpdateHotkeysOnly();
            }

            SyncExternalWindow();
        }

        private void OnRender()
        {
            if (!_visible)
            {
                return;
            }

            bool open = _visible;
            bool begin = HexaImGui.Begin("PhantomRender", ref open, ImGuiWindowFlags.AlwaysAutoResize);
            if (begin)
            {
                HexaImGui.Text(_modeText);
                HexaImGui.Text(_rendererText);
                HexaImGui.Separator();
                HexaImGui.Text("Insert: Show/Hide menu");
                HexaImGui.Text("Delete: Shutdown");
                HexaImGui.Separator();
                HexaImGui.Checkbox("Show ImGui demo", ref _showDemoWindow);

                if (HexaImGui.Button("Close overlay"))
                {
                    _shutdownRequested = true;
                }
            }

            HexaImGui.End();

            _visible = open;
            SyncExternalWindow();

            if (_showDemoWindow)
            {
                HexaImGui.ShowDemoWindow(ref _showDemoWindow);
            }
        }

        private void ToggleVisibility()
        {
            _visible = !_visible;
            SyncExternalWindow();
        }

        private void RequestShutdown()
        {
            _shutdownRequested = true;
        }

        private void SyncExternalWindow()
        {
            if (_externalWindow == null)
            {
                return;
            }

            if (_externalWindow.Mode == ExternalOverlayMode.Overlay)
            {
                _externalWindow.ClickThrough = !_visible;
                _externalWindow.TopMost = true;
            }
        }

        private static void ApplyStyle()
        {
            ImGuiStylePtr style = HexaImGui.GetStyle();
            style.WindowRounding = 6.0f;
            style.FrameRounding = 4.0f;
            style.PopupRounding = 4.0f;
            style.ScrollbarRounding = 6.0f;
            style.GrabRounding = 4.0f;
            style.WindowPadding = new Vector2(10.0f, 10.0f);
            style.FramePadding = new Vector2(8.0f, 4.0f);
            style.ItemSpacing = new Vector2(8.0f, 6.0f);

            style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.07f, 0.08f, 0.10f, 0.96f);
            style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.09f, 0.11f, 0.14f, 1.00f);
            style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.12f, 0.15f, 0.19f, 1.00f);
            style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.13f, 0.15f, 0.19f, 1.00f);
            style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.18f, 0.22f, 0.28f, 1.00f);
            style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.22f, 0.27f, 0.34f, 1.00f);
            style.Colors[(int)ImGuiCol.Button] = new Vector4(0.18f, 0.32f, 0.52f, 1.00f);
            style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.24f, 0.40f, 0.64f, 1.00f);
            style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.16f, 0.28f, 0.46f, 1.00f);
            style.Colors[(int)ImGuiCol.Header] = new Vector4(0.16f, 0.28f, 0.46f, 0.80f);
            style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.24f, 0.40f, 0.64f, 0.80f);
            style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.18f, 0.32f, 0.52f, 0.90f);
        }
    }
}
