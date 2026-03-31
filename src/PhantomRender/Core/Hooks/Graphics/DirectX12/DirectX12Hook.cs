using System;
using System.Runtime.InteropServices;
using System.Threading;
using MinHook;
using PhantomRender.Core.Memory;
using PhantomRender.Core.Native;

namespace PhantomRender.Core.Hooks.Graphics
{
    public class DirectX12Hook : IDisposable
    {
        private const int VTABLE_Present = 8;
        private const int VTABLE_GetDesc = 12;
        private const int VTABLE_ResizeBuffers = 13;
        private const int VTABLE_Present1 = 22;
        private const int VTABLE_ID3D12Device_CreateCommandQueue = 8;
        private const int VTABLE_ID3D12CommandQueue_ExecuteCommandLists = 10;
        private const int VTABLE_IDXGIFactory_CreateSwapChain = 10;
        private const int VTABLE_IDXGIFactory4_CreateSwapChainForComposition = 24;
        private static readonly Guid IID_IDXGISwapChain1 = new Guid("790a45f7-0d42-4876-983a-0a55cfe6f4aa");

        [StructLayout(LayoutKind.Sequential)]
        private struct DXGI_SWAP_CHAIN_DESC1
        {
            public uint Width;
            public uint Height;
            public int Format;
            public int Stereo;
            public DXGI.DXGI_SAMPLE_DESC SampleDesc;
            public uint BufferUsage;
            public uint BufferCount;
            public int Scaling;
            public int SwapEffect;
            public int AlphaMode;
            public uint Flags;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int PresentDelegate(IntPtr swapChain, uint syncInterval, uint flags);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Present1Delegate(IntPtr swapChain, uint syncInterval, uint flags, IntPtr presentParameters);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int ResizeBuffersDelegate(IntPtr swapChain, uint bufferCount, uint width, uint height, int newFormat, uint swapChainFlags);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void ExecuteCommandListsDelegate(IntPtr commandQueue, uint numCommandLists, IntPtr commandLists);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateCommandQueueDelegate(IntPtr device, ref Direct3D12.D3D12_COMMAND_QUEUE_DESC desc, ref Guid riid, out IntPtr ppCommandQueue);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateSwapChainDelegate(IntPtr factory, IntPtr device, ref DXGI.DXGI_SWAP_CHAIN_DESC desc, out IntPtr ppSwapChain);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateSwapChainForCompositionDelegate(IntPtr factory, IntPtr device, ref DXGI_SWAP_CHAIN_DESC1 desc, IntPtr restrictToOutput, out IntPtr ppSwapChain);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SwapChainGetDescDelegate(IntPtr swapChain, out DXGI.DXGI_SWAP_CHAIN_DESC desc);

        public event Action<IntPtr, uint, uint> OnPresent;
        public event Action<IntPtr, uint, uint, uint, int, uint> OnResizeBuffers;

        private readonly HookEngine _hookEngine;
        private readonly PresentDelegate _originalPresent;
        private readonly Present1Delegate _originalPresent1;
        private readonly ResizeBuffersDelegate _originalResizeBuffers;
        private readonly ExecuteCommandListsDelegate _originalExecuteCommandLists;
        private readonly bool _unityCompatibilityMode;
        private readonly IntPtr _unityPresentSlotAddress;
        private readonly IntPtr _unityPresentOriginalAddress;
        private readonly PresentDelegate _unityPresentDetourCallback;
        private readonly IntPtr _unityPresent1SlotAddress;
        private readonly IntPtr _unityPresent1OriginalAddress;
        private readonly Present1Delegate _unityPresent1DetourCallback;
        private bool _unityPresentHookInstalled;
        private bool _unityPresent1HookInstalled;
        private int _executeCommandListsDisablePending;
        private int _executeCommandListsDisabled;

        [ThreadStatic]
        private static int _presentDepth;

        public DirectX12Hook(IntPtr swapChainAddress)
        {
            _hookEngine = new HookEngine();
            _unityCompatibilityMode = IsUnityProcess();

            IntPtr vTable = MemoryUtils.ReadIntPtr(swapChainAddress);
            IntPtr presentSlotAddress = vTable + (VTABLE_Present * IntPtr.Size);
            IntPtr presentAddress = MemoryUtils.ReadIntPtr(presentSlotAddress);
            IntPtr resizeBuffersAddress = MemoryUtils.ReadIntPtr(vTable + (VTABLE_ResizeBuffers * IntPtr.Size));

            if (_unityCompatibilityMode)
            {
                _unityPresentSlotAddress = presentSlotAddress;
                _unityPresentOriginalAddress = presentAddress;
                _unityPresentDetourCallback = new PresentDelegate(PresentHook);
                _originalPresent = Marshal.GetDelegateForFunctionPointer<PresentDelegate>(presentAddress);

                if (TryGetPresent1Slot(swapChainAddress, out IntPtr present1SlotAddress, out IntPtr present1Address))
                {
                    _unityPresent1SlotAddress = present1SlotAddress;
                    _unityPresent1OriginalAddress = present1Address;
                    _unityPresent1DetourCallback = new Present1Delegate(Present1Hook);
                    _originalPresent1 = Marshal.GetDelegateForFunctionPointer<Present1Delegate>(present1Address);
                }
            }
            else
            {
                _originalPresent = _hookEngine.CreateHook<PresentDelegate>(presentAddress, new PresentDelegate(PresentHook));
                _originalPresent1 = TryHookPresent1(swapChainAddress);
            }

            _originalResizeBuffers = _hookEngine.CreateHook<ResizeBuffersDelegate>(resizeBuffersAddress, new ResizeBuffersDelegate(ResizeBuffersHook));

            IntPtr executeCommandListsAddress = GetExecuteCommandListsAddress();
            if (executeCommandListsAddress != IntPtr.Zero)
            {
                _originalExecuteCommandLists = _hookEngine.CreateHook<ExecuteCommandListsDelegate>(executeCommandListsAddress, new ExecuteCommandListsDelegate(ExecuteCommandListsHook));
                Console.WriteLine($"[PhantomRender] DX12 ExecuteCommandLists hook address=0x{executeCommandListsAddress.ToInt64():X}.");
            }
        }

        public void Enable()
        {
            _hookEngine.EnableHooks();

            if (_unityCompatibilityMode)
            {
                EnableUnityPresentVTableHook();
                EnableUnityPresent1VTableHook();
                Console.WriteLine("[PhantomRender] DX12 Unity compatibility mode enabled. Present/Present1 vtable slot hooks are active.");
            }

            Console.WriteLine("[PhantomRender] DX12 Hooks Enabled (MinHook).");
        }

        public void Disable()
        {
            DisableUnityPresentVTableHook();
            DisableUnityPresent1VTableHook();
            _hookEngine.DisableHooks();
        }

        public bool TryGetOutputWindow(IntPtr swapChain, out IntPtr outputWindow)
        {
            outputWindow = IntPtr.Zero;
            if (swapChain == IntPtr.Zero)
            {
                return false;
            }

            IntPtr getDescAddress = GetVTableFunctionAddress(swapChain, VTABLE_GetDesc);
            if (getDescAddress == IntPtr.Zero)
            {
                return false;
            }

            var getDesc = Marshal.GetDelegateForFunctionPointer<SwapChainGetDescDelegate>(getDescAddress);
            if (getDesc(swapChain, out DXGI.DXGI_SWAP_CHAIN_DESC desc) < 0 || desc.OutputWindow == IntPtr.Zero)
            {
                return false;
            }

            outputWindow = desc.OutputWindow;
            return true;
        }

        private int PresentHook(IntPtr swapChain, uint syncInterval, uint flags)
        {
            if (_presentDepth > 0)
            {
                return _originalPresent(swapChain, syncInterval, flags);
            }

            _presentDepth++;
            DisableExecuteCommandListsHookIfNeeded();

            try
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
            finally
            {
                _presentDepth--;
            }
        }

        private int Present1Hook(IntPtr swapChain, uint syncInterval, uint flags, IntPtr presentParameters)
        {
            if (_presentDepth > 0)
            {
                return _originalPresent1(swapChain, syncInterval, flags, presentParameters);
            }

            _presentDepth++;
            DisableExecuteCommandListsHookIfNeeded();

            try
            {
                try
                {
                    OnPresent?.Invoke(swapChain, syncInterval, flags);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PhantomRender] DX12 Present1 error: {ex.Message}");
                }

                return _originalPresent1(swapChain, syncInterval, flags, presentParameters);
            }
            finally
            {
                _presentDepth--;
            }
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

        private void ExecuteCommandListsHook(IntPtr commandQueue, uint numCommandLists, IntPtr commandLists)
        {
            try
            {
                if (DirectX12CommandQueueResolver.CaptureCommandQueue(commandQueue))
                {
                    Interlocked.CompareExchange(ref _executeCommandListsDisablePending, 1, 0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DX12 ExecuteCommandLists capture error: {ex.Message}");
            }

            _originalExecuteCommandLists?.Invoke(commandQueue, numCommandLists, commandLists);
        }

        private void DisableExecuteCommandListsHookIfNeeded()
        {
            if (_originalExecuteCommandLists == null ||
                Volatile.Read(ref _executeCommandListsDisablePending) == 0 ||
                Volatile.Read(ref _executeCommandListsDisabled) != 0)
            {
                return;
            }

            try
            {
                _hookEngine.DisableHook(_originalExecuteCommandLists);
                Volatile.Write(ref _executeCommandListsDisabled, 1);
                Console.WriteLine("[PhantomRender] DX12 ExecuteCommandLists hook disabled after queue capture.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DX12 ExecuteCommandLists hook disable failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            DisableUnityPresentVTableHook();
            DisableUnityPresent1VTableHook();
            _hookEngine.Dispose();
            GC.SuppressFinalize(this);
        }

        public static IntPtr GetSwapChainAddress()
        {
            bool unityCompatibilityMode = IsUnityProcess();
            IntPtr hWnd = NativeWindowHelper.CreateDummyWindow();
            if (hWnd == IntPtr.Zero)
            {
                Console.WriteLine("[PhantomRender] DX12 dummy window creation failed.");
                return IntPtr.Zero;
            }

            IntPtr device = IntPtr.Zero;
            IntPtr commandQueue = IntPtr.Zero;
            IntPtr factory = IntPtr.Zero;

            try
            {
                if (!TryCreateDeviceAndCommandQueue(out device, out commandQueue, out int createQueueHr))
                {
                    Console.WriteLine($"[PhantomRender] DX12 CreateCommandQueue failed. hr=0x{createQueueHr:X8}, queue=0x0");
                    return IntPtr.Zero;
                }

                Guid factoryIid = DXGI.IID_IDXGIFactory4;
                int hr = DXGI.CreateDXGIFactory(ref factoryIid, out factory);
                if (hr < 0 || factory == IntPtr.Zero)
                {
                    Console.WriteLine($"[PhantomRender] DX12 CreateDXGIFactory failed. hr=0x{hr:X8}");
                    return IntPtr.Zero;
                }

                if (unityCompatibilityMode && TryCreateWindowedSwapChain(factory, commandQueue, hWnd, out IntPtr unitySwapChain))
                {
                    Console.WriteLine("[PhantomRender] DX12 Unity compatibility mode selected a windowed dummy swapchain.");
                    return unitySwapChain;
                }

                if (TryCreateCompositionSwapChain(factory, commandQueue, out IntPtr swapChain))
                {
                    Console.WriteLine("[PhantomRender] DX12 dummy swapchain created via CreateSwapChainForComposition.");
                    return swapChain;
                }

                if (TryCreateWindowedSwapChain(factory, commandQueue, hWnd, out swapChain))
                {
                    Console.WriteLine("[PhantomRender] DX12 dummy swapchain created via CreateSwapChain.");
                    return swapChain;
                }

                Console.WriteLine("[PhantomRender] DX12 overlay start failed: dummy swapchain address is null.");
                return IntPtr.Zero;
            }
            finally
            {
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

                NativeWindowHelper.DestroyDummyWindow(hWnd);
            }
        }

        private static bool TryCreateDeviceAndCommandQueue(out IntPtr device, out IntPtr commandQueue, out int createQueueHr)
        {
            device = IntPtr.Zero;
            commandQueue = IntPtr.Zero;
            createQueueHr = Direct3D12.D3D12CreateDevice(IntPtr.Zero, Direct3D12.D3D_FEATURE_LEVEL_11_0, Direct3D12.IID_ID3D12Device, out device);
            if (createQueueHr < 0 || device == IntPtr.Zero)
            {
                return false;
            }

            IntPtr createQueueAddress = GetVTableFunctionAddress(device, VTABLE_ID3D12Device_CreateCommandQueue);
            if (createQueueAddress == IntPtr.Zero)
            {
                createQueueHr = unchecked((int)0x80004005);
                return false;
            }

            var createQueue = Marshal.GetDelegateForFunctionPointer<CreateCommandQueueDelegate>(createQueueAddress);
            var queueDesc = new Direct3D12.D3D12_COMMAND_QUEUE_DESC
            {
                Type = Direct3D12.D3D12_COMMAND_LIST_TYPE_DIRECT,
                Priority = 0,
                Flags = 0,
                NodeMask = 0,
            };

            Guid commandQueueIid = Direct3D12.IID_ID3D12CommandQueue;
            createQueueHr = createQueue(device, ref queueDesc, ref commandQueueIid, out commandQueue);
            return createQueueHr >= 0 && commandQueue != IntPtr.Zero;
        }

        private static bool TryCreateCompositionSwapChain(IntPtr factory, IntPtr commandQueue, out IntPtr swapChain)
        {
            swapChain = IntPtr.Zero;

            IntPtr createSwapChainAddress = GetVTableFunctionAddress(factory, VTABLE_IDXGIFactory4_CreateSwapChainForComposition);
            if (createSwapChainAddress == IntPtr.Zero)
            {
                return false;
            }

            var createSwapChain = Marshal.GetDelegateForFunctionPointer<CreateSwapChainForCompositionDelegate>(createSwapChainAddress);
            var desc = new DXGI_SWAP_CHAIN_DESC1
            {
                Width = 100,
                Height = 100,
                Format = DXGI.DXGI_FORMAT_R8G8B8A8_UNORM,
                Stereo = 0,
                SampleDesc = new DXGI.DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                BufferUsage = DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT,
                BufferCount = 3,
                Scaling = 0,
                SwapEffect = Direct3D12.DXGI_SWAP_EFFECT_FLIP_DISCARD,
                AlphaMode = 0,
                Flags = 0,
            };

            int hr = createSwapChain(factory, commandQueue, ref desc, IntPtr.Zero, out swapChain);
            return hr >= 0 && swapChain != IntPtr.Zero;
        }

        private static bool TryCreateWindowedSwapChain(IntPtr factory, IntPtr commandQueue, IntPtr hWnd, out IntPtr swapChain)
        {
            swapChain = IntPtr.Zero;

            IntPtr createSwapChainAddress = GetVTableFunctionAddress(factory, VTABLE_IDXGIFactory_CreateSwapChain);
            if (createSwapChainAddress == IntPtr.Zero)
            {
                return false;
            }

            var createSwapChain = Marshal.GetDelegateForFunctionPointer<CreateSwapChainDelegate>(createSwapChainAddress);
            var desc = new DXGI.DXGI_SWAP_CHAIN_DESC
            {
                BufferCount = 3,
                BufferDesc = new DXGI.DXGI_MODE_DESC
                {
                    Width = 100,
                    Height = 100,
                    Format = DXGI.DXGI_FORMAT_R8G8B8A8_UNORM,
                    RefreshRate = new DXGI.DXGI_RATIONAL { Numerator = 60, Denominator = 1 },
                    Scaling = 0,
                    ScanlineOrdering = 0,
                },
                BufferUsage = DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT,
                OutputWindow = hWnd,
                SampleDesc = new DXGI.DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                SwapEffect = Direct3D12.DXGI_SWAP_EFFECT_FLIP_DISCARD,
                Windowed = 1,
                Flags = 0,
            };

            int hr = createSwapChain(factory, commandQueue, ref desc, out swapChain);
            return hr >= 0 && swapChain != IntPtr.Zero;
        }

        private Present1Delegate TryHookPresent1(IntPtr swapChain)
        {
            try
            {
                if (!TryGetPresent1Slot(swapChain, out _, out IntPtr present1Address) || present1Address == IntPtr.Zero)
                {
                    return null;
                }

                return _hookEngine.CreateHook<Present1Delegate>(present1Address, new Present1Delegate(Present1Hook));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DX12 Present1 hook init failed: {ex.Message}");
                return null;
            }
        }

        private static IntPtr GetExecuteCommandListsAddress()
        {
            IntPtr device = IntPtr.Zero;
            IntPtr commandQueue = IntPtr.Zero;

            try
            {
                if (!TryCreateDeviceAndCommandQueue(out device, out commandQueue, out _))
                {
                    return IntPtr.Zero;
                }

                return GetVTableFunctionAddress(commandQueue, VTABLE_ID3D12CommandQueue_ExecuteCommandLists);
            }
            finally
            {
                if (commandQueue != IntPtr.Zero)
                {
                    Marshal.Release(commandQueue);
                }

                if (device != IntPtr.Zero)
                {
                    Marshal.Release(device);
                }
            }
        }

        private static bool IsUnityProcess()
        {
            return NativeWindowHelper.GetModuleHandle("UnityPlayer.dll") != IntPtr.Zero;
        }

        private void EnableUnityPresentVTableHook()
        {
            if (_unityPresentHookInstalled || _unityPresentSlotAddress == IntPtr.Zero || _unityPresentOriginalAddress == IntPtr.Zero)
            {
                return;
            }

            IntPtr detourAddress = Marshal.GetFunctionPointerForDelegate(_unityPresentDetourCallback);
            MemoryUtils.WriteProtectedIntPtr(_unityPresentSlotAddress, detourAddress);
            _unityPresentHookInstalled = true;
        }

        private void DisableUnityPresentVTableHook()
        {
            if (!_unityPresentHookInstalled || _unityPresentSlotAddress == IntPtr.Zero || _unityPresentOriginalAddress == IntPtr.Zero)
            {
                return;
            }

            MemoryUtils.WriteProtectedIntPtr(_unityPresentSlotAddress, _unityPresentOriginalAddress);
            _unityPresentHookInstalled = false;
        }

        private static IntPtr GetVTableFunctionAddress(IntPtr instance, int functionIndex)
        {
            if (instance == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr vTable = MemoryUtils.ReadIntPtr(instance);
            if (vTable == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return MemoryUtils.ReadIntPtr(vTable + (functionIndex * IntPtr.Size));
        }

        private static bool TryGetPresent1Slot(IntPtr swapChain, out IntPtr slotAddress, out IntPtr functionAddress)
        {
            slotAddress = IntPtr.Zero;
            functionAddress = IntPtr.Zero;

            IntPtr swapChain1 = IntPtr.Zero;
            Guid iid = IID_IDXGISwapChain1;
            int hr = Marshal.QueryInterface(swapChain, ref iid, out swapChain1);
            if (hr < 0 || swapChain1 == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                IntPtr vTable = MemoryUtils.ReadIntPtr(swapChain1);
                if (vTable == IntPtr.Zero)
                {
                    return false;
                }

                slotAddress = vTable + (VTABLE_Present1 * IntPtr.Size);
                functionAddress = MemoryUtils.ReadIntPtr(slotAddress);
                return functionAddress != IntPtr.Zero;
            }
            finally
            {
                Marshal.Release(swapChain1);
            }
        }

        private void EnableUnityPresent1VTableHook()
        {
            if (_unityPresent1HookInstalled || _unityPresent1SlotAddress == IntPtr.Zero || _unityPresent1OriginalAddress == IntPtr.Zero)
            {
                return;
            }

            IntPtr detourAddress = Marshal.GetFunctionPointerForDelegate(_unityPresent1DetourCallback);
            MemoryUtils.WriteProtectedIntPtr(_unityPresent1SlotAddress, detourAddress);
            _unityPresent1HookInstalled = true;
        }

        private void DisableUnityPresent1VTableHook()
        {
            if (!_unityPresent1HookInstalled || _unityPresent1SlotAddress == IntPtr.Zero || _unityPresent1OriginalAddress == IntPtr.Zero)
            {
                return;
            }

            MemoryUtils.WriteProtectedIntPtr(_unityPresent1SlotAddress, _unityPresent1OriginalAddress);
            _unityPresent1HookInstalled = false;
        }
    }
}
