using System;
using System.Runtime.InteropServices;
using PhantomRender.Core.Native;

namespace PhantomRender.Core.Hooks.Inputs
{
    public class DirectInputHook : VTableHook
    {
        // IDirectInputDevice8::GetDeviceState is index 9
        private const int VTABLE_GetDeviceState = 9;

        // Delegates
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int GetDeviceStateDelegate(IntPtr device, int cbData, IntPtr lpvData);

        public event GetDeviceStateDelegate OnGetDeviceState;
        private GetDeviceStateDelegate _hookDelegate;

        public DirectInputHook(IntPtr deviceAddress) 
            : base(deviceAddress, VTABLE_GetDeviceState, IntPtr.Zero)
        {
            _hookDelegate = new GetDeviceStateDelegate(GetDeviceStateHook);
            NewFunctionAddress = Marshal.GetFunctionPointerForDelegate(_hookDelegate);
        }

        private int GetDeviceStateHook(IntPtr device, int cbData, IntPtr lpvData)
        {
            int result = 0;
            // Call original first to populate the buffer
            if (OriginalFunction != IntPtr.Zero)
            {
                var original = Marshal.GetDelegateForFunctionPointer<GetDeviceStateDelegate>(OriginalFunction);
                result = original(device, cbData, lpvData);
            }

            // Invoke event to allow inspection/modification
            OnGetDeviceState?.Invoke(device, cbData, lpvData);

            return result;
        }

        // Helper to get dummy device address
        public static IntPtr GetDeviceAddress()
        {
            IntPtr hInst = Win32.GetModuleHandle(null);
            IntPtr directInput;

            // IID_IDirectInput8W
            Guid IID_IDirectInput8W = new Guid("BF798031-483A-4DA2-AA99-5D64ED369700");

            if (Native.DirectInput.DirectInput8Create(hInst, Native.DirectInput.DIRECTINPUT_VERSION, IID_IDirectInput8W, out directInput, IntPtr.Zero) < 0)
                return IntPtr.Zero;

            // CreateDevice
            // GUID_SysMouse
            Guid GUID_SysMouse = new Guid("6F1D2B60-D5A0-11CF-BFC7-444553540000");

            IntPtr device;
            // CreateDevice is index 3 in IDirectInput8 VTable
            IntPtr diVTable = MemoryUtils.ReadIntPtr(directInput);
            IntPtr createDevicePtr = MemoryUtils.ReadIntPtr(diVTable + 3 * IntPtr.Size);
            
            var createDevice = Marshal.GetDelegateForFunctionPointer<Native.DirectInput.CreateDeviceDelegate>(createDevicePtr);

            if (createDevice(directInput, ref GUID_SysMouse, out device, IntPtr.Zero) < 0)
            {
                Marshal.Release(directInput);
                return IntPtr.Zero;
            }

            // We have the device. The caller will use this address to find the VTable.
            // We should release the DirectInput object, but ideally keep the device alive 
            // until the VTable address is read.
            // Since this method returns the device pointer, the responsibility to release lies with the caller?
            // Or we assume the hook class constructor reads VTable immediately. 
            // VTableHook calls `Setup` in constructor which reads VTable.
            // So it is safe to release `directInput`.
            // The `device` itself must stay alive at least until `VTableHook` reads `*device`.
            
            Marshal.Release(directInput);
            return device;
        }
    }
}
