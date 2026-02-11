using System;
using System.Runtime.InteropServices;
using MinHook;
using PhantomRender.Core.Native;
using PhantomRender.Core.Memory;

namespace PhantomRender.Core.Hooks.Graphics
{
    public class DirectX12Hook : IDisposable
    {
        // IDXGISwapChain VTable indices
        private const int VTABLE_Present = 8;
        private const int VTABLE_ResizeBuffers = 13;
        
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int PresentDelegate(IntPtr swapChain, uint syncInterval, uint flags);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int ResizeBuffersDelegate(IntPtr swapChain, uint bufferCount, uint width, uint height, int newFormat, uint swapChainFlags);

        public event Action<IntPtr, uint, uint> OnPresent;
        public event Action<IntPtr, uint, uint, uint, int, uint> OnResizeBuffers;

        private HookEngine _hookEngine;
        private PresentDelegate _originalPresent;
        private ResizeBuffersDelegate _originalResizeBuffers;

        public DirectX12Hook(IntPtr swapChainAddress)
        {
            _hookEngine = new HookEngine();

            // Read VTable from swapChain instance
            IntPtr vTable = MemoryUtils.ReadIntPtr(swapChainAddress);
            
            IntPtr presentAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_Present * IntPtr.Size);
            _originalPresent = _hookEngine.CreateHook<PresentDelegate>(presentAddr, new PresentDelegate(PresentHook));

            IntPtr resizeBuffersAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_ResizeBuffers * IntPtr.Size);
            _originalResizeBuffers = _hookEngine.CreateHook<ResizeBuffersDelegate>(resizeBuffersAddr, new ResizeBuffersDelegate(ResizeBuffersHook));
        }

        public void Enable()
        {
            _hookEngine.EnableHooks();
            Console.WriteLine("[PhantomRender] DX12 Hooks Enabled (MinHook).");
        }

        public void Disable()
        {
            _hookEngine.DisableHooks();
        }

        private int PresentHook(IntPtr swapChain, uint syncInterval, uint flags)
        {
            try
            {
                OnPresent?.Invoke(swapChain, syncInterval, flags);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DX12 Present error: {ex.Message}");
            }
            return _originalPresent(swapChain, syncInterval, flags);
        }

        private int ResizeBuffersHook(IntPtr swapChain, uint bufferCount, uint width, uint height, int newFormat, uint swapChainFlags)
        {
            try
            {
                OnResizeBuffers?.Invoke(swapChain, bufferCount, width, height, newFormat, swapChainFlags);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DX12 ResizeBuffers error: {ex.Message}");
            }
            return _originalResizeBuffers(swapChain, bufferCount, width, height, newFormat, swapChainFlags);
        }

        public void Dispose()
        {
            _hookEngine?.Dispose();
            GC.SuppressFinalize(this);
        }

        public static IntPtr GetSwapChainAddress()
        {
            IntPtr hWnd = NativeWindowHelper.CreateDummyWindow();
            if (hWnd == IntPtr.Zero) return IntPtr.Zero;

            try
            {
                IntPtr device;
                if (Direct3D12.D3D12CreateDevice(IntPtr.Zero, Direct3D12.D3D_FEATURE_LEVEL_11_0, Direct3D12.IID_ID3D12Device, out device) < 0)
                    return IntPtr.Zero;

                var queueDesc = new Direct3D12.D3D12_COMMAND_QUEUE_DESC
                {
                    Type = Direct3D12.D3D12_COMMAND_LIST_TYPE_DIRECT,
                    Priority = 0,
                    Flags = 0,
                    NodeMask = 0
                };

                IntPtr commandQueue = IntPtr.Zero;
                IntPtr deviceVTable = MemoryUtils.ReadIntPtr(device);
                IntPtr createQueueAddr = MemoryUtils.ReadIntPtr(deviceVTable + 8 * IntPtr.Size);
                var createQueue = Marshal.GetDelegateForFunctionPointer<CreateCommandQueueDelegate>(createQueueAddr);
                
                if (createQueue(device, ref queueDesc, Direct3D12.IID_ID3D12CommandQueue, out commandQueue) < 0)
                {
                    Marshal.Release(device);
                    return IntPtr.Zero;
                }
                
                var swapChainDesc = new Native.DXGI.DXGI_SWAP_CHAIN_DESC
                {
                    BufferCount = 2,
                    BufferDesc = new Native.DXGI.DXGI_MODE_DESC
                    {
                        Width = 100, Height = 100, Format = Native.DXGI.DXGI_FORMAT_R8G8B8A8_UNORM, RefreshRate = new Native.DXGI.DXGI_RATIONAL{ Numerator=60, Denominator=1 }
                    },
                    BufferUsage = Native.DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT,
                    OutputWindow = hWnd,
                    SampleDesc = new Native.DXGI.DXGI_SAMPLE_DESC { Count=1, Quality=0 },
                    SwapEffect = Direct3D12.DXGI_SWAP_EFFECT_FLIP_DISCARD,
                    Windowed = 1,
                    Flags = 0
                };
                
                IntPtr factory;
                if (Native.DXGI.CreateDXGIFactory(ref Native.DXGI.IID_IDXGIFactory4, out factory) < 0)
                {
                     Marshal.Release(commandQueue);
                     Marshal.Release(device);
                     return IntPtr.Zero;
                }
                
                IntPtr factoryVTable = MemoryUtils.ReadIntPtr(factory);
                IntPtr createSwapChainPtr = MemoryUtils.ReadIntPtr(factoryVTable + 10 * IntPtr.Size); 
                var createSwapChain = Marshal.GetDelegateForFunctionPointer<CreateSwapChainDelegate>(createSwapChainPtr);
                
                IntPtr swapChain;
                if (createSwapChain(factory, commandQueue, ref swapChainDesc, out swapChain) < 0)
                {
                     Marshal.Release(factory);
                     Marshal.Release(commandQueue);
                     Marshal.Release(device);
                     return IntPtr.Zero;
                }
                
                Marshal.Release(factory);
                Marshal.Release(commandQueue);
                Marshal.Release(device);

                return swapChain;
            }
            finally
            {
                NativeWindowHelper.DestroyDummyWindow(hWnd);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateCommandQueueDelegate(IntPtr device, ref Direct3D12.D3D12_COMMAND_QUEUE_DESC desc, Guid riid, out IntPtr ppCommandQueue);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateSwapChainDelegate(IntPtr factory, IntPtr device, ref Native.DXGI.DXGI_SWAP_CHAIN_DESC desc, out IntPtr ppSwapChain);
    }
}
