using System;
using System.Runtime.InteropServices;
using PhantomRender.Core.Memory;
using PhantomRender.Core.Native;

namespace PhantomRender.Core.Hooks
{
    /// <summary>
    /// Base class for IAT hooking.
    /// Redirects functions imported from other DLLs by modifying the Import Address Table of the target module.
    /// </summary>
    public class IATHook : IHook
    {
        private string _targetModule;
        private string _importedModule;
        private string _functionName;
        private IntPtr _newFunctionAddress;
        private IntPtr _originalFunctionAddress;
        private IntPtr _iatEntryAddress;
        private bool _isEnabled;

        public bool IsEnabled => _isEnabled;
        public IntPtr OriginalFunction => _originalFunctionAddress;

        public IATHook(string targetModule, string importedModule, string functionName, IntPtr newFunctionAddress)
        {
            _targetModule = targetModule;
            _importedModule = importedModule;
            _functionName = functionName;
            _newFunctionAddress = newFunctionAddress;

            ResolveIAT();
        }

        private unsafe void ResolveIAT()
        {
            IntPtr hModule = Win32.GetModuleHandle(_targetModule);
            if (hModule == IntPtr.Zero) throw new DllNotFoundException($"Module {_targetModule} not found.");

            byte* baseAddr = (byte*)hModule;
            var dosHeader = Marshal.PtrToStructure<Win32.IMAGE_DOS_HEADER>(hModule);
            
            if (dosHeader.e_magic != 0x5A4D) // MZ
                throw new BadImageFormatException("Invalid DOS Header");

            IntPtr ntHeaderAddr = hModule + dosHeader.e_lfanew;
            uint importDirRva = 0;
            
            // Determine architecture and get Import Directory RVA
            ushort magic = *(ushort*)(ntHeaderAddr + 24); // Magic is at offset 24 in OptionalHeader (which is after FileHeader)
             // Standard offsets: DOS(64) + Signature(4) + FileHeader(20) = 88. But easiest is to read struct.
            
            if (IntPtr.Size == 8)
            {
                var ntHeader64 = Marshal.PtrToStructure<Win32.IMAGE_OPTIONAL_HEADER64>(ntHeaderAddr + 24);
                if (ntHeader64.Magic != 0x20B) // PE32+
                     throw new BadImageFormatException("Invalid NT Header (Expected PE32+)");
                importDirRva = ntHeader64.DataDirectory[1].VirtualAddress;
            }
            else
            {
                var ntHeader32 = Marshal.PtrToStructure<Win32.IMAGE_OPTIONAL_HEADER32>(ntHeaderAddr + 24);
                if (ntHeader32.Magic != 0x10B) // PE32
                     throw new BadImageFormatException("Invalid NT Header (Expected PE32)");
                importDirRva = ntHeader32.DataDirectory[1].VirtualAddress;
            }

            if (importDirRva == 0) throw new EntryPointNotFoundException("No Import Table found in module.");

            var importDesc = (Win32.IMAGE_IMPORT_DESCRIPTOR*)(baseAddr + importDirRva);
            
            while (importDesc->Name != 0)
            {
                string moduleName = Marshal.PtrToStringAnsi((IntPtr)(baseAddr + importDesc->Name));
                
                if (string.Equals(moduleName, _importedModule, StringComparison.OrdinalIgnoreCase))
                {
                    // Found the DLL
                    
                    // OriginalFirstThunk (INT) - lookup by name
                    // FirstThunk (IAT) - the address we want to hook
                    
                    // If OriginalFirstThunk is 0, fallback to FirstThunk (some packers do this)
                    IntPtr* thunkRef = (importDesc->OriginalFirstThunk != 0) 
                        ? (IntPtr*)(baseAddr + importDesc->OriginalFirstThunk) 
                        : (IntPtr*)(baseAddr + importDesc->FirstThunk);
                        
                    IntPtr* funcRef = (IntPtr*)(baseAddr + importDesc->FirstThunk);

                    while (*thunkRef != IntPtr.Zero)
                    {
                        // Check if it's imported by ordinal (highest bit set)
                        bool isOrdinal = (IntPtr.Size == 8) 
                            ? ((*thunkRef).ToInt64() & (1L << 63)) != 0
                            : ((*thunkRef).ToInt32() & (1 << 31)) != 0;

                        if (!isOrdinal)
                        {
                            // Imported by name
                            // RVA to IMAGE_IMPORT_BY_NAME
                            var rva = (IntPtr.Size == 8) ? (*thunkRef).ToInt64() : (*thunkRef).ToInt32();
                            // Address of the name data
                            var nameData = (Win32.IMAGE_IMPORT_BY_NAME*)(baseAddr + rva);
                            
                            // Skipping the Hint (2 bytes) to read the string
                            string funcName = Marshal.PtrToStringAnsi((IntPtr)(&nameData->Name));
                            
                            if (string.Equals(funcName, _functionName, StringComparison.Ordinal))
                            {
                                // Found the function!
                                _iatEntryAddress = (IntPtr)funcRef;
                                _originalFunctionAddress = *funcRef;
                                return;
                            }
                        }
                        
                        thunkRef++;
                        funcRef++;
                    }
                }
                importDesc++;
            }
            
            throw new EntryPointNotFoundException($"Function {_functionName} from {_importedModule} not found in {_targetModule} imports.");
        }

        public void Enable()
        {
            if (_isEnabled || _iatEntryAddress == IntPtr.Zero) return;

             if (MemoryUtils.VirtualProtect(_iatEntryAddress, (UIntPtr)IntPtr.Size, MemoryUtils.PAGE_EXECUTE_READWRITE, out uint oldProtect))
            {
                MemoryUtils.WriteIntPtr(_iatEntryAddress, _newFunctionAddress);
                MemoryUtils.VirtualProtect(_iatEntryAddress, (UIntPtr)IntPtr.Size, oldProtect, out _);
                _isEnabled = true;
            }
        }

        public void Disable()
        {
             if (!_isEnabled || _iatEntryAddress == IntPtr.Zero) return;
             
             if (MemoryUtils.VirtualProtect(_iatEntryAddress, (UIntPtr)IntPtr.Size, MemoryUtils.PAGE_EXECUTE_READWRITE, out uint oldProtect))
            {
                MemoryUtils.WriteIntPtr(_iatEntryAddress, _originalFunctionAddress);
                MemoryUtils.VirtualProtect(_iatEntryAddress, (UIntPtr)IntPtr.Size, oldProtect, out _);
                _isEnabled = false;
            }
        }

        public void Dispose()
        {
            Disable();
        }
    }
}
