using System;
using System.Runtime.InteropServices;

namespace PhantomRender.Core.Native
{
    public static class Direct3D10
    {
        [DllImport("d3d10.dll")]
        public static extern int D3D10CreateDeviceAndSwapChain(
            IntPtr pAdapter,
            int DriverType, // D3D10_DRIVER_TYPE
            IntPtr Software,
            uint Flags,
            uint SDKVersion,
            ref DXGI.DXGI_SWAP_CHAIN_DESC pSwapChainDesc,
            out IntPtr ppSwapChain,
            out IntPtr ppDevice);

        public const int D3D10_DRIVER_TYPE_HARDWARE = 1;
        public const uint D3D10_SDK_VERSION = 29;
    }
}
