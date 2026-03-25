using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace PhantomRender.ImGui.Native
{
    public static class Exports
    {
        private const uint DLL_PROCESS_DETACH = 0;
        private const uint DLL_PROCESS_ATTACH = 1;

        private static IntPtr _hModule;

        [UnmanagedCallersOnly(EntryPoint = "DllMain", CallConvs = new[] { typeof(CallConvStdcall) })]
        public static unsafe bool DllMain(IntPtr hModule, uint ul_reason_for_call, IntPtr lpReserved)
        {
            switch (ul_reason_for_call)
            {
                case DLL_PROCESS_ATTACH:
                    _hModule = hModule;
                    // Use CreateThread (Native) instead of new Thread() (Managed) to avoid Loader Lock
                    // Pass wrapper function address
                    IntPtr handle = CreateThread(IntPtr.Zero, IntPtr.Zero, &InitializeThreadWrapper, IntPtr.Zero, 0, IntPtr.Zero);
                    if (handle != IntPtr.Zero)
                    {
                        CloseHandle(handle);
                    }
                    break;

                case DLL_PROCESS_DETACH:
                    //_runtimeHost.Shutdown();
                    break;
            }
            return true;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static uint InitializeThreadWrapper(IntPtr lpParam)
        {
            //_runtimeHost.Initialize(_hModule);
            return 0;
        }

        [DllImport("kernel32.dll")]
        private static extern unsafe IntPtr CreateThread(IntPtr lpThreadAttributes, IntPtr dwStackSize, delegate* unmanaged[Stdcall]<IntPtr, uint> lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}