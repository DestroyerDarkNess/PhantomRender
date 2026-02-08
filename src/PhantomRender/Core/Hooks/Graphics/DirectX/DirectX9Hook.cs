using System;
using System.Runtime.InteropServices;
using PhantomRender.Core.Native;
using PhantomRender.Core.Memory;

namespace PhantomRender.Core.Hooks.Graphics
{
    public class DirectX9Hook : VTableHook
    {
        // VTable indices for IDirect3DDevice9
        private const int VTABLE_Present = 17;
        private const int VTABLE_EndScene = 42;

        public DirectX9Hook(IntPtr deviceAddress) 
            : base(deviceAddress, VTABLE_EndScene, IntPtr.Zero) // Pending delegate
        {
        }
        
        // This method creates a dummy device to get the VTable address
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Direct3DCreate9Delegate(uint SDKVersion);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateDeviceDelegate(IntPtr instance, uint adapter, int deviceType, IntPtr hFocusWindow, uint behaviorFlags, ref Direct3D9.D3DPRESENT_PARAMETERS pPresentationParameters, out IntPtr returnedDeviceInterface);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ReleaseDelegate(IntPtr instance);

        public static IntPtr GetDeviceAddress()
        {
            IntPtr hWnd = NativeWindowHelper.CreateDummyWindow();
            if (hWnd == IntPtr.Zero) return IntPtr.Zero;

            try
            {
                var d3d = Direct3D9.Direct3DCreate9(Direct3D9.D3D_SDK_VERSION);
                if (d3d == IntPtr.Zero)
                {
                    // Try Ex
                     if (Direct3D9.Direct3DCreate9Ex(Direct3D9.D3D_SDK_VERSION, out d3d) < 0)
                        return IntPtr.Zero;
                }

                var presentParams = new Direct3D9.D3DPRESENT_PARAMETERS
                {
                    Windowed = 1,
                    SwapEffect = 1, // D3DSWAPEFFECT_DISCARD
                    hDeviceWindow = hWnd,
                    BackBufferCount = 1,
                    BackBufferWidth = 4,
                    BackBufferHeight = 4,
                    BackBufferFormat = 0 // D3DFMT_UNKNOWN
                };

                IntPtr device = IntPtr.Zero;
                IntPtr vTable = MemoryUtils.ReadIntPtr(d3d);
                
                // If Ex, the VTable for IDirect3D9Ex might differ slightly in creation methods, 
                // but we need a device.
                // IDirect3D9Ex::CreateDeviceEx is likely what we need if CreateDevice fails or isn't there?
                // Actually IDirect3D9Ex inherits from IDirect3D9, so CreateDevice (index 16) should theoretically work
                // or we use CreateDeviceEx (index 20).
                
                // Let's safe bet on 16 first, if it works.
                IntPtr createDevicePtr = MemoryUtils.ReadIntPtr(vTable + 16 * IntPtr.Size);
                
                var createDevice = Marshal.GetDelegateForFunctionPointer<CreateDeviceDelegate>(createDevicePtr);
                int result = createDevice(d3d, 0, Direct3D9.D3DDEVTYPE_HAL, hWnd, Direct3D9.D3DCREATE_SOFTWARE_VERTEXPROCESSING, ref presentParams, out device);
                
                if (result < 0) // Failed
                {
                    // Try with NULL window if handle specific failed, or different flags
                    // For now, simpler error handling
                     var releaseD3D = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(MemoryUtils.ReadIntPtr(vTable + 2 * IntPtr.Size));
                     releaseD3D(d3d);
                     return IntPtr.Zero;
                }

                // We have the device!
                // We should release d3d immediately
                var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(MemoryUtils.ReadIntPtr(vTable + 2 * IntPtr.Size));
                release(d3d);
                
                // We return the device pointer. 
                // Note: In a real scenario, we might want to release this device after reading its VTable.
                // But typically we keep the VTable address and release the object.
                // Here we return the object address so the caller can read VTable and hook, then release.
                // Ideally, the caller should manage lifecycle.
                // For this helper, we'll return the device and let the caller release it using ReleaseDelegate (index 2).
                return device;
            }
            finally
            {
                NativeWindowHelper.DestroyDummyWindow(hWnd);
            }
        }
    }
}
