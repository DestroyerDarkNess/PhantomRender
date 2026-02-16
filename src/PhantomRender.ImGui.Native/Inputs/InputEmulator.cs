using System;
using System.Runtime.InteropServices;
using System.Text;
using Hexa.NET.ImGui;

namespace PhantomRender.ImGui.Inputs
{
    public class InputEmulator
    {
        private readonly ImGuiIOPtr _io;
        private readonly IntPtr _windowHandle;
        private bool _enabled = true;
        
        // Key state tracking
        private readonly byte[] _keyStateBuffer = new byte[256];
        private readonly bool[] _prevKeysDown = new bool[256];
        private readonly long[] _lastKeyPressTime = new long[256]; // Using ticks for high precision
        
        // Mouse state
        private bool[] _prevMouseDown = new bool[5];

        // Configuration
        public long KeyRepeatDelayTicks { get; set; } = TimeSpan.FromMilliseconds(250).Ticks;
        public long KeyRepeatRateTicks { get; set; } = TimeSpan.FromMilliseconds(50).Ticks;

        public InputEmulator(ImGuiIOPtr io, IntPtr windowHandle)
        {
            _io = io;
            _windowHandle = windowHandle;
        }

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public unsafe void Update()
        {
            if (!_enabled) return;

            UpdateMouse();
            UpdateKeyboard();
        }

        private void UpdateMouse()
        {
            // Update Mouse Position
            if (GetCursorPos(out POINT p))
            {
                // Convert screen coordinates to client coordinates
                ScreenToClient(_windowHandle, ref p);
                
                _io.AddMousePosEvent((float)p.X, (float)p.Y);
            }

            // Update Mouse Buttons
            UpdateMouseButton(0, VK_LBUTTON);
            UpdateMouseButton(1, VK_RBUTTON);
            UpdateMouseButton(2, VK_MBUTTON);
            UpdateMouseButton(3, VK_XBUTTON1);
            UpdateMouseButton(4, VK_XBUTTON2);
        }

        // ... existing methods ...

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        private void UpdateMouseButton(int buttonIndex, int vkKey)
        {
            bool isDown = (GetAsyncKeyState(vkKey) & 0x8000) != 0;
            if (isDown != _prevMouseDown[buttonIndex])
            {
                _io.AddMouseButtonEvent(buttonIndex, isDown);
                _prevMouseDown[buttonIndex] = isDown;
            }
        }

        private unsafe void UpdateKeyboard()
        {
            // Get current keyboard state
            if (!GetKeyboardState(_keyStateBuffer)) return;

            long now = DateTime.UtcNow.Ticks;

            // Iterate through all virtual keys (0-254)
            for (int i = 0; i < 255; i++)
            {
                // Skip mouse buttons (handled in UpdateMouse)
                if (i == VK_LBUTTON || i == VK_RBUTTON || i == VK_MBUTTON || i == VK_XBUTTON1 || i == VK_XBUTTON2) continue;

                // Check high bit for key down
                bool isDown = (_keyStateBuffer[i] & 0x80) != 0;
                
                // Map Virtual Key to ImGui Key
                ImGuiKey imKey = MapVirtualKeyToImGuiKey(i);
                
                if (imKey != ImGuiKey.None)
                {
                    if (isDown)
                    {
                        // Key just pressed or held
                        if (!_prevKeysDown[i])
                        {
                            // Just pressed
                            _io.AddKeyEvent(imKey, true);
                            _prevKeysDown[i] = true;
                            _lastKeyPressTime[i] = now + KeyRepeatDelayTicks; // Schedule first repeat
                            
                            // Handle Text Input on Press
                            ProcessTextInput(i);
                        }
                        else
                        {
                            // Held down - Handle repeat for text input?
                            // ImGui handles generic key repeats for logic, but for Text Input (AddInputCharacter),
                            // we must manually spam it if we want repeated characters.
                            if (now >= _lastKeyPressTime[i])
                            {
                                ProcessTextInput(i);
                                _lastKeyPressTime[i] = now + KeyRepeatRateTicks; // Schedule next repeat
                            }
                        }
                    }
                    else
                    {
                        // Key released
                        if (_prevKeysDown[i])
                        {
                            _io.AddKeyEvent(imKey, false);
                            _prevKeysDown[i] = false;
                        }
                    }
                }
            }
            
            // Modifiers (Control, Shift, Alt, Super) are handled by mapped keys above (ImGui tracks them).
            // We just ensure the mapping is correct.
        }

        private unsafe void ProcessTextInput(int vkCode)
        {
            // Convert to Unicode
            // ToUnicode needs the virtual key, scan code, and current keyboard state
            uint scanCode = MapVirtualKey((uint)vkCode, 0); // MAPVK_VK_TO_VSC
            
            // Buffer for output chars
            // ToUnicode can return multiple chars (e.g. dead keys)
            // 2 chars is usually enough
            char* fileBuffer = stackalloc char[16]; 
            
            // ToUnicode modifies keyboard state! (consumes dead keys). 
            // We pass our captured state buffer.
            int result = ToUnicode((uint)vkCode, scanCode, _keyStateBuffer, fileBuffer, 16, 0);
            
            if (result > 0)
            {
                for (int i = 0; i < result; i++)
                {
                    // Filter control characters? ImGui filters them mostly.
                    _io.AddInputCharacter(fileBuffer[i]);
                }
            }
        }

        private static ImGuiKey MapVirtualKeyToImGuiKey(int vk)
        {
            // Manual mapping of VK (0-254) to ImGuiKey
            // This is tedious but standard
            if (vk >= '0' && vk <= '9') return ImGuiKey.Key0 + (vk - '0');
            if (vk >= 'A' && vk <= 'Z') return ImGuiKey.A + (vk - 'A');
            if (vk >= VK_F1 && vk <= VK_F12) return ImGuiKey.F1 + (vk - VK_F1);

            switch (vk)
            {
                case VK_TAB: return ImGuiKey.Tab;
                case VK_LEFT: return ImGuiKey.LeftArrow;
                case VK_RIGHT: return ImGuiKey.RightArrow;
                case VK_UP: return ImGuiKey.UpArrow;
                case VK_DOWN: return ImGuiKey.DownArrow;
                case VK_PRIOR: return ImGuiKey.PageUp;
                case VK_NEXT: return ImGuiKey.PageDown;
                case VK_HOME: return ImGuiKey.Home;
                case VK_END: return ImGuiKey.End;
                case VK_INSERT: return ImGuiKey.Insert;
                case VK_DELETE: return ImGuiKey.Delete;
                case VK_BACK: return ImGuiKey.Backspace;
                case VK_SPACE: return ImGuiKey.Space;
                case VK_RETURN: return ImGuiKey.Enter;
                case VK_ESCAPE: return ImGuiKey.Escape;
                case VK_OEM_7: return ImGuiKey.Apostrophe;
                case VK_OEM_COMMA: return ImGuiKey.Comma;
                case VK_OEM_MINUS: return ImGuiKey.Minus;
                case VK_OEM_PERIOD: return ImGuiKey.Period;
                case VK_OEM_2: return ImGuiKey.Slash;
                case VK_OEM_1: return ImGuiKey.Semicolon;
                case VK_OEM_PLUS: return ImGuiKey.Equal;
                case VK_OEM_4: return ImGuiKey.LeftBracket; // [
                case VK_OEM_5: return ImGuiKey.Backslash; // \
                case VK_OEM_6: return ImGuiKey.RightBracket; // ]
                case VK_OEM_3: return ImGuiKey.GraveAccent; // `
                case VK_CAPITAL: return ImGuiKey.CapsLock;
                case VK_SCROLL: return ImGuiKey.ScrollLock;
                case VK_NUMLOCK: return ImGuiKey.NumLock;
                case VK_SNAPSHOT: return ImGuiKey.PrintScreen;
                case VK_PAUSE: return ImGuiKey.Pause;
                case VK_NUMPAD0: return ImGuiKey.Keypad0;
                case VK_NUMPAD1: return ImGuiKey.Keypad1;
                case VK_NUMPAD2: return ImGuiKey.Keypad2;
                case VK_NUMPAD3: return ImGuiKey.Keypad3;
                case VK_NUMPAD4: return ImGuiKey.Keypad4;
                case VK_NUMPAD5: return ImGuiKey.Keypad5;
                case VK_NUMPAD6: return ImGuiKey.Keypad6;
                case VK_NUMPAD7: return ImGuiKey.Keypad7;
                case VK_NUMPAD8: return ImGuiKey.Keypad8;
                case VK_NUMPAD9: return ImGuiKey.Keypad9;
                case VK_DECIMAL: return ImGuiKey.KeypadDecimal;
                case VK_DIVIDE: return ImGuiKey.KeypadDivide;
                case VK_MULTIPLY: return ImGuiKey.KeypadMultiply;
                case VK_SUBTRACT: return ImGuiKey.KeypadSubtract;
                case VK_ADD: return ImGuiKey.KeypadAdd;
                case VK_LSHIFT: return ImGuiKey.LeftShift;
                case VK_LCONTROL: return ImGuiKey.LeftCtrl;
                case VK_LMENU: return ImGuiKey.LeftAlt;
                case VK_LWIN: return ImGuiKey.LeftSuper;
                case VK_RSHIFT: return ImGuiKey.RightShift;
                case VK_RCONTROL: return ImGuiKey.RightCtrl;
                case VK_RMENU: return ImGuiKey.RightAlt;
                case VK_RWIN: return ImGuiKey.RightSuper;
                // Add more as needed
            }
            return ImGuiKey.None;
        }

        // --- Native Constants & Import ---
        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;
        private const int VK_MBUTTON = 0x04;
        private const int VK_XBUTTON1 = 0x05;
        private const int VK_XBUTTON2 = 0x06;
        private const int VK_TAB = 0x09;
        private const int VK_RETURN = 0x0D;
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12; // Alt
        private const int VK_PAUSE = 0x13;
        private const int VK_CAPITAL = 0x14;
        private const int VK_ESCAPE = 0x1B;
        private const int VK_SPACE = 0x20;
        private const int VK_PRIOR = 0x21; // PageUp
        private const int VK_NEXT = 0x22; // PageDown
        private const int VK_END = 0x23;
        private const int VK_HOME = 0x24;
        private const int VK_LEFT = 0x25;
        private const int VK_UP = 0x26;
        private const int VK_RIGHT = 0x27;
        private const int VK_DOWN = 0x28;
        private const int VK_SNAPSHOT = 0x2C; // PrintScreen
        private const int VK_INSERT = 0x2D;
        private const int VK_DELETE = 0x2E;
        // 0-9 and A-Z are same as ASCII
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
        private const int VK_NUMPAD0 = 0x60;
        private const int VK_NUMPAD1 = 0x61;
        private const int VK_NUMPAD2 = 0x62;
        private const int VK_NUMPAD3 = 0x63;
        private const int VK_NUMPAD4 = 0x64;
        private const int VK_NUMPAD5 = 0x65;
        private const int VK_NUMPAD6 = 0x66;
        private const int VK_NUMPAD7 = 0x67;
        private const int VK_NUMPAD8 = 0x68;
        private const int VK_NUMPAD9 = 0x69;
        private const int VK_MULTIPLY = 0x6A;
        private const int VK_ADD = 0x6B;
        private const int VK_SEPARATOR = 0x6C;
        private const int VK_SUBTRACT = 0x6D;
        private const int VK_DECIMAL = 0x6E;
        private const int VK_DIVIDE = 0x6F;
        private const int VK_F1 = 0x70;
        private const int VK_F12 = 0x7B;
        private const int VK_NUMLOCK = 0x90;
        private const int VK_SCROLL = 0x91;
        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;
        private const int VK_LCONTROL = 0xA2;
        private const int VK_RCONTROL = 0xA3;
        private const int VK_LMENU = 0xA4;
        private const int VK_RMENU = 0xA5;
        private const int VK_OEM_1 = 0xBA; // ; :
        private const int VK_OEM_PLUS = 0xBB; // +
        private const int VK_OEM_COMMA = 0xBC; // ,
        private const int VK_OEM_MINUS = 0xBD; // -
        private const int VK_OEM_PERIOD = 0xBE; // .
        private const int VK_OEM_2 = 0xBF; // / ?
        private const int VK_OEM_3 = 0xC0; // ` ~
        private const int VK_OEM_4 = 0xDB; // [ {
        private const int VK_OEM_5 = 0xDC; // \ |
        private const int VK_OEM_6 = 0xDD; // ] }
        private const int VK_OEM_7 = 0xDE; // ' "
        private const int VK_BACK = 0x08;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern bool GetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff, uint wFlags);

        [DllImport("user32.dll")]
        private static extern unsafe int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState, char* pwszBuff, int cchBuff, uint wFlags);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }
    }
}
