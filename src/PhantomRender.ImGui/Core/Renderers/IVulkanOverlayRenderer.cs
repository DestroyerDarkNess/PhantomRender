using PhantomRender.Core.Hooks.Graphics.Vulkan;

namespace PhantomRender.ImGui.Core.Renderers
{
    public interface IVulkanOverlayRenderer : IOverlayRenderer
    {
        bool Initialize(VulkanPresentContext context);

        void Render(VulkanPresentContext context, ref VulkanPresentHookArgs hookArgs);
    }
}
