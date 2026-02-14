using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.IO;
using PhantomRender.ImGui;

namespace PhantomRender.ImGui.Native
{
    public static class NativeExports
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
                    break;
            }
            return true;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static uint InitializeThreadWrapper(IntPtr lpParam)
        {
            Initialize();
            return 0;
        }

        private static void Initialize()
        {
            try
            {
                AllocConsole();

                // Mirror all console output into a per-game log file (e.g. "witcher3.log")
                // so we still have the trace when the game exits/crashes.
                ConsoleFileLog.Install(_hModule);

                // Best-effort crash logging (managed unhandled + native SEH).
                CrashHandlers.Install();
                
                Console.WriteLine("[PhantomRender] Console Allocated!");
                
                // Pre-load native dependencies from the same directory as this DLL
                LoadNativeDependencies();

                Console.WriteLine("[PhantomRender] Initializing OverlayManager...");
                
                OverlayManager.Initialize();
                
                Console.WriteLine("[PhantomRender] OverlayManager Initialized.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] Initialization Error: {ex}");
            }
        }
        
        private static unsafe void LoadNativeDependencies()
        {
            try
            {
                char* buffer = stackalloc char[260]; // MAX_PATH
                uint len = GetModuleFileName(_hModule, buffer, 260);
                if (len > 0)
                {
                    string dllPath = new string(buffer, 0, (int)len);
                    string directory = Path.GetDirectoryName(dllPath);
                    
                    // Load cimgui.dll (core ImGui native library)
                    LoadDllFromDirectory(directory, "cimgui.dll");
                    
                    // Load ImGuiImpl.dll (ImGui backends: Win32, OpenGL3, DX9, etc.)
                    LoadDllFromDirectory(directory, "ImGuiImpl.dll");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] Failed to load native dependencies: {ex}");
            }
        }
        
        private static void LoadDllFromDirectory(string directory, string dllName)
        {
            string fullPath = Path.Combine(directory, dllName);
            Console.WriteLine($"[PhantomRender] Loading {dllName} from: {fullPath}");
            
            if (File.Exists(fullPath))
            {
                IntPtr loaded = NativeLibrary.Load(fullPath);
                Console.WriteLine($"[PhantomRender] {dllName} loaded: {loaded}");
            }
            else
            {
                Console.WriteLine($"[PhantomRender] {dllName} not found at expected path!");
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern unsafe IntPtr CreateThread(IntPtr lpThreadAttributes, IntPtr dwStackSize, delegate* unmanaged[Stdcall]<IntPtr, uint> lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern unsafe uint GetModuleFileName(IntPtr hModule, char* lpFilename, uint nSize);
    }
}
