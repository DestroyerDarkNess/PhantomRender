using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PhantomRender.ImGui.Native
{
    internal sealed class DependencyLoader : IDependencyLoader
    {
        public unsafe void LoadDependencies(IntPtr hModule)
        {
            try
            {
                char* buffer = stackalloc char[260]; // MAX_PATH
                uint len = GetModuleFileName(hModule, buffer, 260);
                if (len == 0)
                {
                    return;
                }

                string dllPath = new string(buffer, 0, (int)len);
                string directory = Path.GetDirectoryName(dllPath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return;
                }

                // Load cimgui.dll (core ImGui native library)
                LoadDllFromDirectory(directory, "cimgui.dll");

                // Load ImGuiImpl.dll (ImGui backends: Win32, OpenGL3, DX9, etc.)
                LoadDllFromDirectory(directory, "ImGuiImpl.dll");
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

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern unsafe uint GetModuleFileName(IntPtr hModule, char* lpFilename, uint nSize);
    }
}
