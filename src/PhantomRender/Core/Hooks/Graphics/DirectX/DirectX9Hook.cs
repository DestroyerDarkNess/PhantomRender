using System;
using System.Runtime.InteropServices;
using MinHook;
using PhantomRender.Core.Native;
using PhantomRender.Core.Memory;

namespace PhantomRender.Core.Hooks.Graphics
{
    public class DirectX9Hook : IDisposable
    {
        // VTable indices for IDirect3DDevice9
        private const int VTABLE_EndScene = 42;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int EndSceneDelegate(IntPtr device);

        public event Action<IntPtr> OnEndScene;

        private HookEngine _hookEngine;
        private EndSceneDelegate _originalEndScene;

        public DirectX9Hook(IntPtr deviceAddress)
        {
            _hookEngine = new HookEngine();

            // Read VTable from device instance
            IntPtr vTable = MemoryUtils.ReadIntPtr(deviceAddress);
            IntPtr endSceneAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_EndScene * IntPtr.Size);

            // Create the hook
            _originalEndScene = _hookEngine.CreateHook<EndSceneDelegate>(endSceneAddr, new EndSceneDelegate(EndSceneHook));
        }

        public void Enable()
        {
            _hookEngine.EnableHook(_originalEndScene);
            Console.WriteLine("[PhantomRender] DX9 EndScene Hook Enabled (MinHook NuGet).");
        }

        public void Disable()
        {
            _hookEngine.DisableHook(_originalEndScene);
        }

        private int EndSceneHook(IntPtr device)
        {
            try
            {
                OnEndScene?.Invoke(device);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DX9 EndScene error: {ex.Message}");
            }

            return _originalEndScene(device);
        }

        public void Dispose()
        {
            _hookEngine?.Dispose();
            GC.SuppressFinalize(this);
        }

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
                    if (Direct3D9.Direct3DCreate9Ex(Direct3D9.D3D_SDK_VERSION, out d3d) < 0)
                        return IntPtr.Zero;
                }

                var presentParams = new Direct3D9.D3DPRESENT_PARAMETERS
                {
                    Windowed = 1,
                    SwapEffect = 1,
                    hDeviceWindow = hWnd,
                    BackBufferCount = 1,
                    BackBufferWidth = 4,
                    BackBufferHeight = 4,
                    BackBufferFormat = 0
                };

                IntPtr device = IntPtr.Zero;
                IntPtr vTable = MemoryUtils.ReadIntPtr(d3d);

                IntPtr createDevicePtr = MemoryUtils.ReadIntPtr(vTable + 16 * IntPtr.Size);
                var createDevice = Marshal.GetDelegateForFunctionPointer<CreateDeviceDelegate>(createDevicePtr);
                int result = createDevice(d3d, 0, Direct3D9.D3DDEVTYPE_HAL, hWnd, Direct3D9.D3DCREATE_SOFTWARE_VERTEXPROCESSING, ref presentParams, out device);

                if (result < 0)
                {
                    var releaseD3D = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(MemoryUtils.ReadIntPtr(vTable + 2 * IntPtr.Size));
                    releaseD3D(d3d);
                    return IntPtr.Zero;
                }

                var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(MemoryUtils.ReadIntPtr(vTable + 2 * IntPtr.Size));
                release(d3d);

                return device;
            }
            finally
            {
                NativeWindowHelper.DestroyDummyWindow(hWnd);
            }
        }
    }
}
