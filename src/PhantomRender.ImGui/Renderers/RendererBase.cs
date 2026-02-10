using System;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.Win32;

namespace PhantomRender.ImGui.Renderers
{
    public abstract class RendererBase : IOverlayRenderer
    {
        public ImGuiContextPtr Context { get; protected set; }
        public ImGuiIOPtr IO { get; protected set; }
        public bool IsInitialized { get; protected set; }

        public event Action OnOverlayRender;

        protected IntPtr _windowHandle;

        public abstract bool Initialize(IntPtr device, IntPtr windowHandle);
        public abstract void NewFrame();
        public abstract void Render();
        public abstract void OnLostDevice();
        public abstract void OnResetDevice();
        public abstract void Dispose();

        protected void RaiseOverlayRender()
        {
            OnOverlayRender?.Invoke();
        }

        protected unsafe void InitializeImGui(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;
            Context = Hexa.NET.ImGui.ImGui.CreateContext();
            Hexa.NET.ImGui.ImGui.SetCurrentContext(Context);
            IO = Hexa.NET.ImGui.ImGui.GetIO();
            IO.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard; // Enable Keyboard Controls
            
            // Initialize Win32 Backend (Platform)
            // Note: This does not hook WndProc. Hook must forward messages to ImGui_ImplWin32_WndProcHandler.
            ImGuiImplWin32.Init((void*)windowHandle);
        }

        protected unsafe void ShutdownImGui()
        {
             if (IsInitialized)
             {
                 ImGuiImplWin32.Shutdown();
                 Hexa.NET.ImGui.ImGui.DestroyContext(Context);
             }
        }
    }
}
