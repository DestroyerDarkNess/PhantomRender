namespace PhantomRender.ImGui
{
    public sealed class OverlayMenuOptions
    {
        /// <summary>
        /// Hook strategy used by the native runtime. Auto keeps current probing behavior.
        /// </summary>
        public OverlayHookKind PreferredHook { get; set; } = OverlayHookKind.Auto;

        /// <summary>
        /// When true, the native host may render built-in windows (status/menu/demo toggles).
        /// </summary>
        public bool EnableDefaultUi { get; set; } = true;

        /// <summary>
        /// When true, exceptions thrown by user callbacks are captured and reported through OnError.
        /// </summary>
        public bool CatchUserCallbackExceptions { get; set; } = true;

        /// <summary>
        /// DXGI/DX9/OpenGL probe timeout used when auto-detecting hooks.
        /// </summary>
        public int ProbeTimeoutMs { get; set; } = 10_000;
    }
}
