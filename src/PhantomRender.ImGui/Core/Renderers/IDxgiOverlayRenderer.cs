using System;

namespace PhantomRender.ImGui.Core.Renderers
{
    public interface IDxgiOverlayRenderer : IOverlayRenderer
    {
        nint SwapChainHandle { get; }

        bool InitializeFromSwapChain(nint swapChain);

        bool InitializeFromSwapChain(nint swapChain, nint windowHandle);

        void Render(nint swapChain);

        void OnBeforeResizeBuffers(nint swapChain);

        void OnAfterResizeBuffers(nint swapChain);
    }
}
