using System;
using System.Runtime.InteropServices;

namespace PhantomRender.Core.Native
{
    public static class Direct3D12
    {
        [DllImport("d3d12.dll")]
        public static extern int D3D12CreateDevice(
            IntPtr pAdapter,
            int MinimumFeatureLevel,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IntPtr ppDevice);

        [StructLayout(LayoutKind.Sequential)]
        public struct D3D12_COMMAND_QUEUE_DESC
        {
            public int Type;
            public int Priority;
            public int Flags;
            public uint NodeMask;
        }

        public const int D3D_FEATURE_LEVEL_11_0 = 0xb000;
        public const int D3D12_COMMAND_LIST_TYPE_DIRECT = 0;
        public const int DXGI_SWAP_EFFECT_FLIP_DISCARD = 4;
        
        public static readonly Guid IID_ID3D12Device = new Guid("189819f1-1db6-4b57-be54-1821339b85f7");
        public static readonly Guid IID_ID3D12CommandQueue = new Guid("0ec870a6-5d7e-4c22-8cfc-5baae07616ed");
        public static readonly Guid IID_ID3D12CommandAllocator = new Guid("6102dee4-af59-4b09-b999-b44d73f09b24");
        public static readonly Guid IID_ID3D12GraphicsCommandList = new Guid("5b160d0f-ac1b-4185-8ba8-b3ae42a5a455");
    }
}
