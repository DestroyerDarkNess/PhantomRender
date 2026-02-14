using System;
using System.Runtime.InteropServices;
using MinHook;
using PhantomRender.Core.Native;
using PhantomRender.Core.Memory;

namespace PhantomRender.Core.Hooks.Graphics
{
    [Flags]
    public enum DX9HookFlags
    {
        None = 0,
        EndScene = 1 << 0,
        Present = 1 << 1,
        Reset = 1 << 2,
        All = EndScene | Present | Reset
    }

    public class DirectX9Hook : IDisposable
    {
        // VTable indices for IDirect3DDevice9
        private const int VTABLE_Reset = 16;

        private const int VTABLE_Present = 17;
        private const int VTABLE_EndScene = 42;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int EndSceneDelegate(IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int PresentDelegate(IntPtr device, IntPtr sourceRect, IntPtr destRect, IntPtr hDestWindowOverride, IntPtr dirtyRegion);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int ResetDelegate(IntPtr device, ref Direct3D9.D3DPRESENT_PARAMETERS pPresentationParameters);

        public event Action<IntPtr> OnEndScene;

        public event Action<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr> OnPresent;

        public event Action<IntPtr, Direct3D9.D3DPRESENT_PARAMETERS> OnBeforeReset;

        public event Action<IntPtr, Direct3D9.D3DPRESENT_PARAMETERS> OnAfterReset;

        private HookEngine _hookEngine;

        private EndSceneDelegate _originalEndScene;
        private PresentDelegate _originalPresent;
        private ResetDelegate _originalReset;

        public DirectX9Hook(IntPtr deviceAddress, DX9HookFlags flags = DX9HookFlags.Reset | DX9HookFlags.Reset)
        {
            _hookEngine = new HookEngine();

            // Read VTable from device instance
            IntPtr vTable = MemoryUtils.ReadIntPtr(deviceAddress);

            // Setup hooks based on flags
            if (flags.HasFlag(DX9HookFlags.EndScene))
            {
                IntPtr endSceneAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_EndScene * IntPtr.Size);
                _originalEndScene = _hookEngine.CreateHook<EndSceneDelegate>(endSceneAddr, new EndSceneDelegate(EndSceneHook));
            }

            if (flags.HasFlag(DX9HookFlags.Present))
            {
                IntPtr presentAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_Present * IntPtr.Size);
                _originalPresent = _hookEngine.CreateHook<PresentDelegate>(presentAddr, new PresentDelegate(PresentHook));
            }

            if (flags.HasFlag(DX9HookFlags.Reset))
            {
                IntPtr resetAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_Reset * IntPtr.Size);
                _originalReset = _hookEngine.CreateHook<ResetDelegate>(resetAddr, new ResetDelegate(ResetHook));
            }
        }

        public void Enable()
        {
            _hookEngine.EnableHooks();
            Console.WriteLine("[PhantomRender] DX9 Selective Hooks Enabled.");
        }

        public void Disable()
        {
            _hookEngine.DisableHooks();
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

        private int PresentHook(IntPtr device, IntPtr sourceRect, IntPtr destRect, IntPtr hDestWindowOverride, IntPtr dirtyRegion)
        {
            try
            {
                OnPresent?.Invoke(device, sourceRect, destRect, hDestWindowOverride, dirtyRegion);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DX9 Present error: {ex.Message}");
            }
            return _originalPresent(device, sourceRect, destRect, hDestWindowOverride, dirtyRegion);
        }

        private int ResetHook(IntPtr device, ref Direct3D9.D3DPRESENT_PARAMETERS pPresentationParameters)
        {
            try
            {
                OnBeforeReset?.Invoke(device, pPresentationParameters);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DX9 Pre-Reset error: {ex.Message}");
            }

            int result = _originalReset(device, ref pPresentationParameters);

            if (result >= 0) // D3D_OK or success
            {
                try
                {
                    OnAfterReset?.Invoke(device, pPresentationParameters);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PhantomRender] DX9 Post-Reset error: {ex.Message}");
                }
            }

            return result;
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