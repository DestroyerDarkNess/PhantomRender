using System;
using System.Runtime.InteropServices;
using PhantomRender.Core.Memory;

namespace PhantomRender.Core.Hooks
{
    /// <summary>
    /// Base class for VTable hooking, used for COM interfaces like DirectX.
    /// </summary>
    public abstract class VTableHook : IHook
    {
        protected IntPtr ObjectAddress;
        protected IntPtr VTableAddress;
        protected IntPtr OriginalFunctionAddress;
        protected IntPtr NewFunctionAddress;
        protected int VTableIndex;
        protected bool _isEnabled;

        public bool IsEnabled => _isEnabled;
        public IntPtr OriginalFunction => OriginalFunctionAddress;

        protected VTableHook(IntPtr objectAddress, int vTableIndex, IntPtr newFunctionAddress)
        {
            ObjectAddress = objectAddress;
            VTableIndex = vTableIndex;
            NewFunctionAddress = newFunctionAddress;

            // 1. Get the VTable pointer from the object instance (first pointer in the object)
            VTableAddress = MemoryUtils.ReadIntPtr(ObjectAddress);

            // 2. Calculate the address of the function pointer at the specific index
            IntPtr entryAddress = VTableAddress + (VTableIndex * IntPtr.Size);

            // 3. Read the original function address
            OriginalFunctionAddress = MemoryUtils.ReadIntPtr(entryAddress);
        }

        public virtual void Enable()
        {
            if (_isEnabled) return;

            IntPtr entryAddress = VTableAddress + (VTableIndex * IntPtr.Size);

            // Change memory protection to allow writing
            if (MemoryUtils.VirtualProtect(entryAddress, (UIntPtr)IntPtr.Size, MemoryUtils.PAGE_EXECUTE_READWRITE, out uint oldProtect))
            {
                // Swap the pointer
                MemoryUtils.WriteIntPtr(entryAddress, NewFunctionAddress);
                
                // Restore protection
                MemoryUtils.VirtualProtect(entryAddress, (UIntPtr)IntPtr.Size, oldProtect, out _);
                
                _isEnabled = true;
            }
        }

        public virtual void Disable()
        {
            if (!_isEnabled) return;

            IntPtr entryAddress = VTableAddress + (VTableIndex * IntPtr.Size);

            if (MemoryUtils.VirtualProtect(entryAddress, (UIntPtr)IntPtr.Size, MemoryUtils.PAGE_EXECUTE_READWRITE, out uint oldProtect))
            {
                // Restore the original pointer
                MemoryUtils.WriteIntPtr(entryAddress, OriginalFunctionAddress);
                
                MemoryUtils.VirtualProtect(entryAddress, (UIntPtr)IntPtr.Size, oldProtect, out _);
                
                _isEnabled = false;
            }
        }

        public void Dispose()
        {
            Disable();
            GC.SuppressFinalize(this);
        }
    }
}
