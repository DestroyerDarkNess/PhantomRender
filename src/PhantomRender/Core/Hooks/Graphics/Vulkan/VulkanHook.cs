using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MinHook;
using VkNative = PhantomRender.Core.Native.Vulkan;

namespace PhantomRender.Core.Hooks.Graphics.Vulkan
{
    public unsafe class VulkanHook : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateInstanceDelegate(VkNative.VkInstanceCreateInfo* createInfo, IntPtr allocator, out IntPtr instance);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void DestroyInstanceDelegate(IntPtr instance, IntPtr allocator);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumeratePhysicalDevicesDelegate(IntPtr instance, ref uint physicalDeviceCount, IntPtr physicalDevices);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateDeviceDelegate(IntPtr physicalDevice, VkNative.VkDeviceCreateInfo* createInfo, IntPtr allocator, out IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void DestroyDeviceDelegate(IntPtr device, IntPtr allocator);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr GetInstanceProcAddrDelegate(IntPtr instance, IntPtr name);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr GetDeviceProcAddrDelegate(IntPtr device, IntPtr name);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetPhysicalDeviceQueueFamilyPropertiesDelegate(IntPtr physicalDevice, ref uint queueFamilyPropertyCount, IntPtr queueFamilyProperties);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetDeviceQueueDelegate(IntPtr device, uint queueFamilyIndex, uint queueIndex, out IntPtr queue);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetDeviceQueue2Delegate(IntPtr device, VkNative.VkDeviceQueueInfo2* queueInfo, out IntPtr queue);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateWin32SurfaceKHRDelegate(IntPtr instance, VkNative.VkWin32SurfaceCreateInfoKHR* createInfo, IntPtr allocator, out IntPtr surface);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void DestroySurfaceKHRDelegate(IntPtr instance, IntPtr surface, IntPtr allocator);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateSwapchainKHRDelegate(IntPtr device, VkNative.VkSwapchainCreateInfoKHR* createInfo, IntPtr allocator, out IntPtr swapchain);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void DestroySwapchainKHRDelegate(IntPtr device, IntPtr swapchain, IntPtr allocator);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetSwapchainImagesKHRDelegate(IntPtr device, IntPtr swapchain, ref uint swapchainImageCount, IntPtr swapchainImages);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueuePresentKHRDelegate(IntPtr queue, VkNative.VkPresentInfoKHR* presentInfo);

        private sealed class InstanceState
        {
            public uint ApiVersion;
        }

        private sealed class DeviceState
        {
            public IntPtr PhysicalDevice;
            public IntPtr Instance;
            public Dictionary<uint, uint> QueueFamilyFlags = new Dictionary<uint, uint>();
        }

        private sealed class QueueState
        {
            public IntPtr Device;
            public uint QueueFamilyIndex;
            public uint QueueIndex;
        }

        private sealed class SurfaceState
        {
            public IntPtr Instance;
            public IntPtr WindowHandle;
        }

        private sealed class SwapchainState
        {
            public IntPtr Device;
            public IntPtr Surface;
            public IntPtr OldSwapchain;
            public uint MinImageCount;
            public uint ImageCount;
            public int ImageFormat;
            public uint Width;
            public uint Height;
            public uint ImageUsage;
            public IntPtr[] Images = Array.Empty<IntPtr>();
        }

        private readonly HookEngine _hookEngine;
        private readonly object _sync = new object();
        private readonly Dictionary<IntPtr, InstanceState> _instances = new Dictionary<IntPtr, InstanceState>();
        private readonly Dictionary<IntPtr, IntPtr> _physicalDeviceInstances = new Dictionary<IntPtr, IntPtr>();
        private readonly Dictionary<IntPtr, DeviceState> _devices = new Dictionary<IntPtr, DeviceState>();
        private readonly Dictionary<IntPtr, QueueState> _queues = new Dictionary<IntPtr, QueueState>();
        private readonly Dictionary<IntPtr, SurfaceState> _surfaces = new Dictionary<IntPtr, SurfaceState>();
        private readonly Dictionary<IntPtr, SwapchainState> _swapchains = new Dictionary<IntPtr, SwapchainState>();

        private readonly IntPtr _moduleHandle;

        private CreateInstanceDelegate _createInstanceHookDelegate;
        private DestroyInstanceDelegate _destroyInstanceHookDelegate;
        private EnumeratePhysicalDevicesDelegate _enumeratePhysicalDevicesHookDelegate;
        private CreateDeviceDelegate _createDeviceHookDelegate;
        private DestroyDeviceDelegate _destroyDeviceHookDelegate;
        private GetInstanceProcAddrDelegate _getInstanceProcAddrHookDelegate;
        private GetDeviceProcAddrDelegate _getDeviceProcAddrHookDelegate;
        private GetDeviceQueueDelegate _getDeviceQueueHookDelegate;
        private GetDeviceQueue2Delegate _getDeviceQueue2HookDelegate;
        private CreateWin32SurfaceKHRDelegate _createWin32SurfaceKHRHookDelegate;
        private DestroySurfaceKHRDelegate _destroySurfaceKHRHookDelegate;
        private CreateSwapchainKHRDelegate _createSwapchainKHRHookDelegate;
        private DestroySwapchainKHRDelegate _destroySwapchainKHRHookDelegate;
        private QueuePresentKHRDelegate _queuePresentHookDelegate;

        private CreateInstanceDelegate _originalCreateInstance;
        private DestroyInstanceDelegate _originalDestroyInstance;
        private EnumeratePhysicalDevicesDelegate _originalEnumeratePhysicalDevices;
        private CreateDeviceDelegate _originalCreateDevice;
        private DestroyDeviceDelegate _originalDestroyDevice;
        private GetInstanceProcAddrDelegate _originalGetInstanceProcAddr;
        private GetDeviceProcAddrDelegate _originalGetDeviceProcAddr;
        private GetDeviceQueueDelegate _originalGetDeviceQueue;
        private GetDeviceQueue2Delegate _originalGetDeviceQueue2;
        private CreateWin32SurfaceKHRDelegate _originalCreateWin32SurfaceKHR;
        private DestroySurfaceKHRDelegate _originalDestroySurfaceKHR;
        private CreateSwapchainKHRDelegate _originalCreateSwapchainKHR;
        private DestroySwapchainKHRDelegate _originalDestroySwapchainKHR;
        private QueuePresentKHRDelegate _originalQueuePresentKHR;

        private IntPtr _createInstanceAddress;
        private IntPtr _destroyInstanceAddress;
        private IntPtr _enumeratePhysicalDevicesAddress;
        private IntPtr _createDeviceAddress;
        private IntPtr _destroyDeviceAddress;
        private IntPtr _getInstanceProcAddrAddress;
        private IntPtr _getDeviceProcAddrAddress;
        private IntPtr _getDeviceQueueAddress;
        private IntPtr _getDeviceQueue2Address;
        private IntPtr _createWin32SurfaceKHRAddress;
        private IntPtr _destroySurfaceKHRAddress;
        private IntPtr _createSwapchainKHRAddress;
        private IntPtr _destroySwapchainKHRAddress;
        private IntPtr _queuePresentKHRAddress;
        private bool _loggedMissingQueueContext;
        private bool _loggedMissingSwapchainContext;

        private readonly GetPhysicalDeviceQueueFamilyPropertiesDelegate _getPhysicalDeviceQueueFamilyProperties;

        public event VulkanPresentCallback OnPresent;

        public VulkanHook()
        {
            _hookEngine = new HookEngine();
            _moduleHandle = GetModuleHandleW("vulkan-1.dll");

            _createInstanceHookDelegate = CreateInstanceHook;
            _destroyInstanceHookDelegate = DestroyInstanceHook;
            _enumeratePhysicalDevicesHookDelegate = EnumeratePhysicalDevicesHook;
            _createDeviceHookDelegate = CreateDeviceHook;
            _destroyDeviceHookDelegate = DestroyDeviceHook;
            _getInstanceProcAddrHookDelegate = GetInstanceProcAddrHook;
            _getDeviceProcAddrHookDelegate = GetDeviceProcAddrHook;
            _getDeviceQueueHookDelegate = GetDeviceQueueHook;
            _getDeviceQueue2HookDelegate = GetDeviceQueue2Hook;
            _createWin32SurfaceKHRHookDelegate = CreateWin32SurfaceKHRHook;
            _destroySurfaceKHRHookDelegate = DestroySurfaceKHRHook;
            _createSwapchainKHRHookDelegate = CreateSwapchainKHRHook;
            _destroySwapchainKHRHookDelegate = DestroySwapchainKHRHook;
            _queuePresentHookDelegate = QueuePresentKHRHook;

            _getPhysicalDeviceQueueFamilyProperties = ResolveExportDelegate<GetPhysicalDeviceQueueFamilyPropertiesDelegate>("vkGetPhysicalDeviceQueueFamilyProperties");

            TryInstallExportHook("vkCreateInstance", ref _createInstanceAddress, ref _originalCreateInstance, _createInstanceHookDelegate);
            TryInstallExportHook("vkDestroyInstance", ref _destroyInstanceAddress, ref _originalDestroyInstance, _destroyInstanceHookDelegate);
            TryInstallExportHook("vkEnumeratePhysicalDevices", ref _enumeratePhysicalDevicesAddress, ref _originalEnumeratePhysicalDevices, _enumeratePhysicalDevicesHookDelegate);
            TryInstallExportHook("vkCreateDevice", ref _createDeviceAddress, ref _originalCreateDevice, _createDeviceHookDelegate);
            TryInstallExportHook("vkDestroyDevice", ref _destroyDeviceAddress, ref _originalDestroyDevice, _destroyDeviceHookDelegate);
            TryInstallExportHook("vkGetInstanceProcAddr", ref _getInstanceProcAddrAddress, ref _originalGetInstanceProcAddr, _getInstanceProcAddrHookDelegate);
            TryInstallExportHook("vkGetDeviceProcAddr", ref _getDeviceProcAddrAddress, ref _originalGetDeviceProcAddr, _getDeviceProcAddrHookDelegate);
            TryInstallExportHook("vkGetDeviceQueue", ref _getDeviceQueueAddress, ref _originalGetDeviceQueue, _getDeviceQueueHookDelegate);
            TryInstallExportHook("vkGetDeviceQueue2", ref _getDeviceQueue2Address, ref _originalGetDeviceQueue2, _getDeviceQueue2HookDelegate);
            TryInstallExportHook("vkCreateWin32SurfaceKHR", ref _createWin32SurfaceKHRAddress, ref _originalCreateWin32SurfaceKHR, _createWin32SurfaceKHRHookDelegate);
            TryInstallExportHook("vkDestroySurfaceKHR", ref _destroySurfaceKHRAddress, ref _originalDestroySurfaceKHR, _destroySurfaceKHRHookDelegate);
            TryInstallExportHook("vkCreateSwapchainKHR", ref _createSwapchainKHRAddress, ref _originalCreateSwapchainKHR, _createSwapchainKHRHookDelegate);
            TryInstallExportHook("vkDestroySwapchainKHR", ref _destroySwapchainKHRAddress, ref _originalDestroySwapchainKHR, _destroySwapchainKHRHookDelegate);
            TryInstallExportHook("vkQueuePresentKHR", ref _queuePresentKHRAddress, ref _originalQueuePresentKHR, _queuePresentHookDelegate);
        }

        public void Enable()
        {
            _hookEngine.EnableHooks();
            Console.WriteLine("[PhantomRender] Vulkan hooks enabled.");
        }

        public void Disable()
        {
            _hookEngine.DisableHooks();
        }

        public bool TryGetPresentContext(IntPtr queue, VkNative.VkPresentInfoKHR* presentInfo, IntPtr preferredSwapchain, out VulkanPresentContext context)
        {
            context = default;
            if (queue == IntPtr.Zero ||
                presentInfo == null ||
                presentInfo->SwapchainCount == 0 ||
                presentInfo->PSwapchains == IntPtr.Zero ||
                presentInfo->PImageIndices == IntPtr.Zero)
            {
                return false;
            }

            lock (_sync)
            {
                if (!_queues.TryGetValue(queue, out QueueState queueState) ||
                    !_devices.TryGetValue(queueState.Device, out DeviceState deviceState))
                {
                    if (!_loggedMissingQueueContext)
                    {
                        _loggedMissingQueueContext = true;
                        Console.WriteLine("[PhantomRender] Vulkan: present queue is not tracked yet. Late injection after queue creation is not fully recoverable.");
                    }

                    return false;
                }

                int selectedIndex = FindPresentSwapchainIndexNoLock(presentInfo, preferredSwapchain);
                if (selectedIndex < 0)
                {
                    return false;
                }

                IntPtr swapchain = ((IntPtr*)presentInfo->PSwapchains)[selectedIndex];
                if (!_swapchains.TryGetValue(swapchain, out SwapchainState swapchainState))
                {
                    if (!_loggedMissingSwapchainContext)
                    {
                        _loggedMissingSwapchainContext = true;
                        Console.WriteLine("[PhantomRender] Vulkan: present swapchain is not tracked yet. Late injection before a swapchain recreate may not initialize the overlay.");
                    }

                    return false;
                }

                EnsureSwapchainImagesNoLock(swapchain, swapchainState);

                _surfaces.TryGetValue(swapchainState.Surface, out SurfaceState surfaceState);
                deviceState.QueueFamilyFlags.TryGetValue(queueState.QueueFamilyIndex, out uint queueFamilyFlags);

                uint apiVersion = 0;
                if (deviceState.Instance != IntPtr.Zero &&
                    _instances.TryGetValue(deviceState.Instance, out InstanceState instanceState))
                {
                    apiVersion = instanceState.ApiVersion;
                }

                context = new VulkanPresentContext
                {
                    ApiVersion = apiVersion,
                    Instance = deviceState.Instance,
                    PhysicalDevice = deviceState.PhysicalDevice,
                    Device = queueState.Device,
                    Queue = queue,
                    QueueFamilyIndex = queueState.QueueFamilyIndex,
                    QueueFamilySupportsGraphics = (queueFamilyFlags & VkNative.VK_QUEUE_GRAPHICS_BIT) != 0,
                    Surface = swapchainState.Surface,
                    Swapchain = swapchain,
                    WindowHandle = surfaceState?.WindowHandle ?? IntPtr.Zero,
                    MinImageCount = swapchainState.MinImageCount,
                    ImageCount = swapchainState.ImageCount,
                    ImageFormat = swapchainState.ImageFormat,
                    Width = swapchainState.Width,
                    Height = swapchainState.Height,
                    ImageUsage = swapchainState.ImageUsage,
                    ImageIndex = ((uint*)presentInfo->PImageIndices)[selectedIndex],
                };

                return true;
            }
        }

        private int CreateInstanceHook(VkNative.VkInstanceCreateInfo* createInfo, IntPtr allocator, out IntPtr instance)
        {
            int result = _originalCreateInstance(createInfo, allocator, out instance);
            if (result >= 0 && instance != IntPtr.Zero)
            {
                uint apiVersion = 0;
                if (createInfo != null && createInfo->PApplicationInfo != IntPtr.Zero)
                {
                    apiVersion = ((VkNative.VkApplicationInfo*)createInfo->PApplicationInfo)->ApiVersion;
                }

                lock (_sync)
                {
                    _instances[instance] = new InstanceState
                    {
                        ApiVersion = apiVersion,
                    };
                }
            }

            return result;
        }

        private void DestroyInstanceHook(IntPtr instance, IntPtr allocator)
        {
            try
            {
                lock (_sync)
                {
                    _instances.Remove(instance);
                    RemoveByValue(_physicalDeviceInstances, instance);
                    RemoveWhere(_surfaces, entry => entry.Value.Instance == instance);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] Vulkan DestroyInstance tracking error: {ex.Message}");
            }

            _originalDestroyInstance?.Invoke(instance, allocator);
        }

        private int EnumeratePhysicalDevicesHook(IntPtr instance, ref uint physicalDeviceCount, IntPtr physicalDevices)
        {
            int result = _originalEnumeratePhysicalDevices(instance, ref physicalDeviceCount, physicalDevices);
            if (result >= 0 && physicalDevices != IntPtr.Zero && physicalDeviceCount > 0)
            {
                lock (_sync)
                {
                    for (uint i = 0; i < physicalDeviceCount; i++)
                    {
                        IntPtr physicalDevice = ((IntPtr*)physicalDevices)[i];
                        if (physicalDevice != IntPtr.Zero)
                        {
                            _physicalDeviceInstances[physicalDevice] = instance;
                        }
                    }
                }
            }

            return result;
        }

        private int CreateDeviceHook(IntPtr physicalDevice, VkNative.VkDeviceCreateInfo* createInfo, IntPtr allocator, out IntPtr device)
        {
            int result = _originalCreateDevice(physicalDevice, createInfo, allocator, out device);
            if (result >= 0 && device != IntPtr.Zero)
            {
                lock (_sync)
                {
                    _devices[device] = new DeviceState
                    {
                        PhysicalDevice = physicalDevice,
                        Instance = _physicalDeviceInstances.TryGetValue(physicalDevice, out IntPtr instance) ? instance : IntPtr.Zero,
                        QueueFamilyFlags = CaptureQueueFamilyFlags(physicalDevice),
                    };
                }
            }

            return result;
        }

        private void DestroyDeviceHook(IntPtr device, IntPtr allocator)
        {
            try
            {
                lock (_sync)
                {
                    _devices.Remove(device);
                    RemoveWhere(_queues, entry => entry.Value.Device == device);
                    RemoveWhere(_swapchains, entry => entry.Value.Device == device);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] Vulkan DestroyDevice tracking error: {ex.Message}");
            }

            _originalDestroyDevice?.Invoke(device, allocator);
        }

        private IntPtr GetInstanceProcAddrHook(IntPtr instance, IntPtr name)
        {
            IntPtr function = _originalGetInstanceProcAddr(instance, name);
            if (function == IntPtr.Zero || name == IntPtr.Zero)
            {
                return function;
            }

            try
            {
                string functionName = Marshal.PtrToStringAnsi(name);
                TryInstallResolvedHook(functionName, function);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] Vulkan GetInstanceProcAddr hook error: {ex.Message}");
            }

            return function;
        }

        private IntPtr GetDeviceProcAddrHook(IntPtr device, IntPtr name)
        {
            IntPtr function = _originalGetDeviceProcAddr(device, name);
            if (function == IntPtr.Zero || name == IntPtr.Zero)
            {
                return function;
            }

            try
            {
                string functionName = Marshal.PtrToStringAnsi(name);
                TryInstallResolvedHook(functionName, function);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] Vulkan GetDeviceProcAddr hook error: {ex.Message}");
            }

            return function;
        }

        private void GetDeviceQueueHook(IntPtr device, uint queueFamilyIndex, uint queueIndex, out IntPtr queue)
        {
            _originalGetDeviceQueue(device, queueFamilyIndex, queueIndex, out queue);
            CaptureQueue(device, queueFamilyIndex, queueIndex, queue);
        }

        private void GetDeviceQueue2Hook(IntPtr device, VkNative.VkDeviceQueueInfo2* queueInfo, out IntPtr queue)
        {
            _originalGetDeviceQueue2(device, queueInfo, out queue);
            if (queueInfo != null)
            {
                CaptureQueue(device, queueInfo->QueueFamilyIndex, queueInfo->QueueIndex, queue);
            }
        }

        private int CreateWin32SurfaceKHRHook(IntPtr instance, VkNative.VkWin32SurfaceCreateInfoKHR* createInfo, IntPtr allocator, out IntPtr surface)
        {
            int result = _originalCreateWin32SurfaceKHR(instance, createInfo, allocator, out surface);
            if (result >= 0 && surface != IntPtr.Zero)
            {
                lock (_sync)
                {
                    _surfaces[surface] = new SurfaceState
                    {
                        Instance = instance,
                        WindowHandle = createInfo != null ? createInfo->Hwnd : IntPtr.Zero,
                    };
                }
            }

            return result;
        }

        private void DestroySurfaceKHRHook(IntPtr instance, IntPtr surface, IntPtr allocator)
        {
            try
            {
                lock (_sync)
                {
                    _surfaces.Remove(surface);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] Vulkan DestroySurface tracking error: {ex.Message}");
            }

            _originalDestroySurfaceKHR?.Invoke(instance, surface, allocator);
        }

        private int CreateSwapchainKHRHook(IntPtr device, VkNative.VkSwapchainCreateInfoKHR* createInfo, IntPtr allocator, out IntPtr swapchain)
        {
            int result = _originalCreateSwapchainKHR(device, createInfo, allocator, out swapchain);
            if (result >= 0 && swapchain != IntPtr.Zero && createInfo != null)
            {
                lock (_sync)
                {
                    _swapchains[swapchain] = new SwapchainState
                    {
                        Device = device,
                        Surface = createInfo->Surface,
                        OldSwapchain = createInfo->OldSwapchain,
                        MinImageCount = createInfo->MinImageCount,
                        ImageCount = 0,
                        ImageFormat = createInfo->ImageFormat,
                        Width = createInfo->ImageExtent.Width,
                        Height = createInfo->ImageExtent.Height,
                        ImageUsage = createInfo->ImageUsage,
                        Images = Array.Empty<IntPtr>(),
                    };
                }
            }

            return result;
        }

        private void DestroySwapchainKHRHook(IntPtr device, IntPtr swapchain, IntPtr allocator)
        {
            try
            {
                lock (_sync)
                {
                    _swapchains.Remove(swapchain);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] Vulkan DestroySwapchain tracking error: {ex.Message}");
            }

            _originalDestroySwapchainKHR?.Invoke(device, swapchain, allocator);
        }

        private int QueuePresentKHRHook(IntPtr queue, VkNative.VkPresentInfoKHR* presentInfo)
        {
            VulkanPresentHookArgs args = new VulkanPresentHookArgs(queue, presentInfo);
            try
            {
                OnPresent?.Invoke(ref args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] Vulkan Present error: {ex.Message}");
            }

            if (args.HasReplacementWaitSemaphore && presentInfo != null)
            {
                uint originalWaitCount = presentInfo->WaitSemaphoreCount;
                IntPtr originalWaitSemaphores = presentInfo->PWaitSemaphores;

                IntPtr* replacementSemaphores = stackalloc IntPtr[1];
                replacementSemaphores[0] = args.ReplacementWaitSemaphore;
                presentInfo->WaitSemaphoreCount = 1;
                presentInfo->PWaitSemaphores = (IntPtr)replacementSemaphores;

                try
                {
                    return _originalQueuePresentKHR(queue, presentInfo);
                }
                finally
                {
                    presentInfo->WaitSemaphoreCount = originalWaitCount;
                    presentInfo->PWaitSemaphores = originalWaitSemaphores;
                }
            }

            return _originalQueuePresentKHR(queue, presentInfo);
        }

        private void CaptureQueue(IntPtr device, uint queueFamilyIndex, uint queueIndex, IntPtr queue)
        {
            if (device == IntPtr.Zero || queue == IntPtr.Zero)
            {
                return;
            }

            lock (_sync)
            {
                _queues[queue] = new QueueState
                {
                    Device = device,
                    QueueFamilyIndex = queueFamilyIndex,
                    QueueIndex = queueIndex,
                };
            }
        }

        private Dictionary<uint, uint> CaptureQueueFamilyFlags(IntPtr physicalDevice)
        {
            var flags = new Dictionary<uint, uint>();
            if (physicalDevice == IntPtr.Zero || _getPhysicalDeviceQueueFamilyProperties == null)
            {
                return flags;
            }

            try
            {
                uint count = 0;
                _getPhysicalDeviceQueueFamilyProperties(physicalDevice, ref count, IntPtr.Zero);
                if (count == 0)
                {
                    return flags;
                }

                int structSize = Marshal.SizeOf<VkNative.VkQueueFamilyProperties>();
                IntPtr buffer = Marshal.AllocHGlobal((int)(count * structSize));
                try
                {
                    _getPhysicalDeviceQueueFamilyProperties(physicalDevice, ref count, buffer);
                    for (uint i = 0; i < count; i++)
                    {
                        IntPtr current = buffer + (int)(i * structSize);
                        VkNative.VkQueueFamilyProperties properties = Marshal.PtrToStructure<VkNative.VkQueueFamilyProperties>(current);
                        flags[i] = properties.QueueFlags;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] Vulkan queue family capture error: {ex.Message}");
            }

            return flags;
        }

        private int FindPresentSwapchainIndexNoLock(VkNative.VkPresentInfoKHR* presentInfo, IntPtr preferredSwapchain)
        {
            IntPtr* swapchains = (IntPtr*)presentInfo->PSwapchains;
            if (preferredSwapchain != IntPtr.Zero)
            {
                for (uint i = 0; i < presentInfo->SwapchainCount; i++)
                {
                    if (swapchains[i] == preferredSwapchain && _swapchains.ContainsKey(preferredSwapchain))
                    {
                        return (int)i;
                    }
                }
            }

            for (uint i = 0; i < presentInfo->SwapchainCount; i++)
            {
                if (_swapchains.ContainsKey(swapchains[i]))
                {
                    return (int)i;
                }
            }

            return -1;
        }

        private void EnsureSwapchainImagesNoLock(IntPtr swapchain, SwapchainState state)
        {
            if (state == null || state.Device == IntPtr.Zero || state.Images.Length != 0)
            {
                return;
            }

            GetSwapchainImagesKHRDelegate getSwapchainImages = ResolveDeviceDelegate<GetSwapchainImagesKHRDelegate>(state.Device, "vkGetSwapchainImagesKHR");
            if (getSwapchainImages == null)
            {
                return;
            }

            try
            {
                uint count = 0;
                int result = getSwapchainImages(state.Device, swapchain, ref count, IntPtr.Zero);
                if (result < 0 || count == 0)
                {
                    return;
                }

                IntPtr buffer = Marshal.AllocHGlobal((int)(count * IntPtr.Size));
                try
                {
                    result = getSwapchainImages(state.Device, swapchain, ref count, buffer);
                    if (result < 0 || count == 0)
                    {
                        return;
                    }

                    IntPtr[] images = new IntPtr[count];
                    Marshal.Copy(buffer, images, 0, (int)count);
                    state.Images = images;
                    state.ImageCount = count;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] Vulkan swapchain image capture error: {ex.Message}");
            }
        }

        private void TryInstallResolvedHook(string functionName, IntPtr functionPointer)
        {
            if (string.IsNullOrEmpty(functionName) || functionPointer == IntPtr.Zero)
            {
                return;
            }

            switch (functionName)
            {
                case "vkEnumeratePhysicalDevices":
                    TryInstallAddressHook(functionPointer, ref _enumeratePhysicalDevicesAddress, ref _originalEnumeratePhysicalDevices, _enumeratePhysicalDevicesHookDelegate, functionName);
                    break;
                case "vkCreateDevice":
                    TryInstallAddressHook(functionPointer, ref _createDeviceAddress, ref _originalCreateDevice, _createDeviceHookDelegate, functionName);
                    break;
                case "vkDestroyDevice":
                    TryInstallAddressHook(functionPointer, ref _destroyDeviceAddress, ref _originalDestroyDevice, _destroyDeviceHookDelegate, functionName);
                    break;
                case "vkGetDeviceQueue":
                    TryInstallAddressHook(functionPointer, ref _getDeviceQueueAddress, ref _originalGetDeviceQueue, _getDeviceQueueHookDelegate, functionName);
                    break;
                case "vkGetDeviceQueue2":
                    TryInstallAddressHook(functionPointer, ref _getDeviceQueue2Address, ref _originalGetDeviceQueue2, _getDeviceQueue2HookDelegate, functionName);
                    break;
                case "vkCreateWin32SurfaceKHR":
                    TryInstallAddressHook(functionPointer, ref _createWin32SurfaceKHRAddress, ref _originalCreateWin32SurfaceKHR, _createWin32SurfaceKHRHookDelegate, functionName);
                    break;
                case "vkDestroySurfaceKHR":
                    TryInstallAddressHook(functionPointer, ref _destroySurfaceKHRAddress, ref _originalDestroySurfaceKHR, _destroySurfaceKHRHookDelegate, functionName);
                    break;
                case "vkCreateSwapchainKHR":
                    TryInstallAddressHook(functionPointer, ref _createSwapchainKHRAddress, ref _originalCreateSwapchainKHR, _createSwapchainKHRHookDelegate, functionName);
                    break;
                case "vkDestroySwapchainKHR":
                    TryInstallAddressHook(functionPointer, ref _destroySwapchainKHRAddress, ref _originalDestroySwapchainKHR, _destroySwapchainKHRHookDelegate, functionName);
                    break;
                case "vkQueuePresentKHR":
                    TryInstallAddressHook(functionPointer, ref _queuePresentKHRAddress, ref _originalQueuePresentKHR, _queuePresentHookDelegate, functionName);
                    break;
            }
        }

        private void TryInstallExportHook<TDelegate>(string functionName, ref IntPtr hookedAddress, ref TDelegate original, TDelegate detour)
            where TDelegate : Delegate
        {
            if (_moduleHandle == IntPtr.Zero)
            {
                return;
            }

            IntPtr address = GetProcAddress(_moduleHandle, functionName);
            TryInstallAddressHook(address, ref hookedAddress, ref original, detour, functionName);
        }

        private void TryInstallAddressHook<TDelegate>(IntPtr address, ref IntPtr hookedAddress, ref TDelegate original, TDelegate detour, string functionName)
            where TDelegate : Delegate
        {
            if (address == IntPtr.Zero)
            {
                return;
            }

            if (hookedAddress != IntPtr.Zero)
            {
                if (hookedAddress != address)
                {
                    Console.WriteLine($"[PhantomRender] Vulkan: ignoring alternate address for {functionName} (0x{address.ToInt64():X}).");
                }

                return;
            }

            try
            {
                original = _hookEngine.CreateHook<TDelegate>(address, detour);
                _hookEngine.EnableHook(original);
                hookedAddress = address;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] Vulkan hook install failed for {functionName}: {ex.Message}");
            }
        }

        private TDelegate ResolveExportDelegate<TDelegate>(string functionName)
            where TDelegate : Delegate
        {
            if (_moduleHandle == IntPtr.Zero)
            {
                return null;
            }

            IntPtr address = GetProcAddress(_moduleHandle, functionName);
            if (address == IntPtr.Zero)
            {
                return null;
            }

            return Marshal.GetDelegateForFunctionPointer<TDelegate>(address);
        }

        private TDelegate ResolveDeviceDelegate<TDelegate>(IntPtr device, string functionName)
            where TDelegate : Delegate
        {
            if (device == IntPtr.Zero || _originalGetDeviceProcAddr == null)
            {
                return null;
            }

            IntPtr namePointer = IntPtr.Zero;
            try
            {
                namePointer = Marshal.StringToHGlobalAnsi(functionName);
                IntPtr function = _originalGetDeviceProcAddr(device, namePointer);
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

        private static void RemoveByValue<TKey, TValue>(Dictionary<TKey, TValue> dictionary, TValue value)
        {
            var toRemove = new List<TKey>();
            foreach (KeyValuePair<TKey, TValue> entry in dictionary)
            {
                if (EqualityComparer<TValue>.Default.Equals(entry.Value, value))
                {
                    toRemove.Add(entry.Key);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                dictionary.Remove(toRemove[i]);
            }
        }

        private static void RemoveWhere<TKey, TValue>(Dictionary<TKey, TValue> dictionary, Func<KeyValuePair<TKey, TValue>, bool> predicate)
        {
            var toRemove = new List<TKey>();
            foreach (KeyValuePair<TKey, TValue> entry in dictionary)
            {
                if (predicate(entry))
                {
                    toRemove.Add(entry.Key);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                dictionary.Remove(toRemove[i]);
            }
        }

        public void Dispose()
        {
            _hookEngine?.Dispose();
            GC.SuppressFinalize(this);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW", SetLastError = true)]
        private static extern IntPtr GetModuleHandleW(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, EntryPoint = "GetProcAddress", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procName);
    }
}
