using System;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.D3D12;
using Hexa.NET.ImGui.Backends.Win32;
using PhantomRender.Core;
using PhantomRender.Core.Hooks.Graphics;
using PhantomRender.Core.Native;
using HexaImGui = Hexa.NET.ImGui.ImGui;

namespace PhantomRender.ImGui.Core.Renderers
{
    public sealed unsafe class DirectX12Renderer : DxgiRendererBase
    {
        private const int SRV_HEAP_CAPACITY = 2;
        private const int VTABLE_IUnknown_QueryInterface = 0;
        private const int VTABLE_IDXGISwapChain_GetBuffer = 9;
        private const int VTABLE_IDXGISwapChain_GetDesc = 12;
        private const int VTABLE_ID3D12Device_CreateCommandAllocator = 9;
        private const int VTABLE_ID3D12Device_CreateCommandList = 12;
        private const int VTABLE_ID3D12Device_CreateDescriptorHeap = 14;
        private const int VTABLE_ID3D12Device_GetDescriptorHandleIncrementSize = 15;
        private const int VTABLE_ID3D12Device_CreateRenderTargetView = 20;
        private const int VTABLE_ID3D12Device_CreateFence = 36;
        private const int VTABLE_ID3D12DescriptorHeap_GetCPUDescriptorHandleForHeapStart = 9;
        private const int VTABLE_ID3D12DescriptorHeap_GetGPUDescriptorHandleForHeapStart = 10;
        private const int VTABLE_ID3D12CommandAllocator_Reset = 8;
        private const int VTABLE_ID3D12GraphicsCommandList_Close = 9;
        private const int VTABLE_ID3D12GraphicsCommandList_Reset = 10;
        private const int VTABLE_ID3D12GraphicsCommandList_ResourceBarrier = 26;
        private const int VTABLE_ID3D12GraphicsCommandList_SetDescriptorHeaps = 28;
        private const int VTABLE_ID3D12GraphicsCommandList_OMSetRenderTargets = 46;
        private const int VTABLE_ID3D12CommandQueue_ExecuteCommandLists = 10;
        private const int VTABLE_ID3D12CommandQueue_Signal = 14;
        private const int VTABLE_ID3D12Fence_GetCompletedValue = 8;
        private const int VTABLE_ID3D12Fence_SetEventOnCompletion = 9;

        private const int D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV = 0;
        private const int D3D12_DESCRIPTOR_HEAP_TYPE_RTV = 2;
        private const int D3D12_DESCRIPTOR_HEAP_FLAG_NONE = 0;
        private const int D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE = 1;
        private const int D3D12_COMMAND_LIST_TYPE_DIRECT = 0;
        private const int D3D12_RESOURCE_BARRIER_TYPE_TRANSITION = 0;
        private const int D3D12_RESOURCE_BARRIER_FLAG_NONE = 0;
        private const uint D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES = 0xffffffff;
        private const int D3D12_RESOURCE_STATE_PRESENT = 0;
        private const int D3D12_RESOURCE_STATE_RENDER_TARGET = 0x4;
        private const uint INFINITE = 0xffffffff;

        private static readonly Guid IID_IDXGISwapChain3 = new Guid("94d99bdb-f1f8-4ab0-b236-7da0170edab1");
        private static readonly Guid IID_ID3D12Resource = new Guid("696442be-a72e-4059-bc79-5b5c98040fad");
        private static readonly Guid IID_ID3D12DescriptorHeap = new Guid("8efb471d-616c-4f49-90f7-127bb763fa51");
        private static readonly Guid IID_ID3D12Fence = new Guid("0a753dcf-c4d8-4b91-adf6-be5a60d95a76");

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D12_DESCRIPTOR_HEAP_DESC
        {
            public int Type;
            public int NumDescriptors;
            public int Flags;
            public uint NodeMask;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D12_RESOURCE_TRANSITION_BARRIER
        {
            public IntPtr Resource;
            public uint Subresource;
            public int StateBefore;
            public int StateAfter;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct D3D12_RESOURCE_BARRIER_UNION
        {
            [FieldOffset(0)]
            public D3D12_RESOURCE_TRANSITION_BARRIER Transition;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D12_RESOURCE_BARRIER
        {
            public int Type;
            public int Flags;
            public D3D12_RESOURCE_BARRIER_UNION Union;
            private int _padding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D12CpuDescriptorHandle
        {
            public nuint Ptr;

            public D3D12CpuDescriptorHandle(nuint ptr)
            {
                Ptr = ptr;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D12GpuDescriptorHandle
        {
            public ulong Ptr;

            public D3D12GpuDescriptorHandle(ulong ptr)
            {
                Ptr = ptr;
            }
        }

        private struct FrameContext
        {
            public IntPtr CommandAllocator;
            public IntPtr RenderTarget;
            public D3D12CpuDescriptorHandle RenderTargetView;
            public ulong FenceValue;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryInterfaceDelegate(IntPtr instance, ref Guid riid, out IntPtr objectPointer);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SwapChainGetDescDelegate(IntPtr swapChain, out DXGI.DXGI_SWAP_CHAIN_DESC desc);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SwapChainGetBufferDelegate(IntPtr swapChain, uint index, ref Guid riid, out IntPtr resource);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint SwapChain3GetCurrentBackBufferIndexDelegate(IntPtr swapChain3);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateDescriptorHeapDelegate(IntPtr device, ref D3D12_DESCRIPTOR_HEAP_DESC desc, ref Guid riid, out IntPtr heap);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint GetDescriptorHandleIncrementSizeDelegate(IntPtr device, int type);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CreateRenderTargetViewDelegate(IntPtr device, IntPtr resource, IntPtr desc, nuint destDescriptor);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateCommandAllocatorDelegate(IntPtr device, int type, ref Guid riid, out IntPtr allocator);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateCommandListDelegate(IntPtr device, uint nodeMask, int type, IntPtr allocator, IntPtr initialState, ref Guid riid, out IntPtr commandList);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateFenceDelegate(IntPtr device, ulong initialValue, int flags, ref Guid riid, out IntPtr fence);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetCPUDescriptorHandleForHeapStartDelegate(IntPtr heap, out D3D12CpuDescriptorHandle handle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetGPUDescriptorHandleForHeapStartDelegate(IntPtr heap, out D3D12GpuDescriptorHandle handle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CommandAllocatorResetDelegate(IntPtr allocator);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GraphicsCommandListCloseDelegate(IntPtr commandList);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GraphicsCommandListResetDelegate(IntPtr commandList, IntPtr allocator, IntPtr initialState);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GraphicsCommandListResourceBarrierDelegate(IntPtr commandList, uint numBarriers, D3D12_RESOURCE_BARRIER* barriers);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GraphicsCommandListSetDescriptorHeapsDelegate(IntPtr commandList, uint numHeaps, IntPtr* heaps);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GraphicsCommandListOMSetRenderTargetsDelegate(IntPtr commandList, uint numRenderTargetDescriptors, nuint* rtvs, int singleHandleRange, nuint* dsv);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CommandQueueExecuteCommandListsDelegate(IntPtr queue, uint numCommandLists, IntPtr* commandLists);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CommandQueueSignalDelegate(IntPtr queue, IntPtr fence, ulong value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate ulong FenceGetCompletedValueDelegate(IntPtr fence);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int FenceSetEventOnCompletionDelegate(IntPtr fence, ulong value, IntPtr eventHandle);

        [StructLayout(LayoutKind.Sequential)]
        private struct ImGui_ImplDX12_InitInfo_Fixed
        {
            public IntPtr Device;
            public IntPtr CommandQueue;
            public int NumFramesInFlight;
            public int RTVFormat;
            public int DSVFormat;
            public IntPtr UserData;
            public IntPtr SrvDescriptorHeap;
            public IntPtr SrvDescriptorAllocFn;
            public IntPtr SrvDescriptorFreeFn;
            public nuint LegacySingleSrvCpuDescriptor;
            public ulong LegacySingleSrvGpuDescriptor;
        }

        [DllImport("ImGuiImpl.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "CImGui_ImplDX12_Init")]
        private static extern bool CImGui_ImplDX12_Init_Manual(ImGui_ImplDX12_InitInfo_Fixed* info);

        [DllImport("ImGuiImpl.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "CImGui_ImplDX12_RenderDrawData")]
        private static extern void CImGui_ImplDX12_RenderDrawData_Manual(ImDrawData* drawData, IntPtr graphicsCommandList);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateEventW(IntPtr eventAttributes, int manualReset, int initialState, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private readonly object _sync = new object();
        private readonly Action _backendNewFrameAction;
        private readonly Action _backendRenderAction;

        private IntPtr _device;
        private IntPtr _commandQueue;
        private IntPtr _swapChain3;
        private IntPtr _swapChainForResources;
        private IntPtr _rtvHeap;
        private IntPtr _srvHeap;
        private IntPtr _commandList;
        private IntPtr _fence;
        private IntPtr _fenceEvent;
        private FrameContext[] _frames;
        private D3D12CpuDescriptorHandle _rtvCpuStart;
        private D3D12CpuDescriptorHandle _srvCpuStart;
        private D3D12GpuDescriptorHandle _srvGpuStart;
        private D3D12CpuDescriptorHandle _imguiSrvCpu;
        private D3D12GpuDescriptorHandle _imguiSrvGpu;
        private uint _bufferCount;
        private int _rtvFormat;
        private uint _rtvDescriptorSize;
        private uint _srvDescriptorSize;
        private ulong _fenceValue;
        private bool _imguiBackendInitialized;
        private bool _loggedWaitingQueue;
        private bool _loggedSwapchainDesc;
        private SwapChain3GetCurrentBackBufferIndexDelegate _getCurrentBackBufferIndex;
        private CommandAllocatorResetDelegate _commandAllocatorReset;
        private GraphicsCommandListCloseDelegate _graphicsCommandListClose;
        private GraphicsCommandListResetDelegate _graphicsCommandListReset;
        private GraphicsCommandListResourceBarrierDelegate _graphicsCommandListResourceBarrier;
        private GraphicsCommandListSetDescriptorHeapsDelegate _graphicsCommandListSetDescriptorHeaps;
        private GraphicsCommandListOMSetRenderTargetsDelegate _graphicsCommandListOMSetRenderTargets;
        private CommandQueueExecuteCommandListsDelegate _commandQueueExecuteCommandLists;
        private CommandQueueSignalDelegate _commandQueueSignal;
        private FenceGetCompletedValueDelegate _fenceGetCompletedValue;
        private FenceSetEventOnCompletionDelegate _fenceSetEventOnCompletion;

        public DirectX12Renderer()
            : base(GraphicsApi.DirectX12)
        {
            _backendNewFrameAction = BackendNewFrame;
            _backendRenderAction = BackendRender;
        }

        public override bool Initialize(nint device, nint windowHandle)
        {
            if (IsInitialized)
            {
                return true;
            }

            if (device == IntPtr.Zero || windowHandle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                RaiseRendererInitializing(device, windowHandle);
                InitializeImGui(windowHandle);

                _device = device;
                Marshal.AddRef(_device);
                IsInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                ReportRuntimeError("DirectX12.Initialize", ex);
                Cleanup();
                return false;
            }
        }

        public override void NewFrame()
        {
            if (!IsInitialized || !_imguiBackendInitialized || FrameStarted)
            {
                return;
            }

            try
            {
                BeginFrameCore(_backendNewFrameAction);
                FrameStarted = true;
            }
            catch (Exception ex)
            {
                ReportRuntimeError("DirectX12.NewFrame", ex);
            }
        }

        public override void Render(nint swapChain)
        {
            if (!IsInitialized)
            {
                return;
            }

            if (swapChain != IntPtr.Zero)
            {
                SwapChainHandle = swapChain;
            }

            if (SwapChainHandle == IntPtr.Zero)
            {
                return;
            }

            lock (_sync)
            {
                try
                {
                    if (!EnsureDx12Ready(SwapChainHandle))
                    {
                        return;
                    }

                    if (!FrameStarted)
                    {
                        NewFrame();
                        if (!FrameStarted)
                        {
                            return;
                        }
                    }

                    RenderFrameCore(_backendRenderAction);
                }
                catch (Exception ex)
                {
                    ReportRuntimeError("DirectX12.Render", ex);
                }
                finally
                {
                    FrameStarted = false;
                }
            }
        }

        public override void OnBeforeResizeBuffers(nint swapChain)
        {
            base.OnBeforeResizeBuffers(swapChain);

            if (!IsInitialized)
            {
                return;
            }

            lock (_sync)
            {
                ShutdownBackend();
                ReleaseSwapChainResources();
            }
        }

        public override void OnAfterResizeBuffers(nint swapChain)
        {
            base.OnAfterResizeBuffers(swapChain);
        }

        public override void OnLostDevice()
        {
            OnBeforeResizeBuffers(IntPtr.Zero);
        }

        public override void OnResetDevice()
        {
        }

        public override void Dispose()
        {
            lock (_sync)
            {
                Cleanup();
            }
        }

        protected override bool TryGetDeviceFromSwapChain(nint swapChain, out nint device)
        {
            return TryGetSwapChainDevice(swapChain, Direct3D12.IID_ID3D12Device, out device);
        }

        private void Cleanup()
        {
            ShutdownBackend();
            ReleaseSwapChainResources();

            ReleaseComObject(_commandQueue);
            _commandQueue = IntPtr.Zero;

            ReleaseComObject(_device);
            _device = IntPtr.Zero;

            if (IsInitialized)
            {
                ShutdownImGui();
                IsInitialized = false;
            }

            FrameStarted = false;
            SwapChainHandle = IntPtr.Zero;
        }

        private void BackendNewFrame()
        {
            ImGuiImplD3D12.SetCurrentContext(Context);
            ImGuiImplD3D12.NewFrame();
            ImGuiImplWin32.SetCurrentContext(Context);
            ImGuiImplWin32.NewFrame();
        }

        private void BackendRender()
        {
            ImGuiImplD3D12.SetCurrentContext(Context);
            ImDrawDataPtr drawData = HexaImGui.GetDrawData();
            if (drawData.CmdListsCount > 0 && drawData.TotalVtxCount > 0)
            {
                RenderDrawData(drawData);
            }
        }

        private void ShutdownBackend()
        {
            if (!_imguiBackendInitialized)
            {
                return;
            }

            try
            {
                if (!Context.IsNull)
                {
                    ImGuiImplD3D12.SetCurrentContext(Context);
                }

                ImGuiImplD3D12.Shutdown();
            }
            catch
            {
            }
            finally
            {
                _imguiBackendInitialized = false;
            }
        }

        private void ReleaseSwapChainResources()
        {
            try
            {
                WaitForGpuIdle();
            }
            catch
            {
            }

            _swapChainForResources = IntPtr.Zero;

            ReleaseComObject(_swapChain3);
            _swapChain3 = IntPtr.Zero;

            if (_frames != null)
            {
                for (int i = 0; i < _frames.Length; i++)
                {
                    ReleaseComObject(_frames[i].RenderTarget);
                    _frames[i].RenderTarget = IntPtr.Zero;

                    ReleaseComObject(_frames[i].CommandAllocator);
                    _frames[i].CommandAllocator = IntPtr.Zero;
                    _frames[i].FenceValue = 0;
                }
            }

            _frames = null;

            ReleaseComObject(_rtvHeap);
            _rtvHeap = IntPtr.Zero;

            ReleaseComObject(_srvHeap);
            _srvHeap = IntPtr.Zero;

            ReleaseComObject(_commandList);
            _commandList = IntPtr.Zero;

            ReleaseComObject(_fence);
            _fence = IntPtr.Zero;

            if (_fenceEvent != IntPtr.Zero)
            {
                CloseHandle(_fenceEvent);
                _fenceEvent = IntPtr.Zero;
            }

            _bufferCount = 0;
            _rtvFormat = 0;
            _rtvDescriptorSize = 0;
            _srvDescriptorSize = 0;
            _fenceValue = 0;
            _rtvCpuStart = default;
            _srvCpuStart = default;
            _srvGpuStart = default;
            _imguiSrvCpu = default;
            _imguiSrvGpu = default;
            _loggedSwapchainDesc = false;
            _getCurrentBackBufferIndex = null;
            _commandAllocatorReset = null;
            _graphicsCommandListClose = null;
            _graphicsCommandListReset = null;
            _graphicsCommandListResourceBarrier = null;
            _graphicsCommandListSetDescriptorHeaps = null;
            _graphicsCommandListOMSetRenderTargets = null;
            _commandQueueExecuteCommandLists = null;
            _commandQueueSignal = null;
            _fenceGetCompletedValue = null;
            _fenceSetEventOnCompletion = null;
        }

        private bool EnsureDx12Ready(IntPtr swapChain)
        {
            if (_device == IntPtr.Zero)
            {
                return false;
            }

            if (!EnsureCommandQueue(swapChain))
            {
                return false;
            }

            if (!EnsureSwapChainResources(swapChain))
            {
                return false;
            }

            if (!_imguiBackendInitialized && !InitializeBackend())
            {
                return false;
            }

            return true;
        }

        private bool EnsureCommandQueue(IntPtr swapChain)
        {
            if (_commandQueue != IntPtr.Zero)
            {
                return true;
            }

            if (!DirectX12CommandQueueResolver.TryGetCommandQueueFromSwapChain(swapChain, out IntPtr newQueue) || newQueue == IntPtr.Zero)
            {
                if (!_loggedWaitingQueue)
                {
                    Console.WriteLine("[PhantomRender] DX12 detected, waiting for command queue...");
                    _loggedWaitingQueue = true;
                }

                return false;
            }

            if (_commandQueue == IntPtr.Zero)
            {
                _commandQueue = newQueue;
                Console.WriteLine($"[PhantomRender] DX12: Command queue resolved: 0x{_commandQueue.ToInt64():X}.");
                return true;
            }

            ReleaseComObject(newQueue);
            return true;
        }

        private bool EnsureSwapChainResources(IntPtr swapChain)
        {
            if (_swapChainForResources == swapChain &&
                _swapChain3 != IntPtr.Zero &&
                _frames != null &&
                _frames.Length > 0 &&
                _rtvHeap != IntPtr.Zero &&
                _srvHeap != IntPtr.Zero &&
                _commandList != IntPtr.Zero &&
                _fence != IntPtr.Zero &&
                _fenceEvent != IntPtr.Zero)
            {
                return true;
            }

            if (_swapChainForResources != IntPtr.Zero && _swapChainForResources != swapChain)
            {
                ShutdownBackend();
            }

            ReleaseSwapChainResources();

            if (!TryQuerySwapChain3(swapChain, out _swapChain3))
            {
                return false;
            }

            if (!TryGetSwapChainDesc(swapChain, out DXGI.DXGI_SWAP_CHAIN_DESC desc))
            {
                return false;
            }

            _bufferCount = desc.BufferCount;
            _rtvFormat = desc.BufferDesc.Format;

            if (_bufferCount == 0)
            {
                return false;
            }

            if (!CreateHeapsAndViews(swapChain))
            {
                return false;
            }

            if (!CreateCommandObjects())
            {
                return false;
            }

            if (!CacheHotPathDelegates())
            {
                return false;
            }

            _swapChainForResources = swapChain;

            if (!_loggedSwapchainDesc)
            {
                Console.WriteLine($"[PhantomRender] DX12 Init: SwapChainDesc Buffers={_bufferCount}, Format={_rtvFormat}, Windowed={desc.Windowed}");
                _loggedSwapchainDesc = true;
            }

            return true;
        }

        private bool CreateHeapsAndViews(IntPtr swapChain)
        {
            var rtvHeapDesc = new D3D12_DESCRIPTOR_HEAP_DESC
            {
                Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV,
                NumDescriptors = (int)_bufferCount,
                Flags = D3D12_DESCRIPTOR_HEAP_FLAG_NONE,
                NodeMask = 0,
            };

            Guid heapIid = IID_ID3D12DescriptorHeap;
            if (CreateDescriptorHeap(_device, ref rtvHeapDesc, ref heapIid, out _rtvHeap) < 0 || _rtvHeap == IntPtr.Zero)
            {
                return false;
            }

            _rtvDescriptorSize = GetDescriptorHandleIncrementSize(_device, D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
            _rtvCpuStart = GetCPUDescriptorHandleForHeapStart(_rtvHeap);
            if (_rtvDescriptorSize == 0 || _rtvCpuStart.Ptr == 0)
            {
                return false;
            }

            var srvHeapDesc = new D3D12_DESCRIPTOR_HEAP_DESC
            {
                Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,
                NumDescriptors = SRV_HEAP_CAPACITY,
                Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE,
                NodeMask = 0,
            };

            if (CreateDescriptorHeap(_device, ref srvHeapDesc, ref heapIid, out _srvHeap) < 0 || _srvHeap == IntPtr.Zero)
            {
                return false;
            }

            _srvDescriptorSize = GetDescriptorHandleIncrementSize(_device, D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
            _srvCpuStart = GetCPUDescriptorHandleForHeapStart(_srvHeap);
            _srvGpuStart = GetGPUDescriptorHandleForHeapStart(_srvHeap);

            if (_srvDescriptorSize == 0 || _srvCpuStart.Ptr == 0)
            {
                return false;
            }

            uint legacySrvIndex = _srvGpuStart.Ptr == 0 ? 1u : 0u;
            _imguiSrvCpu = new D3D12CpuDescriptorHandle(_srvCpuStart.Ptr + (nuint)(legacySrvIndex * _srvDescriptorSize));
            _imguiSrvGpu = new D3D12GpuDescriptorHandle(_srvGpuStart.Ptr + (ulong)(legacySrvIndex * _srvDescriptorSize));
            _frames = new FrameContext[_bufferCount];

            for (uint i = 0; i < _bufferCount; i++)
            {
                Guid resourceIid = IID_ID3D12Resource;
                if (GetBuffer(swapChain, i, ref resourceIid, out IntPtr resource) < 0 || resource == IntPtr.Zero)
                {
                    return false;
                }

                D3D12CpuDescriptorHandle rtvHandle = new D3D12CpuDescriptorHandle(_rtvCpuStart.Ptr + (nuint)(i * _rtvDescriptorSize));
                CreateRenderTargetView(_device, resource, IntPtr.Zero, rtvHandle.Ptr);

                _frames[i] = new FrameContext
                {
                    CommandAllocator = IntPtr.Zero,
                    RenderTarget = resource,
                    RenderTargetView = rtvHandle,
                    FenceValue = 0,
                };
            }

            return true;
        }

        private bool CreateCommandObjects()
        {
            for (int i = 0; i < _frames.Length; i++)
            {
                Guid allocatorIid = Direct3D12.IID_ID3D12CommandAllocator;
                if (CreateCommandAllocator(_device, D3D12_COMMAND_LIST_TYPE_DIRECT, ref allocatorIid, out IntPtr allocator) < 0 || allocator == IntPtr.Zero)
                {
                    return false;
                }

                _frames[i].CommandAllocator = allocator;
            }

            Guid commandListIid = Direct3D12.IID_ID3D12GraphicsCommandList;
            if (CreateCommandList(_device, 0, D3D12_COMMAND_LIST_TYPE_DIRECT, _frames[0].CommandAllocator, IntPtr.Zero, ref commandListIid, out _commandList) < 0 || _commandList == IntPtr.Zero)
            {
                return false;
            }

            if (GraphicsCommandListClose(_commandList) < 0)
            {
                return false;
            }

            Guid fenceIid = IID_ID3D12Fence;
            if (CreateFence(_device, 0, 0, ref fenceIid, out _fence) < 0 || _fence == IntPtr.Zero)
            {
                return false;
            }

            _fenceEvent = CreateEventW(IntPtr.Zero, 0, 0, null);
            return _fenceEvent != IntPtr.Zero;
        }

        private bool CacheHotPathDelegates()
        {
            _getCurrentBackBufferIndex = GetVTableDelegate<SwapChain3GetCurrentBackBufferIndexDelegate>(_swapChain3, 36);
            _commandAllocatorReset = GetVTableDelegate<CommandAllocatorResetDelegate>(_frames[0].CommandAllocator, VTABLE_ID3D12CommandAllocator_Reset);
            _graphicsCommandListClose = GetVTableDelegate<GraphicsCommandListCloseDelegate>(_commandList, VTABLE_ID3D12GraphicsCommandList_Close);
            _graphicsCommandListReset = GetVTableDelegate<GraphicsCommandListResetDelegate>(_commandList, VTABLE_ID3D12GraphicsCommandList_Reset);
            _graphicsCommandListResourceBarrier = GetVTableDelegate<GraphicsCommandListResourceBarrierDelegate>(_commandList, VTABLE_ID3D12GraphicsCommandList_ResourceBarrier);
            _graphicsCommandListSetDescriptorHeaps = GetVTableDelegate<GraphicsCommandListSetDescriptorHeapsDelegate>(_commandList, VTABLE_ID3D12GraphicsCommandList_SetDescriptorHeaps);
            _graphicsCommandListOMSetRenderTargets = GetVTableDelegate<GraphicsCommandListOMSetRenderTargetsDelegate>(_commandList, VTABLE_ID3D12GraphicsCommandList_OMSetRenderTargets);
            _commandQueueExecuteCommandLists = GetVTableDelegate<CommandQueueExecuteCommandListsDelegate>(_commandQueue, VTABLE_ID3D12CommandQueue_ExecuteCommandLists);
            _commandQueueSignal = GetVTableDelegate<CommandQueueSignalDelegate>(_commandQueue, VTABLE_ID3D12CommandQueue_Signal);
            _fenceGetCompletedValue = GetVTableDelegate<FenceGetCompletedValueDelegate>(_fence, VTABLE_ID3D12Fence_GetCompletedValue);
            _fenceSetEventOnCompletion = GetVTableDelegate<FenceSetEventOnCompletionDelegate>(_fence, VTABLE_ID3D12Fence_SetEventOnCompletion);

            return _getCurrentBackBufferIndex != null &&
                   _commandAllocatorReset != null &&
                   _graphicsCommandListClose != null &&
                   _graphicsCommandListReset != null &&
                   _graphicsCommandListResourceBarrier != null &&
                   _graphicsCommandListSetDescriptorHeaps != null &&
                   _graphicsCommandListOMSetRenderTargets != null &&
                   _commandQueueExecuteCommandLists != null &&
                   _commandQueueSignal != null &&
                   _fenceGetCompletedValue != null &&
                   _fenceSetEventOnCompletion != null;
        }

        private bool InitializeBackend()
        {
            try
            {
                ImGuiImplD3D12.SetCurrentContext(Context);

                ImGui_ImplDX12_InitInfo_Fixed info = default;
                info.Device = _device;
                info.CommandQueue = _commandQueue;
                info.NumFramesInFlight = (int)_bufferCount;
                info.RTVFormat = _rtvFormat;
                info.DSVFormat = 0;
                info.UserData = IntPtr.Zero;
                info.SrvDescriptorHeap = _srvHeap;
                info.SrvDescriptorAllocFn = IntPtr.Zero;
                info.SrvDescriptorFreeFn = IntPtr.Zero;
                info.LegacySingleSrvCpuDescriptor = _imguiSrvCpu.Ptr;
                info.LegacySingleSrvGpuDescriptor = _imguiSrvGpu.Ptr;

                if (!CImGui_ImplDX12_Init_Manual(&info))
                {
                    return false;
                }

                bool created = ImGuiImplD3D12.CreateDeviceObjects();
                _imguiBackendInitialized = created;
                return created;
            }
            catch (Exception ex)
            {
                ReportRuntimeError("DirectX12.InitializeBackend", ex);
                return false;
            }
        }

        private void RenderDrawData(ImDrawDataPtr drawData)
        {
            if (_frames == null ||
                _commandQueue == IntPtr.Zero ||
                _commandList == IntPtr.Zero ||
                _swapChain3 == IntPtr.Zero ||
                _getCurrentBackBufferIndex == null ||
                _commandAllocatorReset == null ||
                _graphicsCommandListReset == null ||
                _graphicsCommandListResourceBarrier == null ||
                _graphicsCommandListOMSetRenderTargets == null ||
                _graphicsCommandListSetDescriptorHeaps == null ||
                _graphicsCommandListClose == null ||
                _commandQueueExecuteCommandLists == null ||
                _commandQueueSignal == null)
            {
                return;
            }

            HexaImGui.SetCurrentContext(Context);
            ImGuiImplWin32.SetCurrentContext(Context);
            ImGuiImplD3D12.SetCurrentContext(Context);

            uint frameIndex = _getCurrentBackBufferIndex(_swapChain3);
            if (frameIndex >= _frames.Length)
            {
                return;
            }

            ref FrameContext frame = ref _frames[frameIndex];

            WaitForFrame(ref frame);

            if (_commandAllocatorReset(frame.CommandAllocator) < 0)
            {
                return;
            }

            if (_graphicsCommandListReset(_commandList, frame.CommandAllocator, IntPtr.Zero) < 0)
            {
                return;
            }

            var barrier = new D3D12_RESOURCE_BARRIER
            {
                Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION,
                Flags = D3D12_RESOURCE_BARRIER_FLAG_NONE,
                Union = new D3D12_RESOURCE_BARRIER_UNION
                {
                    Transition = new D3D12_RESOURCE_TRANSITION_BARRIER
                    {
                        Resource = frame.RenderTarget,
                        Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES,
                        StateBefore = D3D12_RESOURCE_STATE_PRESENT,
                        StateAfter = D3D12_RESOURCE_STATE_RENDER_TARGET,
                    },
                },
            };

            _graphicsCommandListResourceBarrier(_commandList, 1, &barrier);

            nuint rtvHandle = frame.RenderTargetView.Ptr;
            _graphicsCommandListOMSetRenderTargets(_commandList, 1, &rtvHandle, 0, null);

            IntPtr srvHeap = _srvHeap;
            _graphicsCommandListSetDescriptorHeaps(_commandList, 1, &srvHeap);

            if (drawData.Handle == null)
            {
                return;
            }

            CImGui_ImplDX12_RenderDrawData_Manual(drawData, _commandList);

            barrier.Union.Transition.StateBefore = D3D12_RESOURCE_STATE_RENDER_TARGET;
            barrier.Union.Transition.StateAfter = D3D12_RESOURCE_STATE_PRESENT;
            _graphicsCommandListResourceBarrier(_commandList, 1, &barrier);

            if (_graphicsCommandListClose(_commandList) < 0)
            {
                return;
            }

            IntPtr commandList = _commandList;
            _commandQueueExecuteCommandLists(_commandQueue, 1, &commandList);

            ulong nextFenceValue = ++_fenceValue;
            _commandQueueSignal(_commandQueue, _fence, nextFenceValue);
            frame.FenceValue = nextFenceValue;
        }

        private void WaitForGpuIdle()
        {
            if (_commandQueue == IntPtr.Zero || _fence == IntPtr.Zero || _fenceEvent == IntPtr.Zero || _commandQueueSignal == null || _fenceSetEventOnCompletion == null)
            {
                return;
            }

            ulong value = ++_fenceValue;
            _commandQueueSignal(_commandQueue, _fence, value);
            _fenceSetEventOnCompletion(_fence, value, _fenceEvent);
            WaitForSingleObject(_fenceEvent, INFINITE);
        }

        private void WaitForFrame(ref FrameContext frame)
        {
            if (frame.FenceValue == 0 || _fence == IntPtr.Zero || _fenceGetCompletedValue == null || _fenceSetEventOnCompletion == null)
            {
                return;
            }

            ulong completed = _fenceGetCompletedValue(_fence);
            if (completed >= frame.FenceValue)
            {
                return;
            }

            _fenceSetEventOnCompletion(_fence, frame.FenceValue, _fenceEvent);
            WaitForSingleObject(_fenceEvent, INFINITE);
        }

        private static TDelegate GetVTableDelegate<TDelegate>(IntPtr instance, int functionIndex)
            where TDelegate : class
        {
            IntPtr address = GetVTableFunctionAddress(instance, functionIndex);
            if (address == IntPtr.Zero)
            {
                return null;
            }

#if NET5_0_OR_GREATER
            return Marshal.GetDelegateForFunctionPointer<TDelegate>(address);
#else
            return Marshal.GetDelegateForFunctionPointer(address, typeof(TDelegate)) as TDelegate;
#endif
        }

        private static bool TryGetSwapChainDesc(IntPtr swapChain, out DXGI.DXGI_SWAP_CHAIN_DESC desc)
        {
            desc = default;
            IntPtr address = GetVTableFunctionAddress(swapChain, VTABLE_IDXGISwapChain_GetDesc);
            if (address == IntPtr.Zero)
            {
                return false;
            }

            var getDesc = Marshal.GetDelegateForFunctionPointer<SwapChainGetDescDelegate>(address);
            return getDesc(swapChain, out desc) >= 0 && desc.BufferCount != 0;
        }

        private static bool TryQuerySwapChain3(IntPtr swapChain, out IntPtr swapChain3)
        {
            swapChain3 = IntPtr.Zero;
            IntPtr address = GetVTableFunctionAddress(swapChain, VTABLE_IUnknown_QueryInterface);
            if (address == IntPtr.Zero)
            {
                return false;
            }

            var queryInterface = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(address);
            Guid iid = IID_IDXGISwapChain3;
            return queryInterface(swapChain, ref iid, out swapChain3) >= 0 && swapChain3 != IntPtr.Zero;
        }

        private static uint GetCurrentBackBufferIndex(IntPtr swapChain3)
        {
            IntPtr address = GetVTableFunctionAddress(swapChain3, 36);
            if (address == IntPtr.Zero)
            {
                return 0;
            }

            var getCurrentBackBufferIndex = Marshal.GetDelegateForFunctionPointer<SwapChain3GetCurrentBackBufferIndexDelegate>(address);
            return getCurrentBackBufferIndex(swapChain3);
        }

        private static int GetBuffer(IntPtr swapChain, uint bufferIndex, ref Guid riid, out IntPtr resource)
        {
            resource = IntPtr.Zero;
            IntPtr address = GetVTableFunctionAddress(swapChain, VTABLE_IDXGISwapChain_GetBuffer);
            if (address == IntPtr.Zero)
            {
                return -1;
            }

            var getBuffer = Marshal.GetDelegateForFunctionPointer<SwapChainGetBufferDelegate>(address);
            return getBuffer(swapChain, bufferIndex, ref riid, out resource);
        }

        private static int CreateDescriptorHeap(IntPtr device, ref D3D12_DESCRIPTOR_HEAP_DESC desc, ref Guid riid, out IntPtr heap)
        {
            heap = IntPtr.Zero;
            IntPtr address = GetVTableFunctionAddress(device, VTABLE_ID3D12Device_CreateDescriptorHeap);
            if (address == IntPtr.Zero)
            {
                return -1;
            }

            var createDescriptorHeap = Marshal.GetDelegateForFunctionPointer<CreateDescriptorHeapDelegate>(address);
            return createDescriptorHeap(device, ref desc, ref riid, out heap);
        }

        private static uint GetDescriptorHandleIncrementSize(IntPtr device, int type)
        {
            IntPtr address = GetVTableFunctionAddress(device, VTABLE_ID3D12Device_GetDescriptorHandleIncrementSize);
            if (address == IntPtr.Zero)
            {
                return 0;
            }

            var getDescriptorHandleIncrementSize = Marshal.GetDelegateForFunctionPointer<GetDescriptorHandleIncrementSizeDelegate>(address);
            return getDescriptorHandleIncrementSize(device, type);
        }

        private static D3D12CpuDescriptorHandle GetCPUDescriptorHandleForHeapStart(IntPtr heap)
        {
            IntPtr address = GetVTableFunctionAddress(heap, VTABLE_ID3D12DescriptorHeap_GetCPUDescriptorHandleForHeapStart);
            if (address == IntPtr.Zero)
            {
                return default;
            }

            var getHandle = Marshal.GetDelegateForFunctionPointer<GetCPUDescriptorHandleForHeapStartDelegate>(address);
            getHandle(heap, out D3D12CpuDescriptorHandle handle);
            return handle;
        }

        private static D3D12GpuDescriptorHandle GetGPUDescriptorHandleForHeapStart(IntPtr heap)
        {
            IntPtr address = GetVTableFunctionAddress(heap, VTABLE_ID3D12DescriptorHeap_GetGPUDescriptorHandleForHeapStart);
            if (address == IntPtr.Zero)
            {
                return default;
            }

            var getHandle = Marshal.GetDelegateForFunctionPointer<GetGPUDescriptorHandleForHeapStartDelegate>(address);
            getHandle(heap, out D3D12GpuDescriptorHandle handle);
            return handle;
        }

        private static void CreateRenderTargetView(IntPtr device, IntPtr resource, IntPtr desc, nuint destDescriptor)
        {
            IntPtr address = GetVTableFunctionAddress(device, VTABLE_ID3D12Device_CreateRenderTargetView);
            if (address == IntPtr.Zero)
            {
                return;
            }

            var createRenderTargetView = Marshal.GetDelegateForFunctionPointer<CreateRenderTargetViewDelegate>(address);
            createRenderTargetView(device, resource, desc, destDescriptor);
        }

        private static int CreateCommandAllocator(IntPtr device, int type, ref Guid riid, out IntPtr allocator)
        {
            allocator = IntPtr.Zero;
            IntPtr address = GetVTableFunctionAddress(device, VTABLE_ID3D12Device_CreateCommandAllocator);
            if (address == IntPtr.Zero)
            {
                return -1;
            }

            var createCommandAllocator = Marshal.GetDelegateForFunctionPointer<CreateCommandAllocatorDelegate>(address);
            return createCommandAllocator(device, type, ref riid, out allocator);
        }

        private static int CreateCommandList(IntPtr device, uint nodeMask, int type, IntPtr allocator, IntPtr initialState, ref Guid riid, out IntPtr commandList)
        {
            commandList = IntPtr.Zero;
            IntPtr address = GetVTableFunctionAddress(device, VTABLE_ID3D12Device_CreateCommandList);
            if (address == IntPtr.Zero)
            {
                return -1;
            }

            var createCommandList = Marshal.GetDelegateForFunctionPointer<CreateCommandListDelegate>(address);
            return createCommandList(device, nodeMask, type, allocator, initialState, ref riid, out commandList);
        }

        private static int CreateFence(IntPtr device, ulong initialValue, int flags, ref Guid riid, out IntPtr fence)
        {
            fence = IntPtr.Zero;
            IntPtr address = GetVTableFunctionAddress(device, VTABLE_ID3D12Device_CreateFence);
            if (address == IntPtr.Zero)
            {
                return -1;
            }

            var createFence = Marshal.GetDelegateForFunctionPointer<CreateFenceDelegate>(address);
            return createFence(device, initialValue, flags, ref riid, out fence);
        }

        private static int CommandAllocatorReset(IntPtr allocator)
        {
            IntPtr address = GetVTableFunctionAddress(allocator, VTABLE_ID3D12CommandAllocator_Reset);
            if (address == IntPtr.Zero)
            {
                return -1;
            }

            var reset = Marshal.GetDelegateForFunctionPointer<CommandAllocatorResetDelegate>(address);
            return reset(allocator);
        }

        private static int GraphicsCommandListClose(IntPtr commandList)
        {
            IntPtr address = GetVTableFunctionAddress(commandList, VTABLE_ID3D12GraphicsCommandList_Close);
            if (address == IntPtr.Zero)
            {
                return -1;
            }

            var close = Marshal.GetDelegateForFunctionPointer<GraphicsCommandListCloseDelegate>(address);
            return close(commandList);
        }

        private static int GraphicsCommandListReset(IntPtr commandList, IntPtr allocator, IntPtr initialState)
        {
            IntPtr address = GetVTableFunctionAddress(commandList, VTABLE_ID3D12GraphicsCommandList_Reset);
            if (address == IntPtr.Zero)
            {
                return -1;
            }

            var reset = Marshal.GetDelegateForFunctionPointer<GraphicsCommandListResetDelegate>(address);
            return reset(commandList, allocator, initialState);
        }

        private static void GraphicsCommandListResourceBarrier(IntPtr commandList, uint numBarriers, D3D12_RESOURCE_BARRIER* barriers)
        {
            IntPtr address = GetVTableFunctionAddress(commandList, VTABLE_ID3D12GraphicsCommandList_ResourceBarrier);
            if (address == IntPtr.Zero)
            {
                return;
            }

            var resourceBarrier = Marshal.GetDelegateForFunctionPointer<GraphicsCommandListResourceBarrierDelegate>(address);
            resourceBarrier(commandList, numBarriers, barriers);
        }

        private static void GraphicsCommandListSetDescriptorHeaps(IntPtr commandList, uint numHeaps, IntPtr* heaps)
        {
            IntPtr address = GetVTableFunctionAddress(commandList, VTABLE_ID3D12GraphicsCommandList_SetDescriptorHeaps);
            if (address == IntPtr.Zero)
            {
                return;
            }

            var setDescriptorHeaps = Marshal.GetDelegateForFunctionPointer<GraphicsCommandListSetDescriptorHeapsDelegate>(address);
            setDescriptorHeaps(commandList, numHeaps, heaps);
        }

        private static void GraphicsCommandListOMSetRenderTargets(IntPtr commandList, uint numRenderTargetDescriptors, nuint* rtvs, int singleHandleRange, nuint* dsv)
        {
            IntPtr address = GetVTableFunctionAddress(commandList, VTABLE_ID3D12GraphicsCommandList_OMSetRenderTargets);
            if (address == IntPtr.Zero)
            {
                return;
            }

            var setRenderTargets = Marshal.GetDelegateForFunctionPointer<GraphicsCommandListOMSetRenderTargetsDelegate>(address);
            setRenderTargets(commandList, numRenderTargetDescriptors, rtvs, singleHandleRange, dsv);
        }

        private static void CommandQueueExecuteCommandLists(IntPtr queue, uint numCommandLists, IntPtr* commandLists)
        {
            IntPtr address = GetVTableFunctionAddress(queue, VTABLE_ID3D12CommandQueue_ExecuteCommandLists);
            if (address == IntPtr.Zero)
            {
                return;
            }

            var executeCommandLists = Marshal.GetDelegateForFunctionPointer<CommandQueueExecuteCommandListsDelegate>(address);
            executeCommandLists(queue, numCommandLists, commandLists);
        }

        private static int CommandQueueSignal(IntPtr queue, IntPtr fence, ulong value)
        {
            IntPtr address = GetVTableFunctionAddress(queue, VTABLE_ID3D12CommandQueue_Signal);
            if (address == IntPtr.Zero)
            {
                return -1;
            }

            var signal = Marshal.GetDelegateForFunctionPointer<CommandQueueSignalDelegate>(address);
            return signal(queue, fence, value);
        }

        private static ulong FenceGetCompletedValue(IntPtr fence)
        {
            IntPtr address = GetVTableFunctionAddress(fence, VTABLE_ID3D12Fence_GetCompletedValue);
            if (address == IntPtr.Zero)
            {
                return 0;
            }

            var getCompletedValue = Marshal.GetDelegateForFunctionPointer<FenceGetCompletedValueDelegate>(address);
            return getCompletedValue(fence);
        }

        private static int FenceSetEventOnCompletion(IntPtr fence, ulong value, IntPtr eventHandle)
        {
            IntPtr address = GetVTableFunctionAddress(fence, VTABLE_ID3D12Fence_SetEventOnCompletion);
            if (address == IntPtr.Zero)
            {
                return -1;
            }

            var setEventOnCompletion = Marshal.GetDelegateForFunctionPointer<FenceSetEventOnCompletionDelegate>(address);
            return setEventOnCompletion(fence, value, eventHandle);
        }

    }
}
