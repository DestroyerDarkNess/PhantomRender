using System;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.D3D11;
using Hexa.NET.ImGui.Backends.Win32;

namespace PhantomRender.ImGui.Renderers
{
    public sealed class DirectX11Renderer : RendererBase
    {
        // ID3D11Device VTable index for GetImmediateContext
        private const int VTABLE_GetImmediateContext = 40;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetImmediateContextDelegate(IntPtr device, out IntPtr ppImmediateContext);

        public override unsafe bool Initialize(IntPtr device, IntPtr windowHandle)
        {
            if (IsInitialized) return true;

            try
            {
                Console.WriteLine($"[PhantomRender] DirectX11Renderer: Entering Initialize. Device: {device}, Window: {windowHandle}");
                Console.Out.Flush();

                // Get the immediate context from the device via VTable
                IntPtr deviceContext = GetImmediateContext(device);
                if (deviceContext == IntPtr.Zero)
                {
                    Console.WriteLine("[PhantomRender] DirectX11Renderer: Failed to get ImmediateContext!");
                    Console.Out.Flush();
                    return false;
                }

                Console.WriteLine($"[PhantomRender] DirectX11Renderer: Got ImmediateContext: {deviceContext}");
                Console.Out.Flush();

                InitializeImGui(windowHandle);

                // Synchronize context
                ImGuiImplD3D11.SetCurrentContext(Context);

                // Initialize D3D11 Backend — needs both device and device context
                if (!ImGuiImplD3D11.Init((ID3D11Device*)device, (ID3D11DeviceContext*)deviceContext))
                {
                    Console.WriteLine("[PhantomRender] DirectX11Renderer: ImGuiImplD3D11.Init returned FALSE!");
                    Console.Out.Flush();
                    // Release the context ref we obtained
                    Marshal.Release(deviceContext);
                    ShutdownImGui();
                    return false;
                }

                // Release our extra ref — ImGui backend internally AddRef'd what it needs
                Marshal.Release(deviceContext);

                IsInitialized = true;
                Console.WriteLine("[PhantomRender] DirectX11Renderer: Initialized Successfully!");
                Console.Out.Flush();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DirectX11Renderer: Init Error: {ex}");
                Console.Out.Flush();
                return false;
            }
        }

        public override void NewFrame()
        {
            if (!IsInitialized) return;

            Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
            ImGuiImplD3D11.SetCurrentContext(Context);
            ImGuiImplWin32.SetCurrentContext(Context);

            ImGuiImplD3D11.NewFrame();
            ImGuiImplWin32.NewFrame();
            _inputEmulator?.Update();
            Hexa.NET.ImGui.ImGui.NewFrame();
        }

        public override void Render()
        {
            if (!IsInitialized) return;

            Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);

            // Test window
            Hexa.NET.ImGui.ImGui.SetNextWindowPos(new System.Numerics.Vector2(50, 50), ImGuiCond.FirstUseEver);
            if (Hexa.NET.ImGui.ImGui.Begin("PhantomRender DX11"))
            {
                Hexa.NET.ImGui.ImGui.Text("Status: Active (DX11)");
                Hexa.NET.ImGui.ImGui.Text($"Window: {_windowHandle}");
                Hexa.NET.ImGui.ImGui.End();
            }

            // Demo window
            Hexa.NET.ImGui.ImGui.ShowDemoWindow();

            RaiseOverlayRender();
            Hexa.NET.ImGui.ImGui.Render();
            ImGuiImplD3D11.RenderDrawData(Hexa.NET.ImGui.ImGui.GetDrawData());
        }

        public override void OnLostDevice()
        {
            if (IsInitialized)
            {
                ImGuiImplD3D11.InvalidateDeviceObjects();
            }
        }

        public override void OnResetDevice()
        {
            if (IsInitialized)
            {
                ImGuiImplD3D11.CreateDeviceObjects();
            }
        }

        public override void Dispose()
        {
            if (IsInitialized)
            {
                ImGuiImplD3D11.Shutdown();
                ShutdownImGui();
                IsInitialized = false;
            }
        }

        /// <summary>
        /// Gets the immediate context from the ID3D11Device via VTable call.
        /// ID3D11Device::GetImmediateContext is at VTable index 40.
        /// This adds a reference to the returned context.
        /// </summary>
        private static IntPtr GetImmediateContext(IntPtr device)
        {
            if (device == IntPtr.Zero) return IntPtr.Zero;

            try
            {
                IntPtr vTable = Marshal.ReadIntPtr(device);
                IntPtr getImmediateContextAddr = Marshal.ReadIntPtr(vTable + VTABLE_GetImmediateContext * IntPtr.Size);

                var getImmediateContext = Marshal.GetDelegateForFunctionPointer<GetImmediateContextDelegate>(getImmediateContextAddr);
                IntPtr context;
                getImmediateContext(device, out context);
                return context;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhantomRender] DirectX11Renderer: GetImmediateContext error: {ex}");
                return IntPtr.Zero;
            }
        }
    }
}
