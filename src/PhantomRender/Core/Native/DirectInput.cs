using System;
using System.Runtime.InteropServices;

namespace PhantomRender.Core.Native
{
    public static class DirectInput
    {
        [DllImport("dinput8.dll", EntryPoint = "DirectInput8Create", SetLastError = true)]
        public static extern int DirectInput8Create(
            IntPtr hinst,
            uint dwVersion,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riidltf,
            out IntPtr ppvOut,
            IntPtr punkOuter);

        public const uint DIRECTINPUT_VERSION = 0x0800;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int CreateDeviceDelegate(IntPtr instance, ref Guid rguid, out IntPtr lplpDirectInputDevice, IntPtr pUnkOuter);
    }
}
