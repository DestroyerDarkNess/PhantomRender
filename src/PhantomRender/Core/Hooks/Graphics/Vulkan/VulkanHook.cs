using System;
using PhantomRender.Core.Hooks;

namespace PhantomRender.Core.Hooks.Graphics.Vulkan
{
    public class VulkanHook : IATHook
    {
        public VulkanHook(string targetModule, IntPtr newFunctionAddress) 
            : base(targetModule, "vulkan-1.dll", "vkQueuePresentKHR", newFunctionAddress)
        {
        }
    }
}
