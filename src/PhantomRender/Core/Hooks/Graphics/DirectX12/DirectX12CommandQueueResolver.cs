using System;
using System.Runtime.InteropServices;
using System.Threading;
using PhantomRender.Core.Memory;
using PhantomRender.Core.Native;

namespace PhantomRender.Core.Hooks.Graphics
{
    public static class DirectX12CommandQueueResolver
    {
        private const int VTABLE_IDXGIDeviceSubObject_GetDevice = 7;

        private static readonly object SyncRoot = new object();
        private static IntPtr _capturedCommandQueue;
        private static int _loggedResolveMethod;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDeviceDelegate(IntPtr swapChain, ref Guid riid, out IntPtr ppDevice);

        public static bool CaptureCommandQueue(IntPtr commandQueue)
        {
            if (commandQueue == IntPtr.Zero)
            {
                return false;
            }

            lock (SyncRoot)
            {
                if (_capturedCommandQueue == commandQueue)
                {
                    return false;
                }

                Marshal.AddRef(commandQueue);

                IntPtr previous = _capturedCommandQueue;
                _capturedCommandQueue = commandQueue;

                if (previous != IntPtr.Zero)
                {
                    Marshal.Release(previous);
                }

                return true;
            }
        }

        public static bool TryGetCommandQueueFromSwapChain(IntPtr swapChain, out IntPtr commandQueue)
        {
            commandQueue = IntPtr.Zero;

            if (TryGetCapturedCommandQueue(out commandQueue))
            {
                if (Interlocked.CompareExchange(ref _loggedResolveMethod, 1, 0) == 0)
                {
                    Console.WriteLine("[PhantomRender] DX12: Command queue resolved via ExecuteCommandLists capture.");
                }

                return true;
            }

            if (swapChain == IntPtr.Zero)
            {
                return false;
            }

            if (TryGetCommandQueueViaGetDevice(swapChain, out commandQueue))
            {
                if (Interlocked.CompareExchange(ref _loggedResolveMethod, 2, 0) == 0)
                {
                    Console.WriteLine("[PhantomRender] DX12: Command queue resolved via IDXGISwapChain::GetDevice(IID_ID3D12CommandQueue).");
                }

                return true;
            }

            return false;
        }

        private static bool TryGetCapturedCommandQueue(out IntPtr commandQueue)
        {
            lock (SyncRoot)
            {
                if (_capturedCommandQueue == IntPtr.Zero)
                {
                    commandQueue = IntPtr.Zero;
                    return false;
                }

                Marshal.AddRef(_capturedCommandQueue);
                commandQueue = _capturedCommandQueue;
                return true;
            }
        }

        private static bool TryGetCommandQueueViaGetDevice(IntPtr swapChain, out IntPtr commandQueue)
        {
            commandQueue = IntPtr.Zero;

            try
            {
                IntPtr vTable = MemoryUtils.ReadIntPtr(swapChain);
                if (vTable == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr getDeviceAddress = MemoryUtils.ReadIntPtr(vTable + (VTABLE_IDXGIDeviceSubObject_GetDevice * IntPtr.Size));
                if (getDeviceAddress == IntPtr.Zero)
                {
                    return false;
                }

                var getDevice = Marshal.GetDelegateForFunctionPointer<GetDeviceDelegate>(getDeviceAddress);
                Guid iid = Direct3D12.IID_ID3D12CommandQueue;
                int hr = getDevice(swapChain, ref iid, out commandQueue);
                return hr >= 0 && commandQueue != IntPtr.Zero;
            }
            catch
            {
                commandQueue = IntPtr.Zero;
                return false;
            }
        }
    }
}
