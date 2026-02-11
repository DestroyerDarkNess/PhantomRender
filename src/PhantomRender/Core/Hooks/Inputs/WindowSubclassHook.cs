using System;
using System.Runtime.InteropServices;
using PhantomRender.Core.Native;

namespace PhantomRender.Core.Hooks.Inputs
{
    public class WindowSubclassHook : IDisposable
    {
        private readonly IntPtr _hWnd;
        private readonly SUBCLASSPROC _subclassProc;
        private bool _isEnabled;
        private readonly IntPtr _uIdSubclass = (IntPtr)1337;

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

        public void Dispose()
        {
            Disable();
            GC.SuppressFinalize(this);
        }

        private IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
        {
            OnWndProc?.Invoke(hWnd, uMsg, wParam, lParam);
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
    }
}
