using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Hexa.NET.ImGui;

namespace PhantomRender.ImGui.Core.Inputs
{
    public class InputEmulator : IDisposable
    {
        private const int GWLP_WNDPROC = -4;
        private const uint WM_MOUSEWHEEL = 0x020A;
        private const uint WM_MOUSEHWHEEL = 0x020E;
        private const int WHEEL_DELTA = 120;

        [DllImport("user32.dll")]
        private static extern int ToUnicode(
            uint wVirtKey,
            uint wScanCode,
            byte[] lpKeyState,
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff,
            int cchBuff,
            uint wFlags);

        [DllImport("user32.dll")]
        private static extern bool GetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private readonly ImGuiIOPtr _io;
        private readonly nint _windowHandle;
        private readonly byte[] _keyboardStateBuffer = new byte[256];
        private readonly StringBuilder _unicodeBuffer = new StringBuilder(2);
        private readonly Dictionary<ImGuiKey, DateTime> _keyLastPressed = new Dictionary<ImGuiKey, DateTime>();
        private readonly Dictionary<Keys, Action> _singleKeyEvents = new Dictionary<Keys, Action>();
        private readonly Dictionary<Keys, DateTime> _singleKeyLastTriggered = new Dictionary<Keys, DateTime>();
        private readonly Dictionary<HashSet<Keys>, Action> _comboKeyEvents =
            new Dictionary<HashSet<Keys>, Action>(HashSet<Keys>.CreateSetComparer());
        private readonly Dictionary<HashSet<Keys>, DateTime> _comboKeyLastTriggered =
            new Dictionary<HashSet<Keys>, DateTime>(HashSet<Keys>.CreateSetComparer());
        private WndProcDelegate _subclassWndProc;
        private nint _originalWndProc;
        private int _pendingMouseWheelVertical;
        private int _pendingMouseWheelHorizontal;
        private bool _disposed;

        public InputEmulator(ImGuiIOPtr io)
            : this(io, IntPtr.Zero)
        {
        }

        public InputEmulator(ImGuiIOPtr io, nint windowHandle)
        {
            _io = io;
            _windowHandle = windowHandle;

            foreach (ImGuiKey key in VirtualKeyToImGuiKeyMap.Values)
            {
                if (!_keyLastPressed.ContainsKey(key))
                {
                    _keyLastPressed[key] = DateTime.MinValue;
                }
            }

            TryInstallMouseWheelHook();
        }

        public bool Enabled { get; set; } = true;

        public TimeSpan KeyRepeatDelay { get; set; } = TimeSpan.FromMilliseconds(150);

        // Backwards-compatible alias based on previous style used by consumers.
        public TimeSpan keyRepeatDelay
        {
            get => KeyRepeatDelay;
            set => KeyRepeatDelay = value;
        }

        public Dictionary<Keys, ImGuiKey> VirtualKeyToImGuiKeyMap { get; } = CreateDefaultVirtualKeyMap();

        public virtual void Update()
        {
            UpdateKeyboardState();
            UpdateMouseState();
        }

        public void UpdateHotkeysOnly()
        {
            Interlocked.Exchange(ref _pendingMouseWheelVertical, 0);
            Interlocked.Exchange(ref _pendingMouseWheelHorizontal, 0);
            ProcessRegisteredHotkeys(DateTime.UtcNow);
        }

        public void AddEvent(Keys key, Action callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            _singleKeyEvents[key] = callback;
            if (!_singleKeyLastTriggered.ContainsKey(key))
            {
                _singleKeyLastTriggered[key] = DateTime.MinValue;
            }
        }

        public void AddEvent(Action callback, params Keys[] keysCombo)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            if (keysCombo == null || keysCombo.Length == 0)
            {
                throw new ArgumentException("At least one key is required for a combo event.", nameof(keysCombo));
            }

            HashSet<Keys> set = new HashSet<Keys>(keysCombo);
            _comboKeyEvents[set] = callback;
            if (!_comboKeyLastTriggered.ContainsKey(set))
            {
                _comboKeyLastTriggered[set] = DateTime.MinValue;
            }
        }

        public void ClearEvents()
        {
            _singleKeyEvents.Clear();
            _singleKeyLastTriggered.Clear();
            _comboKeyEvents.Clear();
            _comboKeyLastTriggered.Clear();
        }

        public virtual void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ClearEvents();
            Interlocked.Exchange(ref _pendingMouseWheelVertical, 0);
            Interlocked.Exchange(ref _pendingMouseWheelHorizontal, 0);
            TryRemoveMouseWheelHook();
        }

        public void UpdateKeyboardState()
        {
            DateTime now = DateTime.UtcNow;

            foreach (KeyValuePair<Keys, ImGuiKey> key in VirtualKeyToImGuiKeyMap)
            {
                try
                {
                    bool isKeyDown = IsKeyDown(key.Key);

                    if (isKeyDown)
                    {
                        DateTime lastPressed = _keyLastPressed[key.Value];
                        if (now - lastPressed >= KeyRepeatDelay)
                        {
                            _io.AddKeyEvent(key.Value, true);

                            char c = ConvertKeyToChar(key.Key);
                            if (c != '\0' && Enabled)
                            {
                                _io.AddInputCharacter(c);
                            }

                            _keyLastPressed[key.Value] = now;
                        }
                    }
                    else
                    {
                        _io.AddKeyEvent(key.Value, false);
                    }
                }
                catch
                {
                    // Ignore isolated key update failures to keep overlay responsive.
                }
            }

            UpdateModifierKey(ImGuiKey.ModShift, Keys.ShiftKey);
            UpdateModifierKey(ImGuiKey.ModCtrl, Keys.ControlKey);
            UpdateModifierKey(ImGuiKey.ModAlt, Keys.Menu);
            UpdateModifierKey(ImGuiKey.ModSuper, Keys.LWin);

            ProcessRegisteredHotkeys(now);
        }

        public bool UpdateMouseState()
        {
            try
            {
                _io.MouseDown[0] = IsKeyDown(Keys.LButton);
                _io.MouseDown[1] = IsKeyDown(Keys.RButton);
                _io.MouseDown[2] = IsKeyDown(Keys.MButton);
                _io.MouseDown[3] = IsKeyDown(Keys.XButton1);
                _io.MouseDown[4] = IsKeyDown(Keys.XButton2);
                FlushPendingMouseWheel();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool IsKeyDown(Keys vk)
        {
            return (GetAsyncKeyState((int)vk) & 0x8000) != 0;
        }

        protected virtual char ConvertKeyToChar(Keys key)
        {
            if (!GetKeyboardState(_keyboardStateBuffer))
            {
                return '\0';
            }

            _unicodeBuffer.Clear();
            uint virtualKey = (uint)key;
            uint scanCode = MapVirtualKey(virtualKey, 0);
            int result = ToUnicode(virtualKey, scanCode, _keyboardStateBuffer, _unicodeBuffer, _unicodeBuffer.Capacity, 0);
            return result > 0 ? _unicodeBuffer[0] : '\0';
        }

        protected virtual void UpdateModifierKey(ImGuiKey imguiKey, Keys virtualKey)
        {
            try
            {
                _io.AddKeyEvent(imguiKey, IsKeyDown(virtualKey));
            }
            catch
            {
                // Ignore modifier polling failures.
            }
        }

        private void ProcessRegisteredHotkeys(DateTime now)
        {
            foreach (KeyValuePair<Keys, Action> singleEvent in _singleKeyEvents)
            {
                try
                {
                    if (IsKeyDown(singleEvent.Key) &&
                        now - _singleKeyLastTriggered[singleEvent.Key] >= KeyRepeatDelay)
                    {
                        singleEvent.Value?.Invoke();
                        _singleKeyLastTriggered[singleEvent.Key] = now;
                    }
                }
                catch
                {
                    // User callbacks should not break the input update path.
                }
            }

            foreach (KeyValuePair<HashSet<Keys>, Action> comboEvent in _comboKeyEvents)
            {
                try
                {
                    bool allKeysDown = true;
                    foreach (Keys key in comboEvent.Key)
                    {
                        if (!IsKeyDown(key))
                        {
                            allKeysDown = false;
                            break;
                        }
                    }

                    if (allKeysDown &&
                        now - _comboKeyLastTriggered[comboEvent.Key] >= KeyRepeatDelay)
                    {
                        comboEvent.Value?.Invoke();
                        _comboKeyLastTriggered[comboEvent.Key] = now;
                    }
                }
                catch
                {
                    // User callbacks should not break the input update path.
                }
            }
        }

        private void FlushPendingMouseWheel()
        {
            int verticalDelta = Interlocked.Exchange(ref _pendingMouseWheelVertical, 0);
            int horizontalDelta = Interlocked.Exchange(ref _pendingMouseWheelHorizontal, 0);
            if (verticalDelta == 0 && horizontalDelta == 0)
            {
                return;
            }

            _io.AddMouseWheelEvent(
                horizontalDelta / (float)WHEEL_DELTA,
                verticalDelta / (float)WHEEL_DELTA);
        }

        private void TryInstallMouseWheelHook()
        {
            if (_windowHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                _subclassWndProc = WindowProc;
                nint newWndProc = Marshal.GetFunctionPointerForDelegate(_subclassWndProc);
                _originalWndProc = SetWindowLongPtr(_windowHandle, GWLP_WNDPROC, newWndProc);
                if (_originalWndProc == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
                {
                    _subclassWndProc = null;
                }
            }
            catch
            {
                _originalWndProc = IntPtr.Zero;
                _subclassWndProc = null;
            }
        }

        private void TryRemoveMouseWheelHook()
        {
            if (_windowHandle == IntPtr.Zero || _originalWndProc == IntPtr.Zero)
            {
                return;
            }

            try
            {
                SetWindowLongPtr(_windowHandle, GWLP_WNDPROC, _originalWndProc);
            }
            catch
            {
            }
            finally
            {
                _originalWndProc = IntPtr.Zero;
                _subclassWndProc = null;
            }
        }

        private nint WindowProc(nint hWnd, uint msg, nuint wParam, nint lParam)
        {
            switch (msg)
            {
                case WM_MOUSEWHEEL:
                    Interlocked.Add(ref _pendingMouseWheelVertical, GetWheelDelta(wParam));
                    break;

                case WM_MOUSEHWHEEL:
                    Interlocked.Add(ref _pendingMouseWheelHorizontal, GetWheelDelta(wParam));
                    break;
            }

            return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
        }

        private static int GetWheelDelta(nuint wParam)
        {
            return unchecked((short)(((ulong)wParam >> 16) & 0xFFFF));
        }

        private static nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
                : (nint)SetWindowLong32(hWnd, nIndex, (int)dwNewLong);
        }

        private static Dictionary<Keys, ImGuiKey> CreateDefaultVirtualKeyMap()
        {
            return new Dictionary<Keys, ImGuiKey>
            {
                { Keys.D0, ImGuiKey.Key0 },
                { Keys.D1, ImGuiKey.Key1 },
                { Keys.D2, ImGuiKey.Key2 },
                { Keys.D3, ImGuiKey.Key3 },
                { Keys.D4, ImGuiKey.Key4 },
                { Keys.D5, ImGuiKey.Key5 },
                { Keys.D6, ImGuiKey.Key6 },
                { Keys.D7, ImGuiKey.Key7 },
                { Keys.D8, ImGuiKey.Key8 },
                { Keys.D9, ImGuiKey.Key9 },
                { Keys.NumPad0, ImGuiKey.Key0 },
                { Keys.NumPad1, ImGuiKey.Key1 },
                { Keys.NumPad2, ImGuiKey.Key2 },
                { Keys.NumPad3, ImGuiKey.Key3 },
                { Keys.NumPad4, ImGuiKey.Key4 },
                { Keys.NumPad5, ImGuiKey.Key5 },
                { Keys.NumPad6, ImGuiKey.Key6 },
                { Keys.NumPad7, ImGuiKey.Key7 },
                { Keys.NumPad8, ImGuiKey.Key8 },
                { Keys.NumPad9, ImGuiKey.Key9 },
                { Keys.A, ImGuiKey.A },
                { Keys.B, ImGuiKey.B },
                { Keys.C, ImGuiKey.C },
                { Keys.D, ImGuiKey.D },
                { Keys.E, ImGuiKey.E },
                { Keys.F, ImGuiKey.F },
                { Keys.G, ImGuiKey.G },
                { Keys.H, ImGuiKey.H },
                { Keys.I, ImGuiKey.I },
                { Keys.J, ImGuiKey.J },
                { Keys.K, ImGuiKey.K },
                { Keys.L, ImGuiKey.L },
                { Keys.M, ImGuiKey.M },
                { Keys.N, ImGuiKey.N },
                { Keys.O, ImGuiKey.O },
                { Keys.P, ImGuiKey.P },
                { Keys.Q, ImGuiKey.Q },
                { Keys.R, ImGuiKey.R },
                { Keys.S, ImGuiKey.S },
                { Keys.T, ImGuiKey.T },
                { Keys.U, ImGuiKey.U },
                { Keys.V, ImGuiKey.V },
                { Keys.W, ImGuiKey.W },
                { Keys.X, ImGuiKey.X },
                { Keys.Y, ImGuiKey.Y },
                { Keys.Z, ImGuiKey.Z },
                { Keys.Enter, ImGuiKey.Enter },
                { Keys.Escape, ImGuiKey.Escape },
                { Keys.Back, ImGuiKey.Backspace },
                { Keys.Delete, ImGuiKey.Delete },
                { Keys.Space, ImGuiKey.Space },
                { Keys.Tab, ImGuiKey.Tab },
                { Keys.Left, ImGuiKey.LeftArrow },
                { Keys.Right, ImGuiKey.RightArrow },
                { Keys.Up, ImGuiKey.UpArrow },
                { Keys.Down, ImGuiKey.DownArrow },
                { Keys.F1, ImGuiKey.F1 },
                { Keys.F2, ImGuiKey.F2 },
                { Keys.F3, ImGuiKey.F3 },
                { Keys.F4, ImGuiKey.F4 },
                { Keys.F5, ImGuiKey.F5 },
                { Keys.F6, ImGuiKey.F6 },
                { Keys.F7, ImGuiKey.F7 },
                { Keys.F8, ImGuiKey.F8 },
                { Keys.F9, ImGuiKey.F9 },
                { Keys.F10, ImGuiKey.F10 },
                { Keys.F11, ImGuiKey.F11 },
                { Keys.F12, ImGuiKey.F12 },
                { Keys.OemPeriod, ImGuiKey.Period },
                { Keys.Oemcomma, ImGuiKey.Comma },
                { Keys.OemSemicolon, ImGuiKey.Semicolon },
                { Keys.OemQuotes, ImGuiKey.Apostrophe },
                { Keys.OemQuestion, ImGuiKey.Slash },
                { Keys.OemPipe, ImGuiKey.Backslash },
                { Keys.OemCloseBrackets, ImGuiKey.RightBracket },
                { Keys.OemOpenBrackets, ImGuiKey.LeftBracket },
                { Keys.OemMinus, ImGuiKey.Minus },
                { Keys.Oemplus, ImGuiKey.Equal },
                { Keys.Oemtilde, ImGuiKey.GraveAccent },
                { Keys.OemBackslash, ImGuiKey.Backslash }
            };
        }

        private delegate nint WndProcDelegate(nint hWnd, uint msg, nuint wParam, nint lParam);

        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
        private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nuint wParam, nint lParam);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);
    }

    public sealed class InputImguiEmu : InputEmulator
    {
        public InputImguiEmu(ImGuiIOPtr io)
            : base(io)
        {
        }

        public InputImguiEmu(ImGuiIOPtr io, nint windowHandle)
            : base(io, windowHandle)
        {
        }
    }
}
