using System;
using System.Runtime.InteropServices;
using PhantomRender.Core;
using PhantomRender.Core.Native;

namespace PhantomRender.ImGui.Core.Renderers
{
    public abstract class DxgiRendererBase : RendererBase, IDxgiOverlayRenderer
    {
        private const int VTABLE_IDXGISwapChain_GetDevice = 7;
        private const int VTABLE_IDXGISwapChain_GetDesc = 12;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDeviceDelegate(nint swapChain, ref Guid riid, out nint ppDevice);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDescDelegate(nint swapChain, out DXGI.DXGI_SWAP_CHAIN_DESC desc);

        protected DxgiRendererBase(GraphicsApi graphicsApi)
            : base(graphicsApi)
        {
        }

        public nint SwapChainHandle { get; protected set; }

        protected bool FrameStarted { get; set; }

        public bool InitializeFromSwapChain(nint swapChain)
        {
            return InitializeFromSwapChain(swapChain, IntPtr.Zero);
        }

        public bool InitializeFromSwapChain(nint swapChain, nint windowHandle)
        {
            if (swapChain == IntPtr.Zero)
            {
                return false;
            }

            SwapChainHandle = swapChain;

            if (windowHandle == IntPtr.Zero && !TryGetOutputWindow(swapChain, out windowHandle))
            {
                windowHandle = WindowHandle;
            }

            if (IsInitialized)
            {
                return true;
            }

            if (!TryGetDeviceFromSwapChain(swapChain, out nint device) || device == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                return Initialize(device, windowHandle);
            }
            finally
            {
                ReleaseComObject(device);
            }
        }

        public override void Render()
        {
            Render(SwapChainHandle);
        }

        public abstract void Render(nint swapChain);

        public virtual void OnBeforeResizeBuffers(nint swapChain)
        {
        }

        public virtual void OnAfterResizeBuffers(nint swapChain)
        {
            if (swapChain != IntPtr.Zero)
            {
                SwapChainHandle = swapChain;
            }
        }

        protected abstract bool TryGetDeviceFromSwapChain(nint swapChain, out nint device);

        protected static nint GetVTableFunctionAddress(nint instance, int functionIndex)
        {
            if (instance == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            nint vTable = Marshal.ReadIntPtr(instance);
            if (vTable == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return Marshal.ReadIntPtr(vTable + functionIndex * IntPtr.Size);
        }

        protected static void ReleaseComObject(nint pointer)
        {
            if (pointer != IntPtr.Zero)
            {
                Marshal.Release(pointer);
            }
        }

        protected bool TryGetSwapChainDevice(nint swapChain, Guid iid, out nint device)
        {
            device = IntPtr.Zero;
            if (swapChain == IntPtr.Zero)
            {
                return false;
            }

            nint getDeviceAddress = GetVTableFunctionAddress(swapChain, VTABLE_IDXGISwapChain_GetDevice);
            if (getDeviceAddress == IntPtr.Zero)
            {
                return false;
            }

            var getDevice = Marshal.GetDelegateForFunctionPointer<GetDeviceDelegate>(getDeviceAddress);
            return getDevice(swapChain, ref iid, out device) >= 0 && device != IntPtr.Zero;
        }

        protected bool TryGetOutputWindow(nint swapChain, out nint windowHandle)
        {
            windowHandle = IntPtr.Zero;
            if (swapChain == IntPtr.Zero)
            {
                return false;
            }

            nint getDescAddress = GetVTableFunctionAddress(swapChain, VTABLE_IDXGISwapChain_GetDesc);
            if (getDescAddress == IntPtr.Zero)
            {
                return false;
            }

            var getDesc = Marshal.GetDelegateForFunctionPointer<GetDescDelegate>(getDescAddress);
            if (getDesc(swapChain, out DXGI.DXGI_SWAP_CHAIN_DESC desc) < 0 || desc.OutputWindow == IntPtr.Zero)
            {
                return false;
            }

            windowHandle = desc.OutputWindow;
            return true;
        }
    }
}
