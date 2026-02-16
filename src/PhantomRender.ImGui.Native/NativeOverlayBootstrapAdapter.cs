using PhantomRender.ImGui;

namespace PhantomRender.ImGui.Native
{
    internal sealed class NativeOverlayBootstrapAdapter : INativeOverlayBootstrap
    {
        public void Initialize(OverlayMenu menu)
        {
            NativeOverlayBootstrap.Initialize(menu);
        }

        public void Shutdown()
        {
            NativeOverlayBootstrap.Shutdown();
        }
    }
}

