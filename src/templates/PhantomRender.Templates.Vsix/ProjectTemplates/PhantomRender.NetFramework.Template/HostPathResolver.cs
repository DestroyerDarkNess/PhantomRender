using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace $safeprojectname$
{
    internal static class HostPathResolver
    {
        private const uint Th32csSnapModule = 0x00000008;
        private const uint Th32csSnapModule32 = 0x00000010;
        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        public static string ResolveLoaderDirectory(string preferredModuleName)
        {
            if (TryResolveModuleDirectory(preferredModuleName, out string directory))
            {
                return directory;
            }

            if (TryResolveInjectedModuleDirectory(false, out directory))
            {
                return directory;
            }

            return AppDomain.CurrentDomain.BaseDirectory;
        }

        public static string ResolveInjectedHostDirectory(string preferredModuleName)
        {
            if (TryResolveModuleDirectory(preferredModuleName, out string directory) && HasNativeDependencies(directory))
            {
                return directory;
            }

            if (TryResolveInjectedModuleDirectory(true, out directory))
            {
                return directory;
            }

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (HasNativeDependencies(baseDirectory))
            {
                return baseDirectory;
            }

            return baseDirectory;
        }

        private static bool TryResolveModuleDirectory(string moduleName, out string directory)
        {
            directory = null;
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                return false;
            }

            IntPtr moduleHandle = GetModuleHandleW(moduleName);
            if (moduleHandle == IntPtr.Zero)
            {
                return false;
            }

            return TryGetModuleDirectory(moduleHandle, out directory) && HasNativeDependencies(directory);
        }

        private static bool TryResolveInjectedModuleDirectory(bool requireNativeDependencies, out string directory)
        {
            directory = null;
            IntPtr snapshot = CreateToolhelp32Snapshot(Th32csSnapModule | Th32csSnapModule32, (uint)Process.GetCurrentProcess().Id);
            if (snapshot == InvalidHandleValue || snapshot == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                ModuleEntry32W entry = new ModuleEntry32W
                {
                    Size = (uint)Marshal.SizeOf(typeof(ModuleEntry32W))
                };

                if (!Module32FirstW(snapshot, ref entry))
                {
                    return false;
                }

                string hydraCandidate = null;
                string genericCandidate = null;

                do
                {
                    string modulePath = TrimAtNull(entry.ExePath);
                    if (string.IsNullOrWhiteSpace(modulePath))
                    {
                        continue;
                    }

                    string moduleDirectory;
                    try
                    {
                        moduleDirectory = Path.GetDirectoryName(modulePath);
                    }
                    catch
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(moduleDirectory))
                    {
                        continue;
                    }

                    if (requireNativeDependencies && !HasNativeDependencies(moduleDirectory))
                    {
                        continue;
                    }

                    string moduleName = TrimAtNull(entry.ModuleName);
                    if (!string.IsNullOrWhiteSpace(moduleName) &&
                        moduleName.IndexOf("hydra", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hydraCandidate = moduleDirectory;
                        break;
                    }

                    genericCandidate = moduleDirectory;
                }
                while (Module32NextW(snapshot, ref entry));

                directory = hydraCandidate ?? genericCandidate;
                return !string.IsNullOrWhiteSpace(directory);
            }
            finally
            {
                CloseHandle(snapshot);
            }
        }

        private static bool TryGetModuleDirectory(IntPtr moduleHandle, out string directory)
        {
            directory = null;
            var builder = new StringBuilder(1024);
            int length = GetModuleFileNameW(moduleHandle, builder, builder.Capacity);
            if (length <= 0)
            {
                return false;
            }

            try
            {
                directory = Path.GetDirectoryName(builder.ToString(0, length));
                return !string.IsNullOrWhiteSpace(directory);
            }
            catch
            {
                directory = null;
                return false;
            }
        }

        private static bool HasNativeDependencies(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            try
            {
                return File.Exists(Path.Combine(directory, "cimgui.dll")) &&
                       File.Exists(Path.Combine(directory, "ImGuiImpl.dll"));
            }
            catch
            {
                return false;
            }
        }

        private static string TrimAtNull(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            int nullIndex = value.IndexOf('\0');
            return nullIndex >= 0 ? value.Substring(0, nullIndex) : value;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ModuleEntry32W
        {
            public uint Size;
            public uint ModuleId;
            public uint ProcessId;
            public uint GlobalCountUsage;
            public uint ProcessCountUsage;
            public IntPtr BaseAddress;
            public uint BaseSize;
            public IntPtr ModuleHandle;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string ModuleName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string ExePath;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandleW(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetModuleFileNameW(IntPtr moduleHandle, StringBuilder fileName, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Module32FirstW(IntPtr snapshot, ref ModuleEntry32W entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Module32NextW(IntPtr snapshot, ref ModuleEntry32W entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
