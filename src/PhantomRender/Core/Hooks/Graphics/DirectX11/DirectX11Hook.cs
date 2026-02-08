using System;
using System.Runtime.InteropServices;
using PhantomRender.Core.Native;

namespace PhantomRender.Core.Hooks.Graphics
{
    public class DirectX11Hook : VTableHook
    {
        // IDXGISwapChain VTable indices
        private const int VTABLE_Present = 8;
        private const int VTABLE_ResizeBuffers = 13;

        public DirectX11Hook(IntPtr swapChainAddress) 
            : base(swapChainAddress, VTABLE_Present, IntPtr.Zero) // Hooking Present by default
        {
        }

        public static IntPtr GetSwapChainAddress()
        {
            using (var window = new System.Windows.Forms.Form())
            {
                var desc = new DXGI.DXGI_SWAP_CHAIN_DESC
                {
                    BufferCount = 1,
                    BufferDesc = new DXGI.DXGI_MODE_DESC
                    {
                        Format = DXGI.DXGI_FORMAT_R8G8B8A8_UNORM,
                        Width = 100,
                        Height = 100,
                        Scaling = 0, // DXGI_MODE_SCALING_UNSPECIFIED
                        ScanlineOrdering = 0, // DXGI_MODE_SCANLINE_ORDER_UNSPECIFIED
                        RefreshRate = new DXGI.DXGI_RATIONAL { Numerator = 60, Denominator = 1 }
                    },
                    BufferUsage = DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT,
                    OutputWindow = window.Handle,
                    SampleDesc = new DXGI.DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                    SwapEffect = 0, // DXGI_SWAP_EFFECT_DISCARD
                    Windowed = 1,
                    Flags = 0 // DXGI_SWAP_CHAIN_FLAG_ALLOW_MODE_SWITCH
                };

                IntPtr device;
                IntPtr swapChain;
                IntPtr immediateContext;
                int featureLevel;

                int result = Direct3D11.D3D11CreateDeviceAndSwapChain(
                    IntPtr.Zero,
                    Direct3D11.D3D_DRIVER_TYPE_HARDWARE,
                    IntPtr.Zero,
                    0,
                    null,
                    0,
                    Direct3D11.D3D11_SDK_VERSION,
                    ref desc,
                    out swapChain,
                    out device,
                    out featureLevel,
                    out immediateContext);

                if (result >= 0 && swapChain != IntPtr.Zero)
                {
                    // We found it!
                    // Cleanup required.
                    // For this helper, we return the SwapChain pointer explicitly.
                    // The caller must capture the VTable from it immediately.
                    
                    // Note: We need to release device and context too to avoid leaks, 
                    // but we need to return swapChain.
                    // In a perfect world we get the VTable address here and release everything.
                    // But our VTableHook expects an object instance to hook *that specific instance* 
                    // OR to read the VTable from it.
                    
                    // Since VTable is shared, we can read the VTable pointer, 
                    // but we can't "return" an object that we are about to release if we want to hook THAT object.
                    // However, for GLOBAL hooking (all swap chains), we just need the VTable address.
                    // For INSTANCE hooking (only the game's swap chain), we need the GAME'S swap chain address,
                    // which we don't have here (this is a dummy).
                    
                    // Wait, usually we use the Dummy to FIND the VTable offsets or the VTable code (if we byte patch).
                    // If we use VTable overwrite (pointer swap in VTable), we need the VTable location.
                    // All instances of IDXGISwapChain point to the same VTable (usually).
                    // So hooking the VTable found via Dummy *should* affect the Game's SwapChain.
                    
                    // So we return the Dummy SwapChain, the caller generic VTableHook reads the VTable ptr, 
                    // calculates the function address, and invalidating the dummy properties doesn't matter 
                    // APART from the fact that we might crash if we release the code backing the VTable? 
                    // No, the code is in the DLL (`dxgi.dll`). Releasing the device/swapchain destroys the INSTANCE data.
                    // The VTable *structure* (the list of pointers) is static in data section of DLL? 
                    // OR is it dynamically allocated? COM objects usually have static VTables.
                    // So it is safe to release the instance.
                    
                    // BUT, to be safe, we usually keep the dummy alive or extract the VTable pointer and return THAT.
                    // Refactoring Hook to take VTable pointer directly might be better?
                    // VTableHook constructor takes `objectAddress`. It reads `*objectAddress` to get `vTable`.
                    
                    // So we must return `swapChain`. We will leak `device` and `context` and `swapChain` 
                    // for the duration of the hook setup?
                    // Let's release device and context here properly using Marshal.Release.
                    
                    Marshal.Release(device);
                    Marshal.Release(immediateContext);
                    
                    return swapChain;
                }

                return IntPtr.Zero;
            }
        }
    }
}
