using System;
using System.Runtime.CompilerServices;
using PhantomRender.ImGui;
using PhantomRender.ImGui.Inputs;
using PhantomRender.ImGui.Renderers;

namespace PhantomRender.ImGui.Native
{
    internal sealed class InputEmulation : IDisposable
    {
        private readonly OverlayMenu _menu;
        private readonly Func<bool> _isMenuVisible;
        private readonly Action _toggleMenuVisibility;
        private readonly ConditionalWeakTable<IOverlayRenderer, InputImguiEmu> _inputsByRenderer = new ConditionalWeakTable<IOverlayRenderer, InputImguiEmu>();
        private bool _disposed;

        public InputEmulation(OverlayMenu menu, Func<bool> isMenuVisible, Action toggleMenuVisibility)
        {
            _menu = menu ?? throw new ArgumentNullException(nameof(menu));
            _isMenuVisible = isMenuVisible ?? throw new ArgumentNullException(nameof(isMenuVisible));
            _toggleMenuVisibility = toggleMenuVisibility ?? throw new ArgumentNullException(nameof(toggleMenuVisibility));
            _menu.InitializeImGui += OnInitializeImGui;
            _menu.NewFrame += OnNewFrame;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _menu.InitializeImGui -= OnInitializeImGui;
            _menu.NewFrame -= OnNewFrame;
        }

        private void OnInitializeImGui(object sender, OverlayImGuiInitializedEventArgs e)
        {
            if (_disposed || e == null || e.Renderer == null)
            {
                return;
            }

            // Recreate emulator when renderer/context is recreated.
            _inputsByRenderer.Remove(e.Renderer);
            var input = new InputImguiEmu(e.IO);
            input.AddEvent(Keys.Insert, _toggleMenuVisibility);
            _inputsByRenderer.Add(e.Renderer, input);

            e.IO.MouseDrawCursor = _isMenuVisible();
        }

        private void OnNewFrame(object sender, OverlayNewFrameEventArgs e)
        {
            if (_disposed || e == null || e.Renderer == null)
            {
                return;
            }

            if (_inputsByRenderer.TryGetValue(e.Renderer, out InputImguiEmu input))
            {
                input.Update();
                e.Renderer.IO.MouseDrawCursor = _isMenuVisible();
            }
        }
    }
}
