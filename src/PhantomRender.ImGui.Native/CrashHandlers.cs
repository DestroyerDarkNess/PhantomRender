#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PhantomRender.ImGui.Native
{
    internal static class CrashHandlers
    {
        private static bool _installed;
        private static UnhandledExceptionFilterDelegate? _sehFilter;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            try
            {
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            }
            catch { }

            try
            {
                TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            }
            catch { }

            try
            {
                // Best-effort: if we crash in native code (AV, etc.), this can log the exception code/address.
                _sehFilter = SehUnhandledExceptionFilter;
                SetUnhandledExceptionFilter(_sehFilter);
            }
            catch (Exception ex)
            {
                try
                {
                    Console.WriteLine($"[PhantomRender] Failed to install SEH filter: {ex.Message}");
                    Console.Out.Flush();
                }
                catch { }
            }
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                Console.WriteLine($"[PhantomRender] Unhandled managed exception (IsTerminating={e.IsTerminating}): {e.ExceptionObject}");
                Console.Out.Flush();
            }
            catch { }
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                Console.WriteLine($"[PhantomRender] Unobserved task exception: {e.Exception}");
                Console.Out.Flush();
            }
            catch { }

            try { e.SetObserved(); } catch { }
        }

        private static void OnProcessExit(object? sender, EventArgs e)
        {
            try
            {
                Console.WriteLine("[PhantomRender] ProcessExit.");
                Console.Out.Flush();
            }
            catch { }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int UnhandledExceptionFilterDelegate(IntPtr exceptionPointers);

        [StructLayout(LayoutKind.Sequential)]
        private struct EXCEPTION_POINTERS
        {
            public IntPtr ExceptionRecord;
            public IntPtr ContextRecord;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EXCEPTION_RECORD_BASIC
        {
            public uint ExceptionCode;
            public uint ExceptionFlags;
            public IntPtr ExceptionRecord;
            public IntPtr ExceptionAddress;
        }

        private static int SehUnhandledExceptionFilter(IntPtr exceptionPointers)
        {
            try
            {
                var pointers = Marshal.PtrToStructure<EXCEPTION_POINTERS>(exceptionPointers);
                var record = Marshal.PtrToStructure<EXCEPTION_RECORD_BASIC>(pointers.ExceptionRecord);

                uint tid = 0;
                try { tid = GetCurrentThreadId(); } catch { }

                Console.WriteLine($"[PhantomRender] Unhandled SEH exception: code=0x{record.ExceptionCode:X8} addr=0x{record.ExceptionAddress.ToInt64():X} tid={tid}");
                Console.Out.Flush();
            }
            catch { }

            return 1; // EXCEPTION_EXECUTE_HANDLER
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr SetUnhandledExceptionFilter(UnhandledExceptionFilterDelegate lpTopLevelExceptionFilter);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}

