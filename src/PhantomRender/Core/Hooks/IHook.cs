using System;

namespace PhantomRender.Core.Hooks
{
    /// <summary>
    /// Interface for all hooking implementations (VTable, IAT, Detour).
    /// </summary>
    public interface IHook : IDisposable
    {
        /// <summary>
        /// Enable the hook.
        /// </summary>
        void Enable();

        /// <summary>
        /// Disable the hook.
        /// </summary>
        void Disable();

        /// <summary>
        /// Check if the hook is currently enabled.
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Helper to get the original function pointer.
        /// </summary>
        IntPtr OriginalFunction { get; }
    }
}
