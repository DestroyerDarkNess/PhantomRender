using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace PhantomRender.ImGui.Core
{
    public sealed class DependencyResolver
    {
        private const int InitialModulePathCapacity = 260;
        private const int MaxModulePathCapacity = 32768;

        private readonly string _baseDirectoryOverride;

        public DependencyResolver()
        {
        }

        public DependencyResolver(string baseDirectoryOverride)
        {
            _baseDirectoryOverride = baseDirectoryOverride;
        }

        public bool LoadDependencies()
        {
            return LoadDependencies(IntPtr.Zero, null);
        }

        public bool LoadDependencies(string baseDirectory)
        {
            return LoadDependencies(IntPtr.Zero, baseDirectory);
        }

        public bool LoadDependencies(IntPtr hModule)
        {
            return LoadDependencies(hModule, null);
        }

        public bool LoadDependencies(IntPtr hModule, string baseDirectory)
        {
            try
            {
                string directory = ResolveBaseDirectory(hModule, baseDirectory ?? _baseDirectoryOverride);

                bool cimguiLoaded = LoadDependency(directory, "cimgui.dll");
                bool imGuiImplLoaded = LoadDependency(directory, "ImGuiImpl.dll");

                return cimguiLoaded && imGuiImplLoaded;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] Failed to resolve dependency directory: {ex}");
                return false;
            }
        }

        private static string ResolveBaseDirectory(IntPtr hModule, string baseDirectoryOverride)
        {
            if (!string.IsNullOrWhiteSpace(baseDirectoryOverride))
            {
                return Path.GetFullPath(baseDirectoryOverride);
            }

            if (hModule != IntPtr.Zero)
            {
                string modulePath = GetModuleFilePath(hModule);
                string moduleDirectory = Path.GetDirectoryName(modulePath);
                if (!string.IsNullOrWhiteSpace(moduleDirectory))
                {
                    return moduleDirectory;
                }
            }

            return AppContext.BaseDirectory;
        }

        private static bool LoadDependency(string directory, string dllName)
        {
            string fullPath = Path.Combine(directory, dllName);

            IntPtr existingHandle = GetModuleHandleW(dllName);
            if (existingHandle != IntPtr.Zero)
            {
                Console.WriteLine($"[PhantomRender] {dllName} already loaded: {existingHandle}");
                return true;
            }

            if (!File.Exists(fullPath))
            {
                Console.WriteLine($"[PhantomRender] {dllName} not found: {fullPath}");
                return false;
            }

            try
            {
                IntPtr moduleHandle = LoadNativeLibrary(fullPath);
                Console.WriteLine($"[PhantomRender] {dllName} loaded: {moduleHandle}");
                return moduleHandle != IntPtr.Zero;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] Failed to load {dllName} from {fullPath}: {ex.Message}");
                return false;
            }
        }

        private static string GetModuleFilePath(IntPtr hModule)
        {
            int capacity = InitialModulePathCapacity;

            while (capacity <= MaxModulePathCapacity)
            {
                var buffer = new char[capacity];
                uint length = GetModuleFileNameW(hModule, buffer, (uint)buffer.Length);
                if (length == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetModuleFileNameW failed.");
                }

                if (length < buffer.Length)
                {
                    return new string(buffer, 0, (int)length);
                }

                capacity *= 2;
            }

            throw new PathTooLongException("Unable to resolve module path because it exceeds the supported buffer size.");
        }

        private static IntPtr LoadNativeLibrary(string fullPath)
        {
#if NETFRAMEWORK
            IntPtr module = LoadLibraryW(fullPath);
            if (module == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"LoadLibraryW failed for '{fullPath}'.");
            }

            return module;
#else
            return NativeLibrary.Load(fullPath);
#endif
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleFileNameW", SetLastError = true)]
        private static extern uint GetModuleFileNameW(IntPtr hModule, [Out] char[] lpFilename, uint nSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW", SetLastError = true)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);

#if NETFRAMEWORK
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryW", SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string lpLibFileName);
#endif
    }
}
