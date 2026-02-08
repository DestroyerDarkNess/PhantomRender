using System;
using System.Runtime.InteropServices;
using PhantomRender.Core.Native;

namespace PhantomRender.Core.Hooks.Inputs
{
    public class WindowSubclassHook : IHook
    {
        private readonly IntPtr _hWnd;
        private readonly SUBCLASSPROC _subclassProc;
        private bool _isEnabled;
        private readonly IntPtr _uIdSubclass = (IntPtr)1337; // Unique ID for our subclass

        public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        public event WndProcDelegate OnWndProc;

        public WindowSubclassHook(IntPtr hWnd)
        {
            _hWnd = hWnd;
            _subclassProc = new SUBCLASSPROC(SubclassProc);
        }

        public void Enable()
        {
            if (_isEnabled) return;
            if (SetWindowSubclass(_hWnd, _subclassProc, _uIdSubclass, IntPtr.Zero))
            {
                _isEnabled = true;
            }
        }

        public void Disable()
        {
            if (!_isEnabled) return;
            if (RemoveWindowSubclass(_hWnd, _subclassProc, _uIdSubclass))
            {
                _isEnabled = false;
            }
        }

        public bool IsEnabled => _isEnabled;

        public IntPtr OriginalFunction => IntPtr.Zero; // Not applicable for Subclassing

        public void Dispose()
        {
            Disable();
        }

        private IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (OnWndProc != null)
            {
                // Allow valid input processing or blocking
                // If OnWndProc returns IntPtr.Zero, we continue? 
                // Let's assume standard WndProc behavior: return value matters if we handle it.
                // But for a hook, we usually want to "peek" and maybe "consume".
                
                // For now, let's just invoke. 
                // If we want to block, we might need a ref bool 'handled'.
                // keeping it simple: observe only for now.
                OnWndProc(hWnd, uMsg, wParam, lParam);
            }

            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        // Native imports
        private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
    }
}
