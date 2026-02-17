#if NET5_0_OR_GREATER
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.D3D12;
using Hexa.NET.ImGui.Backends.Win32;
using PhantomRender.Core.Hooks.Graphics;
using PhantomRender.Core.Native;
using PhantomRender.ImGui;

namespace PhantomRender.ImGui.Renderers
{
    public sealed unsafe class DirectX12Renderer : RendererBase
    {
        private const int SRV_HEAP_CAPACITY = 2048;

        // IDXGISwapChain vtable indices.
        private const int VTABLE_IUnknown_QueryInterface = 0;
        private const int VTABLE_IDXGISwapChain_GetBuffer = 9;
        private const int VTABLE_IDXGISwapChain_GetDesc = 12;

        // ID3D12Device vtable indices.
        private const int VTABLE_ID3D12Device_CreateCommandAllocator = 9;
        private const int VTABLE_ID3D12Device_CreateCommandList = 12;
        private const int VTABLE_ID3D12Device_CreateDescriptorHeap = 14;
        private const int VTABLE_ID3D12Device_GetDescriptorHandleIncrementSize = 15;
        private const int VTABLE_ID3D12Device_CreateRenderTargetView = 20;
        private const int VTABLE_ID3D12Device_CreateFence = 36;

        // ID3D12DescriptorHeap vtable indices.
        private const int VTABLE_ID3D12DescriptorHeap_GetDesc = 8;
        private const int VTABLE_ID3D12DescriptorHeap_GetCPUDescriptorHandleForHeapStart = 9;
        private const int VTABLE_ID3D12DescriptorHeap_GetGPUDescriptorHandleForHeapStart = 10;

        // ID3D12CommandAllocator vtable indices.
        private const int VTABLE_ID3D12CommandAllocator_Reset = 8;

        // ID3D12GraphicsCommandList vtable indices.
        private const int VTABLE_ID3D12GraphicsCommandList_Close = 9;
        private const int VTABLE_ID3D12GraphicsCommandList_Reset = 10;
        private const int VTABLE_ID3D12GraphicsCommandList_ResourceBarrier = 26;
        private const int VTABLE_ID3D12GraphicsCommandList_SetDescriptorHeaps = 28;
        private const int VTABLE_ID3D12GraphicsCommandList_OMSetRenderTargets = 46;

        // ID3D12CommandQueue vtable indices.
        private const int VTABLE_ID3D12CommandQueue_ExecuteCommandLists = 10;
        private const int VTABLE_ID3D12CommandQueue_Signal = 14;

        // ID3D12Fence vtable indices.
        private const int VTABLE_ID3D12Fence_GetCompletedValue = 8;
        private const int VTABLE_ID3D12Fence_SetEventOnCompletion = 9;

        // IID_IDXGISwapChain3: used to call GetCurrentBackBufferIndex safely.
        private static readonly Guid IID_IDXGISwapChain3 = new Guid("94d99bdb-f1f8-4ab0-b236-7da0170edab1");

        // D3D12 COM IIDs.
        private static readonly Guid IID_ID3D12Resource = new Guid("696442be-a72e-4059-bc79-5b5c98040fad");
        private static readonly Guid IID_ID3D12DescriptorHeap = new Guid("8efb471d-616c-4f49-90f7-127bb763fa51");
        private static readonly Guid IID_ID3D12Fence = new Guid("0a753dcf-c4d8-4b91-adf6-be5a60d95a76");

        private const int D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV = 0;
        private const int D3D12_DESCRIPTOR_HEAP_TYPE_RTV = 2;

        private const int D3D12_DESCRIPTOR_HEAP_FLAG_NONE = 0;
        private const int D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE = 1;

        private const int D3D12_COMMAND_LIST_TYPE_DIRECT = 0;

        private const int D3D12_RESOURCE_BARRIER_TYPE_TRANSITION = 0;
        private const int D3D12_RESOURCE_BARRIER_FLAG_NONE = 0;
        private const uint D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES = 0xffffffff;

        private const int D3D12_RESOURCE_STATE_COMMON = 0;
        private const int D3D12_RESOURCE_STATE_PRESENT = 0;
        private const int D3D12_RESOURCE_STATE_RENDER_TARGET = 0x4;

        private const uint INFINITE = 0xffffffff;

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
            public IntPtr pResource;
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
            private int _pad; // Enforce 32-byte size on x64
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D12_VIEWPORT
        {
            public float TopLeftX;
            public float TopLeftY;
            public float Width;
            public float Height;
            public float MinDepth;
            public float MaxDepth;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D12_RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryInterfaceDelegate(IntPtr thisPtr, ref Guid riid, out IntPtr ppvObject);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SwapChainGetDescDelegate(IntPtr swapChain, out DXGI.DXGI_SWAP_CHAIN_DESC desc);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SwapChainGetBufferDelegate(IntPtr swapChain, uint index, ref Guid riid, out IntPtr ppSurface);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint SwapChain3GetCurrentBackBufferIndexDelegate(IntPtr swapChain3);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateDescriptorHeapDelegate(IntPtr device, ref D3D12_DESCRIPTOR_HEAP_DESC desc, ref Guid riid, out IntPtr heap);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetDescDelegate(out D3D12_DESCRIPTOR_HEAP_DESC pDesc, IntPtr heap);

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

        // NOTE: COM x64 ABI for D3D12 Handles (8 bytes) - Historical C-ABI requires (this, &result) order.
        // This means RCX = this, RDX = out result.
        // Using return values or (out, this) order causes VTable corruption.
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetCPUDescriptorHandleForHeapStartDelegate(IntPtr heap, out D3D12CpuDescriptorHandle handle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetGPUDescriptorHandleForHeapStartDelegate(IntPtr heap, out D3D12GpuDescriptorHandle handle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void RSSetViewportsDelegate(IntPtr commandList, uint numViewports, D3D12_VIEWPORT* pViewports);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void RSSetScissorRectsDelegate(IntPtr commandList, uint numRects, D3D12_RECT* pRects);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void ClearRenderTargetViewDelegate(IntPtr commandList, nuint rtvHandle, float* colorRGBA, uint numRects, D3D12_RECT* pRects);

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
        private delegate void GraphicsCommandListOMSetRenderTargetsDelegate(IntPtr commandList, uint numRenderTargetDescriptors, nuint* rtvs, int rtsSingleHandleToDescriptorRange, nuint* dsv);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CommandQueueExecuteCommandListsDelegate(IntPtr queue, uint numCommandLists, IntPtr* ppCommandLists);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CommandQueueSignalDelegate(IntPtr queue, IntPtr fence, ulong value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate ulong FenceGetCompletedValueDelegate(IntPtr fence);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int FenceSetEventOnCompletionDelegate(IntPtr fence, ulong value, IntPtr hEvent);

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

        // Simple SRV descriptor allocator used by the DX12 backend (when it requires callbacks).
        // We also keep legacy handles for older backends; both can coexist safely.
        private static int _srvAllocatorReady;
        private static nuint _srvAllocCpuStart;
        private static ulong _srvAllocGpuStart;
        private static uint _srvAllocDescriptorSize;
        private static int _srvAllocNextIndex;
        private static int _srvAllocLoggedOutOfSpace;

        private static void ConfigureSrvAllocator(D3D12CpuDescriptorHandle cpuStart, D3D12GpuDescriptorHandle gpuStart, uint descriptorSize, uint startIndex)
        {
            _srvAllocCpuStart = cpuStart.Ptr;
            _srvAllocGpuStart = gpuStart.Ptr;
            _srvAllocDescriptorSize = descriptorSize;
            _srvAllocNextIndex = unchecked((int)startIndex);
            Volatile.Write(ref _srvAllocatorReady, 1);
            Volatile.Write(ref _srvAllocLoggedOutOfSpace, 0);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void SrvDescriptorAlloc(ImGui_ImplDX12_InitInfo_Fixed* info, D3D12CpuDescriptorHandle* outCpuDesc, D3D12GpuDescriptorHandle* outGpuDesc)
        {
            if (outCpuDesc == null || outGpuDesc == null)
                return;

            if (Volatile.Read(ref _srvAllocatorReady) == 0 || _srvAllocDescriptorSize == 0)
            {
                outCpuDesc->Ptr = 0;
                outGpuDesc->Ptr = 0;
                return;
            }

            int idx = Interlocked.Increment(ref _srvAllocNextIndex) - 1;
            if ((uint)idx >= SRV_HEAP_CAPACITY)
            {
                // Avoid returning garbage handles. Reuse descriptor 0 as a last resort.
                if (Interlocked.CompareExchange(ref _srvAllocLoggedOutOfSpace, 1, 0) == 0)
                {
                    Console.WriteLine("[PhantomRender] DX12: SRV descriptor heap is exhausted; reusing descriptor 0.");
                    Console.Out.Flush();
                }

                idx = 0;
            }

            ulong offset = (ulong)idx * _srvAllocDescriptorSize;
            outCpuDesc->Ptr = _srvAllocCpuStart + (nuint)offset;
            outGpuDesc->Ptr = _srvAllocGpuStart + offset;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void SrvDescriptorFree(ImGui_ImplDX12_InitInfo_Fixed* info, D3D12CpuDescriptorHandle cpuDesc, D3D12GpuDescriptorHandle gpuDesc)
        {
            // No-op (linear allocator). Heap is released on shutdown.
        }

        [DllImport("ImGuiImpl.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "CImGui_ImplDX12_Init")]
        private static extern bool CImGui_ImplDX12_Init_Manual(ImGui_ImplDX12_InitInfo_Fixed* info);

        [DllImport("ImGuiImpl.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "CImGui_ImplDX12_RenderDrawData")]
        private static extern void CImGui_ImplDX12_RenderDrawData_Manual(ImDrawData* drawData, IntPtr graphicsCommandList);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateEventW(IntPtr lpEventAttributes, int bManualReset, int bInitialState, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private readonly object _lock = new object();

        private IntPtr _device;
        private IntPtr _commandQueue;

        private IntPtr _swapChainForResources;

        // Swapchain-derived resources.
        private IntPtr _swapChain3;
        private uint _bufferCount;
        private int _rtvFormat;
        private int _width;
        private int _height;

        private IntPtr _rtvHeap;
        private IntPtr _srvHeap;
        private D3D12CpuDescriptorHandle _rtvCpuStart;
        private D3D12CpuDescriptorHandle _srvCpuStart;
        private D3D12GpuDescriptorHandle _srvGpuStart;
        private D3D12CpuDescriptorHandle _imguiSrvCpu;
        private D3D12GpuDescriptorHandle _imguiSrvGpu;
        private uint _srvDescriptorSize;
        private uint _rtvDescriptorSize;

        private FrameContext[] _frames;

        private IntPtr _commandList;
        private IntPtr _fence;
        private IntPtr _fenceEvent;
        private ulong _fenceValue;

        private IntPtr _lastSwapChain;

        private bool _imguiDx12Initialized;
        private bool _frameStarted;

        private bool _loggedWaitingQueue;
        private bool _loggedSwapchainDesc;

        public DirectX12Renderer(OverlayMenu overlayMenu)
            : base(overlayMenu, GraphicsApi.DirectX12)
        {
        }

        private struct FrameContext
        {
            public IntPtr CommandAllocator;
            public IntPtr RenderTarget;
            public D3D12CpuDescriptorHandle Rtv;
            public ulong FenceValue;
        }

        public override bool Initialize(IntPtr device, IntPtr windowHandle)
        {
            if (IsInitialized) return true;
            if (device == IntPtr.Zero || windowHandle == IntPtr.Zero) return false;

            try
            {
                Console.WriteLine($"[PhantomRender] DirectX12Renderer: Entering Initialize. Device: {device}, Window: {windowHandle}");
                Console.Out.Flush();

                RaiseRendererInitializing(device, windowHandle);
                _device = device;
                Marshal.AddRef(_device);

                InitializeImGui(windowHandle);

                // DX12 backend init is deferred until we have a command queue and swapchain resources.
                IsInitialized = true;
                Console.WriteLine("[PhantomRender] DirectX12Renderer: Initialized (waiting for swapchain/queue)...");
                Console.Out.Flush();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DirectX12Renderer: Init Error: {ex}");
                Console.Out.Flush();
                CleanupAll();
                return false;
            }
        }

        public override void NewFrame()
        {
            if (!IsInitialized) return;

            Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
            ImGuiImplWin32.SetCurrentContext(Context);
            ImGuiImplD3D12.SetCurrentContext(Context);

            if (!_imguiDx12Initialized)
            {
                _frameStarted = false;
                return;
            }

            ImGuiImplD3D12.NewFrame();
            ImGuiImplWin32.NewFrame();
            RaiseNewFrame();
            Hexa.NET.ImGui.ImGui.NewFrame();
            _frameStarted = true;
        }

        public override void Render()
        {
            Render(_lastSwapChain);
        }

        public void Render(IntPtr swapChain)
        {
            if (!IsInitialized || swapChain == IntPtr.Zero) return;

            lock (_lock)
            {
                _lastSwapChain = swapChain;

                if (!EnsureDx12Ready(swapChain))
                    return;

                if (!_frameStarted)
                {
                    NewFrame();
                    if (!_frameStarted)
                        return;
                }

                try
                {
                    Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
                    ImGuiImplWin32.SetCurrentContext(Context);
                    ImGuiImplD3D12.SetCurrentContext(Context);

                    RenderMenuFrame();

                    RaiseOverlayRender();
                    Hexa.NET.ImGui.ImGui.Render();

                    var drawData = Hexa.NET.ImGui.ImGui.GetDrawData();

                    if (drawData.CmdListsCount > 0)
                    {
                        RenderDrawData(drawData);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PhantomRender] DirectX12Renderer: Render error: {ex}");
                    Console.Out.Flush();
                }
                finally
                {
                    _frameStarted = false;
                }
            }
        }

        public void OnBeforeResizeBuffers(IntPtr swapChain)
        {
            if (!IsInitialized) return;

            lock (_lock)
            {
                ShutdownImGuiDx12();
                ReleaseSwapchainResources();
            }
        }

        public void OnAfterResizeBuffers(IntPtr swapChain)
        {
            // Resources will be lazily recreated on next Present.
        }

        public override void OnLostDevice()
        {
            OnBeforeResizeBuffers(IntPtr.Zero);
        }

        public override void OnResetDevice()
        {
            // No-op. We recreate lazily.
        }

        public override void Dispose()
        {
            lock (_lock)
            {
                CleanupAll();
            }
        }

        private void CleanupAll()
        {
            try { ShutdownImGuiDx12(); } catch { }
            try { ReleaseSwapchainResources(); } catch { }

            if (_commandQueue != IntPtr.Zero)
            {
                Marshal.Release(_commandQueue);
                _commandQueue = IntPtr.Zero;
            }

            if (_device != IntPtr.Zero)
            {
                Marshal.Release(_device);
                _device = IntPtr.Zero;
            }

            if (IsInitialized)
            {
                try { ShutdownImGui(); } catch { }
                IsInitialized = false;
            }
        }

        private void ShutdownImGuiDx12()
        {
            if (_imguiDx12Initialized)
            {
                try { ImGuiImplD3D12.Shutdown(); } catch { }
                _imguiDx12Initialized = false;
            }
        }

        private void WaitForGpuIdle()
        {
            if (_commandQueue == IntPtr.Zero || _fence == IntPtr.Zero || _fenceEvent == IntPtr.Zero)
                return;

            ulong value = ++_fenceValue;
            CommandQueueSignal(_commandQueue, _fence, value);
            FenceSetEventOnCompletion(_fence, value, _fenceEvent);
            WaitForSingleObject(_fenceEvent, INFINITE);
        }

        private void ReleaseSwapchainResources()
        {
            Console.WriteLine("[PhantomRender] DirectX12Renderer: ReleaseSwapchainResources called.");
            Console.Out.Flush();

            try { WaitForGpuIdle(); } catch { }

            _swapChainForResources = IntPtr.Zero;

            if (_swapChain3 != IntPtr.Zero)
            {
                Marshal.Release(_swapChain3);
                _swapChain3 = IntPtr.Zero;
            }

            if (_frames != null)
            {
                for (int i = 0; i < _frames.Length; i++)
                {
                    if (i < _frames.Length && _frames[i].RenderTarget != IntPtr.Zero)
                    {
                        Marshal.Release(_frames[i].RenderTarget);
                        _frames[i].RenderTarget = IntPtr.Zero;
                    }

                    if (i < _frames.Length && _frames[i].CommandAllocator != IntPtr.Zero)
                    {
                        Marshal.Release(_frames[i].CommandAllocator);
                        _frames[i].CommandAllocator = IntPtr.Zero;
                    }

                    _frames[i].FenceValue = 0;
                }
            }

            _frames = null;
            _bufferCount = 0;
            _width = 0;
            _height = 0;
            _rtvFormat = 0;
            _rtvDescriptorSize = 0;
            _rtvCpuStart = default;
            _srvCpuStart = default;
            _srvGpuStart = default;
            _loggedSwapchainDesc = false;

            if (_rtvHeap != IntPtr.Zero)
            {
                Marshal.Release(_rtvHeap);
                _rtvHeap = IntPtr.Zero;
            }

            if (_srvHeap != IntPtr.Zero)
            {
                Marshal.Release(_srvHeap);
                _srvHeap = IntPtr.Zero;
            }

            if (_commandList != IntPtr.Zero)
            {
                Marshal.Release(_commandList);
                _commandList = IntPtr.Zero;
            }

            if (_fence != IntPtr.Zero)
            {
                Marshal.Release(_fence);
                _fence = IntPtr.Zero;
            }

            if (_fenceEvent != IntPtr.Zero)
            {
                CloseHandle(_fenceEvent);
                _fenceEvent = IntPtr.Zero;
            }

            _fenceValue = 0;
            _frameStarted = false;

            // Ensure allocator callbacks don't hand out stale handles after heap release.
            Volatile.Write(ref _srvAllocatorReady, 0);
            _srvAllocCpuStart = 0;
            _srvAllocGpuStart = 0;
            _srvAllocDescriptorSize = 0;
            _srvAllocNextIndex = 0;
        }

        private bool EnsureDx12Ready(IntPtr swapChain)
        {
            if (_device == IntPtr.Zero) return false;

            if (_commandQueue != IntPtr.Zero)
            {
                if (!DirectX12CommandQueueResolver.TryGetCommandQueueFromSwapChain(swapChain, out var newQueue) || newQueue == IntPtr.Zero)
                {
                    // Keep existing queue if possible, but log if we detect a mismatch
                    return true;
                }

                if (newQueue != _commandQueue)
                {
                    Console.WriteLine($"[PhantomRender] DX12: Command queue CHANGED! Old: {_commandQueue.ToString("X")}, New: {newQueue.ToString("X")}. Re-initializing ImGui...");
                    Console.Out.Flush();

                    ShutdownImGuiDx12();
                    Marshal.Release(_commandQueue);
                    _commandQueue = newQueue;
                    _imguiDx12Initialized = false;
                }
                else
                {
                    // Resolver returns an owned reference; release it when the queue hasn't changed.
                    Marshal.Release(newQueue);
                }
            }
            else
            {
                if (!DirectX12CommandQueueResolver.TryGetCommandQueueFromSwapChain(swapChain, out var queue) || queue == IntPtr.Zero)
                {
                    if (!_loggedWaitingQueue)
                    {
                        Console.WriteLine("[PhantomRender] DX12 detected, waiting for command queue...");
                        Console.Out.Flush();
                        _loggedWaitingQueue = true;
                    }

                    return false;
                }

                _commandQueue = queue;

                Console.WriteLine($"[PhantomRender] DX12: Command queue resolved: {_commandQueue.ToString("X")}");
                Console.Out.Flush();
            }

            if (!EnsureSwapchainResources(swapChain))
                return false;

            if (!_imguiDx12Initialized)
            {
                if (!InitializeImGuiDx12Backend())
                    return false;
            }

            return true;
        }

        private bool EnsureSwapchainResources(IntPtr swapChain)
        {
            if (_swapChainForResources != IntPtr.Zero && _swapChainForResources != swapChain)
            {
                ShutdownImGuiDx12();
                ReleaseSwapchainResources();
            }

            if (_swapChainForResources == swapChain &&
                _frames != null &&
                _frames.Length > 0 &&
                _swapChain3 != IntPtr.Zero &&
                _rtvHeap != IntPtr.Zero &&
                _srvHeap != IntPtr.Zero &&
                _commandList != IntPtr.Zero &&
                _fence != IntPtr.Zero &&
                _fenceEvent != IntPtr.Zero)
            {
                return true;
            }

            ShutdownImGuiDx12();
            ReleaseSwapchainResources();

            if (!TryQuerySwapChain3(swapChain, out _swapChain3) || _swapChain3 == IntPtr.Zero)
            {
                Console.WriteLine("[PhantomRender] DirectX12Renderer: IDXGISwapChain3 QI failed.");
                Console.Out.Flush();
                ReleaseSwapchainResources();
                return false;
            }

            if (!TryGetSwapChainDesc(swapChain, out var desc))
            {
                Console.WriteLine("[PhantomRender] DirectX12Renderer: GetDesc failed.");
                Console.Out.Flush();
                ReleaseSwapchainResources();
                return false;
            }

            _bufferCount = desc.BufferCount;
            _rtvFormat = desc.BufferDesc.Format;
            _width = (int)desc.BufferDesc.Width;
            _height = (int)desc.BufferDesc.Height;

            if (_bufferCount == 0)
            {
                Console.WriteLine("[PhantomRender] DirectX12Renderer: Swapchain buffer count is 0.");
                Console.Out.Flush();
                ReleaseSwapchainResources();
                return false;
            }

            if (!TryCreateHeapsAndViews(swapChain))
            {
                ReleaseSwapchainResources();
                return false;
            }

            if (!TryCreateCommandObjects())
            {
                ReleaseSwapchainResources();
                return false;
            }

            _swapChainForResources = swapChain;

            if (!_loggedSwapchainDesc)
            {
                Console.WriteLine($"[PhantomRender] DX12 Init: SwapChainDesc Buffers={_bufferCount}, Format={_rtvFormat}, Windowed={desc.Windowed}");
                Console.Out.Flush();
                _loggedSwapchainDesc = true;
            }

            return true;
        }

        private bool TryCreateHeapsAndViews(IntPtr swapChain)
        {
            // RTV heap
            var rtvHeapDesc = new D3D12_DESCRIPTOR_HEAP_DESC
            {
                Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV,
                NumDescriptors = (int)_bufferCount,
                Flags = D3D12_DESCRIPTOR_HEAP_FLAG_NONE,
                NodeMask = 0
            };

            Guid iidHeap = IID_ID3D12DescriptorHeap;
            if (CreateDescriptorHeap(_device, ref rtvHeapDesc, ref iidHeap, out _rtvHeap) < 0 || _rtvHeap == IntPtr.Zero)
            {
                Console.WriteLine("[PhantomRender] DirectX12Renderer: Failed to create RTV heap.");
                Console.Out.Flush();
                return false;
            }

            _rtvDescriptorSize = GetDescriptorHandleIncrementSize(_device, D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
            if (_rtvDescriptorSize == 0)
            {
                Console.WriteLine("[PhantomRender] DirectX12Renderer: RTV descriptor increment size is 0.");
                Console.Out.Flush();
                return false;
            }

            _rtvCpuStart = GetCPUDescriptorHandleForHeapStart(_rtvHeap);
            if (_rtvCpuStart.Ptr == 0)
            {
                Console.WriteLine("[PhantomRender] DirectX12Renderer: RTV heap CPU start is 0.");
                Console.Out.Flush();
                return false;
            }

            // SRV heap (shader visible)
            var srvHeapDesc = new D3D12_DESCRIPTOR_HEAP_DESC
            {
                Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,
                NumDescriptors = 2048,
                Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE,
                NodeMask = 0
            };

            int hr = CreateDescriptorHeap(_device, ref srvHeapDesc, ref iidHeap, out _srvHeap);
            Console.WriteLine($"[PhantomRender] DX12 Init: CreateDescriptorHeap (SRV) hr=0x{hr:X8}, pointer=0x{_srvHeap:X}");
            Console.Out.Flush();

            if (hr < 0 || _srvHeap == IntPtr.Zero)
            {
                Console.WriteLine("[PhantomRender] DirectX12Renderer: Failed to create SRV heap.");
                return false;
            }

            // We skip GetDesc for now as it risks ABI-related corruption.
            // But we can check the vtable pointer to see if the object is likely valid.
            IntPtr srvVtbl = Marshal.ReadIntPtr(_srvHeap);
            Console.WriteLine($"[PhantomRender] DX12 Init: SRV Heap created. Pointer=0x{_srvHeap:X}, VTable=0x{srvVtbl:X}");
            Console.Out.Flush();

            _srvCpuStart = GetCPUDescriptorHandleForHeapStart(_srvHeap);
            _srvGpuStart = GetGPUDescriptorHandleForHeapStart(_srvHeap);

            _srvDescriptorSize = GetDescriptorHandleIncrementSize(_device, D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
            if (_srvDescriptorSize == 0)
            {
                Console.WriteLine("[PhantomRender] DirectX12Renderer: SRV descriptor increment size is 0.");
                Console.Out.Flush();
                return false;
            }

            if (_srvCpuStart.Ptr == 0)
            {
                Console.WriteLine("[PhantomRender] DirectX12Renderer: SRV heap CPU start is 0.");
                Console.Out.Flush();
                return false;
            }

            if (_srvGpuStart.Ptr == 0)
            {
                Console.WriteLine("[PhantomRender] DirectX12Renderer: SRV heap GPU start is 0. Running VTable Diagnostic...");

                // Diagnostic: Dump VTable of Heap (indices 0-15)
                IntPtr vtbl = Marshal.ReadIntPtr(_srvHeap);
                Console.WriteLine($"[PhantomRender] DX12 VTable Dump (_srvHeap=0x{_srvHeap:X}): vtbl=0x{vtbl:X}");
                for (int i = 0; i < 16; i++)
                {
                    IntPtr fnPtr = Marshal.ReadIntPtr(vtbl + (i * IntPtr.Size));
                    Console.WriteLine($"  [{i}] = 0x{fnPtr:X}");
                }
                Console.Out.Flush();

                // Add heap verification here if possible (e.g. GetDesc)
                // For now, we proceed, but it is suspicious.
            }

            // Prefer descriptor 0 for the legacy font SRV. If the GPU start handle is 0 (suspicious),
            // use descriptor 1 so the resulting GPU handle isn't 0.
            uint legacySrvIndex = _srvGpuStart.Ptr == 0 ? 1u : 0u;
            _imguiSrvCpu = new D3D12CpuDescriptorHandle(_srvCpuStart.Ptr + (nuint)(legacySrvIndex * _srvDescriptorSize));
            _imguiSrvGpu = new D3D12GpuDescriptorHandle(_srvGpuStart.Ptr + (ulong)(legacySrvIndex * _srvDescriptorSize));

            // Configure allocator callbacks used by newer ImGui DX12 backends.
            ConfigureSrvAllocator(_srvCpuStart, _srvGpuStart, _srvDescriptorSize, legacySrvIndex);

            Console.WriteLine($"[PhantomRender] DX12 Init: SRV heap CPU start=0x{_srvCpuStart.Ptr:X}, GPU start=0x{_srvGpuStart.Ptr:X}, Inc={_srvDescriptorSize}");
            Console.WriteLine($"[PhantomRender] DX12 Init: ImGui legacy SRV CPU=0x{_imguiSrvCpu.Ptr:X}, GPU=0x{_imguiSrvGpu.Ptr:X}");
            Console.Out.Flush();

            Console.WriteLine($"[PhantomRender] DX12 Init: Creating backbuffer RTVs... Buffers={_bufferCount}");
            Console.Out.Flush();

            // Create RTVs + store backbuffers
            _frames = new FrameContext[_bufferCount];

            uint inc = _rtvDescriptorSize;
            for (uint i = 0; i < _bufferCount; i++)
            {
                Console.WriteLine($"[PhantomRender] DX12 Init: GetBuffer({i})...");
                Console.Out.Flush();

                Guid iidResource = IID_ID3D12Resource;
                int bufferHr = GetBuffer(swapChain, i, ref iidResource, out var resource);
                Console.WriteLine($"[PhantomRender] DX12 Init: GetBuffer({i}) hr=0x{bufferHr:X8}, resource=0x{resource:X}");
                Console.Out.Flush();

                if (bufferHr < 0 || resource == IntPtr.Zero)
                {
                    Console.WriteLine($"[PhantomRender] DirectX12Renderer: GetBuffer({i}) failed (0x{bufferHr:X8}).");
                    return false;
                }

                var rtvHandle = new D3D12CpuDescriptorHandle(_rtvCpuStart.Ptr + (nuint)(i * inc));
                CreateRenderTargetView(_device, resource, IntPtr.Zero, rtvHandle.Ptr);

                Console.WriteLine($"[PhantomRender] DX12 Init: RTV {i} created at 0x{rtvHandle.Ptr:X}");
                Console.Out.Flush();

                _frames[i] = new FrameContext
                {
                    RenderTarget = resource,
                    Rtv = rtvHandle,
                    CommandAllocator = IntPtr.Zero,
                    FenceValue = 0
                };
            }

            return true;
        }

        private bool TryCreateCommandObjects()
        {
            // Command allocators per frame
            for (int i = 0; i < _frames.Length; i++)
            {
                Guid iidAllocator = Direct3D12.IID_ID3D12CommandAllocator;
                if (CreateCommandAllocator(_device, D3D12_COMMAND_LIST_TYPE_DIRECT, ref iidAllocator, out var allocator) < 0 || allocator == IntPtr.Zero)
                {
                    Console.WriteLine("[PhantomRender] DirectX12Renderer: CreateCommandAllocator failed.");
                    Console.Out.Flush();
                    return false;
                }

                _frames[i].CommandAllocator = allocator;
            }

            // Command list
            Guid iidCmdList = Direct3D12.IID_ID3D12GraphicsCommandList;
            if (CreateCommandList(_device, 0, D3D12_COMMAND_LIST_TYPE_DIRECT, _frames[0].CommandAllocator, IntPtr.Zero, ref iidCmdList, out _commandList) < 0 || _commandList == IntPtr.Zero)
            {
                Console.WriteLine("[PhantomRender] DirectX12Renderer: CreateCommandList failed.");
                Console.Out.Flush();
                return false;
            }

            if (GraphicsCommandListClose(_commandList) < 0)
            {
                Console.WriteLine("[PhantomRender] DirectX12Renderer: Close() on new command list failed.");
                Console.Out.Flush();
                return false;
            }

            // Fence + event
            Guid iidFence = IID_ID3D12Fence;
            if (CreateFence(_device, 0, 0, ref iidFence, out _fence) < 0 || _fence == IntPtr.Zero)
            {
                Console.WriteLine("[PhantomRender] DirectX12Renderer: CreateFence failed.");
                Console.Out.Flush();
                return false;
            }

            _fenceEvent = CreateEventW(IntPtr.Zero, 0, 0, null);
            if (_fenceEvent == IntPtr.Zero)
            {
                Console.WriteLine("[PhantomRender] DirectX12Renderer: CreateEvent failed.");
                Console.Out.Flush();
                return false;
            }

            _fenceValue = 0;
            return true;
        }

        private bool InitializeImGuiDx12Backend()
        {
            try
            {
                Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
                ImGuiImplWin32.SetCurrentContext(Context);
                ImGuiImplD3D12.SetCurrentContext(Context);

                ImGui_ImplDX12_InitInfo_Fixed info = default;
                info.Device = _device;
                info.CommandQueue = _commandQueue;
                info.NumFramesInFlight = (int)_bufferCount;
                info.RTVFormat = _rtvFormat;
                info.DSVFormat = 0;
                info.UserData = IntPtr.Zero;
                info.SrvDescriptorHeap = _srvHeap;

                info.SrvDescriptorAllocFn = (IntPtr)(delegate* unmanaged[Cdecl]<ImGui_ImplDX12_InitInfo_Fixed*, D3D12CpuDescriptorHandle*, D3D12GpuDescriptorHandle*, void>)&SrvDescriptorAlloc;
                info.SrvDescriptorFreeFn = (IntPtr)(delegate* unmanaged[Cdecl]<ImGui_ImplDX12_InitInfo_Fixed*, D3D12CpuDescriptorHandle, D3D12GpuDescriptorHandle, void>)&SrvDescriptorFree;

                info.LegacySingleSrvCpuDescriptor = _imguiSrvCpu.Ptr;
                info.LegacySingleSrvGpuDescriptor = _imguiSrvGpu.Ptr;

                Console.WriteLine($"[PhantomRender] DX12: Calling Manual CImGui Init. Heap=0x{info.SrvDescriptorHeap:X}, Queue=0x{info.CommandQueue:X}");
                Console.Out.Flush();

                if (!CImGui_ImplDX12_Init_Manual(&info))
                {
                    Console.WriteLine("[PhantomRender] DirectX12Renderer: CImGui_ImplDX12_Init_Manual returned FALSE!");
                    Console.Out.Flush();
                    return false;
                }

                // Force creation of device objects (font texture/pipeline state) early.
                // If this fails, later RenderDrawData may crash in native code.
                bool created = false;
                try { created = ImGuiImplD3D12.CreateDeviceObjects(); } catch { created = false; }
                Console.WriteLine($"[PhantomRender] DX12: ImGuiImplD3D12.CreateDeviceObjects()={(created ? "OK" : "FAILED")}");
                Console.Out.Flush();

                if (!created)
                {
                    try { ImGuiImplD3D12.Shutdown(); } catch { }
                    _imguiDx12Initialized = false;
                    return false;
                }

                _imguiDx12Initialized = true;
                Console.WriteLine("[PhantomRender] DirectX12Renderer: DX12 backend initialized.");
                Console.Out.Flush();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DirectX12Renderer: ImGui DX12 init error: {ex}");
                Console.Out.Flush();
                return false;
            }
        }

        private void RenderDrawData(ImDrawDataPtr drawData)
        {
            if (_commandQueue == IntPtr.Zero || _commandList == IntPtr.Zero || _frames == null)
            {
                return;
            }

            // Ensure the DX12 backend sees the correct ImGui context before touching draw lists.
            Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
            ImGuiImplWin32.SetCurrentContext(Context);
            ImGuiImplD3D12.SetCurrentContext(Context);

            uint frameIndex = GetCurrentBackBufferIndex(_swapChain3);
            if (frameIndex >= _frames.Length)
            {
                return;
            }

            ref FrameContext frame = ref _frames[frameIndex];

            WaitForFrame(ref frame);

            int hr = CommandAllocatorReset(frame.CommandAllocator);
            if (hr < 0)
            {
                return;
            }

            hr = GraphicsCommandListReset(_commandList, frame.CommandAllocator, IntPtr.Zero);
            if (hr < 0)
            {
                return;
            }

            // Transition backbuffer to RT
            var barrier = new D3D12_RESOURCE_BARRIER
            {
                Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION,
                Flags = D3D12_RESOURCE_BARRIER_FLAG_NONE,
                Union = new D3D12_RESOURCE_BARRIER_UNION
                {
                    Transition = new D3D12_RESOURCE_TRANSITION_BARRIER
                    {
                        pResource = frame.RenderTarget,
                        Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES,
                        StateBefore = D3D12_RESOURCE_STATE_COMMON, // More robust transition for OnPresent
                        StateAfter = D3D12_RESOURCE_STATE_RENDER_TARGET
                    }
                }
            };

            GraphicsCommandListResourceBarrier(_commandList, 1, &barrier);

            // Bind RT
            nuint rtvPtr = frame.Rtv.Ptr;
            GraphicsCommandListOMSetRenderTargets(_commandList, 1, &rtvPtr, 0, null);

            // Descriptor heaps
            IntPtr srvHeap = _srvHeap;
            GraphicsCommandListSetDescriptorHeaps(_commandList, 1, &srvHeap);

            // Viewport & Scissor
            // Viewport is critical after Reset()
            var viewport = new D3D12_VIEWPORT
            {
                TopLeftX = 0,
                TopLeftY = 0,
                Width = (float)_width,
                Height = (float)_height,
                MinDepth = 0.0f,
                MaxDepth = 1.0f
            };

            var rect = new D3D12_RECT
            {
                left = 0,
                top = 0,
                right = _width,
                bottom = _height
            };

            GraphicsCommandListRSSetViewports(_commandList, 1, &viewport);
            GraphicsCommandListRSSetScissorRects(_commandList, 1, &rect);

            try
            {
                // Safety check on DrawData handle
                if (drawData.Handle == null)
                {
                    Console.WriteLine("[PhantomRender] DX12 RenderDrawData: DrawData Handle is NULL!");
                    Console.Out.Flush();
                    return;
                }

                // Using manual P/Invoke to ensure correct entry point and ABI.
                CImGui_ImplDX12_RenderDrawData_Manual(drawData, _commandList);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DX12: RenderDrawData EXCEPTION: {ex}");
                Console.Out.Flush();
            }

            // Transition back to COMMON (equivalent to PRESENT for swapchain buffers)
            barrier.Union.Transition.StateBefore = D3D12_RESOURCE_STATE_RENDER_TARGET;
            barrier.Union.Transition.StateAfter = D3D12_RESOURCE_STATE_COMMON;
            GraphicsCommandListResourceBarrier(_commandList, 1, &barrier);

            hr = GraphicsCommandListClose(_commandList);
            if (hr < 0)
            {
                return;
            }

            IntPtr cmdList = _commandList;
            CommandQueueExecuteCommandLists(_commandQueue, 1, &cmdList);

            ulong nextFence = ++_fenceValue;
            CommandQueueSignal(_commandQueue, _fence, nextFence);
            frame.FenceValue = nextFence;
        }

        private void WaitForFrame(ref FrameContext frame)
        {
            if (frame.FenceValue == 0 || _fence == IntPtr.Zero)
                return;

            ulong completed = FenceGetCompletedValue(_fence);
            if (completed >= frame.FenceValue)
                return;

            FenceSetEventOnCompletion(_fence, frame.FenceValue, _fenceEvent);
            WaitForSingleObject(_fenceEvent, INFINITE);
        }

        private static IntPtr GetVTableFunctionAddress(IntPtr instance, int index)
        {
            if (instance == IntPtr.Zero) return IntPtr.Zero;
            IntPtr vTable = Marshal.ReadIntPtr(instance);
            if (vTable == IntPtr.Zero) return IntPtr.Zero;
            return Marshal.ReadIntPtr(vTable + index * IntPtr.Size);
        }

        private static bool TryGetSwapChainDesc(IntPtr swapChain, out DXGI.DXGI_SWAP_CHAIN_DESC desc)
        {
            desc = default;
            IntPtr addr = GetVTableFunctionAddress(swapChain, VTABLE_IDXGISwapChain_GetDesc);
            if (addr == IntPtr.Zero) return false;

            var fn = Marshal.GetDelegateForFunctionPointer<SwapChainGetDescDelegate>(addr);
            return fn(swapChain, out desc) >= 0 && desc.BufferCount != 0;
        }

        private static bool TryQuerySwapChain3(IntPtr swapChain, out IntPtr swapChain3)
        {
            swapChain3 = IntPtr.Zero;

            IntPtr addr = GetVTableFunctionAddress(swapChain, VTABLE_IUnknown_QueryInterface);
            if (addr == IntPtr.Zero) return false;

            var queryInterface = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(addr);
            Guid iid = IID_IDXGISwapChain3;
            int hr = queryInterface(swapChain, ref iid, out swapChain3);
            return hr >= 0 && swapChain3 != IntPtr.Zero;
        }

        private static uint GetCurrentBackBufferIndex(IntPtr swapChain3)
        {
            // IDXGISwapChain3::GetCurrentBackBufferIndex is at vtable index 36.
            // Vtable layout: IUnknown(3) + IDXGIObject(4) + IDXGIDeviceSubObject(1)
            //   + IDXGISwapChain(10) + IDXGISwapChain1(11) + IDXGISwapChain2(7)
            //   + IDXGISwapChain3: GetCurrentBackBufferIndex=36, CheckColorSpaceSupport=37, SetColorSpace1=38
            IntPtr addr = GetVTableFunctionAddress(swapChain3, 36);
            if (addr == IntPtr.Zero) return 0;

            var fn = Marshal.GetDelegateForFunctionPointer<SwapChain3GetCurrentBackBufferIndexDelegate>(addr);
            return fn(swapChain3);
        }

        private static int GetBuffer(IntPtr swapChain, uint bufferIndex, ref Guid riid, out IntPtr resource)
        {
            resource = IntPtr.Zero;

            IntPtr addr = GetVTableFunctionAddress(swapChain, VTABLE_IDXGISwapChain_GetBuffer);
            if (addr == IntPtr.Zero) return -1;

            var fn = Marshal.GetDelegateForFunctionPointer<SwapChainGetBufferDelegate>(addr);
            return fn(swapChain, bufferIndex, ref riid, out resource);
        }

        private static int CreateDescriptorHeap(IntPtr device, ref D3D12_DESCRIPTOR_HEAP_DESC desc, ref Guid riid, out IntPtr heap)
        {
            heap = IntPtr.Zero;

            IntPtr addr = GetVTableFunctionAddress(device, VTABLE_ID3D12Device_CreateDescriptorHeap);
            if (addr == IntPtr.Zero) return -1;

            var fn = Marshal.GetDelegateForFunctionPointer<CreateDescriptorHeapDelegate>(addr);
            return fn(device, ref desc, ref riid, out heap);
        }

        private static uint GetDescriptorHandleIncrementSize(IntPtr device, int type)
        {
            IntPtr addr = GetVTableFunctionAddress(device, VTABLE_ID3D12Device_GetDescriptorHandleIncrementSize);
            if (addr == IntPtr.Zero) return 0;

            var fn = Marshal.GetDelegateForFunctionPointer<GetDescriptorHandleIncrementSizeDelegate>(addr);
            return fn(device, type);
        }

        private D3D12CpuDescriptorHandle GetCPUDescriptorHandleForHeapStart(IntPtr heap)
        {
            IntPtr addr = GetVTableFunctionAddress(heap, VTABLE_ID3D12DescriptorHeap_GetCPUDescriptorHandleForHeapStart);
            if (addr == IntPtr.Zero) return default;

            var del = Marshal.GetDelegateForFunctionPointer<GetCPUDescriptorHandleForHeapStartDelegate>(addr);
            del(heap, out var handle);
            return handle;
        }

        private D3D12GpuDescriptorHandle GetGPUDescriptorHandleForHeapStart(IntPtr heap)
        {
            IntPtr addr = GetVTableFunctionAddress(heap, VTABLE_ID3D12DescriptorHeap_GetGPUDescriptorHandleForHeapStart);
            if (addr == IntPtr.Zero) return default;

            var del = Marshal.GetDelegateForFunctionPointer<GetGPUDescriptorHandleForHeapStartDelegate>(addr);
            del(heap, out var handle);
            return handle;
        }

        private static D3D12_DESCRIPTOR_HEAP_DESC GetDescriptorHeapDesc(IntPtr heap)
        {
            // GetDesc is at vtable index 8.
            IntPtr addr = GetVTableFunctionAddress(heap, VTABLE_ID3D12DescriptorHeap_GetDesc);
            if (addr == IntPtr.Zero) return default;

            var fn = Marshal.GetDelegateForFunctionPointer<GetDescDelegate>(addr);
            fn(out var desc, heap); // Hidden pointer ABI: hiddenPtr first.
            return desc;
        }

        private static void CreateRenderTargetView(IntPtr device, IntPtr resource, IntPtr desc, nuint destDescriptor)
        {
            IntPtr addr = GetVTableFunctionAddress(device, VTABLE_ID3D12Device_CreateRenderTargetView);
            if (addr == IntPtr.Zero) return;

            var fn = Marshal.GetDelegateForFunctionPointer<CreateRenderTargetViewDelegate>(addr);
            fn(device, resource, desc, destDescriptor);
        }

        private static int CreateCommandAllocator(IntPtr device, int type, ref Guid riid, out IntPtr allocator)
        {
            allocator = IntPtr.Zero;

            IntPtr addr = GetVTableFunctionAddress(device, VTABLE_ID3D12Device_CreateCommandAllocator);
            if (addr == IntPtr.Zero) return -1;

            var fn = Marshal.GetDelegateForFunctionPointer<CreateCommandAllocatorDelegate>(addr);
            return fn(device, type, ref riid, out allocator);
        }

        private static int CreateCommandList(IntPtr device, uint nodeMask, int type, IntPtr allocator, IntPtr initialState, ref Guid riid, out IntPtr commandList)
        {
            commandList = IntPtr.Zero;

            IntPtr addr = GetVTableFunctionAddress(device, VTABLE_ID3D12Device_CreateCommandList);
            if (addr == IntPtr.Zero) return -1;

            var fn = Marshal.GetDelegateForFunctionPointer<CreateCommandListDelegate>(addr);
            return fn(device, nodeMask, type, allocator, initialState, ref riid, out commandList);
        }

        private static int CreateFence(IntPtr device, ulong initialValue, int flags, ref Guid riid, out IntPtr fence)
        {
            fence = IntPtr.Zero;

            IntPtr addr = GetVTableFunctionAddress(device, VTABLE_ID3D12Device_CreateFence);
            if (addr == IntPtr.Zero) return -1;

            var fn = Marshal.GetDelegateForFunctionPointer<CreateFenceDelegate>(addr);
            return fn(device, initialValue, flags, ref riid, out fence);
        }

        private static int CommandAllocatorReset(IntPtr allocator)
        {
            IntPtr addr = GetVTableFunctionAddress(allocator, VTABLE_ID3D12CommandAllocator_Reset);
            if (addr == IntPtr.Zero) return -1;

            var fn = Marshal.GetDelegateForFunctionPointer<CommandAllocatorResetDelegate>(addr);
            return fn(allocator);
        }

        private static int GraphicsCommandListClose(IntPtr commandList)
        {
            IntPtr addr = GetVTableFunctionAddress(commandList, VTABLE_ID3D12GraphicsCommandList_Close);
            if (addr == IntPtr.Zero) return -1;

            var fn = Marshal.GetDelegateForFunctionPointer<GraphicsCommandListCloseDelegate>(addr);
            return fn(commandList);
        }

        private static int GraphicsCommandListReset(IntPtr commandList, IntPtr allocator, IntPtr initialState)
        {
            IntPtr addr = GetVTableFunctionAddress(commandList, VTABLE_ID3D12GraphicsCommandList_Reset);
            if (addr == IntPtr.Zero) return -1;

            var fn = Marshal.GetDelegateForFunctionPointer<GraphicsCommandListResetDelegate>(addr);
            return fn(commandList, allocator, initialState);
        }

        private static void GraphicsCommandListResourceBarrier(IntPtr commandList, uint numBarriers, D3D12_RESOURCE_BARRIER* barriers)
        {
            IntPtr addr = GetVTableFunctionAddress(commandList, VTABLE_ID3D12GraphicsCommandList_ResourceBarrier);
            if (addr == IntPtr.Zero) return;

            var fn = Marshal.GetDelegateForFunctionPointer<GraphicsCommandListResourceBarrierDelegate>(addr);
            fn(commandList, numBarriers, barriers);
        }

        private static void GraphicsCommandListSetDescriptorHeaps(IntPtr commandList, uint numHeaps, IntPtr* heaps)
        {
            IntPtr addr = GetVTableFunctionAddress(commandList, VTABLE_ID3D12GraphicsCommandList_SetDescriptorHeaps);
            if (addr == IntPtr.Zero) return;

            var fn = Marshal.GetDelegateForFunctionPointer<GraphicsCommandListSetDescriptorHeapsDelegate>(addr);
            fn(commandList, numHeaps, heaps);
        }

        private static void GraphicsCommandListOMSetRenderTargets(IntPtr commandList, uint numRenderTargetDescriptors, nuint* rtvs, int rtsSingleHandleToDescriptorRange, nuint* dsv)
        {
            IntPtr addr = GetVTableFunctionAddress(commandList, VTABLE_ID3D12GraphicsCommandList_OMSetRenderTargets);
            if (addr == IntPtr.Zero) return;

            var fn = Marshal.GetDelegateForFunctionPointer<GraphicsCommandListOMSetRenderTargetsDelegate>(addr);
            fn(commandList, numRenderTargetDescriptors, rtvs, rtsSingleHandleToDescriptorRange, dsv);
        }

        private static void CommandQueueExecuteCommandLists(IntPtr queue, uint numCommandLists, IntPtr* ppCommandLists)
        {
            IntPtr addr = GetVTableFunctionAddress(queue, VTABLE_ID3D12CommandQueue_ExecuteCommandLists);
            if (addr == IntPtr.Zero) return;

            var fn = Marshal.GetDelegateForFunctionPointer<CommandQueueExecuteCommandListsDelegate>(addr);
            fn(queue, numCommandLists, ppCommandLists);
        }

        private static void GraphicsCommandListRSSetViewports(IntPtr commandList, uint numViewports, D3D12_VIEWPORT* pViewports)
        {
            // RSSetViewports is at index 21 (ID3D12GraphicsCommandList)
            // 0-2: IUnknown
            // 3-6: ID3D12Object
            // 7: ID3D12DeviceChild
            // 8:  (Inherited ID3D12CommandList::GetType)
            // 9:  Close (id3d12commandlist) ?? No, ID3D12CommandList has GetType.
            //     Wait. ID3D12GraphicsCommandList : ID3D12CommandList.
            //     ID3D12CommandList : ID3D12DeviceChild.
            //     ID3D12DeviceChild : ID3D12Object.
            //     ID3D12Object : IUnknown.
            //
            //     IUnknown: 3 methods (0-2)
            //     ID3D12Object: 4 methods (3-6)
            //     ID3D12DeviceChild: 1 method (GetDevice, 7)
            //     ID3D12CommandList: 1 method (GetType, 8)
            //     ID3D12GraphicsCommandList starts at 9.
            //     9: Close
            //     10: Reset
            //     ...
            //     21: RSSetViewports
            //     22: RSSetScissorRects

            IntPtr addr = GetVTableFunctionAddress(commandList, 21);
            if (addr == IntPtr.Zero) return;

            var fn = Marshal.GetDelegateForFunctionPointer<RSSetViewportsDelegate>(addr);
            fn(commandList, numViewports, pViewports);
        }

        private static void GraphicsCommandListRSSetScissorRects(IntPtr commandList, uint numRects, D3D12_RECT* pRects)
        {
            // RSSetScissorRects is at index 22
            IntPtr addr = GetVTableFunctionAddress(commandList, 22);
            if (addr == IntPtr.Zero) return;

            var fn = Marshal.GetDelegateForFunctionPointer<RSSetScissorRectsDelegate>(addr);
            fn(commandList, numRects, pRects);
        }

        private static void GraphicsCommandListClearRenderTargetView(IntPtr commandList, nuint rtvHandle, float* colorRGBA, uint numRects, D3D12_RECT* pRects)
        {
            // ClearRenderTargetView is at index 48 (ID3D12GraphicsCommandList)
            // OMSetRenderTargets=46
            // ClearDepthStencilView=47
            // ClearRenderTargetView=48
            IntPtr addr = GetVTableFunctionAddress(commandList, 48);
            if (addr == IntPtr.Zero) return;

            var fn = Marshal.GetDelegateForFunctionPointer<ClearRenderTargetViewDelegate>(addr);
            fn(commandList, rtvHandle, colorRGBA, numRects, pRects);
        }

        private static int CommandQueueSignal(IntPtr queue, IntPtr fence, ulong value)
        {
            IntPtr addr = GetVTableFunctionAddress(queue, VTABLE_ID3D12CommandQueue_Signal);
            if (addr == IntPtr.Zero) return -1;

            var fn = Marshal.GetDelegateForFunctionPointer<CommandQueueSignalDelegate>(addr);
            return fn(queue, fence, value);
        }

        private static ulong FenceGetCompletedValue(IntPtr fence)
        {
            IntPtr addr = GetVTableFunctionAddress(fence, VTABLE_ID3D12Fence_GetCompletedValue);
            if (addr == IntPtr.Zero) return 0;

            var fn = Marshal.GetDelegateForFunctionPointer<FenceGetCompletedValueDelegate>(addr);
            return fn(fence);
        }

        private static int FenceSetEventOnCompletion(IntPtr fence, ulong value, IntPtr hEvent)
        {
            IntPtr addr = GetVTableFunctionAddress(fence, VTABLE_ID3D12Fence_SetEventOnCompletion);
            if (addr == IntPtr.Zero) return -1;

            var fn = Marshal.GetDelegateForFunctionPointer<FenceSetEventOnCompletionDelegate>(addr);
            return fn(fence, value, hEvent);
        }
    }
}
#endif