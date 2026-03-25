using PhantomRender.ImGui.Core;
using PhantomRender.ImGui.Core.Renderers;
using PhantomRender.Core;

namespace PhantomRender.ImGui
{
    public sealed class InternalOverlay : Overlay
    {
        public InternalOverlay(GraphicsApi graphicsApi)
            : this(CreateDefaultRenderer(graphicsApi))
        {
        }

        public InternalOverlay(RendererBase renderer)
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
