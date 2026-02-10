using System;
using System.Runtime.InteropServices;
using PhantomRender.Core.Memory;
using PhantomRender.Core.Native;

namespace PhantomRender.Core.Hooks
{
    public unsafe class SimpleInlineHook : IHook
    {
        private IntPtr _targetAddress;
        private IntPtr _hookFunctionAddress;
        private IntPtr _trampolineAddress;
        private byte[] _originalBytes;
        private bool _isEnabled;

        public bool IsEnabled => _isEnabled;
        public IntPtr OriginalFunction => _trampolineAddress;

        public SimpleInlineHook(string library, string functionName)
        {
            IntPtr hModule = NativeWindowHelper.GetModuleHandle(library);
            if (hModule == IntPtr.Zero)
                hModule = LoadLibrary(library);

            _targetAddress = GetProcAddress(hModule, functionName);
            if (_targetAddress == IntPtr.Zero)
                throw new EntryPointNotFoundException($"Function {functionName} not found in {library}");
        }

        public void SetHook(IntPtr hookFunctionAddress)
        {
            _hookFunctionAddress = hookFunctionAddress;
        }

        public void Enable()
        {
            if (_isEnabled) return;

            // 1. Prepare Hook Bytes (JMP rel32)
            // JMP opcode = E9
            // Offset = Destination - (Source + 5)
            // Source = _targetAddress
            // Destination = _hookFunctionAddress
            
            int hookSize = 5;
            _originalBytes = new byte[hookSize];
            
            // Allow Read/Write on Target
            if (MemoryUtils.VirtualProtect(_targetAddress, (UIntPtr)hookSize, MemoryUtils.PAGE_EXECUTE_READWRITE, out uint oldProtect))
            {
                // Backup original bytes
                Marshal.Copy(_targetAddress, _originalBytes, 0, hookSize);

                // 2. Create Trampoline
                // Trampoline: [Original Bytes] + [JMP back to Target+5]
                // Size = 5 + 5 = 10 bytes
                
                _trampolineAddress = VirtualAlloc(IntPtr.Zero, (UIntPtr)1024, 0x1000 | 0x2000, 0x40); // Commit | Reserve, ExecRW

                // Write Original Bytes to Trampoline
                Marshal.Copy(_originalBytes, 0, _trampolineAddress, hookSize);

                // Write JMP back to Target+5 from Trampoline+5
                // Source = Trampoline+5
                // Destination = Target+5
                
                int backOffset = (int)(_targetAddress + hookSize) - (int)(_trampolineAddress + hookSize) - 5;
                
                byte* pTrampoline = (byte*)_trampolineAddress;
                pTrampoline[5] = 0xE9;
                *(int*)(pTrampoline + 6) = backOffset;

                // 3. Write Hook to Target
                // JMP to Hook Function
                int hookOffset = (int)_hookFunctionAddress - (int)_targetAddress - 5;
                
                byte* pTarget = (byte*)_targetAddress;
                pTarget[0] = 0xE9;
                *(int*)(pTarget + 1) = hookOffset;

                // Restore Protection
                MemoryUtils.VirtualProtect(_targetAddress, (UIntPtr)hookSize, oldProtect, out _);
                _isEnabled = true;
            }
        }

        public void Disable()
        {
             if (!_isEnabled) return;
             
             if (MemoryUtils.VirtualProtect(_targetAddress, (UIntPtr)_originalBytes.Length, MemoryUtils.PAGE_EXECUTE_READWRITE, out uint oldProtect))
             {
                 Marshal.Copy(_originalBytes, 0, _targetAddress, _originalBytes.Length);
                 MemoryUtils.VirtualProtect(_targetAddress, (UIntPtr)_originalBytes.Length, oldProtect, out _);
                 _isEnabled = false;
             }
        }

        public void Dispose()
        {
            Disable();
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);
    }
}
