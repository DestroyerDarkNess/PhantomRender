using System;
using System.Runtime.InteropServices;

namespace PhantomRender.Core.Native
{
    public static class Direct3D11
    {
        [DllImport("d3d11.dll")]
        public static extern int D3D11CreateDeviceAndSwapChain(
            IntPtr pAdapter,
            int DriverType,
            IntPtr Software,
            uint Flags,
            [In] int[] pFeatureLevels,
            uint FeatureLevels,
            uint SDKVersion,
            ref DXGI.DXGI_SWAP_CHAIN_DESC pSwapChainDesc,
            out IntPtr ppSwapChain,
            out IntPtr ppDevice,
            out int pFeatureLevel,
            out IntPtr ppImmediateContext);

        public const int D3D_DRIVER_TYPE_HARDWARE = 1;
        public const uint D3D11_SDK_VERSION = 7;
    }

    public static class DXGI
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_SWAP_CHAIN_DESC
        {
            public DXGI_MODE_DESC BufferDesc;
            public DXGI_SAMPLE_DESC SampleDesc;
            public uint BufferUsage;
            public uint BufferCount;
            public IntPtr OutputWindow;
            public int Windowed;
            public int SwapEffect;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_MODE_DESC
        {
            public uint Width;
            public uint Height;
            public DXGI_RATIONAL RefreshRate;
            public int Format;
            public int ScanlineOrdering;
            public int Scaling;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_RATIONAL
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_SAMPLE_DESC
        {
            public uint Count;
            public uint Quality;
        }

        public const int DXGI_FORMAT_R8G8B8A8_UNORM = 28;
        public const uint DXGI_USAGE_RENDER_TARGET_OUTPUT = 0x00000020;
    }
}
