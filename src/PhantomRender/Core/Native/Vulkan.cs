using System;
using System.Runtime.InteropServices;

namespace PhantomRender.Core.Native
{
    public static class Vulkan
    {
        public const int VK_SUCCESS = 0;

        public const uint VK_TRUE = 1;
        public const uint VK_FALSE = 0;
        public const uint VK_QUEUE_FAMILY_IGNORED = 0xFFFFFFFF;
        public const uint VK_SUBPASS_EXTERNAL = 0xFFFFFFFF;

        public const int VK_STRUCTURE_TYPE_APPLICATION_INFO = 0;
        public const int VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO = 1;
        public const int VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO = 2;
        public const int VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO = 3;
        public const int VK_STRUCTURE_TYPE_SUBMIT_INFO = 4;
        public const int VK_STRUCTURE_TYPE_FENCE_CREATE_INFO = 8;
        public const int VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO = 9;
        public const int VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO = 15;
        public const int VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO = 37;
        public const int VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO = 38;
        public const int VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO = 39;
        public const int VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO = 40;
        public const int VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO = 42;
        public const int VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO = 43;
        public const int VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER = 45;
        public const int VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR = 1000001000;
        public const int VK_STRUCTURE_TYPE_PRESENT_INFO_KHR = 1000001001;
        public const int VK_STRUCTURE_TYPE_WIN32_SURFACE_CREATE_INFO_KHR = 1000009000;
        public const int VK_STRUCTURE_TYPE_DEVICE_QUEUE_INFO_2 = 1000145003;

        public const int VK_COMPONENT_SWIZZLE_IDENTITY = 0;
        public const int VK_IMAGE_VIEW_TYPE_2D = 1;
        public const int VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL = 2;
        public const int VK_IMAGE_LAYOUT_PRESENT_SRC_KHR = 1000001002;
        public const int VK_ATTACHMENT_LOAD_OP_LOAD = 0;
        public const int VK_ATTACHMENT_STORE_OP_STORE = 0;
        public const int VK_PIPELINE_BIND_POINT_GRAPHICS = 0;
        public const int VK_COMMAND_BUFFER_LEVEL_PRIMARY = 0;
        public const int VK_SUBPASS_CONTENTS_INLINE = 0;
        public const int VK_DESCRIPTOR_TYPE_SAMPLER = 0;
        public const int VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER = 1;
        public const int VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE = 2;
        public const int VK_DESCRIPTOR_TYPE_STORAGE_IMAGE = 3;
        public const int VK_DESCRIPTOR_TYPE_UNIFORM_TEXEL_BUFFER = 4;
        public const int VK_DESCRIPTOR_TYPE_STORAGE_TEXEL_BUFFER = 5;
        public const int VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER = 6;
        public const int VK_DESCRIPTOR_TYPE_STORAGE_BUFFER = 7;
        public const int VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER_DYNAMIC = 8;
        public const int VK_DESCRIPTOR_TYPE_STORAGE_BUFFER_DYNAMIC = 9;
        public const int VK_DESCRIPTOR_TYPE_INPUT_ATTACHMENT = 10;

        public const uint VK_DESCRIPTOR_POOL_CREATE_FREE_DESCRIPTOR_SET_BIT = 0x00000001;
        public const uint VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT = 0x00000100;
        public const uint VK_IMAGE_ASPECT_COLOR_BIT = 0x00000001;
        public const uint VK_SAMPLE_COUNT_1_BIT = 0x00000001;
        public const uint VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT = 0x00000010;
        public const uint VK_QUEUE_GRAPHICS_BIT = 0x00000001;
        public const uint VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT = 0x00000001;
        public const uint VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT = 0x00000400;
        public const uint VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT = 0x00002000;
        public const uint VK_FENCE_CREATE_SIGNALED_BIT = 0x00000001;
        public const uint VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        public struct VkExtent2D
        {
            public uint Width;
            public uint Height;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkExtent3D
        {
            public uint Width;
            public uint Height;
            public uint Depth;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkOffset2D
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkRect2D
        {
            public VkOffset2D Offset;
            public VkExtent2D Extent;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkApplicationInfo
        {
            public int SType;
            public IntPtr PNext;
            public IntPtr PApplicationName;
            public uint ApplicationVersion;
            public IntPtr PEngineName;
            public uint EngineVersion;
            public uint ApiVersion;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkInstanceCreateInfo
        {
            public int SType;
            public IntPtr PNext;
            public uint Flags;
            public IntPtr PApplicationInfo;
            public uint EnabledLayerCount;
            public IntPtr PpEnabledLayerNames;
            public uint EnabledExtensionCount;
            public IntPtr PpEnabledExtensionNames;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkQueueFamilyProperties
        {
            public uint QueueFlags;
            public uint QueueCount;
            public uint TimestampValidBits;
            public VkExtent3D MinImageTransferGranularity;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkDeviceQueueCreateInfo
        {
            public int SType;
            public IntPtr PNext;
            public uint Flags;
            public uint QueueFamilyIndex;
            public uint QueueCount;
            public IntPtr PQueuePriorities;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkDeviceCreateInfo
        {
            public int SType;
            public IntPtr PNext;
            public uint Flags;
            public uint QueueCreateInfoCount;
            public IntPtr PQueueCreateInfos;
            public uint EnabledLayerCount;
            public IntPtr PpEnabledLayerNames;
            public uint EnabledExtensionCount;
            public IntPtr PpEnabledExtensionNames;
            public IntPtr PEnabledFeatures;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkDeviceQueueInfo2
        {
            public int SType;
            public IntPtr PNext;
            public uint Flags;
            public uint QueueFamilyIndex;
            public uint QueueIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkWin32SurfaceCreateInfoKHR
        {
            public int SType;
            public IntPtr PNext;
            public uint Flags;
            public IntPtr HInstance;
            public IntPtr Hwnd;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkSwapchainCreateInfoKHR
        {
            public int SType;
            public IntPtr PNext;
            public uint Flags;
            public IntPtr Surface;
            public uint MinImageCount;
            public int ImageFormat;
            public int ImageColorSpace;
            public VkExtent2D ImageExtent;
            public uint ImageArrayLayers;
            public uint ImageUsage;
            public int ImageSharingMode;
            public uint QueueFamilyIndexCount;
            public IntPtr PQueueFamilyIndices;
            public int PreTransform;
            public int CompositeAlpha;
            public int PresentMode;
            public uint Clipped;
            public IntPtr OldSwapchain;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkPresentInfoKHR
        {
            public int SType;
            public IntPtr PNext;
            public uint WaitSemaphoreCount;
            public IntPtr PWaitSemaphores;
            public uint SwapchainCount;
            public IntPtr PSwapchains;
            public IntPtr PImageIndices;
            public IntPtr PResults;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkDescriptorPoolSize
        {
            public int Type;
            public uint DescriptorCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkDescriptorPoolCreateInfo
        {
            public int SType;
            public IntPtr PNext;
            public uint Flags;
            public uint MaxSets;
            public uint PoolSizeCount;
            public IntPtr PPoolSizes;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkAttachmentDescription
        {
            public uint Flags;
            public int Format;
            public uint Samples;
            public int LoadOp;
            public int StoreOp;
            public int StencilLoadOp;
            public int StencilStoreOp;
            public int InitialLayout;
            public int FinalLayout;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkAttachmentReference
        {
            public uint Attachment;
            public int Layout;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkFramebufferCreateInfo
        {
            public int SType;
            public IntPtr PNext;
            public uint Flags;
            public IntPtr RenderPass;
            public uint AttachmentCount;
            public IntPtr PAttachments;
            public uint Width;
            public uint Height;
            public uint Layers;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkSubpassDescription
        {
            public uint Flags;
            public int PipelineBindPoint;
            public uint InputAttachmentCount;
            public IntPtr PInputAttachments;
            public uint ColorAttachmentCount;
            public IntPtr PColorAttachments;
            public IntPtr PResolveAttachments;
            public IntPtr PDepthStencilAttachment;
            public uint PreserveAttachmentCount;
            public IntPtr PPreserveAttachments;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkSubpassDependency
        {
            public uint SrcSubpass;
            public uint DstSubpass;
            public uint SrcStageMask;
            public uint DstStageMask;
            public uint SrcAccessMask;
            public uint DstAccessMask;
            public uint DependencyFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkRenderPassCreateInfo
        {
            public int SType;
            public IntPtr PNext;
            public uint Flags;
            public uint AttachmentCount;
            public IntPtr PAttachments;
            public uint SubpassCount;
            public IntPtr PSubpasses;
            public uint DependencyCount;
            public IntPtr PDependencies;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkCommandPoolCreateInfo
        {
            public int SType;
            public IntPtr PNext;
            public uint Flags;
            public uint QueueFamilyIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkCommandBufferAllocateInfo
        {
            public int SType;
            public IntPtr PNext;
            public IntPtr CommandPool;
            public int Level;
            public uint CommandBufferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkCommandBufferBeginInfo
        {
            public int SType;
            public IntPtr PNext;
            public uint Flags;
            public IntPtr PInheritanceInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkFenceCreateInfo
        {
            public int SType;
            public IntPtr PNext;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkSemaphoreCreateInfo
        {
            public int SType;
            public IntPtr PNext;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkComponentMapping
        {
            public int R;
            public int G;
            public int B;
            public int A;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkImageSubresourceRange
        {
            public uint AspectMask;
            public uint BaseMipLevel;
            public uint LevelCount;
            public uint BaseArrayLayer;
            public uint LayerCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkImageViewCreateInfo
        {
            public int SType;
            public IntPtr PNext;
            public uint Flags;
            public IntPtr Image;
            public int ViewType;
            public int Format;
            public VkComponentMapping Components;
            public VkImageSubresourceRange SubresourceRange;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkImageMemoryBarrier
        {
            public int SType;
            public IntPtr PNext;
            public uint SrcAccessMask;
            public uint DstAccessMask;
            public int OldLayout;
            public int NewLayout;
            public uint SrcQueueFamilyIndex;
            public uint DstQueueFamilyIndex;
            public IntPtr Image;
            public VkImageSubresourceRange SubresourceRange;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkSubmitInfo
        {
            public int SType;
            public IntPtr PNext;
            public uint WaitSemaphoreCount;
            public IntPtr PWaitSemaphores;
            public IntPtr PWaitDstStageMask;
            public uint CommandBufferCount;
            public IntPtr PCommandBuffers;
            public uint SignalSemaphoreCount;
            public IntPtr PSignalSemaphores;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct VkRenderPassBeginInfo
        {
            public int SType;
            public IntPtr PNext;
            public IntPtr RenderPass;
            public IntPtr Framebuffer;
            public VkRect2D RenderArea;
            public uint ClearValueCount;
            public IntPtr PClearValues;
        }
    }
}
