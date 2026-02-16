using PhantomRender.ImGui;

namespace PhantomRender.ImGui.Native
{
    internal interface INativeOverlayBootstrap
    {
        void Initialize(OverlayMenu menu);
        void Shutdown();
    }
}

