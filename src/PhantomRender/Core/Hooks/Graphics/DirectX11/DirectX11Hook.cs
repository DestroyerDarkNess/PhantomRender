using System;
using System.Runtime.InteropServices;
using MinHook;
using PhantomRender.Core.Memory;
using PhantomRender.Core.Native;

namespace PhantomRender.Core.Hooks.Graphics
{
    public sealed class DirectX11Hook : IDisposable
    {
        private const int VTABLE_GET_DEVICE = 7;
        private const int VTABLE_PRESENT = 8;
        private const int VTABLE_GET_DESC = 12;
        private const int VTABLE_RESIZE_BUFFERS = 13;
        private const int VTABLE_PRESENT1 = 22;

        private static readonly Guid IID_IDXGISwapChain1 = new Guid("790a45f7-0d42-4876-983a-0a55cfe6f4aa");
        private static readonly Guid IID_ID3D11Device = new Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140");

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int PresentDelegate(IntPtr swapChain, uint syncInterval, uint flags);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Present1Delegate(IntPtr swapChain, uint syncInterval, uint flags, IntPtr presentParameters);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int ResizeBuffersDelegate(IntPtr swapChain, uint bufferCount, uint width, uint height, int newFormat, uint swapChainFlags);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDeviceDelegate(IntPtr swapChain, ref Guid riid, out IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDescDelegate(IntPtr swapChain, out DXGI.DXGI_SWAP_CHAIN_DESC desc);

        public event Action<IntPtr, uint, uint> OnPresent;
        public event Action<IntPtr, uint, uint, uint, int, uint> OnBeforeResizeBuffers;
        public event Action<IntPtr, uint, uint, uint, int, uint, int> OnAfterResizeBuffers;

        private readonly HookEngine _hookEngine;
        private readonly PresentDelegate _originalPresent;
        private readonly ResizeBuffersDelegate _originalResizeBuffers;
        private Present1Delegate _originalPresent1;

        [ThreadStatic]
        private static int _presentDepth;

        [ThreadStatic]
        private static int _resizeDepth;

        public DirectX11Hook(IntPtr swapChainAddress)
        {
            if (swapChainAddress == IntPtr.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(swapChainAddress));
            }

            _hookEngine = new HookEngine();

            IntPtr vTable = MemoryUtils.ReadIntPtr(swapChainAddress);
            IntPtr presentAddress = MemoryUtils.ReadIntPtr(vTable + VTABLE_PRESENT * IntPtr.Size);
            IntPtr resizeBuffersAddress = MemoryUtils.ReadIntPtr(vTable + VTABLE_RESIZE_BUFFERS * IntPtr.Size);

            _originalPresent = _hookEngine.CreateHook<PresentDelegate>(presentAddress, PresentHook);
            _originalResizeBuffers = _hookEngine.CreateHook<ResizeBuffersDelegate>(resizeBuffersAddress, ResizeBuffersHook);

            TryHookPresent1(swapChainAddress);
        }

        public void Enable()
        {
            _hookEngine.EnableHooks();
            Console.WriteLine("[PhantomRender] DX11 hooks enabled.");
        }

        public void Disable()
        {
            _hookEngine.DisableHooks();
        }

        public void Dispose()
        {
            _hookEngine.Dispose();
            GC.SuppressFinalize(this);
        }

        public static DirectX11Hook Create()
        {
            IntPtr swapChain = GetSwapChainAddress();
            if (swapChain == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return new DirectX11Hook(swapChain);
            }
            finally
            {
                Marshal.Release(swapChain);
            }
        }

        public IntPtr GetDevice(IntPtr swapChain)
        {
            if (swapChain == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr getDeviceAddress = GetVTableFunctionAddress(swapChain, VTABLE_GET_DEVICE);
            if (getDeviceAddress == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var getDevice = Marshal.GetDelegateForFunctionPointer<GetDeviceDelegate>(getDeviceAddress);
            Guid iid = IID_ID3D11Device;
            return getDevice(swapChain, ref iid, out IntPtr device) >= 0 ? device : IntPtr.Zero;
        }

        public bool TryGetOutputWindow(IntPtr swapChain, out IntPtr outputWindow)
        {
            outputWindow = IntPtr.Zero;
            if (swapChain == IntPtr.Zero)
            {
                return false;
            }

            IntPtr getDescAddress = GetVTableFunctionAddress(swapChain, VTABLE_GET_DESC);
            if (getDescAddress == IntPtr.Zero)
            {
                return false;
            }

            var getDesc = Marshal.GetDelegateForFunctionPointer<GetDescDelegate>(getDescAddress);
            if (getDesc(swapChain, out DXGI.DXGI_SWAP_CHAIN_DESC desc) < 0 || desc.OutputWindow == IntPtr.Zero)
            {
                return false;
            }

            outputWindow = desc.OutputWindow;
            return true;
        }

        public static IntPtr GetSwapChainAddress()
        {
            IntPtr windowHandle = NativeWindowHelper.CreateDummyWindow();
            if (windowHandle == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            try
            {
                var desc = new DXGI.DXGI_SWAP_CHAIN_DESC
                {
                    BufferCount = 1,
                    BufferDesc = new DXGI.DXGI_MODE_DESC
                    {
                        Width = 100,
                        Height = 100,
                        Format = DXGI.DXGI_FORMAT_R8G8B8A8_UNORM,
                        RefreshRate = new DXGI.DXGI_RATIONAL { Numerator = 60, Denominator = 1 },
                        ScanlineOrdering = 0,
                        Scaling = 0,
                    },
                    BufferUsage = DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT,
                    OutputWindow = windowHandle,
                    SampleDesc = new DXGI.DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                    SwapEffect = 0,
                    Windowed = 1,
                    Flags = 0,
                };

                int hr = Direct3D11.D3D11CreateDeviceAndSwapChain(
                    IntPtr.Zero,
                    Direct3D11.D3D_DRIVER_TYPE_HARDWARE,
                    IntPtr.Zero,
                    0,
                    null,
                    0,
                    Direct3D11.D3D11_SDK_VERSION,
                    ref desc,
                    out IntPtr swapChain,
                    out IntPtr device,
                    out int featureLevel,
                    out IntPtr immediateContext);

                if (hr < 0 || swapChain == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }

                if (device != IntPtr.Zero)
                {
                    Marshal.Release(device);
                }

                if (immediateContext != IntPtr.Zero)
                {
                    Marshal.Release(immediateContext);
                }

                return swapChain;
            }
            finally
            {
                NativeWindowHelper.DestroyDummyWindow(windowHandle);
            }
        }

        private int PresentHook(IntPtr swapChain, uint syncInterval, uint flags)
        {
            if (_presentDepth > 0)
            {
                return _originalPresent(swapChain, syncInterval, flags);
            }

            _presentDepth++;
            try
            {
                try
                {
                    OnPresent?.Invoke(swapChain, syncInterval, flags);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PhantomRender] DX11 Present error: {ex}");
                }

                return _originalPresent(swapChain, syncInterval, flags);
            }
            finally
            {
                _presentDepth--;
            }
        }

        private int Present1Hook(IntPtr swapChain, uint syncInterval, uint flags, IntPtr presentParameters)
        {
            if (_presentDepth > 0)
            {
                return _originalPresent1(swapChain, syncInterval, flags, presentParameters);
            }

            _presentDepth++;
            try
            {
                try
                {
                    OnPresent?.Invoke(swapChain, syncInterval, flags);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PhantomRender] DX11 Present1 error: {ex}");
                }

                return _originalPresent1(swapChain, syncInterval, flags, presentParameters);
            }
            finally
            {
                _presentDepth--;
            }
        }

        private int ResizeBuffersHook(IntPtr swapChain, uint bufferCount, uint width, uint height, int newFormat, uint swapChainFlags)
        {
            if (_resizeDepth > 0)
            {
                return _originalResizeBuffers(swapChain, bufferCount, width, height, newFormat, swapChainFlags);
            }

            _resizeDepth++;
            try
            {
                try
                {
                    OnBeforeResizeBuffers?.Invoke(swapChain, bufferCount, width, height, newFormat, swapChainFlags);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PhantomRender] DX11 Before ResizeBuffers error: {ex}");
                }

                int hr = _originalResizeBuffers(swapChain, bufferCount, width, height, newFormat, swapChainFlags);

                try
                {
                    OnAfterResizeBuffers?.Invoke(swapChain, bufferCount, width, height, newFormat, swapChainFlags, hr);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PhantomRender] DX11 After ResizeBuffers error: {ex}");
                }

                return hr;
            }
            finally
            {
                _resizeDepth--;
            }
        }

        private void TryHookPresent1(IntPtr swapChain)
        {
            IntPtr swapChain1 = IntPtr.Zero;
            try
            {
                Guid iid = IID_IDXGISwapChain1;
                if (Marshal.QueryInterface(swapChain, ref iid, out swapChain1) < 0 || swapChain1 == IntPtr.Zero)
                {
                    return;
                }

                IntPtr vTable = MemoryUtils.ReadIntPtr(swapChain1);
                IntPtr present1Address = MemoryUtils.ReadIntPtr(vTable + VTABLE_PRESENT1 * IntPtr.Size);
                if (present1Address == IntPtr.Zero)
                {
                    return;
                }

                _originalPresent1 = _hookEngine.CreateHook<Present1Delegate>(present1Address, Present1Hook);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DX11 Present1 hook init failed: {ex}");
            }
            finally
            {
                if (swapChain1 != IntPtr.Zero)
                {
                    Marshal.Release(swapChain1);
                }
            }
        }

        private static IntPtr GetVTableFunctionAddress(IntPtr instance, int index)
        {
            if (instance == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr vTable = MemoryUtils.ReadIntPtr(instance);
            if (vTable == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return MemoryUtils.ReadIntPtr(vTable + index * IntPtr.Size);
        }
    }
}
