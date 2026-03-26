using PhantomRender.Core;
using PhantomRender.ImGui.Core;
using PhantomRender.ImGui.Core.Renderers;

namespace PhantomRender.ImGui
{
    public sealed class ExternalOverlay : Overlay
    {
        public ExternalOverlay(GraphicsApi graphicsApi)
            : this(CreateDefaultRenderer(graphicsApi))
        {
        }

        public ExternalOverlay(RendererBase renderer)
            : base(renderer)
        {
            Renderer = renderer ?? throw new System.ArgumentNullException(nameof(renderer));
        }

        public RendererBase Renderer { get; }

        public bool Initialize(nint device, nint windowHandle)
        {
            return Renderer.Initialize(device, windowHandle);
        }

        public void BeginFrame()
        {
            Renderer.NewFrame();
        }

        public void RenderFrame()
        {
            Renderer.Render();
        }

        public void OnLostDevice()
        {
            Renderer.OnLostDevice();
        }

        public void OnResetDevice()
        {
            Renderer.OnResetDevice();
        }

        public override void Dispose()
        {
            Renderer.Dispose();
            base.Dispose();
        }
    }
}
