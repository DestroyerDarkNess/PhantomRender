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
        private const int VTABLE_PresentEx = 121;
        private const int VTABLE_ResetEx = 132;
        private const int VTABLE_CreateDevice = 16;
        private const int VTABLE_CreateDeviceEx = 20;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int EndSceneDelegate(IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int PresentDelegate(IntPtr device, IntPtr sourceRect, IntPtr destRect, IntPtr hDestWindowOverride, IntPtr dirtyRegion);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int ResetDelegate(IntPtr device, ref Direct3D9.D3DPRESENT_PARAMETERS pPresentationParameters);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int PresentExDelegate(IntPtr device, IntPtr sourceRect, IntPtr destRect, IntPtr hDestWindowOverride, IntPtr dirtyRegion, uint flags);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int ResetExDelegate(IntPtr device, ref Direct3D9.D3DPRESENT_PARAMETERS pPresentationParameters, IntPtr fullscreenDisplayMode);

        public event Action<IntPtr> OnEndScene;

        public event Action<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr> OnPresent;

        public event Action<IntPtr, Direct3D9.D3DPRESENT_PARAMETERS> OnBeforeReset;

        public event Action<IntPtr, Direct3D9.D3DPRESENT_PARAMETERS> OnAfterReset;

        private HookEngine _hookEngine;

        private EndSceneDelegate _originalEndScene;
        private PresentDelegate _originalPresent;
        private ResetDelegate _originalReset;
        private PresentExDelegate _originalPresentEx;
        private ResetExDelegate _originalResetEx;
        private readonly bool _isExDevice;

        [ThreadStatic]
        private static int _endSceneDepth;

        [ThreadStatic]
        private static int _presentDepth;

        [ThreadStatic]
        private static int _resetDepth;

        public DirectX9Hook(IntPtr deviceAddress, DX9HookFlags flags = DX9HookFlags.Present | DX9HookFlags.Reset, bool isExDevice = false)
        {
            _hookEngine = new HookEngine();
            _isExDevice = isExDevice;

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

                if (_isExDevice)
                {
                    IntPtr presentExAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_PresentEx * IntPtr.Size);
                    if (presentExAddr != IntPtr.Zero)
                    {
                        _originalPresentEx = _hookEngine.CreateHook<PresentExDelegate>(presentExAddr, new PresentExDelegate(PresentExHook));
                    }
                }
            }

            if (flags.HasFlag(DX9HookFlags.Reset))
            {
                IntPtr resetAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_Reset * IntPtr.Size);
                _originalReset = _hookEngine.CreateHook<ResetDelegate>(resetAddr, new ResetDelegate(ResetHook));

                if (_isExDevice)
                {
                    IntPtr resetExAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_ResetEx * IntPtr.Size);
                    if (resetExAddr != IntPtr.Zero)
                    {
                        _originalResetEx = _hookEngine.CreateHook<ResetExDelegate>(resetExAddr, new ResetExDelegate(ResetExHook));
                    }
                }
            }
        }

        public void Enable()
        {
            _hookEngine.EnableHooks();
            if (_isExDevice)
            {
                Console.WriteLine("[PhantomRender] DX9Ex Present/Reset hooks enabled.");
            }

            Console.WriteLine("[PhantomRender] DX9 Selective Hooks Enabled.");
        }

        public void Disable()
        {
            _hookEngine.DisableHooks();
        }

        private int EndSceneHook(IntPtr device)
        {
            if (_endSceneDepth > 0)
            {
                return _originalEndScene(device);
            }

            _endSceneDepth++;
            try
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
            finally
            {
                _endSceneDepth--;
            }
        }

        private int PresentHook(IntPtr device, IntPtr sourceRect, IntPtr destRect, IntPtr hDestWindowOverride, IntPtr dirtyRegion)
        {
            if (_presentDepth > 0)
            {
                return _originalPresent(device, sourceRect, destRect, hDestWindowOverride, dirtyRegion);
            }

            _presentDepth++;
            try
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
            finally
            {
                _presentDepth--;
            }
        }

        private int PresentExHook(IntPtr device, IntPtr sourceRect, IntPtr destRect, IntPtr hDestWindowOverride, IntPtr dirtyRegion, uint flags)
        {
            if (_presentDepth > 0)
            {
                return _originalPresentEx(device, sourceRect, destRect, hDestWindowOverride, dirtyRegion, flags);
            }

            _presentDepth++;
            try
            {
                try
                {
                    OnPresent?.Invoke(device, sourceRect, destRect, hDestWindowOverride, dirtyRegion);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PhantomRender] DX9Ex Present error: {ex.Message}");
                }

                return _originalPresentEx(device, sourceRect, destRect, hDestWindowOverride, dirtyRegion, flags);
            }
            finally
            {
                _presentDepth--;
            }
        }

        private int ResetHook(IntPtr device, ref Direct3D9.D3DPRESENT_PARAMETERS pPresentationParameters)
        {
            if (_resetDepth > 0)
            {
                return _originalReset(device, ref pPresentationParameters);
            }

            _resetDepth++;
            try
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
            finally
            {
                _resetDepth--;
            }
        }

        private int ResetExHook(IntPtr device, ref Direct3D9.D3DPRESENT_PARAMETERS pPresentationParameters, IntPtr fullscreenDisplayMode)
        {
            if (_resetDepth > 0)
            {
                return _originalResetEx(device, ref pPresentationParameters, fullscreenDisplayMode);
            }

            _resetDepth++;
            try
            {
                try
                {
                    OnBeforeReset?.Invoke(device, pPresentationParameters);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PhantomRender] DX9Ex Pre-Reset error: {ex.Message}");
                }

                int result = _originalResetEx(device, ref pPresentationParameters, fullscreenDisplayMode);

                if (result >= 0)
                {
                    try
                    {
                        OnAfterReset?.Invoke(device, pPresentationParameters);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[PhantomRender] DX9Ex Post-Reset error: {ex.Message}");
                    }
                }

                return result;
            }
            finally
            {
                _resetDepth--;
            }
        }

        public void Dispose()
        {
            _hookEngine?.Dispose();
            GC.SuppressFinalize(this);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateDeviceDelegate(IntPtr instance, uint adapter, int deviceType, IntPtr hFocusWindow, uint behaviorFlags, ref Direct3D9.D3DPRESENT_PARAMETERS pPresentationParameters, out IntPtr returnedDeviceInterface);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateDeviceExDelegate(IntPtr instance, uint adapter, int deviceType, IntPtr hFocusWindow, uint behaviorFlags, ref Direct3D9.D3DPRESENT_PARAMETERS pPresentationParameters, IntPtr fullscreenDisplayMode, out IntPtr returnedDeviceInterface);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ReleaseDelegate(IntPtr instance);

        public static IntPtr GetDeviceAddress()
        {
            return GetDeviceAddress(out _);
        }

        public static IntPtr GetDeviceAddress(out bool isExDevice)
        {
            isExDevice = false;
            IntPtr hWnd = NativeWindowHelper.CreateDummyWindow();
            if (hWnd == IntPtr.Zero) return IntPtr.Zero;

            try
            {
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

                if (TryCreateDeviceEx(hWnd, ref presentParams, out IntPtr exDevice))
                {
                    isExDevice = true;
                    return exDevice;
                }

                IntPtr d3d = Direct3D9.Direct3DCreate9(Direct3D9.D3D_SDK_VERSION);
                if (d3d == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }

                IntPtr device = IntPtr.Zero;
                IntPtr vTable = MemoryUtils.ReadIntPtr(d3d);

                IntPtr createDevicePtr = MemoryUtils.ReadIntPtr(vTable + VTABLE_CreateDevice * IntPtr.Size);
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

        private static bool TryCreateDeviceEx(IntPtr hWnd, ref Direct3D9.D3DPRESENT_PARAMETERS presentParams, out IntPtr device)
        {
            device = IntPtr.Zero;
            if (Direct3D9.Direct3DCreate9Ex(Direct3D9.D3D_SDK_VERSION, out IntPtr d3dEx) < 0 || d3dEx == IntPtr.Zero)
            {
                return false;
            }

            IntPtr vTable = MemoryUtils.ReadIntPtr(d3dEx);
            var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(MemoryUtils.ReadIntPtr(vTable + 2 * IntPtr.Size));

            try
            {
                IntPtr createDeviceExPtr = MemoryUtils.ReadIntPtr(vTable + VTABLE_CreateDeviceEx * IntPtr.Size);
                if (createDeviceExPtr == IntPtr.Zero)
                {
                    return false;
                }

                var createDeviceEx = Marshal.GetDelegateForFunctionPointer<CreateDeviceExDelegate>(createDeviceExPtr);
                int result = createDeviceEx(d3dEx, 0, Direct3D9.D3DDEVTYPE_HAL, hWnd, Direct3D9.D3DCREATE_SOFTWARE_VERTEXPROCESSING, ref presentParams, IntPtr.Zero, out device);
                return result >= 0 && device != IntPtr.Zero;
            }
            finally
            {
                release(d3dEx);
            }
        }
    }
}
