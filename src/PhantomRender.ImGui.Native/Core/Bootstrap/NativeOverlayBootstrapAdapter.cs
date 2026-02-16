using PhantomRender.ImGui;

namespace PhantomRender.ImGui.Native
{
    internal sealed class OverlayBootstrapAdapter : IOverlayBootstrap
    {
        public void Initialize(OverlayMenu menu)
        {
            OverlayBootstrap.Initialize(menu);
        }

        public void Shutdown()
        {
            OverlayBootstrap.Shutdown();
        }
    }
}
