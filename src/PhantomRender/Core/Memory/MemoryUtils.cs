using System;
using System.Runtime.InteropServices;

namespace PhantomRender.Core.Memory
{
    /// <summary>
    /// Utilities for memory manipulation, handling both safe and unsafe scenarios.
    /// </summary>
    public static class MemoryUtils
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        public const uint PAGE_EXECUTE_READWRITE = 0x40;

        /// <summary>
        /// Reads a pointer from memory, handling 32/64 bit differences.
        /// </summary>
        public static IntPtr ReadIntPtr(IntPtr address)
        {
            if (IntPtr.Size == 8)
                return (IntPtr)Marshal.ReadInt64(address);
            return (IntPtr)Marshal.ReadInt32(address);
        }

        /// <summary>
        /// Writes a pointer to memory, handling 32/64 bit differences.
        /// </summary>
        public static void WriteIntPtr(IntPtr address, IntPtr value)
        {
            if (IntPtr.Size == 8)
                Marshal.WriteInt64(address, (long)value);
            else
                Marshal.WriteInt32(address, (int)value);
        }

        /// <summary>
        /// Writes a pointer to protected memory, handling page protection automatically.
        /// </summary>
        public static void WriteProtectedIntPtr(IntPtr address, IntPtr value)
        {
            byte[] data = IntPtr.Size == 8
                ? BitConverter.GetBytes(value.ToInt64())
                : BitConverter.GetBytes(value.ToInt32());

            WriteProtected(address, data);
        }

        /// <summary>
        /// Temporarily changes memory protection to allow writing.
        /// </summary>
        public static void WriteProtected(IntPtr address, byte[] data)
        {
            VirtualProtect(address, (UIntPtr)data.Length, PAGE_EXECUTE_READWRITE, out uint oldProtect);
            Marshal.Copy(data, 0, address, data.Length);
            VirtualProtect(address, (UIntPtr)data.Length, oldProtect, out _);
        }
        
#if NETCOREAPP
        // Modern .NET optimizations using Span/Memory can handle unsafe blocks directly
        public static unsafe void WriteSafe(IntPtr address, void* data, int length)
        {
             VirtualProtect(address, (UIntPtr)length, PAGE_EXECUTE_READWRITE, out uint oldProtect);
             Buffer.MemoryCopy(data, (void*)address, length, length);
             VirtualProtect(address, (UIntPtr)length, oldProtect, out _);
        }
#endif
    }
}
