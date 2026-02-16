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
        private readonly ConditionalWeakTable<IOverlayRenderer, InputImguiEmu> _inputsByRenderer = new ConditionalWeakTable<IOverlayRenderer, InputImguiEmu>();
        private bool _disposed;

        public InputEmulation(OverlayMenu menu)
        {
            _menu = menu ?? throw new ArgumentNullException(nameof(menu));
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
            _inputsByRenderer.Add(e.Renderer, new InputImguiEmu(e.IO));
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
            }
        }
    }
}
