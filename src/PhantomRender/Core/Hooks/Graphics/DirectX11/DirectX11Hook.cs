using System;
using System.Runtime.InteropServices;
using MinHook;
using PhantomRender.Core.Native;
using PhantomRender.Core.Memory;

namespace PhantomRender.Core.Hooks.Graphics
{
    public class DirectX11Hook : IDisposable
    {
        // IDXGISwapChain VTable indices
        private const int VTABLE_Present = 8;
        private const int VTABLE_ResizeBuffers = 13;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int PresentDelegate(IntPtr swapChain, uint syncInterval, uint flags);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int ResizeBuffersDelegate(IntPtr swapChain, uint bufferCount, uint width, uint height, int newFormat, uint swapChainFlags);

        public event Action<IntPtr, uint, uint> OnPresent;
        public event Action<IntPtr, uint, uint, uint, int, uint> OnResizeBuffers;

        private HookEngine _hookEngine;
        private PresentDelegate _originalPresent;
        private ResizeBuffersDelegate _originalResizeBuffers;

        public DirectX11Hook(IntPtr swapChainAddress)
        {
            _hookEngine = new HookEngine();

            // Read VTable from swapChain instance
            IntPtr vTable = MemoryUtils.ReadIntPtr(swapChainAddress);
            
            IntPtr presentAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_Present * IntPtr.Size);
            _originalPresent = _hookEngine.CreateHook<PresentDelegate>(presentAddr, new PresentDelegate(PresentHook));

            IntPtr resizeBuffersAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_ResizeBuffers * IntPtr.Size);
            _originalResizeBuffers = _hookEngine.CreateHook<ResizeBuffersDelegate>(resizeBuffersAddr, new ResizeBuffersDelegate(ResizeBuffersHook));
        }

        public void Enable()
        {
            _hookEngine.EnableHooks();
            Console.WriteLine("[PhantomRender] DX11 Hooks Enabled (MinHook).");
        }

        public void Disable()
        {
            _hookEngine.DisableHooks();
        }

        private int PresentHook(IntPtr swapChain, uint syncInterval, uint flags)
        {
            try
            {
                OnPresent?.Invoke(swapChain, syncInterval, flags);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DX11 Present error: {ex.Message}");
            }
            return _originalPresent(swapChain, syncInterval, flags);
        }

        private int ResizeBuffersHook(IntPtr swapChain, uint bufferCount, uint width, uint height, int newFormat, uint swapChainFlags)
        {
            try
            {
                OnResizeBuffers?.Invoke(swapChain, bufferCount, width, height, newFormat, swapChainFlags);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DX11 ResizeBuffers error: {ex.Message}");
            }
            return _originalResizeBuffers(swapChain, bufferCount, width, height, newFormat, swapChainFlags);
        }

        public void Dispose()
        {
            _hookEngine?.Dispose();
            GC.SuppressFinalize(this);
        }

        public static IntPtr GetSwapChainAddress()
        {
            IntPtr hWnd = NativeWindowHelper.CreateDummyWindow();
            if (hWnd == IntPtr.Zero) return IntPtr.Zero;

            try
            {
                var desc = new DXGI.DXGI_SWAP_CHAIN_DESC
                {
                    BufferCount = 1,
                    BufferDesc = new DXGI.DXGI_MODE_DESC
                    {
                        Format = DXGI.DXGI_FORMAT_R8G8B8A8_UNORM,
                        Width = 100,
                        Height = 100,
                        Scaling = 0,
                        ScanlineOrdering = 0,
                        RefreshRate = new DXGI.DXGI_RATIONAL { Numerator = 60, Denominator = 1 }
                    },
                    BufferUsage = DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT,
                    OutputWindow = hWnd,
                    SampleDesc = new DXGI.DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                    SwapEffect = 0, // DXGI_SWAP_EFFECT_DISCARD
                    Windowed = 1,
                    Flags = 0
                };

                IntPtr device;
                IntPtr swapChain;
                IntPtr immediateContext;
                int featureLevel;

                int result = Direct3D11.D3D11CreateDeviceAndSwapChain(
                    IntPtr.Zero,
                    Direct3D11.D3D_DRIVER_TYPE_HARDWARE,
                    IntPtr.Zero,
                    0,
                    null,
                    0,
                    Direct3D11.D3D11_SDK_VERSION,
                    ref desc,
                    out swapChain,
                    out device,
                    out featureLevel,
                    out immediateContext);

                if (result >= 0 && swapChain != IntPtr.Zero)
                {
                    Marshal.Release(device);
                    Marshal.Release(immediateContext);
                    return swapChain;
                }

                return IntPtr.Zero;
            }
            finally
            {
                NativeWindowHelper.DestroyDummyWindow(hWnd);
            }
        }
    }
}
