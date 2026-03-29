using System;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui.Backends.Vulkan;
using Hexa.NET.ImGui.Backends.Win32;
using PhantomRender.Core;
using PhantomRender.Core.Hooks.Graphics.Vulkan;
using PhantomRender.Core.Native;
using HexaImGui = Hexa.NET.ImGui.ImGui;

namespace PhantomRender.ImGui.Core.Renderers
{
    public sealed unsafe class VulkanRenderer : RendererBase, IVulkanOverlayRenderer
    {
        private const uint DescriptorPoolSize = 128;

        [StructLayout(LayoutKind.Sequential)]
        private struct FrameResource
        {
            public IntPtr Image;
            public IntPtr ImageView;
            public IntPtr Framebuffer;
            public IntPtr CommandBuffer;
            public IntPtr Fence;
            public IntPtr RenderCompleteSemaphore;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetSwapchainImagesKHRDelegate(IntPtr device, IntPtr swapchain, ref uint swapchainImageCount, IntPtr swapchainImages);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateImageViewDelegate(IntPtr device, ref Vulkan.VkImageViewCreateInfo createInfo, IntPtr allocator, out IntPtr imageView);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void DestroyImageViewDelegate(IntPtr device, IntPtr imageView, IntPtr allocator);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateRenderPassDelegate(IntPtr device, ref Vulkan.VkRenderPassCreateInfo createInfo, IntPtr allocator, out IntPtr renderPass);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void DestroyRenderPassDelegate(IntPtr device, IntPtr renderPass, IntPtr allocator);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateFramebufferDelegate(IntPtr device, ref Vulkan.VkFramebufferCreateInfo createInfo, IntPtr allocator, out IntPtr framebuffer);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void DestroyFramebufferDelegate(IntPtr device, IntPtr framebuffer, IntPtr allocator);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateCommandPoolDelegate(IntPtr device, ref Vulkan.VkCommandPoolCreateInfo createInfo, IntPtr allocator, out IntPtr commandPool);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void DestroyCommandPoolDelegate(IntPtr device, IntPtr commandPool, IntPtr allocator);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int AllocateCommandBuffersDelegate(IntPtr device, ref Vulkan.VkCommandBufferAllocateInfo allocateInfo, IntPtr commandBuffers);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void FreeCommandBuffersDelegate(IntPtr device, IntPtr commandPool, uint commandBufferCount, IntPtr commandBuffers);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateFenceDelegate(IntPtr device, ref Vulkan.VkFenceCreateInfo createInfo, IntPtr allocator, out IntPtr fence);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void DestroyFenceDelegate(IntPtr device, IntPtr fence, IntPtr allocator);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateSemaphoreDelegate(IntPtr device, ref Vulkan.VkSemaphoreCreateInfo createInfo, IntPtr allocator, out IntPtr semaphore);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void DestroySemaphoreDelegate(IntPtr device, IntPtr semaphore, IntPtr allocator);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int WaitForFencesDelegate(IntPtr device, uint fenceCount, IntPtr fences, uint waitAll, ulong timeout);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ResetFencesDelegate(IntPtr device, uint fenceCount, IntPtr fences);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ResetCommandBufferDelegate(IntPtr commandBuffer, uint flags);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int BeginCommandBufferDelegate(IntPtr commandBuffer, ref Vulkan.VkCommandBufferBeginInfo beginInfo);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EndCommandBufferDelegate(IntPtr commandBuffer);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CmdPipelineBarrierDelegate(IntPtr commandBuffer, uint srcStageMask, uint dstStageMask, uint dependencyFlags, uint memoryBarrierCount, IntPtr memoryBarriers, uint bufferMemoryBarrierCount, IntPtr bufferMemoryBarriers, uint imageMemoryBarrierCount, ref Vulkan.VkImageMemoryBarrier imageMemoryBarriers);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CmdBeginRenderPassDelegate(IntPtr commandBuffer, ref Vulkan.VkRenderPassBeginInfo beginInfo, int contents);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CmdEndRenderPassDelegate(IntPtr commandBuffer);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueueSubmitDelegate(IntPtr queue, uint submitCount, ref Vulkan.VkSubmitInfo submitInfo, IntPtr fence);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DeviceWaitIdleDelegate(IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr GetDeviceProcAddrDelegate(IntPtr device, IntPtr name);

        private readonly object _sync = new object();
        private readonly Action _backendNewFrameAction;
        private bool _frameStarted;

        private IntPtr _instance;
        private IntPtr _physicalDevice;
        private IntPtr _device;
        private IntPtr _queue;
        private IntPtr _swapchain;
        private IntPtr _commandPool;
        private IntPtr _renderPass;
        private uint _apiVersion;
        private uint _queueFamilyIndex;
        private uint _minImageCount;
        private uint _imageCount;
        private uint _width;
        private uint _height;
        private int _imageFormat;
        private FrameResource[] _frames = Array.Empty<FrameResource>();

        private GetSwapchainImagesKHRDelegate _getSwapchainImages;
        private CreateImageViewDelegate _createImageView;
        private DestroyImageViewDelegate _destroyImageView;
        private CreateRenderPassDelegate _createRenderPass;
        private DestroyRenderPassDelegate _destroyRenderPass;
        private CreateFramebufferDelegate _createFramebuffer;
        private DestroyFramebufferDelegate _destroyFramebuffer;
        private CreateCommandPoolDelegate _createCommandPool;
        private DestroyCommandPoolDelegate _destroyCommandPool;
        private AllocateCommandBuffersDelegate _allocateCommandBuffers;
        private FreeCommandBuffersDelegate _freeCommandBuffers;
        private CreateFenceDelegate _createFence;
        private DestroyFenceDelegate _destroyFence;
        private CreateSemaphoreDelegate _createSemaphore;
        private DestroySemaphoreDelegate _destroySemaphore;
        private WaitForFencesDelegate _waitForFences;
        private ResetFencesDelegate _resetFences;
        private ResetCommandBufferDelegate _resetCommandBuffer;
        private BeginCommandBufferDelegate _beginCommandBuffer;
        private EndCommandBufferDelegate _endCommandBuffer;
        private CmdPipelineBarrierDelegate _cmdPipelineBarrier;
        private CmdBeginRenderPassDelegate _cmdBeginRenderPass;
        private CmdEndRenderPassDelegate _cmdEndRenderPass;
        private QueueSubmitDelegate _queueSubmit;
        private DeviceWaitIdleDelegate _deviceWaitIdle;

        public VulkanRenderer()
            : base(GraphicsApi.Vulkan)
        {
            _backendNewFrameAction = BackendNewFrame;
        }

        public override bool Initialize(nint device, nint windowHandle)
        {
            return false;
        }

        public bool Initialize(VulkanPresentContext context)
        {
            lock (_sync)
            {
                return InitializeCore(context);
            }
        }

        public override void NewFrame()
        {
            lock (_sync)
            {
                if (!IsInitialized || _frameStarted || Context.IsNull)
                {
                    return;
                }

                try
                {
                    BeginFrameCore(_backendNewFrameAction);
                    _frameStarted = true;
                }
                catch (Exception ex)
                {
                    ReportRuntimeError("Vulkan.NewFrame", ex);
                }
            }
        }

        public override void Render()
        {
        }

        public void Render(VulkanPresentContext context, ref VulkanPresentHookArgs hookArgs)
        {
            lock (_sync)
            {
                if (!IsInitialized || !_frameStarted || Context.IsNull)
                {
                    return;
                }

                try
                {
                    SetSharedContextsCurrent();
                    RaiseRender();
                    RaiseOverlayRender();
                    HexaImGui.Render();
                    RenderBackend(context, ref hookArgs);
                }
                catch (Exception ex)
                {
                    ReportRuntimeError("Vulkan.Render", ex);
                }
                finally
                {
                    _frameStarted = false;
                }
            }
        }

        public override void OnLostDevice()
        {
        }

        public override void OnResetDevice()
        {
        }

        public override void Dispose()
        {
            lock (_sync)
            {
                DisposeCore();
            }
        }

        private bool InitializeCore(VulkanPresentContext context)
        {
            if (context.Instance == IntPtr.Zero ||
                context.PhysicalDevice == IntPtr.Zero ||
                context.Device == IntPtr.Zero ||
                context.Queue == IntPtr.Zero ||
                context.Swapchain == IntPtr.Zero ||
                context.WindowHandle == IntPtr.Zero)
            {
                return false;
            }

            if (!context.QueueFamilySupportsGraphics)
            {
                Console.WriteLine("[PhantomRender] Vulkan: present queue family does not support graphics. This path currently requires a graphics-capable present queue.");
                return false;
            }

            if ((context.ImageUsage & Vulkan.VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT) == 0)
            {
                Console.WriteLine("[PhantomRender] Vulkan: swapchain images do not expose VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT.");
                return false;
            }

            if (IsInitialized && !RequiresFullReinitialize(context))
            {
                return true;
            }

            if (IsInitialized)
            {
                DisposeCore();
            }

            try
            {
                if (!ResolveFunctions(context.Device))
                {
                    return false;
                }

                RaiseRendererInitializing(context.Device, context.WindowHandle);
                InitializeImGui(context.WindowHandle);

                ImGuiImplVulkan.SetCurrentContext(Context);

                var initInfo = new ImGuiImplVulkanInitInfo
                {
                    ApiVersion = context.ApiVersion,
                    Instance = context.Instance,
                    PhysicalDevice = context.PhysicalDevice,
                    Device = context.Device,
                    QueueFamily = context.QueueFamilyIndex,
                    Queue = context.Queue,
                    DescriptorPool = default,
                    DescriptorPoolSize = DescriptorPoolSize,
                    RenderPass = context.ImageFormat != 0 ? _renderPass : default,
                    MinImageCount = Math.Max(context.MinImageCount, 2u),
                    ImageCount = Math.Max(context.ImageCount, Math.Max(context.MinImageCount, 2u)),
                    MSAASamples = Vulkan.VK_SAMPLE_COUNT_1_BIT,
                    UseDynamicRendering = 0,
                };

                _instance = context.Instance;
                _physicalDevice = context.PhysicalDevice;
                _device = context.Device;
                _queue = context.Queue;
                _queueFamilyIndex = context.QueueFamilyIndex;
                _swapchain = context.Swapchain;
                _apiVersion = context.ApiVersion;
                _imageFormat = context.ImageFormat;
                _width = context.Width;
                _height = context.Height;
                _minImageCount = Math.Max(context.MinImageCount, 2u);
                _imageCount = Math.Max(context.ImageCount, _minImageCount);

                if (!CreateSwapchainResources(context))
                {
                    DisposeCore();
                    return false;
                }

                initInfo.RenderPass = _renderPass;
                initInfo.ImageCount = _imageCount;
                initInfo.MinImageCount = _minImageCount;

                if (!ImGuiImplVulkan.Init(ref initInfo))
                {
                    DisposeCore();
                    return false;
                }

                IsInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                ReportRuntimeError("Vulkan.Initialize", ex);
                DisposeCore();
                return false;
            }
        }

        private bool RequiresFullReinitialize(VulkanPresentContext context)
        {
            return _instance != context.Instance ||
                   _physicalDevice != context.PhysicalDevice ||
                   _device != context.Device ||
                   _queue != context.Queue ||
                   _queueFamilyIndex != context.QueueFamilyIndex ||
                   WindowHandle != context.WindowHandle ||
                   _swapchain != context.Swapchain ||
                   _imageFormat != context.ImageFormat ||
                   _width != context.Width ||
                   _height != context.Height ||
                   _imageCount != Math.Max(context.ImageCount, Math.Max(context.MinImageCount, 2u)) ||
                   _minImageCount != Math.Max(context.MinImageCount, 2u);
        }

        private void BackendNewFrame()
        {
            ImGuiImplVulkan.SetCurrentContext(Context);
            ImGuiImplVulkan.NewFrame();
            ImGuiImplWin32.SetCurrentContext(Context);
            ImGuiImplWin32.NewFrame();
        }

        private void RenderBackend(VulkanPresentContext context, ref VulkanPresentHookArgs hookArgs)
        {
            if (hookArgs.PresentInfo == null || context.ImageIndex >= _frames.Length)
            {
                return;
            }

            Hexa.NET.ImGui.ImDrawDataPtr drawData = HexaImGui.GetDrawData();
            if (drawData.Handle == null || drawData.CmdListsCount == 0)
            {
                return;
            }

            ref FrameResource frame = ref _frames[context.ImageIndex];
            if (frame.CommandBuffer == IntPtr.Zero || frame.Framebuffer == IntPtr.Zero)
            {
                return;
            }

            if (!WaitAndResetFrame(ref frame))
            {
                return;
            }

            if (!RecordCommandBuffer(ref frame, drawData, context))
            {
                return;
            }

            if (SubmitFrame(ref frame, hookArgs))
            {
                hookArgs.SetWaitSemaphore(frame.RenderCompleteSemaphore);
            }
        }

        private bool CreateSwapchainResources(VulkanPresentContext context)
        {
            Vulkan.VkCommandPoolCreateInfo commandPoolInfo = new Vulkan.VkCommandPoolCreateInfo
            {
                SType = Vulkan.VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO,
                QueueFamilyIndex = context.QueueFamilyIndex,
                Flags = Vulkan.VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT,
            };

            if (_createCommandPool(_device, ref commandPoolInfo, IntPtr.Zero, out _commandPool) < 0 || _commandPool == IntPtr.Zero)
            {
                return false;
            }

            uint imageCount = 0;
            if (_getSwapchainImages(_device, _swapchain, ref imageCount, IntPtr.Zero) < 0 || imageCount == 0)
            {
                return false;
            }

            IntPtr imagesBuffer = Marshal.AllocHGlobal((int)(imageCount * IntPtr.Size));
            try
            {
                if (_getSwapchainImages(_device, _swapchain, ref imageCount, imagesBuffer) < 0 || imageCount == 0)
                {
                    return false;
                }

                _imageCount = Math.Max(imageCount, _minImageCount);
                _frames = new FrameResource[imageCount];
                IntPtr[] images = new IntPtr[imageCount];
                Marshal.Copy(imagesBuffer, images, 0, (int)imageCount);

                if (!CreateRenderPass())
                {
                    return false;
                }

                if (!AllocateCommandBuffers(imageCount))
                {
                    return false;
                }

                for (int i = 0; i < images.Length; i++)
                {
                    _frames[i].Image = images[i];
                    if (!CreateFrameResources(i))
                    {
                        return false;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(imagesBuffer);
            }

            return true;
        }

        private bool CreateRenderPass()
        {
            Vulkan.VkAttachmentDescription attachment = new Vulkan.VkAttachmentDescription
            {
                Format = _imageFormat,
                Samples = Vulkan.VK_SAMPLE_COUNT_1_BIT,
                LoadOp = Vulkan.VK_ATTACHMENT_LOAD_OP_LOAD,
                StoreOp = Vulkan.VK_ATTACHMENT_STORE_OP_STORE,
                StencilLoadOp = Vulkan.VK_ATTACHMENT_LOAD_OP_LOAD,
                StencilStoreOp = Vulkan.VK_ATTACHMENT_STORE_OP_STORE,
                InitialLayout = Vulkan.VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,
                FinalLayout = Vulkan.VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,
            };

            Vulkan.VkAttachmentReference colorAttachment = new Vulkan.VkAttachmentReference
            {
                Attachment = 0,
                Layout = Vulkan.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,
            };

            Vulkan.VkSubpassDescription subpass = new Vulkan.VkSubpassDescription
            {
                PipelineBindPoint = Vulkan.VK_PIPELINE_BIND_POINT_GRAPHICS,
                ColorAttachmentCount = 1,
                PColorAttachments = AllocateStruct(ref colorAttachment),
            };

            Vulkan.VkSubpassDependency dependency = new Vulkan.VkSubpassDependency
            {
                SrcSubpass = Vulkan.VK_SUBPASS_EXTERNAL,
                DstSubpass = 0,
                SrcStageMask = Vulkan.VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
                DstStageMask = Vulkan.VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
                DstAccessMask = Vulkan.VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT,
            };

            IntPtr attachmentsPtr = IntPtr.Zero;
            IntPtr subpassesPtr = IntPtr.Zero;
            IntPtr dependenciesPtr = IntPtr.Zero;
            try
            {
                attachmentsPtr = AllocateStruct(ref attachment);
                subpassesPtr = AllocateStruct(ref subpass);
                dependenciesPtr = AllocateStruct(ref dependency);

                Vulkan.VkRenderPassCreateInfo createInfo = new Vulkan.VkRenderPassCreateInfo
                {
                    SType = Vulkan.VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO,
                    AttachmentCount = 1,
                    PAttachments = attachmentsPtr,
                    SubpassCount = 1,
                    PSubpasses = subpassesPtr,
                    DependencyCount = 1,
                    PDependencies = dependenciesPtr,
                };

                return _createRenderPass(_device, ref createInfo, IntPtr.Zero, out _renderPass) >= 0 && _renderPass != IntPtr.Zero;
            }
            finally
            {
                FreeHGlobal(subpass.PColorAttachments);
                FreeHGlobal(attachmentsPtr);
                FreeHGlobal(subpassesPtr);
                FreeHGlobal(dependenciesPtr);
            }
        }

        private bool AllocateCommandBuffers(uint imageCount)
        {
            Vulkan.VkCommandBufferAllocateInfo allocateInfo = new Vulkan.VkCommandBufferAllocateInfo
            {
                SType = Vulkan.VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO,
                CommandPool = _commandPool,
                Level = Vulkan.VK_COMMAND_BUFFER_LEVEL_PRIMARY,
                CommandBufferCount = imageCount,
            };

            IntPtr commandBuffers = Marshal.AllocHGlobal((int)(imageCount * IntPtr.Size));
            try
            {
                if (_allocateCommandBuffers(_device, ref allocateInfo, commandBuffers) < 0)
                {
                    return false;
                }

                IntPtr[] commands = new IntPtr[imageCount];
                Marshal.Copy(commandBuffers, commands, 0, (int)imageCount);
                for (int i = 0; i < commands.Length; i++)
                {
                    _frames[i].CommandBuffer = commands[i];
                }

                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(commandBuffers);
            }
        }

        private bool CreateFrameResources(int index)
        {
            ref FrameResource frame = ref _frames[index];

            Vulkan.VkImageViewCreateInfo imageViewInfo = new Vulkan.VkImageViewCreateInfo
            {
                SType = Vulkan.VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO,
                Image = frame.Image,
                ViewType = Vulkan.VK_IMAGE_VIEW_TYPE_2D,
                Format = _imageFormat,
                Components = new Vulkan.VkComponentMapping
                {
                    R = Vulkan.VK_COMPONENT_SWIZZLE_IDENTITY,
                    G = Vulkan.VK_COMPONENT_SWIZZLE_IDENTITY,
                    B = Vulkan.VK_COMPONENT_SWIZZLE_IDENTITY,
                    A = Vulkan.VK_COMPONENT_SWIZZLE_IDENTITY,
                },
                SubresourceRange = new Vulkan.VkImageSubresourceRange
                {
                    AspectMask = Vulkan.VK_IMAGE_ASPECT_COLOR_BIT,
                    LevelCount = 1,
                    LayerCount = 1,
                },
            };

            if (_createImageView(_device, ref imageViewInfo, IntPtr.Zero, out frame.ImageView) < 0 || frame.ImageView == IntPtr.Zero)
            {
                return false;
            }

            IntPtr attachmentPtr = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(attachmentPtr, frame.ImageView);
                Vulkan.VkFramebufferCreateInfo framebufferInfo = new Vulkan.VkFramebufferCreateInfo
                {
                    SType = Vulkan.VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO,
                    RenderPass = _renderPass,
                    AttachmentCount = 1,
                    PAttachments = attachmentPtr,
                    Width = _width,
                    Height = _height,
                    Layers = 1,
                };

                if (_createFramebuffer(_device, ref framebufferInfo, IntPtr.Zero, out frame.Framebuffer) < 0 || frame.Framebuffer == IntPtr.Zero)
                {
                    return false;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(attachmentPtr);
            }

            Vulkan.VkFenceCreateInfo fenceInfo = new Vulkan.VkFenceCreateInfo
            {
                SType = Vulkan.VK_STRUCTURE_TYPE_FENCE_CREATE_INFO,
                Flags = Vulkan.VK_FENCE_CREATE_SIGNALED_BIT,
            };

            if (_createFence(_device, ref fenceInfo, IntPtr.Zero, out frame.Fence) < 0 || frame.Fence == IntPtr.Zero)
            {
                return false;
            }

            Vulkan.VkSemaphoreCreateInfo semaphoreInfo = new Vulkan.VkSemaphoreCreateInfo
            {
                SType = Vulkan.VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO,
            };

            return _createSemaphore(_device, ref semaphoreInfo, IntPtr.Zero, out frame.RenderCompleteSemaphore) >= 0 &&
                   frame.RenderCompleteSemaphore != IntPtr.Zero;
        }

        private bool WaitAndResetFrame(ref FrameResource frame)
        {
            IntPtr fencePtr = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(fencePtr, frame.Fence);
                if (_waitForFences(_device, 1, fencePtr, Vulkan.VK_TRUE, ulong.MaxValue) < 0)
                {
                    return false;
                }

                if (_resetFences(_device, 1, fencePtr) < 0)
                {
                    return false;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(fencePtr);
            }

            return _resetCommandBuffer(frame.CommandBuffer, 0) >= 0;
        }

        private bool RecordCommandBuffer(ref FrameResource frame, Hexa.NET.ImGui.ImDrawDataPtr drawData, VulkanPresentContext context)
        {
            Vulkan.VkCommandBufferBeginInfo beginInfo = new Vulkan.VkCommandBufferBeginInfo
            {
                SType = Vulkan.VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO,
            };

            if (_beginCommandBuffer(frame.CommandBuffer, ref beginInfo) < 0)
            {
                return false;
            }

            Vulkan.VkImageMemoryBarrier barrier = new Vulkan.VkImageMemoryBarrier
            {
                SType = Vulkan.VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER,
                OldLayout = Vulkan.VK_IMAGE_LAYOUT_PRESENT_SRC_KHR,
                NewLayout = Vulkan.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,
                DstAccessMask = Vulkan.VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT,
                SrcQueueFamilyIndex = Vulkan.VK_QUEUE_FAMILY_IGNORED,
                DstQueueFamilyIndex = Vulkan.VK_QUEUE_FAMILY_IGNORED,
                Image = frame.Image,
                SubresourceRange = new Vulkan.VkImageSubresourceRange
                {
                    AspectMask = Vulkan.VK_IMAGE_ASPECT_COLOR_BIT,
                    LevelCount = 1,
                    LayerCount = 1,
                },
            };

            _cmdPipelineBarrier(
                frame.CommandBuffer,
                Vulkan.VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
                Vulkan.VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
                0,
                0,
                IntPtr.Zero,
                0,
                IntPtr.Zero,
                1,
                ref barrier);

            Vulkan.VkRenderPassBeginInfo renderPassBeginInfo = new Vulkan.VkRenderPassBeginInfo
            {
                SType = Vulkan.VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO,
                RenderPass = _renderPass,
                Framebuffer = frame.Framebuffer,
                RenderArea = new Vulkan.VkRect2D
                {
                    Offset = new Vulkan.VkOffset2D(),
                    Extent = new Vulkan.VkExtent2D
                    {
                        Width = _width,
                        Height = _height,
                    },
                },
            };

            _cmdBeginRenderPass(frame.CommandBuffer, ref renderPassBeginInfo, Vulkan.VK_SUBPASS_CONTENTS_INLINE);
            ImGuiImplVulkan.SetCurrentContext(Context);
            ImGuiImplVulkan.RenderDrawData(drawData, frame.CommandBuffer, default);
            _cmdEndRenderPass(frame.CommandBuffer);

            barrier.OldLayout = Vulkan.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
            barrier.NewLayout = Vulkan.VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
            barrier.SrcAccessMask = Vulkan.VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
            barrier.DstAccessMask = 0;

            _cmdPipelineBarrier(
                frame.CommandBuffer,
                Vulkan.VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
                Vulkan.VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
                0,
                0,
                IntPtr.Zero,
                0,
                IntPtr.Zero,
                1,
                ref barrier);

            return _endCommandBuffer(frame.CommandBuffer) >= 0;
        }

        private bool SubmitFrame(ref FrameResource frame, VulkanPresentHookArgs hookArgs)
        {
            IntPtr commandBuffersPtr = Marshal.AllocHGlobal(IntPtr.Size);
            IntPtr signalSemaphoresPtr = Marshal.AllocHGlobal(IntPtr.Size);
            IntPtr stageMaskPtr = IntPtr.Zero;
            try
            {
                Marshal.WriteIntPtr(commandBuffersPtr, frame.CommandBuffer);
                Marshal.WriteIntPtr(signalSemaphoresPtr, frame.RenderCompleteSemaphore);

                Vulkan.VkSubmitInfo submitInfo = new Vulkan.VkSubmitInfo
                {
                    SType = Vulkan.VK_STRUCTURE_TYPE_SUBMIT_INFO,
                    CommandBufferCount = 1,
                    PCommandBuffers = commandBuffersPtr,
                    SignalSemaphoreCount = 1,
                    PSignalSemaphores = signalSemaphoresPtr,
                    WaitSemaphoreCount = hookArgs.PresentInfo->WaitSemaphoreCount,
                    PWaitSemaphores = hookArgs.PresentInfo->PWaitSemaphores,
                };

                if (submitInfo.WaitSemaphoreCount > 0 && submitInfo.PWaitSemaphores != IntPtr.Zero)
                {
                    stageMaskPtr = Marshal.AllocHGlobal((int)(submitInfo.WaitSemaphoreCount * sizeof(uint)));
                    for (int i = 0; i < submitInfo.WaitSemaphoreCount; i++)
                    {
                        Marshal.WriteInt32(stageMaskPtr + (i * sizeof(uint)), unchecked((int)Vulkan.VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT));
                    }

                    submitInfo.PWaitDstStageMask = stageMaskPtr;
                }

                return _queueSubmit(_queue, 1, ref submitInfo, frame.Fence) >= 0;
            }
            finally
            {
                if (stageMaskPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(stageMaskPtr);
                }

                Marshal.FreeHGlobal(commandBuffersPtr);
                Marshal.FreeHGlobal(signalSemaphoresPtr);
            }
        }

        private bool ResolveFunctions(IntPtr device)
        {
            _getSwapchainImages = ResolveDeviceDelegate<GetSwapchainImagesKHRDelegate>(device, "vkGetSwapchainImagesKHR");
            _createImageView = ResolveDeviceDelegate<CreateImageViewDelegate>(device, "vkCreateImageView");
            _destroyImageView = ResolveDeviceDelegate<DestroyImageViewDelegate>(device, "vkDestroyImageView");
            _createRenderPass = ResolveDeviceDelegate<CreateRenderPassDelegate>(device, "vkCreateRenderPass");
            _destroyRenderPass = ResolveDeviceDelegate<DestroyRenderPassDelegate>(device, "vkDestroyRenderPass");
            _createFramebuffer = ResolveDeviceDelegate<CreateFramebufferDelegate>(device, "vkCreateFramebuffer");
            _destroyFramebuffer = ResolveDeviceDelegate<DestroyFramebufferDelegate>(device, "vkDestroyFramebuffer");
            _createCommandPool = ResolveDeviceDelegate<CreateCommandPoolDelegate>(device, "vkCreateCommandPool");
            _destroyCommandPool = ResolveDeviceDelegate<DestroyCommandPoolDelegate>(device, "vkDestroyCommandPool");
            _allocateCommandBuffers = ResolveDeviceDelegate<AllocateCommandBuffersDelegate>(device, "vkAllocateCommandBuffers");
            _freeCommandBuffers = ResolveDeviceDelegate<FreeCommandBuffersDelegate>(device, "vkFreeCommandBuffers");
            _createFence = ResolveDeviceDelegate<CreateFenceDelegate>(device, "vkCreateFence");
            _destroyFence = ResolveDeviceDelegate<DestroyFenceDelegate>(device, "vkDestroyFence");
            _createSemaphore = ResolveDeviceDelegate<CreateSemaphoreDelegate>(device, "vkCreateSemaphore");
            _destroySemaphore = ResolveDeviceDelegate<DestroySemaphoreDelegate>(device, "vkDestroySemaphore");
            _waitForFences = ResolveDeviceDelegate<WaitForFencesDelegate>(device, "vkWaitForFences");
            _resetFences = ResolveDeviceDelegate<ResetFencesDelegate>(device, "vkResetFences");
            _resetCommandBuffer = ResolveDeviceDelegate<ResetCommandBufferDelegate>(device, "vkResetCommandBuffer");
            _beginCommandBuffer = ResolveDeviceDelegate<BeginCommandBufferDelegate>(device, "vkBeginCommandBuffer");
            _endCommandBuffer = ResolveDeviceDelegate<EndCommandBufferDelegate>(device, "vkEndCommandBuffer");
            _cmdPipelineBarrier = ResolveDeviceDelegate<CmdPipelineBarrierDelegate>(device, "vkCmdPipelineBarrier");
            _cmdBeginRenderPass = ResolveDeviceDelegate<CmdBeginRenderPassDelegate>(device, "vkCmdBeginRenderPass");
            _cmdEndRenderPass = ResolveDeviceDelegate<CmdEndRenderPassDelegate>(device, "vkCmdEndRenderPass");
            _queueSubmit = ResolveDeviceDelegate<QueueSubmitDelegate>(device, "vkQueueSubmit");
            _deviceWaitIdle = ResolveDeviceDelegate<DeviceWaitIdleDelegate>(device, "vkDeviceWaitIdle");

            return _getSwapchainImages != null &&
                   _createImageView != null &&
                   _destroyImageView != null &&
                   _createRenderPass != null &&
                   _destroyRenderPass != null &&
                   _createFramebuffer != null &&
                   _destroyFramebuffer != null &&
                   _createCommandPool != null &&
                   _destroyCommandPool != null &&
                   _allocateCommandBuffers != null &&
                   _freeCommandBuffers != null &&
                   _createFence != null &&
                   _destroyFence != null &&
                   _createSemaphore != null &&
                   _destroySemaphore != null &&
                   _waitForFences != null &&
                   _resetFences != null &&
                   _resetCommandBuffer != null &&
                   _beginCommandBuffer != null &&
                   _endCommandBuffer != null &&
                   _cmdPipelineBarrier != null &&
                   _cmdBeginRenderPass != null &&
                   _cmdEndRenderPass != null &&
                   _queueSubmit != null &&
                   _deviceWaitIdle != null;
        }

        private void DisposeCore()
        {
            try
            {
                if (_device != IntPtr.Zero)
                {
                    _deviceWaitIdle?.Invoke(_device);
                }
            }
            catch
            {
            }

            try
            {
                if (!Context.IsNull)
                {
                    ImGuiImplVulkan.SetCurrentContext(Context);
                    ImGuiImplVulkan.Shutdown();
                }
            }
            catch
            {
            }

            DestroySwapchainResources();

            ShutdownImGui();

            _instance = IntPtr.Zero;
            _physicalDevice = IntPtr.Zero;
            _device = IntPtr.Zero;
            _queue = IntPtr.Zero;
            _swapchain = IntPtr.Zero;
            _commandPool = IntPtr.Zero;
            _renderPass = IntPtr.Zero;
            _apiVersion = 0;
            _queueFamilyIndex = 0;
            _minImageCount = 0;
            _imageCount = 0;
            _width = 0;
            _height = 0;
            _imageFormat = 0;
            _frames = Array.Empty<FrameResource>();
            _frameStarted = false;
            IsInitialized = false;
        }

        private void DestroySwapchainResources()
        {
            if (_device == IntPtr.Zero)
            {
                return;
            }

            for (int i = 0; i < _frames.Length; i++)
            {
                if (_frames[i].RenderCompleteSemaphore != IntPtr.Zero)
                {
                    _destroySemaphore?.Invoke(_device, _frames[i].RenderCompleteSemaphore, IntPtr.Zero);
                    _frames[i].RenderCompleteSemaphore = IntPtr.Zero;
                }

                if (_frames[i].Fence != IntPtr.Zero)
                {
                    _destroyFence?.Invoke(_device, _frames[i].Fence, IntPtr.Zero);
                    _frames[i].Fence = IntPtr.Zero;
                }

                if (_frames[i].Framebuffer != IntPtr.Zero)
                {
                    _destroyFramebuffer?.Invoke(_device, _frames[i].Framebuffer, IntPtr.Zero);
                    _frames[i].Framebuffer = IntPtr.Zero;
                }

                if (_frames[i].ImageView != IntPtr.Zero)
                {
                    _destroyImageView?.Invoke(_device, _frames[i].ImageView, IntPtr.Zero);
                    _frames[i].ImageView = IntPtr.Zero;
                }
            }

            if (_commandPool != IntPtr.Zero)
            {
                IntPtr commandBuffers = Marshal.AllocHGlobal(_frames.Length * IntPtr.Size);
                try
                {
                    for (int i = 0; i < _frames.Length; i++)
                    {
                        Marshal.WriteIntPtr(commandBuffers, i * IntPtr.Size, _frames[i].CommandBuffer);
                    }

                    if (_frames.Length > 0)
                    {
                        _freeCommandBuffers?.Invoke(_device, _commandPool, (uint)_frames.Length, commandBuffers);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(commandBuffers);
                }

                _destroyCommandPool?.Invoke(_device, _commandPool, IntPtr.Zero);
            }

            if (_renderPass != IntPtr.Zero)
            {
                _destroyRenderPass?.Invoke(_device, _renderPass, IntPtr.Zero);
            }
        }

        private static IntPtr AllocateStruct<T>(ref T value)
            where T : struct
        {
            IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
            Marshal.StructureToPtr(value, pointer, false);
            return pointer;
        }

        private static void FreeHGlobal(IntPtr pointer)
        {
            if (pointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        private TDelegate ResolveDeviceDelegate<TDelegate>(IntPtr device, string functionName)
            where TDelegate : Delegate
        {
            IntPtr namePointer = IntPtr.Zero;
            try
            {
                namePointer = Marshal.StringToHGlobalAnsi(functionName);
                IntPtr function = vkGetDeviceProcAddr(device, namePointer);
                if (function == IntPtr.Zero)
                {
                    return null;
                }

                return Marshal.GetDelegateForFunctionPointer<TDelegate>(function);
            }
            finally
            {
                if (namePointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(namePointer);
                }
            }
        }

        [DllImport("vulkan-1.dll", EntryPoint = "vkGetDeviceProcAddr", CallingConvention = CallingConvention.StdCall)]
        private static extern IntPtr vkGetDeviceProcAddr(IntPtr device, IntPtr name);
    }
}
