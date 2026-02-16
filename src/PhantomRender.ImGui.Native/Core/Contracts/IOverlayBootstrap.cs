using PhantomRender.ImGui;

namespace PhantomRender.ImGui.Native
{
    internal interface IOverlayBootstrap
    {
        void Initialize(OverlayMenu menu);
        void Shutdown();
    }
}
