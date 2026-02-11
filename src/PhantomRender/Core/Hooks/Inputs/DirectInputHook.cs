using System;
using System.Runtime.InteropServices;
using MinHook;
using PhantomRender.Core.Native;
using PhantomRender.Core.Memory;

namespace PhantomRender.Core.Hooks.Inputs
{
    public class DirectInputHook : IDisposable
    {
        // IDirectInputDevice8::GetDeviceState is index 9
        private const int VTABLE_GetDeviceState = 9;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int GetDeviceStateDelegate(IntPtr device, int cbData, IntPtr lpvData);

        public event Action<IntPtr, int, IntPtr> OnGetDeviceState;

        private HookEngine _hookEngine;
        private GetDeviceStateDelegate _originalGetDeviceState;

        public DirectInputHook(IntPtr deviceAddress)
        {
            _hookEngine = new HookEngine();

            IntPtr vTable = MemoryUtils.ReadIntPtr(deviceAddress);
            IntPtr getDeviceStateAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_GetDeviceState * IntPtr.Size);

            _originalGetDeviceState = _hookEngine.CreateHook<GetDeviceStateDelegate>(getDeviceStateAddr, new GetDeviceStateDelegate(GetDeviceStateHook));
        }

        public void Enable()
        {
            _hookEngine.EnableHooks();
            Console.WriteLine("[PhantomRender] DirectInput GetDeviceState Hook Enabled (MinHook).");
        }

        public void Disable()
        {
            _hookEngine.DisableHooks();
        }

        private int GetDeviceStateHook(IntPtr device, int cbData, IntPtr lpvData)
        {
            int result = _originalGetDeviceState(device, cbData, lpvData);

            try
            {
                OnGetDeviceState?.Invoke(device, cbData, lpvData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DirectInput GetDeviceState error: {ex.Message}");
            }

            return result;
        }

        public void Dispose()
        {
            _hookEngine?.Dispose();
            GC.SuppressFinalize(this);
        }

        public static IntPtr GetDeviceAddress()
        {
            IntPtr hInst = Win32.GetModuleHandle(null);
            IntPtr directInput;
            Guid IID_IDirectInput8W = new Guid("BF798031-483A-4DA2-AA99-5D64ED369700");

            if (Native.DirectInput.DirectInput8Create(hInst, Native.DirectInput.DIRECTINPUT_VERSION, IID_IDirectInput8W, out directInput, IntPtr.Zero) < 0)
                return IntPtr.Zero;

            Guid GUID_SysMouse = new Guid("6F1D2B60-D5A0-11CF-BFC7-444553540000");
            IntPtr device;
            IntPtr diVTable = MemoryUtils.ReadIntPtr(directInput);
            IntPtr createDevicePtr = MemoryUtils.ReadIntPtr(diVTable + 3 * IntPtr.Size);
            var createDevice = Marshal.GetDelegateForFunctionPointer<Native.DirectInput.CreateDeviceDelegate>(createDevicePtr);

            if (createDevice(directInput, ref GUID_SysMouse, out device, IntPtr.Zero) < 0)
            {
                Marshal.Release(directInput);
                return IntPtr.Zero;
            }

            Marshal.Release(directInput);
            return device;
        }
    }
}
