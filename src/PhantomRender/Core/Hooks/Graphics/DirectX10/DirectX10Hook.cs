using System;
using System.Runtime.InteropServices;
using PhantomRender.Core.Native;

namespace PhantomRender.Core.Hooks.Graphics
{
    public class DirectX10Hook : VTableHook
    {
        // IDXGISwapChain VTable indices (Identical to DX11: Present=8 is standard for IDXGISwapChain)
        private const int VTABLE_Present = 8;
        
        public DirectX10Hook(IntPtr swapChainAddress) 
            : base(swapChainAddress, VTABLE_Present, IntPtr.Zero)
        {
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

                int result = Direct3D10.D3D10CreateDeviceAndSwapChain(
                    IntPtr.Zero,
                    Direct3D10.D3D10_DRIVER_TYPE_HARDWARE,
                    IntPtr.Zero,
                    0,
                    Direct3D10.D3D10_SDK_VERSION,
                    ref desc,
                    out swapChain,
                    out device);

                if (result >= 0 && swapChain != IntPtr.Zero)
                {
                    Marshal.Release(device);
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
