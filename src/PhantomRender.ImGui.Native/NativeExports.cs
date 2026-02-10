using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;
using PhantomRender.ImGui;

namespace PhantomRender.ImGui.Native
{
    public static class NativeExports
    {
        private const uint DLL_PROCESS_DETACH = 0;
        private const uint DLL_PROCESS_ATTACH = 1;

        [UnmanagedCallersOnly(EntryPoint = "DllMain", CallConvs = new[] { typeof(CallConvStdcall) })]
        public static int DllMain(IntPtr hModule, uint ul_reason_for_call, IntPtr lpReserved)
        {
            switch (ul_reason_for_call)
            {
                case DLL_PROCESS_ATTACH:
                    // Create a new thread to avoid Loader Lock deadlocks when initializing things like Dummy Windows or Hooks
                    var thread = new Thread(InitializeThread);
                    thread.SetApartmentState(ApartmentState.STA); // Graphics frameworks often like STA
                    thread.Start();
                    break;
                case DLL_PROCESS_DETACH:
                    // Cleanup if needed
                    break;
            }
            return 1; // TRUE
        }

        private static void InitializeThread()
        {
            try
            {
                OverlayManager.Initialize();
            }
            catch
            {
                // Swallow
            }
        }
    }
}
