using System;
using System.Runtime.InteropServices;
using System.Threading;
using PhantomRender.Core.Memory;
using PhantomRender.Core.Native;

namespace PhantomRender.Overlays
{
    public sealed class DirectX9ExternalOverlayHost : IDisposable
    {
        private const uint D3DCLEAR_TARGET = 0x00000001;
        private const uint D3DSWAPEFFECT_DISCARD = 1;
        private const int VTABLE_RELEASE = 2;
        private const int VTABLE_RESET = 16;
        private const int VTABLE_PRESENT = 17;
        private const int VTABLE_BEGINSCENE = 41;
        private const int VTABLE_ENDSCENE = 42;
        private const int VTABLE_CLEAR = 43;

        private int _shutdownRequested;
        private bool _disposed;
        private IntPtr _d3d9;
        private IntPtr _device;
        private ResetDelegate _reset;
        private ClearDelegate _clear;
        private BeginSceneDelegate _beginScene;
        private EndSceneDelegate _endScene;
        private PresentDelegate _present;

        public DirectX9ExternalOverlayHost()
        {
            Window = new ExternalOverlayWindow(Core.GraphicsApi.DirectX9);
        }

        public ExternalOverlayWindow Window { get; }

        public bool IsInitialized => _device != IntPtr.Zero;

        public bool IsShutdownRequested => Volatile.Read(ref _shutdownRequested) != 0;

        public string Title
        {
            get => Window.Title;
            set => Window.Title = value;
        }

        public ExternalOverlayMode Mode
        {
            get => Window.Mode;
            set => Window.Mode = value;
        }

        public bool ClickThrough
        {
            get => Window.ClickThrough;
            set => Window.ClickThrough = value;
        }

        public bool TopMost
        {
            get => Window.TopMost;
            set => Window.TopMost = value;
        }

        public event EventHandler<RenderEventArgs> DeviceCreated;

        public event EventHandler<RenderEventArgs> FrameRendering;

        public event EventHandler<ResetEventArgs> BeforeReset;

        public event EventHandler<ResetEventArgs> AfterReset;

        public bool Run(Func<bool> shouldContinue = null)
        {
            ThrowIfDisposed();

            if (!EnsureInitialized())
            {
                return false;
            }

            int width = Window.Width;
            int height = Window.Height;

            while (!Window.IsClosed && !IsShutdownRequested && (shouldContinue == null || shouldContinue()))
            {
                Window.ProcessEvents();

                if (Window.IsAttached)
                {
                    Window.SyncToAttachedWindow();
                }

                if (!TryGetClientSize(Window.WindowHandle, out int currentWidth, out int currentHeight))
                {
                    Thread.Sleep(1);
                    continue;
                }

                if (currentWidth <= 0 || currentHeight <= 0)
                {
                    Thread.Sleep(16);
                    continue;
                }

                if (currentWidth != width || currentHeight != height)
                {
                    if (TryResetDevice(currentWidth, currentHeight))
                    {
                        width = currentWidth;
                        height = currentHeight;
                    }
                }

                _clear(_device, 0, IntPtr.Zero, D3DCLEAR_TARGET, 0x00000000, 1.0f, 0);

                if (_beginScene(_device) >= 0)
                {
                    FrameRendering?.Invoke(this, new RenderEventArgs(_device, Window.WindowHandle, width, height));
                    _endScene(_device);
                }

                _present(_device, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                Thread.Sleep(1);
            }

            return true;
        }

        public void RequestShutdown()
        {
            Interlocked.Exchange(ref _shutdownRequested, 1);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            ReleaseComObject(_device);
            ReleaseComObject(_d3d9);

            _device = IntPtr.Zero;
            _d3d9 = IntPtr.Zero;
            _reset = null;
            _clear = null;
            _beginScene = null;
            _endScene = null;
            _present = null;

            Window.Dispose();
        }

        private bool EnsureInitialized()
        {
            if (_device != IntPtr.Zero)
            {
                return true;
            }

            if (!Window.CreateWindow())
            {
                return false;
            }

            Window.Show();

            if (!TryCreateDirect3D9Device(Window.WindowHandle, Window.Width, Window.Height, out _d3d9, out _device))
            {
                return false;
            }

            GetDeviceDelegates(
                _device,
                out _reset,
                out _clear,
                out _beginScene,
                out _endScene,
                out _present);

            DeviceCreated?.Invoke(this, new RenderEventArgs(_device, Window.WindowHandle, Window.Width, Window.Height));
            return true;
        }

        private bool TryResetDevice(int width, int height)
        {
            var presentParameters = CreatePresentParameters(Window.WindowHandle, width, height);
            BeforeReset?.Invoke(this, new ResetEventArgs(_device, Window.WindowHandle, width, height, presentParameters));

            int resetResult = _reset(_device, ref presentParameters);
            if (resetResult < 0)
            {
                return false;
            }

            AfterReset?.Invoke(this, new ResetEventArgs(_device, Window.WindowHandle, width, height, presentParameters));
            return true;
        }

        private static bool TryCreateDirect3D9Device(nint windowHandle, int width, int height, out IntPtr d3d9, out IntPtr device)
        {
            d3d9 = Direct3D9.Direct3DCreate9(Direct3D9.D3D_SDK_VERSION);
            device = IntPtr.Zero;

            if (d3d9 == IntPtr.Zero)
            {
                return false;
            }

            IntPtr vTable = MemoryUtils.ReadIntPtr(d3d9);
            IntPtr createDeviceAddress = MemoryUtils.ReadIntPtr(vTable + 16 * IntPtr.Size);
            var createDevice = Marshal.GetDelegateForFunctionPointer<CreateDeviceDelegate>(createDeviceAddress);

            var presentParameters = CreatePresentParameters(windowHandle, width, height);
            int result = createDevice(
                d3d9,
                0,
                Direct3D9.D3DDEVTYPE_HAL,
                windowHandle,
                Direct3D9.D3DCREATE_SOFTWARE_VERTEXPROCESSING,
                ref presentParameters,
                out device);

            if (result < 0 || device == IntPtr.Zero)
            {
                ReleaseComObject(d3d9);
                d3d9 = IntPtr.Zero;
                return false;
            }

            return true;
        }

        private static Direct3D9.D3DPRESENT_PARAMETERS CreatePresentParameters(nint windowHandle, int width, int height)
        {
            return new Direct3D9.D3DPRESENT_PARAMETERS
            {
                Windowed = 1,
                SwapEffect = (int)D3DSWAPEFFECT_DISCARD,
                hDeviceWindow = windowHandle,
                BackBufferCount = 1,
                BackBufferWidth = (uint)Math.Max(1, width),
                BackBufferHeight = (uint)Math.Max(1, height),
                BackBufferFormat = 0,
                PresentationInterval = 0,
            };
        }

        private static void GetDeviceDelegates(
            IntPtr device,
            out ResetDelegate reset,
            out ClearDelegate clear,
            out BeginSceneDelegate beginScene,
            out EndSceneDelegate endScene,
            out PresentDelegate present)
        {
            IntPtr vTable = MemoryUtils.ReadIntPtr(device);
            reset = Marshal.GetDelegateForFunctionPointer<ResetDelegate>(MemoryUtils.ReadIntPtr(vTable + VTABLE_RESET * IntPtr.Size));
            clear = Marshal.GetDelegateForFunctionPointer<ClearDelegate>(MemoryUtils.ReadIntPtr(vTable + VTABLE_CLEAR * IntPtr.Size));
            beginScene = Marshal.GetDelegateForFunctionPointer<BeginSceneDelegate>(MemoryUtils.ReadIntPtr(vTable + VTABLE_BEGINSCENE * IntPtr.Size));
            endScene = Marshal.GetDelegateForFunctionPointer<EndSceneDelegate>(MemoryUtils.ReadIntPtr(vTable + VTABLE_ENDSCENE * IntPtr.Size));
            present = Marshal.GetDelegateForFunctionPointer<PresentDelegate>(MemoryUtils.ReadIntPtr(vTable + VTABLE_PRESENT * IntPtr.Size));
        }

        private static bool TryGetClientSize(nint windowHandle, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (!GetClientRect(windowHandle, out RECT rect))
            {
                return false;
            }

            width = Math.Max(1, rect.Right - rect.Left);
            height = Math.Max(1, rect.Bottom - rect.Top);
            return true;
        }

        private static void ReleaseComObject(IntPtr instance)
        {
            if (instance == IntPtr.Zero)
            {
                return;
            }

            IntPtr vTable = MemoryUtils.ReadIntPtr(instance);
            IntPtr releaseAddress = MemoryUtils.ReadIntPtr(vTable + VTABLE_RELEASE * IntPtr.Size);
            var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(releaseAddress);
            release(instance);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DirectX9ExternalOverlayHost));
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public class RenderEventArgs : EventArgs
        {
            internal RenderEventArgs(IntPtr device, IntPtr windowHandle, int width, int height)
            {
                Device = device;
                WindowHandle = windowHandle;
                Width = width;
                Height = height;
            }

            public IntPtr Device { get; }

            public IntPtr WindowHandle { get; }

            public int Width { get; }

            public int Height { get; }
        }

        public sealed class ResetEventArgs : RenderEventArgs
        {
            internal ResetEventArgs(IntPtr device, IntPtr windowHandle, int width, int height, Direct3D9.D3DPRESENT_PARAMETERS presentParameters)
                : base(device, windowHandle, width, height)
            {
                PresentParameters = presentParameters;
            }

            public Direct3D9.D3DPRESENT_PARAMETERS PresentParameters { get; }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateDeviceDelegate(
            IntPtr instance,
            uint adapter,
            int deviceType,
            IntPtr hFocusWindow,
            uint behaviorFlags,
            ref Direct3D9.D3DPRESENT_PARAMETERS presentParameters,
            out IntPtr returnedDeviceInterface);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ReleaseDelegate(IntPtr instance);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ResetDelegate(IntPtr device, ref Direct3D9.D3DPRESENT_PARAMETERS presentParameters);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ClearDelegate(
            IntPtr device,
            uint count,
            IntPtr rectangles,
            uint flags,
            uint color,
            float z,
            uint stencil);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int BeginSceneDelegate(IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EndSceneDelegate(IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int PresentDelegate(
            IntPtr device,
            IntPtr sourceRect,
            IntPtr destRect,
            IntPtr destWindowOverride,
            IntPtr dirtyRegion);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(nint hWnd, out RECT lpRect);
    }
}
