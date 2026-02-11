using System;
using System.Runtime.InteropServices;
using MinHook;

namespace PhantomRender.Core.Hooks.Graphics.Vulkan
{
    public class VulkanHook : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int vkQueuePresentKHRDelegate(IntPtr queue, IntPtr pPresentInfo);

        public event Action<IntPtr, IntPtr> OnPresent;

        private HookEngine _hookEngine;
        private vkQueuePresentKHRDelegate _originalPresent;

        public VulkanHook()
        {
            _hookEngine = new HookEngine();
            // Direct Export Hook
            _originalPresent = _hookEngine.CreateHook<vkQueuePresentKHRDelegate>("vulkan-1.dll", "vkQueuePresentKHR", new vkQueuePresentKHRDelegate(PresentHook));
        }

        public void Enable()
        {
            _hookEngine.EnableHooks();
            Console.WriteLine("[PhantomRender] Vulkan vkQueuePresentKHR Hook Enabled (MinHook).");
        }

        public void Disable()
        {
            _hookEngine.DisableHooks();
        }

        private int PresentHook(IntPtr queue, IntPtr pPresentInfo)
        {
            try
            {
                OnPresent?.Invoke(queue, pPresentInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] Vulkan Present error: {ex.Message}");
            }
            return _originalPresent(queue, pPresentInfo);
        }

        public void Dispose()
        {
            _hookEngine?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
