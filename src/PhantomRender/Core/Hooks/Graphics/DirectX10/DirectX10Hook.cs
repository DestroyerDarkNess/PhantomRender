using System;
using System.Runtime.InteropServices;
using MinHook;
using PhantomRender.Core.Native;
using PhantomRender.Core.Memory;

namespace PhantomRender.Core.Hooks.Graphics
{
    public class DirectX10Hook : IDisposable
    {
        // IDXGISwapChain VTable indices
        private const int VTABLE_GetDevice = 7;
        private const int VTABLE_Present = 8;
        private const int VTABLE_GetDesc = 12;
        private const int VTABLE_ResizeBuffers = 13;
         
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int PresentDelegate(IntPtr swapChain, uint syncInterval, uint flags);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int ResizeBuffersDelegate(IntPtr swapChain, uint bufferCount, uint width, uint height, int newFormat, uint swapChainFlags);

        public event Action<IntPtr, uint, uint> OnPresent;
        public event Action<IntPtr, uint, uint, uint, int, uint> OnBeforeResizeBuffers;
        public event Action<IntPtr, uint, uint, uint, int, uint, int> OnAfterResizeBuffers;

        private HookEngine _hookEngine;
        private PresentDelegate _originalPresent;
        private ResizeBuffersDelegate _originalResizeBuffers;

        public DirectX10Hook(IntPtr swapChainAddress)
        {
            _hookEngine = new HookEngine();

            // Read VTable from swapChain instance
            IntPtr vTable = MemoryUtils.ReadIntPtr(swapChainAddress);
            IntPtr presentAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_Present * IntPtr.Size);
            IntPtr resizeBuffersAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_ResizeBuffers * IntPtr.Size);

            _originalPresent = _hookEngine.CreateHook<PresentDelegate>(presentAddr, new PresentDelegate(PresentHook));
            _originalResizeBuffers = _hookEngine.CreateHook<ResizeBuffersDelegate>(resizeBuffersAddr, new ResizeBuffersDelegate(ResizeBuffersHook));
        }

        public void Enable()
        {
            _hookEngine.EnableHooks();
            Console.WriteLine("[PhantomRender] DX10 Present Hook Enabled (MinHook).");
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
                Console.WriteLine($"[PhantomRender] DX10 Present error: {ex.Message}");
            }
            return _originalPresent(swapChain, syncInterval, flags);
        }

        private int ResizeBuffersHook(IntPtr swapChain, uint bufferCount, uint width, uint height, int newFormat, uint swapChainFlags)
        {
            try
            {
                OnBeforeResizeBuffers?.Invoke(swapChain, bufferCount, width, height, newFormat, swapChainFlags);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DXGI Before ResizeBuffers error: {ex.Message}");
            }

            int hr = _originalResizeBuffers(swapChain, bufferCount, width, height, newFormat, swapChainFlags);

            try
            {
                OnAfterResizeBuffers?.Invoke(swapChain, bufferCount, width, height, newFormat, swapChainFlags, hr);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DXGI After ResizeBuffers error: {ex.Message}");
            }

            return hr;
        }

        public void Dispose()
        {
            _hookEngine?.Dispose();
            GC.SuppressFinalize(this);
        }

        public unsafe IntPtr GetDevice(IntPtr swapChain)
        {
            if (swapChain == IntPtr.Zero) return IntPtr.Zero;

            IntPtr vTable = MemoryUtils.ReadIntPtr(swapChain);
            IntPtr getDeviceAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_GetDevice * IntPtr.Size);

            // HRESULT GetDevice(REFIID riid, void **ppDevice)
            var getDevice = Marshal.GetDelegateForFunctionPointer<GetDeviceDelegate>(getDeviceAddr);

            Guid iid = new Guid("9B7E4E00-342C-4106-A19F-4F2704F689F0"); // IID_ID3D10Device
            IntPtr device;
            if (getDevice(swapChain, ref iid, out device) == 0) // S_OK
            {
                return device;
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Tries to get the device as ID3D11Device from the swapchain.
        /// Returns IntPtr.Zero if the device is not DX11.
        /// </summary>
        public unsafe IntPtr GetDeviceAs11(IntPtr swapChain)
        {
            if (swapChain == IntPtr.Zero) return IntPtr.Zero;

            IntPtr vTable = MemoryUtils.ReadIntPtr(swapChain);
            IntPtr getDeviceAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_GetDevice * IntPtr.Size);

            var getDevice = Marshal.GetDelegateForFunctionPointer<GetDeviceDelegate>(getDeviceAddr);

            Guid iid = new Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140"); // IID_ID3D11Device
            IntPtr device;
            if (getDevice(swapChain, ref iid, out device) == 0) // S_OK
            {
                return device;
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Tries to get the device as ID3D12Device from the swapchain.
        /// Returns IntPtr.Zero if the device is not DX12 (or not exposed via GetDevice).
        /// </summary>
        public unsafe IntPtr GetDeviceAs12(IntPtr swapChain)
        {
            if (swapChain == IntPtr.Zero) return IntPtr.Zero;

            IntPtr vTable = MemoryUtils.ReadIntPtr(swapChain);
            IntPtr getDeviceAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_GetDevice * IntPtr.Size);

            var getDevice = Marshal.GetDelegateForFunctionPointer<GetDeviceDelegate>(getDeviceAddr);

            Guid iid = Direct3D12.IID_ID3D12Device;
            IntPtr device;
            if (getDevice(swapChain, ref iid, out device) == 0) // S_OK
            {
                return device;
            }

            return IntPtr.Zero;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDeviceDelegate(IntPtr swapChain, ref Guid riid, out IntPtr ppDevice);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDescDelegate(IntPtr swapChain, out DXGI.DXGI_SWAP_CHAIN_DESC pDesc);

        public bool TryGetOutputWindow(IntPtr swapChain, out IntPtr outputWindow)
        {
            outputWindow = IntPtr.Zero;
            if (swapChain == IntPtr.Zero) return false;

            try
            {
                IntPtr vTable = MemoryUtils.ReadIntPtr(swapChain);
                IntPtr getDescAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_GetDesc * IntPtr.Size);
                var getDesc = Marshal.GetDelegateForFunctionPointer<GetDescDelegate>(getDescAddr);

                DXGI.DXGI_SWAP_CHAIN_DESC desc;
                int hr = getDesc(swapChain, out desc);
                if (hr >= 0 && desc.OutputWindow != IntPtr.Zero)
                {
                    outputWindow = desc.OutputWindow;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DXGI: GetDesc failed: {ex.Message}");
            }

            return false;
        }

        public static IntPtr GetSwapChainAddress()
        {
            IntPtr hWnd = NativeWindowHelper.CreateDummyWindow();
            if (hWnd == IntPtr.Zero) return IntPtr.Zero;

            try
            {
                var desc = new DXGI.DXGI_SWAP_CHAIN_DESC
                {
                    BufferCount = 1,
                    BufferDesc = new DXGI.DXGI_MODE_DESC
                    {
                        Format = DXGI.DXGI_FORMAT_R8G8B8A8_UNORM,
                        Width = 100,
                        Height = 100,
                        Scaling = 0,
                        ScanlineOrdering = 0,
                        RefreshRate = new DXGI.DXGI_RATIONAL { Numerator = 60, Denominator = 1 }
                    },
                    BufferUsage = DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT,
                    OutputWindow = hWnd,
                    SampleDesc = new DXGI.DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                    SwapEffect = 0, // DXGI_SWAP_EFFECT_DISCARD
                    Windowed = 1,
                    Flags = 0
                };

                IntPtr device;
                IntPtr swapChain;

                int result = Direct3D10.D3D10CreateDeviceAndSwapChain(
                    IntPtr.Zero,
                    Direct3D10.D3D10_DRIVER_TYPE_HARDWARE,
                    IntPtr.Zero,
                    0,
                    Direct3D10.D3D10_SDK_VERSION,
                    ref desc,
                    out swapChain,
                    out device);

                if (result >= 0 && swapChain != IntPtr.Zero)
                {
                    Marshal.Release(device);
                    // Note: We intentionally don't release the dummy swapChain here —
                    // we need it alive to read its VTable in the constructor.
                    // It will be leaked, but that's standard for hooking dummy objects.
                    return swapChain;
                }

                return IntPtr.Zero;
            }
            finally
            {
                NativeWindowHelper.DestroyDummyWindow(hWnd);
            }
        }
    }
}
