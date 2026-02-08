using System;
using System.Runtime.InteropServices;
using PhantomRender.Core.Native;

namespace PhantomRender.Core.Hooks.Graphics
{
    public class DirectX12Hook : VTableHook
    {
        // IDXGISwapChain3 VTable indices
        // Present is typically index 8 (inherited from IDXGISwapChain) -> 140 (ExecuteCommandLists)? No.
        // IDXGISwapChain3 inherits IDXGISwapChain2 -> IDXGISwapChain1 -> IDXGISwapChain -> IDXGIDeviceSubObject -> IDXGIObject -> IUnknown.
        // IUnknown: 3 methods
        // IDXGIObject: +4 = 7
        // IDXGIDeviceSubObject: +1 = 8
        // IDXGISwapChain: +10 = 18 
        // Present is index 8 (0-based) in IDXGISwapChain.
        // ResizeBuffers is index 13.
        // ExecuteCommandLists is in ID3D12CommandQueue (index 10).
        
        // We usually hook Present (IDXGISwapChain) AND ExecuteCommandLists (ID3D12CommandQueue) for ImGui.
        // Kiero hooks:
        // SwapChain: Present (8), ResizeBuffers (13)
        // CommandQueue: ExecuteCommandLists (10)
        
        // This class will primarily target SwapChain.Present for now.
        
        private const int VTABLE_Present = 8;
        private const int VTABLE_ExecuteCommandLists = 10; 

        public DirectX12Hook(IntPtr address, int index) 
            : base(address, index, IntPtr.Zero)
        {
        }

        public static IntPtr GetSwapChainAddress()
        {
            using (var window = new System.Windows.Forms.Form())
            {
                IntPtr device;
                // 1. Create Device
                if (Direct3D12.D3D12CreateDevice(IntPtr.Zero, Direct3D12.D3D_FEATURE_LEVEL_11_0, Direct3D12.IID_ID3D12Device, out device) < 0)
                    return IntPtr.Zero;

                // 2. Create Command Queue
                var queueDesc = new Direct3D12.D3D12_COMMAND_QUEUE_DESC
                {
                    Type = Direct3D12.D3D12_COMMAND_LIST_TYPE_DIRECT,
                    Priority = 0,
                    Flags = 0, // NONE
                    NodeMask = 0
                };

                IntPtr commandQueue = IntPtr.Zero;
                
                // ID3D12Device VTable:
                // IUnknown (3) + ID3D12Object (4) + ...
                // CreateCommandQueue is usually index 9.
                // Let's verify with standard headers or Kiero.
                // Kiero: device->CreateCommandQueue.
                // Index check:
                // 0: QueryInterface, 1: AddRef, 2: Release
                // 3: GetPrivateData, 4: SetPrivateData, 5: SetPrivateDataInterface, 6: SetName
                // 7: GetNodeCount
                // 8: CreateCommandQueue
                // 9: CreateCommandAllocator
                
                IntPtr deviceVTable = MemoryUtils.ReadIntPtr(device);
                
                // CreateCommandQueue (Index 8)
                IntPtr createQueueAddr = MemoryUtils.ReadIntPtr(deviceVTable + 8 * IntPtr.Size);
                var createQueue = Marshal.GetDelegateForFunctionPointer<CreateCommandQueueDelegate>(createQueueAddr);
                
                if (createQueue(device, ref queueDesc, Direct3D12.IID_ID3D12CommandQueue, out commandQueue) < 0)
                {
                    Marshal.Release(device);
                    return IntPtr.Zero;
                }
                
                // 3. Create Command Allocator
                // CreateCommandAllocator (Index 9)
                IntPtr createAllocatorAddr = MemoryUtils.ReadIntPtr(deviceVTable + 9 * IntPtr.Size);
                var createAllocator = Marshal.GetDelegateForFunctionPointer<CreateCommandAllocatorDelegate>(createAllocatorAddr);
                
                IntPtr commandAllocator;
                if (createAllocator(device, Direct3D12.D3D12_COMMAND_LIST_TYPE_DIRECT, Direct3D12.IID_ID3D12CommandAllocator, out commandAllocator) < 0)
                {
                    Marshal.Release(commandQueue);
                    Marshal.Release(device);
                    return IntPtr.Zero;
                }

                // 4. Create Command List
                // CreateCommandList (Index 10)
                IntPtr createListAddr = MemoryUtils.ReadIntPtr(deviceVTable + 10 * IntPtr.Size);
                var createList = Marshal.GetDelegateForFunctionPointer<CreateCommandListDelegate>(createListAddr);
                
                IntPtr commandList;
                // Note: pInitialState can be null
                if (createList(device, 0, Direct3D12.D3D12_COMMAND_LIST_TYPE_DIRECT, commandAllocator, IntPtr.Zero, Direct3D12.IID_ID3D12GraphicsCommandList, out commandList) < 0)
                {
                    Marshal.Release(commandAllocator);
                    Marshal.Release(commandQueue);
                    Marshal.Release(device);
                    return IntPtr.Zero;
                }

                // 5. Create SwapChain
                // Need IDXGIFactory4
                IntPtr factory;
                if (Native.DXGI.CreateDXGIFactory(ref Native.DXGI.IID_IDXGIFactory4, out factory) < 0)
                {
                     Marshal.Release(commandList);
                     Marshal.Release(commandAllocator);
                     Marshal.Release(commandQueue);
                     Marshal.Release(device);
                     return IntPtr.Zero;
                }
                
                // IDXGIFactory::CreateSwapChain (Index 10 in IDXGIFactory?)
                // IDXGIFactory4 inherits IDXGIFactory3 -> 2 -> 1 -> IDXGIFactory -> IDXGIObject -> IUnknown
                // IUnknown: 3
                // IDXGIObject: 4
                // IDXGIFactory: EnumAdapters, MakeWindowAssociation, GetWindowAssociation, CreateSwapChain (Index 10), CreateSoftwareAdapter 
                // Wait, CreateSwapChain takes (IUnknown* pDevice, DXGI_SWAP_CHAIN_DESC* pDesc, IDXGISwapChain** ppSwapChain)
                
                // BUT for DX12 we usually use CreateSwapChainForHwnd (IDXGIFactory2)
                // Kiero uses CreateSwapChain (old one) passing the CommandQueue as device!
                // Yes, for DX12, the "Device" param in CreateSwapChain IS the CommandQueue.
                
                var swapChainDesc = new Native.DXGI.DXGI_SWAP_CHAIN_DESC
                {
                    BufferCount = 2,
                    BufferDesc = new Native.DXGI.DXGI_MODE_DESC
                    {
                        Width = 100, Height = 100, Format = Native.DXGI.DXGI_FORMAT_R8G8B8A8_UNORM, RefreshRate = new Native.DXGI.DXGI_RATIONAL{ Numerator=60, Denominator=1 }
                    },
                    BufferUsage = Native.DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT,
                    OutputWindow = window.Handle,
                    SampleDesc = new Native.DXGI.DXGI_SAMPLE_DESC { Count=1, Quality=0 },
                    SwapEffect = Direct3D12.DXGI_SWAP_EFFECT_FLIP_DISCARD,
                    Windowed = 1,
                    Flags = 0 // DXGI_SWAP_CHAIN_FLAG_ALLOW_MODE_SWITCH
                };
                
                IntPtr factoryVTable = MemoryUtils.ReadIntPtr(factory);
                IntPtr createSwapChainPtr = MemoryUtils.ReadIntPtr(factoryVTable + 10 * IntPtr.Size); 
                var createSwapChain = Marshal.GetDelegateForFunctionPointer<CreateSwapChainDelegate>(createSwapChainPtr);
                
                IntPtr swapChain;
                // Passing commandQueue as PDevice
                if (createSwapChain(factory, commandQueue, ref swapChainDesc, out swapChain) < 0)
                {
                     Marshal.Release(factory);
                     Marshal.Release(commandList);
                     Marshal.Release(commandAllocator);
                     Marshal.Release(commandQueue);
                     Marshal.Release(device);
                     return IntPtr.Zero;
                }
                
                // Success! We have the SwapChain.
                // We should clean up everything else, but keep SwapChain alive? 
                // No, we return it, caller reads VTable, then caller releases.
                // But we must release the intermediate objects.
                
                Marshal.Release(factory);
                Marshal.Release(commandList);
                Marshal.Release(commandAllocator);
                Marshal.Release(commandQueue);
                Marshal.Release(device);

                return swapChain;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateCommandQueueDelegate(IntPtr device, ref Direct3D12.D3D12_COMMAND_QUEUE_DESC desc, Guid riid, out IntPtr ppCommandQueue);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateCommandAllocatorDelegate(IntPtr device, int type, Guid riid, out IntPtr ppCommandAllocator);
        
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateCommandListDelegate(IntPtr device, uint nodeMask, int type, IntPtr allocator, IntPtr initialState, Guid riid, out IntPtr ppCommandList);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateSwapChainDelegate(IntPtr factory, IntPtr device, ref Native.DXGI.DXGI_SWAP_CHAIN_DESC desc, out IntPtr ppSwapChain);
    }
}
