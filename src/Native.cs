using System;
using System.Runtime.InteropServices;

namespace MouseSwitch
{
    internal static class Native
    {
        // ---- hook ids ----
        public const int WH_MOUSE_LL = 14;
        public const int WH_KEYBOARD_LL = 13;

        // ---- window messages ----
        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_MBUTTONUP = 0x0208;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_MOUSEHWHEEL = 0x020E;
        public const int WM_XBUTTONDOWN = 0x020B;
        public const int WM_XBUTTONUP = 0x020C;

        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        public const uint LLKHF_EXTENDED = 0x01;

        // ---- SendInput ----
        public const uint INPUT_MOUSE = 0;
        public const uint INPUT_KEYBOARD = 1;

        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        public const uint MOUSEEVENTF_XDOWN = 0x0080;
        public const uint MOUSEEVENTF_XUP = 0x0100;
        public const uint MOUSEEVENTF_WHEEL = 0x0800;
        public const uint MOUSEEVENTF_HWHEEL = 0x1000;
        public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
        public const uint MOUSEEVENTF_MOVE_NOCOALESCE = 0x2000;

        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_SCANCODE = 0x0008;

        public const uint XBUTTON1 = 0x0001;
        public const uint XBUTTON2 = 0x0002;

        // Tag placed in dwExtraInfo of every event we inject, so our own
        // hooks can recognise and ignore them (prevents feedback loops).
        public static readonly IntPtr MARKER = new IntPtr(0x4D535731); // "MSW1"

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct INPUTUNION
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        // ---- raw input ----
        public const int WM_INPUT = 0x00FF;
        public const uint RID_INPUT = 0x10000003;
        public const uint RIM_TYPEMOUSE = 0;
        public const uint RIDEV_INPUTSINK = 0x00000100;
        public const uint RIDEV_REMOVE = 0x00000001;
        public const ushort MOUSE_MOVE_ABSOLUTE = 0x0001;
        public const int HWND_MESSAGE = -3;

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices,
                                                          uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData,
                                                  ref uint pcbSize, uint cbSizeHeader);

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12;
        public const int VK_SHIFT = 0x10;
        public const int VK_LCONTROL = 0xA2;
        public const int VK_RCONTROL = 0xA3;
        public const int VK_LMENU = 0xA4;
        public const int VK_RMENU = 0xA5;
        public const int VK_LSHIFT = 0xA0;
        public const int VK_RSHIFT = 0xA1;

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        public static extern uint TimeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        public static extern uint TimeEndPeriod(uint uMilliseconds);

        [DllImport("kernel32.dll")]
        public static extern uint SetThreadExecutionState(uint esFlags);

        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;

        /// <summary>
        /// Builds one keyboard event, by scan code when there is one (that is
        /// what games reading raw input or DirectInput look at).
        ///
        /// Shift is special. The low level hook reports Right Shift (scan 0x36)
        /// with the "extended" flag, but E0 36 and E0 2A are the "fake shift"
        /// codes a keyboard emits around Num Lock, and Windows deliberately
        /// throws them away. Replaying the flag as-is made Right Shift vanish
        /// on the other PC, so it is always dropped for the two shift keys.
        /// </summary>
        public static INPUT MakeKey(ushort vk, ushort scan, bool extended, bool up)
        {
            if (scan == 0x2A || scan == 0x36) extended = false;

            INPUT input = new INPUT();
            input.type = INPUT_KEYBOARD;
            uint flags = 0;
            if (scan != 0)
            {
                flags |= KEYEVENTF_SCANCODE;
                input.u.ki.wVk = 0;
                input.u.ki.wScan = scan;
            }
            else
            {
                input.u.ki.wVk = vk;
            }
            if (extended) flags |= KEYEVENTF_EXTENDEDKEY;
            if (up) flags |= KEYEVENTF_KEYUP;
            input.u.ki.dwFlags = flags;
            input.u.ki.dwExtraInfo = MARKER;
            return input;
        }

        /// <summary>button: 1 left, 2 right, 3 middle, 4 X1, 5 X2 (same numbering as the protocol)</summary>
        public static INPUT MakeButton(byte button, bool down)
        {
            INPUT input = new INPUT();
            input.type = INPUT_MOUSE;
            switch (button)
            {
                case 1: input.u.mi.dwFlags = down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP; break;
                case 2: input.u.mi.dwFlags = down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP; break;
                case 3: input.u.mi.dwFlags = down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP; break;
                case 4: input.u.mi.dwFlags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; input.u.mi.mouseData = XBUTTON1; break;
                case 5: input.u.mi.dwFlags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; input.u.mi.mouseData = XBUTTON2; break;
            }
            input.u.mi.dwExtraInfo = MARKER;
            return input;
        }

        public static int HiWordSigned(uint value)
        {
            return (short)((value >> 16) & 0xFFFF);
        }

        public static uint HiWord(uint value)
        {
            return (value >> 16) & 0xFFFF;
        }
    }
}
