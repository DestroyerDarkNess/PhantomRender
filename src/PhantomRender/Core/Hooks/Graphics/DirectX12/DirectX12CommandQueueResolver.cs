using System;
using System.Runtime.InteropServices;
using System.Threading;
using PhantomRender.Core.Memory;
using PhantomRender.Core.Native;

namespace PhantomRender.Core.Hooks.Graphics
{
    /// <summary>
    /// Resolves the real ID3D12CommandQueue* used by a DX12 swapchain by discovering
    /// the command queue pointer offset in the swapchain object (dummy swapchain scan).
    /// This follows the approach used by multiple working DX12 overlays and avoids
    /// hooking ExecuteCommandLists, which can be fragile in some games.
    /// </summary>
    public static unsafe class DirectX12CommandQueueResolver
    {
        // IDXGISwapChain inherits IDXGIDeviceSubObject, which exposes GetDevice at vtable index 7.
        // For DX12 swapchains, GetDevice can return an ID3D12CommandQueue when queried with IID_ID3D12CommandQueue.
        // This is more robust than memory-offset scanning when it works, so we try it first.
        private const int VTABLE_IDXGIDeviceSubObject_GetDevice = 7;

        private static readonly object _lock = new object();
        private static bool _initialized;
        private static bool _initFailed;
        private static nuint _commandQueueOffset;
        private static int _loggedResolveMethod; // 0 = not logged, 1 = GetDevice, 2 = offset scan

        public static bool IsInitialized => _initialized && !_initFailed;

        public static bool EnsureInitialized()
        {
            if (_initialized) return !_initFailed;

            lock (_lock)
            {
                if (_initialized) return !_initFailed;

                if (!TryFindCommandQueueOffset(out _commandQueueOffset))
                {
                    _initFailed = true;
                }

                _initialized = true;
                return !_initFailed;
            }
        }

        public static bool TryGetCommandQueueFromSwapChain(IntPtr swapChain, out IntPtr commandQueue)
        {
            commandQueue = IntPtr.Zero;

            if (swapChain == IntPtr.Zero) return false;

            try
            {
                // 1) Prefer querying via swapchain->GetDevice(IID_ID3D12CommandQueue).
                // This returns an owned reference (AddRef'ed) on success.
                if (TryGetCommandQueueViaGetDevice(swapChain, out commandQueue))
                {
                    if (Interlocked.CompareExchange(ref _loggedResolveMethod, 1, 0) == 0)
                    {
                        Console.WriteLine("[PhantomRender] DX12: Command queue resolved via IDXGISwapChain::GetDevice(IID_ID3D12CommandQueue).");
                        Console.Out.Flush();
                    }
                    return true;
                }

                // 2) Fallback: read the command queue pointer from the swapchain object at the discovered offset,
                // then QueryInterface to validate + obtain an owned ID3D12CommandQueue reference.
                if (!EnsureInitialized()) return false;

                IntPtr candidate = MemoryUtils.ReadIntPtr(swapChain + (int)_commandQueueOffset);
                if (candidate == IntPtr.Zero) return false;

                Guid iid = Direct3D12.IID_ID3D12CommandQueue;
                int hr = Marshal.QueryInterface(candidate, ref iid, out commandQueue);
                if (hr >= 0 && commandQueue != IntPtr.Zero && Interlocked.CompareExchange(ref _loggedResolveMethod, 2, 0) == 0)
                {
                    Console.WriteLine($"[PhantomRender] DX12: Command queue resolved via swapchain offset scan (offset=0x{_commandQueueOffset:X}).");
                    Console.Out.Flush();
                }
                return hr >= 0 && commandQueue != IntPtr.Zero;
            }
            catch
            {
                commandQueue = IntPtr.Zero;
                return false;
            }
        }

        private static bool TryGetCommandQueueViaGetDevice(IntPtr swapChain, out IntPtr commandQueue)
        {
            commandQueue = IntPtr.Zero;

            try
            {
                IntPtr vTable = MemoryUtils.ReadIntPtr(swapChain);
                if (vTable == IntPtr.Zero) return false;

                IntPtr getDeviceAddr = MemoryUtils.ReadIntPtr(vTable + VTABLE_IDXGIDeviceSubObject_GetDevice * IntPtr.Size);
                if (getDeviceAddr == IntPtr.Zero) return false;

                var getDevice = Marshal.GetDelegateForFunctionPointer<GetDeviceDelegate>(getDeviceAddr);
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

        private static bool TryFindCommandQueueOffset(out nuint commandQueueOffset)
        {
            commandQueueOffset = 0;

            IntPtr hwnd = NativeWindowHelper.CreateDummyWindow();
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            IntPtr device = IntPtr.Zero;
            IntPtr commandQueue = IntPtr.Zero;
            IntPtr factory = IntPtr.Zero;
            IntPtr swapChain = IntPtr.Zero;

            try
            {
                int hr = Direct3D12.D3D12CreateDevice(IntPtr.Zero, Direct3D12.D3D_FEATURE_LEVEL_11_0, Direct3D12.IID_ID3D12Device, out device);
                if (hr < 0 || device == IntPtr.Zero)
                {
                    return false;
                }

                var queueDesc = new Direct3D12.D3D12_COMMAND_QUEUE_DESC
                {
                    Type = Direct3D12.D3D12_COMMAND_LIST_TYPE_DIRECT,
                    Priority = 0,
                    Flags = 0,
                    NodeMask = 0
                };

                // ID3D12Device::CreateCommandQueue is at vtable index 8.
                IntPtr deviceVTable = MemoryUtils.ReadIntPtr(device);
                IntPtr createQueueAddr = MemoryUtils.ReadIntPtr(deviceVTable + 8 * IntPtr.Size);
                if (createQueueAddr == IntPtr.Zero)
                {
                    return false;
                }

                var createQueue = Marshal.GetDelegateForFunctionPointer<CreateCommandQueueDelegate>(createQueueAddr);
                hr = createQueue(device, ref queueDesc, Direct3D12.IID_ID3D12CommandQueue, out commandQueue);
                if (hr < 0 || commandQueue == IntPtr.Zero)
                {
                    return false;
                }

                Guid factoryIid = DXGI.IID_IDXGIFactory4;
                hr = DXGI.CreateDXGIFactory(ref factoryIid, out factory);
                if (hr < 0 || factory == IntPtr.Zero)
                {
                    return false;
                }

                // Create a dummy swapchain (IDXGIFactory::CreateSwapChain is vtable index 10).
                var swapChainDesc = new DXGI.DXGI_SWAP_CHAIN_DESC
                {
                    BufferCount = 3,
                    BufferDesc = new DXGI.DXGI_MODE_DESC
                    {
                        Width = 100,
                        Height = 100,
                        Format = DXGI.DXGI_FORMAT_R8G8B8A8_UNORM,
                        RefreshRate = new DXGI.DXGI_RATIONAL { Numerator = 60, Denominator = 1 },
                        Scaling = 0,
                        ScanlineOrdering = 0
                    },
                    BufferUsage = DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT,
                    OutputWindow = hwnd,
                    SampleDesc = new DXGI.DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                    SwapEffect = Direct3D12.DXGI_SWAP_EFFECT_FLIP_DISCARD,
                    Windowed = 1,
                    Flags = 0
                };

                IntPtr factoryVTable = MemoryUtils.ReadIntPtr(factory);
                IntPtr createSwapChainAddr = MemoryUtils.ReadIntPtr(factoryVTable + 10 * IntPtr.Size);
                if (createSwapChainAddr == IntPtr.Zero)
                {
                    return false;
                }

                var createSwapChain = Marshal.GetDelegateForFunctionPointer<CreateSwapChainDelegate>(createSwapChainAddr);
                hr = createSwapChain(factory, commandQueue, ref swapChainDesc, out swapChain);
                if (hr < 0 || swapChain == IntPtr.Zero)
                {
                    return false;
                }

                // Scan the swapchain object memory to locate the command queue pointer.
                nint* p = (nint*)swapChain;
                for (int i = 0; i < 1000; i++)
                {
                    if (p[i] == commandQueue)
                    {
                        commandQueueOffset = (nuint)(i * IntPtr.Size);
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (swapChain != IntPtr.Zero)
                {
                    Marshal.Release(swapChain);
                }

                if (factory != IntPtr.Zero)
                {
                    Marshal.Release(factory);
                }

                if (commandQueue != IntPtr.Zero)
                {
                    Marshal.Release(commandQueue);
                }

                if (device != IntPtr.Zero)
                {
                    Marshal.Release(device);
                }

                NativeWindowHelper.DestroyDummyWindow(hwnd);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateCommandQueueDelegate(IntPtr device, ref Direct3D12.D3D12_COMMAND_QUEUE_DESC desc, Guid riid, out IntPtr ppCommandQueue);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateSwapChainDelegate(IntPtr factory, IntPtr device, ref DXGI.DXGI_SWAP_CHAIN_DESC desc, out IntPtr ppSwapChain);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDeviceDelegate(IntPtr swapChain, ref Guid riid, out IntPtr ppDevice);
    }
}
