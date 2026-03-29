using System;
using VkNative = PhantomRender.Core.Native.Vulkan;

namespace PhantomRender.Core.Hooks.Graphics.Vulkan
{
    public struct VulkanPresentContext
    {
        public uint ApiVersion;
        public IntPtr Instance;
        public IntPtr PhysicalDevice;
        public IntPtr Device;
        public IntPtr Queue;
        public uint QueueFamilyIndex;
        public bool QueueFamilySupportsGraphics;
        public IntPtr Surface;
        public IntPtr Swapchain;
        public IntPtr WindowHandle;
        public uint MinImageCount;
        public uint ImageCount;
        public int ImageFormat;
        public uint Width;
        public uint Height;
        public uint ImageUsage;
        public uint ImageIndex;
    }

    public unsafe struct VulkanPresentHookArgs
    {
        internal IntPtr ReplacementWaitSemaphore;
        internal bool ReplaceWaitSemaphore;

        internal VulkanPresentHookArgs(IntPtr queue, VkNative.VkPresentInfoKHR* presentInfo)
        {
            Queue = queue;
            PresentInfo = presentInfo;
            ReplacementWaitSemaphore = IntPtr.Zero;
            ReplaceWaitSemaphore = false;
        }

        public IntPtr Queue { get; }

        public VkNative.VkPresentInfoKHR* PresentInfo { get; }

        public bool HasReplacementWaitSemaphore => ReplaceWaitSemaphore && ReplacementWaitSemaphore != IntPtr.Zero;

        public void SetWaitSemaphore(IntPtr semaphore)
        {
            ReplacementWaitSemaphore = semaphore;
            ReplaceWaitSemaphore = semaphore != IntPtr.Zero;
        }
    }

    public unsafe delegate void VulkanPresentCallback(ref VulkanPresentHookArgs args);
}
