using System;
using System.Runtime.InteropServices;

namespace PhantomRender.Core.Native
{
    public static class Direct3D9
    {
        [DllImport("d3d9.dll")]
        public static extern IntPtr Direct3DCreate9(uint sdkVersion);

        [DllImport("d3d9.dll")]
        public static extern int Direct3DCreate9Ex(uint sdkVersion, out IntPtr receivedInterface);

        [StructLayout(LayoutKind.Sequential)]
        public struct D3DPRESENT_PARAMETERS
        {
            public uint BackBufferWidth;
            public uint BackBufferHeight;
            public int BackBufferFormat;
            public uint BackBufferCount;
            public int MultiSampleType;
            public uint MultiSampleQuality;
            public int SwapEffect;
            public IntPtr hDeviceWindow;
            public int Windowed;
            public int EnableAutoDepthStencil;
            public int AutoDepthStencilFormat;
            public uint Flags;
            public uint FullScreen_RefreshRateInHz;
            public uint PresentationInterval;
        }

        public const uint D3D_SDK_VERSION = 32;
        public const int D3DDEVTYPE_HAL = 1;
        public const int D3DCREATE_SOFTWARE_VERTEXPROCESSING = 0x00000020;
    }
}
