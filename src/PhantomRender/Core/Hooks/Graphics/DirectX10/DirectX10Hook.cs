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
        
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int PresentDelegate(IntPtr swapChain, uint syncInterval, uint flags);

        public event Action<IntPtr, uint, uint> OnPresent;

        private HookEngine _hookEngine;
        private PresentDelegate _originalPresent;

        public DirectX10Hook(IntPtr swapChainAddress)
        {
            _hookEngine = new HookEngine();

            // Read VTable from swapChain instance
            IntPtr vTable = MemoryUtils.ReadIntPtr(swapChainAddress);
            IntPtr presentAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_Present * IntPtr.Size);

            _originalPresent = _hookEngine.CreateHook<PresentDelegate>(presentAddr, new PresentDelegate(PresentHook));
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

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDeviceDelegate(IntPtr swapChain, ref Guid riid, out IntPtr ppDevice);

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
