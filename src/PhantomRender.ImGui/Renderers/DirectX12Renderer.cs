#if NET5_0_OR_GREATER
using System;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.D3D12;
using Hexa.NET.ImGui.Backends.Win32;
using PhantomRender.Core.Hooks.Graphics;
using PhantomRender.Core.Native;

namespace PhantomRender.ImGui.Renderers
{
    public sealed unsafe class DirectX12Renderer : RendererBase
    {
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
        private delegate uint GetDescriptorHandleIncrementSizeDelegate(IntPtr device, int type);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CreateRenderTargetViewDelegate(IntPtr device, IntPtr resource, IntPtr desc, D3D12CpuDescriptorHandle destDescriptor);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateCommandAllocatorDelegate(IntPtr device, int type, ref Guid riid, out IntPtr allocator);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateCommandListDelegate(IntPtr device, uint nodeMask, int type, IntPtr allocator, IntPtr initialState, ref Guid riid, out IntPtr commandList);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateFenceDelegate(IntPtr device, ulong initialValue, int flags, ref Guid riid, out IntPtr fence);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate D3D12CpuDescriptorHandle GetCPUDescriptorHandleForHeapStartDelegate(IntPtr heap);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate D3D12GpuDescriptorHandle GetGPUDescriptorHandleForHeapStartDelegate(IntPtr heap);

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
        private delegate void GraphicsCommandListOMSetRenderTargetsDelegate(IntPtr commandList, uint numRenderTargetDescriptors, D3D12CpuDescriptorHandle* rtvs, int rtsSingleHandleToDescriptorRange, D3D12CpuDescriptorHandle* dsv);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CommandQueueExecuteCommandListsDelegate(IntPtr queue, uint numCommandLists, IntPtr* ppCommandLists);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CommandQueueSignalDelegate(IntPtr queue, IntPtr fence, ulong value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate ulong FenceGetCompletedValueDelegate(IntPtr fence);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int FenceSetEventOnCompletionDelegate(IntPtr fence, ulong value, IntPtr hEvent);

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
        private ulong _frameCounter;

        private bool _loggedWaitingQueue;
        private bool _loggedSwapchainDesc;

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
            _inputEmulator?.Update();
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
                    _frameCounter++;

                    Hexa.NET.ImGui.ImGui.SetNextWindowPos(new System.Numerics.Vector2(50, 50), ImGuiCond.FirstUseEver);
                    if (Hexa.NET.ImGui.ImGui.Begin("PhantomRender DX12"))
                    {
                        Hexa.NET.ImGui.ImGui.Text("Status: Active (DX12)");
                        Hexa.NET.ImGui.ImGui.Text($"Window: {_windowHandle}");
                        Hexa.NET.ImGui.ImGui.End();
                    }

                    Hexa.NET.ImGui.ImGui.ShowDemoWindow();

                    RaiseOverlayRender();
                    Hexa.NET.ImGui.ImGui.Render();

                    var drawData = Hexa.NET.ImGui.ImGui.GetDrawData();
                    if (_frameCounter <= 5 || _frameCounter % 300 == 0)
                    {
                        Console.WriteLine($"[PhantomRender] DX12 Frame {_frameCounter}: DrawLists={drawData.CmdListsCount}, Vtx={drawData.TotalVtxCount}, Idx={drawData.TotalIdxCount}");
                        Console.Out.Flush();
                    }

                    RenderDrawData(drawData);
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
                    if (_frames[i].RenderTarget != IntPtr.Zero)
                    {
                        Marshal.Release(_frames[i].RenderTarget);
                        _frames[i].RenderTarget = IntPtr.Zero;
                    }

                    if (_frames[i].CommandAllocator != IntPtr.Zero)
                    {
                        Marshal.Release(_frames[i].CommandAllocator);
                        _frames[i].CommandAllocator = IntPtr.Zero;
                    }

                    _frames[i].FenceValue = 0;
                }
            }

            _frames = null;
            _bufferCount = 0;
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
        }

        private bool EnsureDx12Ready(IntPtr swapChain)
        {
            if (_device == IntPtr.Zero) return false;

            if (_commandQueue == IntPtr.Zero)
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
                Marshal.AddRef(_commandQueue);

                Console.WriteLine($"[PhantomRender] DX12: Command queue resolved: {_commandQueue}");
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

            if (CreateDescriptorHeap(_device, ref srvHeapDesc, ref iidHeap, out _srvHeap) < 0 || _srvHeap == IntPtr.Zero)
            {
                Console.WriteLine("[PhantomRender] DirectX12Renderer: Failed to create SRV heap.");
                Console.Out.Flush();
                return false;
            }

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
                // Some drivers can legitimately return 0 for the start handle; don't treat this as fatal.
                Console.WriteLine("[PhantomRender] DirectX12Renderer: SRV heap GPU start is 0 (continuing).");
                Console.Out.Flush();
            }

            // Avoid passing a 0 GPU handle into ImGui init in case the backend treats it as "null".
            // We reserve descriptor slot 0 and use slot 1 for the legacy font SRV.
            const uint legacySrvIndex = 1;
            _imguiSrvCpu = new D3D12CpuDescriptorHandle(_srvCpuStart.Ptr + (nuint)(legacySrvIndex * _srvDescriptorSize));
            _imguiSrvGpu = new D3D12GpuDescriptorHandle(_srvGpuStart.Ptr + (ulong)(legacySrvIndex * _srvDescriptorSize));

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
                if (GetBuffer(swapChain, i, ref iidResource, out var resource) < 0 || resource == IntPtr.Zero)
                {
                    Console.WriteLine($"[PhantomRender] DirectX12Renderer: GetBuffer({i}) failed.");
                    Console.Out.Flush();
                    return false;
                }

                Console.WriteLine($"[PhantomRender] DX12 Init: Backbuffer {i} resource={resource}");
                Console.Out.Flush();

                var rtvHandle = new D3D12CpuDescriptorHandle(_rtvCpuStart.Ptr + (nuint)(i * inc));
                CreateRenderTargetView(_device, resource, IntPtr.Zero, rtvHandle);

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

                ImGuiImplDX12InitInfo info = default;
                info.Device = (ID3D12Device*)_device;
                info.CommandQueue = (ID3D12CommandQueue*)_commandQueue;
                info.NumFramesInFlight = (int)_bufferCount;
                info.RTVFormat = _rtvFormat;
                info.DSVFormat = 0;
                info.UserData = null;
                info.SrvDescriptorHeap = (ID3D12DescriptorHeap*)_srvHeap;
                info.SrvDescriptorAllocFn = null;
                info.SrvDescriptorFreeFn = null;
                info.LegacySingleSrvCpuDescriptor = _imguiSrvCpu;
                info.LegacySingleSrvGpuDescriptor = _imguiSrvGpu;

                if (!ImGuiImplD3D12.Init(ref info))
                {
                    Console.WriteLine("[PhantomRender] DirectX12Renderer: ImGuiImplD3D12.Init returned FALSE!");
                    Console.Out.Flush();
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
                return;

            uint frameIndex = GetCurrentBackBufferIndex(_swapChain3);
            if (frameIndex >= _frames.Length)
                return;

            ref FrameContext frame = ref _frames[frameIndex];

            WaitForFrame(ref frame);

            if (CommandAllocatorReset(frame.CommandAllocator) < 0)
                return;

            if (GraphicsCommandListReset(_commandList, frame.CommandAllocator, IntPtr.Zero) < 0)
                return;

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
                        StateBefore = D3D12_RESOURCE_STATE_PRESENT,
                        StateAfter = D3D12_RESOURCE_STATE_RENDER_TARGET
                    }
                }
            };

            GraphicsCommandListResourceBarrier(_commandList, 1, &barrier);

            // Bind RT
            D3D12CpuDescriptorHandle rtv = frame.Rtv;
            GraphicsCommandListOMSetRenderTargets(_commandList, 1, &rtv, 0, null);

            // Descriptor heaps
            IntPtr srvHeap = _srvHeap;
            GraphicsCommandListSetDescriptorHeaps(_commandList, 1, &srvHeap);

            ImGuiImplD3D12.RenderDrawData(drawData, (ID3D12GraphicsCommandList*)_commandList);

            // Transition back to PRESENT
            barrier.Union.Transition.StateBefore = D3D12_RESOURCE_STATE_RENDER_TARGET;
            barrier.Union.Transition.StateAfter = D3D12_RESOURCE_STATE_PRESENT;
            GraphicsCommandListResourceBarrier(_commandList, 1, &barrier);

            if (GraphicsCommandListClose(_commandList) < 0)
                return;

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
            // IDXGISwapChain3::GetCurrentBackBufferIndex is at vtable index 35.
            IntPtr addr = GetVTableFunctionAddress(swapChain3, 35);
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

        private static D3D12CpuDescriptorHandle GetCPUDescriptorHandleForHeapStart(IntPtr heap)
        {
            IntPtr addr = GetVTableFunctionAddress(heap, VTABLE_ID3D12DescriptorHeap_GetCPUDescriptorHandleForHeapStart);
            if (addr == IntPtr.Zero) return default;

            var fn = Marshal.GetDelegateForFunctionPointer<GetCPUDescriptorHandleForHeapStartDelegate>(addr);
            return fn(heap);
        }

        private static D3D12GpuDescriptorHandle GetGPUDescriptorHandleForHeapStart(IntPtr heap)
        {
            IntPtr addr = GetVTableFunctionAddress(heap, VTABLE_ID3D12DescriptorHeap_GetGPUDescriptorHandleForHeapStart);
            if (addr == IntPtr.Zero) return default;

            var fn = Marshal.GetDelegateForFunctionPointer<GetGPUDescriptorHandleForHeapStartDelegate>(addr);
            return fn(heap);
        }

        private static void CreateRenderTargetView(IntPtr device, IntPtr resource, IntPtr desc, D3D12CpuDescriptorHandle destDescriptor)
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

        private static void GraphicsCommandListOMSetRenderTargets(IntPtr commandList, uint numRenderTargetDescriptors, D3D12CpuDescriptorHandle* rtvs, int rtsSingleHandleToDescriptorRange, D3D12CpuDescriptorHandle* dsv)
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
