using System;
using System.Runtime.InteropServices;

namespace PhantomRender.Core.Native
{
    public static class Win32
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [StructLayout(LayoutKind.Sequential)]
        public struct IMAGE_DOS_HEADER
        {
            public ushort e_magic;    // Magic number
            public ushort e_cblp;     // Bytes on last page of file
            public ushort e_cp;       // Pages in file
            public ushort e_crlc;     // Relocations
            public ushort e_cparhdr;  // Size of header in paragraphs
            public ushort e_minalloc; // Minimum extra paragraphs needed
            public ushort e_maxalloc; // Maximum extra paragraphs needed
            public ushort e_ss;       // Initial (relative) SS value
            public ushort e_sp;       // Initial SP value
            public ushort e_csum;     // Checksum
            public ushort e_ip;       // Initial IP value
            public ushort e_cs;       // Initial (relative) CS value
            public ushort e_lfarlc;   // File address of relocation table
            public ushort e_ovno;     // Overlay number
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public ushort[] e_res1;   // Reserved words
            public ushort e_oemid;    // OEM identifier (for e_oeminfo)
            public ushort e_oeminfo;  // OEM information; e_oemid specific
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
            public ushort[] e_res2;   // Reserved words
            public int e_lfanew;      // File address of new exe header
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct IMAGE_FILE_HEADER
        {
            public ushort Machine;
            public ushort NumberOfSections;
            public uint TimeDateStamp;
            public uint PointerToSymbolTable;
            public uint NumberOfSymbols;
            public ushort SizeOfOptionalHeader;
            public ushort Characteristics;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IMAGE_DATA_DIRECTORY
        {
            public uint VirtualAddress;
            public uint Size;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IMAGE_OPTIONAL_HEADER64
        {
            public ushort Magic;
            public byte MajorLinkerVersion;
            public byte MinorLinkerVersion;
            public uint SizeOfCode;
            public uint SizeOfInitializedData;
            public uint SizeOfUninitializedData;
            public uint AddressOfEntryPoint;
            public uint BaseOfCode;
            public ulong ImageBase;
            public uint SectionAlignment;
            public uint FileAlignment;
            public ushort MajorOperatingSystemVersion;
            public ushort MinorOperatingSystemVersion;
            public ushort MajorImageVersion;
            public ushort MinorImageVersion;
            public ushort MajorSubsystemVersion;
            public ushort MinorSubsystemVersion;
            public uint Win32VersionValue;
            public uint SizeOfImage;
            public uint SizeOfHeaders;
            public uint CheckSum;
            public ushort Subsystem;
            public ushort DllCharacteristics;
            public ulong SizeOfStackReserve;
            public ulong SizeOfStackCommit;
            public ulong SizeOfHeapReserve;
            public ulong SizeOfHeapCommit;
            public uint LoaderFlags;
            public uint NumberOfRvaAndSizes;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public IMAGE_DATA_DIRECTORY[] DataDirectory;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IMAGE_OPTIONAL_HEADER32
        {
            public ushort Magic;
            public byte MajorLinkerVersion;
            public byte MinorLinkerVersion;
            public uint SizeOfCode;
            public uint SizeOfInitializedData;
            public uint SizeOfUninitializedData;
            public uint AddressOfEntryPoint;
            public uint BaseOfCode;
            public uint BaseOfData;
            public uint ImageBase;
            public uint SectionAlignment;
            public uint FileAlignment;
            public ushort MajorOperatingSystemVersion;
            public ushort MinorOperatingSystemVersion;
            public ushort MajorImageVersion;
            public ushort MinorImageVersion;
            public ushort MajorSubsystemVersion;
            public ushort MinorSubsystemVersion;
            public uint Win32VersionValue;
            public uint SizeOfImage;
            public uint SizeOfHeaders;
            public uint CheckSum;
            public ushort Subsystem;
            public ushort DllCharacteristics;
            public uint SizeOfStackReserve;
            public uint SizeOfStackCommit;
            public uint SizeOfHeapReserve;
            public uint SizeOfHeapCommit;
            public uint LoaderFlags;
            public uint NumberOfRvaAndSizes;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public IMAGE_DATA_DIRECTORY[] DataDirectory;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IMAGE_IMPORT_DESCRIPTOR
        {
            public uint OriginalFirstThunk; 
            public uint TimeDateStamp;
            public uint ForwarderChain; 
            public uint Name;
            public uint FirstThunk;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IMAGE_IMPORT_BY_NAME
        {
            public ushort Hint;
            public byte Name; // Start of the string
        }
    }
}
