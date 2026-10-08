using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MouseSwitch
{
    /// <summary>
    /// Runs on the PC the mouse is plugged into. Hooks the low level input,
    /// swallows it while the remote PC has control, and streams it over the
    /// encrypted link.
    /// </summary>
    internal sealed class HostSide : IDisposable
    {
        private struct Ev
        {
            public byte op;
            public int a, b, c, d;
        }

        private readonly Config _cfg;

        private IntPtr _mouseHook = IntPtr.Zero;
        private IntPtr _keyHook = IntPtr.Zero;
        private Native.HookProc _mouseProc;   // kept in a field so the GC cannot collect it
        private Native.HookProc _keyProc;

        private volatile bool _remote;
        private Native.POINT _anchor;
        private Native.POINT _savedPos;

        private List<Ev> _q = new List<Ev>(512);
        private List<Ev> _spare = new List<Ev>(512);
        private readonly object _qLock = new object();
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);

        private volatile Link _link;
        private volatile bool _running;
        private Thread _netThread;
        private Thread _senderThread;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private readonly ChordDetector _chord = new ChordDetector();
        private readonly HeldTracker _held = new HeldTracker();
        private int _lastToggleTick = Environment.TickCount - 10000;

        // Raw input gives the mouse's unfiltered movement. The low level hook
        // only reports where Windows *would* put the cursor, which already has
        // pointer acceleration baked in; feeding that to the other PC applies
        // acceleration twice and loses small movements, which games notice.
        private RawInputWindow _rawWindow;
        private IntPtr _rawBuffer = IntPtr.Zero;
        private uint _rawBufferSize;
        private int _rawDx, _rawDy;
        private readonly object _rawLock = new object();
        private volatile bool _useRaw;

        public event Action<string> Status;
        public event Action<bool> RemoteChanged;
        public event Action KeyboardToggleRequested;

        public int LatencyMs = -1;
        public bool IsConnected { get { return _link != null; } }
        public bool IsRemote { get { return _remote; } }

        public HostSide(Config cfg)
        {
            _cfg = cfg;
        }

        // ------------------------------------------------------------------
        // lifecycle
        // ------------------------------------------------------------------

        public void Start()
        {
            if (_running) return;
            _running = true;

            IntPtr mod = Native.GetModuleHandle(null);
            _mouseProc = MouseProc;
            _keyProc = KeyProc;
            _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, mod, 0);
            _keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _keyProc, mod, 0);
            if (_mouseHook == IntPtr.Zero || _keyHook == IntPtr.Zero)
                Report("입력 후킹에 실패했습니다");

            StartRawInput();

            _senderThread = new Thread(SenderLoop);
            _senderThread.IsBackground = true;
            _senderThread.Priority = ThreadPriority.AboveNormal;
            _senderThread.Start();

            _netThread = new Thread(NetLoop);
            _netThread.IsBackground = true;
            _netThread.Start();
        }

        public void Dispose()
        {
            _running = false;
            ForceLocal();
            _signal.Set();
            try { if (_mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_mouseHook); }
            catch { }
            try { if (_keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_keyHook); }
            catch { }
            _mouseHook = IntPtr.Zero;
            _keyHook = IntPtr.Zero;
            StopRawInput();
            Link l = _link;
            _link = null;
            if (l != null) l.Dispose();
        }

        private void Report(string s)
        {
            LogAsync("[host] " + s);
            Action<string> h = Status;
            if (h != null) h(s);
        }

        /// <summary>Writing the log file takes milliseconds; never do that inside a hook.</summary>
        private static void LogAsync(string message)
        {
            ThreadPool.QueueUserWorkItem(delegate { Config.Log(message); });
        }

        // ------------------------------------------------------------------
        // switching
        // ------------------------------------------------------------------

        public void Toggle()
        {
            Toggle("트레이 메뉴");
        }

        private void Toggle(string cause)
        {
            int now = Environment.TickCount;
            if (unchecked(now - _lastToggleTick) < 300)
            {
                // A worn wheel switch can "chatter" and report two clicks for one press.
                LogAsync("[host] 전환 무시 (0.3초 안에 다시 들어옴): " + cause);
                return;
            }
            _lastToggleTick = now;
            LogAsync("[host] 전환: " + cause + (_remote ? " -> 이 PC로" : " -> 상대 PC로"));
            SetRemote(!_remote);
        }

        public void ForceLocal()
        {
            if (_remote) SetRemote(false);
        }

        private void SetRemote(bool on)
        {
            if (on == _remote) return;

            if (on)
            {
                if (_link == null)
                {
                    Report("아직 상대 PC에 연결되지 않았습니다");
                    PlayTone(300, 120);
                    return;
                }
                Native.GetCursorPos(out _savedPos);
                int cx = Native.GetSystemMetrics(Native.SM_CXSCREEN) / 2;
                int cy = Native.GetSystemMetrics(Native.SM_CYSCREEN) / 2;
                Native.SetCursorPos(cx, cy);
                // Read the position back rather than trusting our own numbers:
                // DPI scaling and multi monitor layouts can shift it.
                Native.POINT actual;
                if (Native.GetCursorPos(out actual)) _anchor = actual;
                else { _anchor.x = cx; _anchor.y = cy; }
                _remote = true;
                // Mouse (and keyboard, if shared) now go out: whatever is held
                // here must be released here, or it stays pressed on this PC.
                HandOffHeld(_cfg.ShareKeyboard, true, true, true);
                Enqueue(Op.Config, _cfg.ShareKeyboard ? 1 : 0, 0, 0, 0);
                Enqueue(Op.Enter, 0, 0, 0, 0);
                PlayTone(1180, 55);
            }
            else
            {
                _remote = false;
                // The other PC lets go of everything (ReleaseAll); here we just
                // make sure the remaining repeats / releases do not leak in.
                HandOffHeld(_cfg.ShareKeyboard, false, true, false);
                Enqueue(Op.ReleaseAll, 0, 0, 0, 0);
                Enqueue(Op.Leave, 0, 0, 0, 0);
                Native.SetCursorPos(_savedPos.x, _savedPos.y);
                PlayTone(620, 55);
            }

            Action<bool> h = RemoteChanged;
            if (h != null) h(_remote);
        }

        private void PlayTone(int freq, int ms)
        {
            PlayTones(freq, ms, 1);
        }

        private void PlayTones(int freq, int ms, int count)
        {
            if (!_cfg.Sound) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (i > 0) Thread.Sleep(35);
                        Console.Beep(freq, ms);
                    }
                }
                catch { }
            });
        }

        /// <summary>Called after ShareKeyboard changes.</summary>
        public void OnShareKeyboardChanged()
        {
            if (!_remote) return;
            if (_cfg.ShareKeyboard)
            {
                // Keys held down went to this PC; let go of them here.
                HandOffHeld(true, true, false, false);
            }
            else
            {
                // Keys held down went to the other PC; let go of them there.
                HandOffHeld(true, false, false, false);
                Enqueue(Op.ReleaseAll, 0, 0, 0, 0);
            }
            Enqueue(Op.Config, _cfg.ShareKeyboard ? 1 : 0, 0, 0, 0);
        }


        /// <summary>
        /// Called whenever keyboard and/or mouse change destination.
        /// keysLeftHere / mouseLeftHere: true when they used to go to this PC,
        /// in which case this PC is sent the releases it will otherwise never get.
        /// Everything held is marked orphaned either way.
        /// </summary>
        private void HandOffHeld(bool keysMoved, bool keysLeftHere, bool mouseMoved, bool mouseLeftHere)
        {
            List<int> keyUps = new List<int>();
            List<byte> buttonUps = new List<byte>();
            _held.HandOff(keysMoved, keysLeftHere, mouseMoved, mouseLeftHere, keyUps, buttonUps);
            if (keysMoved && keysLeftHere || buttonUps.Count > 0)
                InjectLocalReleases(keyUps, buttonUps, keysMoved && keysLeftHere);
        }

        // L/R Shift, L/R Ctrl, L/R Alt, L/R Win
        private static readonly int[] LocalModifierVks = { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C };

        private static void InjectLocalReleases(List<int> keyUps, List<byte> buttonUps, bool checkModifiers)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    List<Native.INPUT> list = new List<Native.INPUT>();
                    HashSet<int> done = new HashSet<int>();
                    for (int i = 0; i + 1 < keyUps.Count; i += 2)
                    {
                        int vk = keyUps[i];
                        int code = keyUps[i + 1];
                        list.Add(Native.MakeKey((ushort)vk, (ushort)(code & 0xFFFF), (code & 0x10000) != 0, true));
                        done.Add(vk);
                    }
                    if (checkModifiers)
                    {
                        // Belt and braces for modifiers pressed before we started watching.
                        foreach (int vk in LocalModifierVks)
                        {
                            if (done.Contains(vk)) continue;
                            if ((Native.GetAsyncKeyState(vk) & 0x8000) == 0) continue;
                            bool ext = vk == 0xA3 || vk == 0xA5 || vk == 0x5B || vk == 0x5C;
                            list.Add(Native.MakeKey((ushort)vk, 0, ext, true));
                        }
                    }
                    foreach (byte b in buttonUps) list.Add(Native.MakeButton(b, false));
                    if (list.Count > 0)
                        Native.SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf(typeof(Native.INPUT)));
                }
                catch { }
            });
        }

        // ------------------------------------------------------------------
        // hooks
        // ------------------------------------------------------------------

        private static int ExtraInfoOffsetMouse
        {
            get { return IntPtr.Size == 8 ? 24 : 20; }
        }

        private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0) return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);

            IntPtr extra = Marshal.ReadIntPtr(lParam, ExtraInfoOffsetMouse);
            if (extra == Native.MARKER) return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);

            int msg = (int)wParam;
            uint mouseData = unchecked((uint)Marshal.ReadInt32(lParam, 8));

            // ---- the switch button ----
            bool triggerDown = false;
            bool triggerUp = false;
            if (_cfg.Trigger == SwitchTrigger.MiddleButton)
            {
                if (msg == Native.WM_MBUTTONDOWN) triggerDown = true;
                else if (msg == Native.WM_MBUTTONUP) triggerUp = true;
            }
            else if (_cfg.Trigger == SwitchTrigger.SideButton1 || _cfg.Trigger == SwitchTrigger.SideButton2)
            {
                uint want = _cfg.Trigger == SwitchTrigger.SideButton1 ? Native.XBUTTON1 : Native.XBUTTON2;
                if (msg == Native.WM_XBUTTONDOWN && Native.HiWord(mouseData) == want) triggerDown = true;
                else if (msg == Native.WM_XBUTTONUP && Native.HiWord(mouseData) == want) triggerUp = true;
            }

            if (triggerDown)
            {
                Toggle(_cfg.Trigger == SwitchTrigger.MiddleButton ? "휠 클릭" : "옆 버튼");
                return (IntPtr)1;
            }
            if (triggerUp)
            {
                return (IntPtr)1;
            }

            byte button = 0;
            bool buttonDown = false;
            switch (msg)
            {
                case Native.WM_LBUTTONDOWN: button = Btn.Left; buttonDown = true; break;
                case Native.WM_LBUTTONUP: button = Btn.Left; break;
                case Native.WM_RBUTTONDOWN: button = Btn.Right; buttonDown = true; break;
                case Native.WM_RBUTTONUP: button = Btn.Right; break;
                case Native.WM_MBUTTONDOWN: button = Btn.Middle; buttonDown = true; break;
                case Native.WM_MBUTTONUP: button = Btn.Middle; break;
                case Native.WM_XBUTTONDOWN:
                    button = Native.HiWord(mouseData) == Native.XBUTTON1 ? Btn.X1 : Btn.X2;
                    buttonDown = true;
                    break;
                case Native.WM_XBUTTONUP:
                    button = Native.HiWord(mouseData) == Native.XBUTTON1 ? Btn.X1 : Btn.X2;
                    break;
            }
            if (button != 0 && _held.OnButton(button, buttonDown)) return (IntPtr)1;

            if (!_remote) return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);

            switch (msg)
            {
                case Native.WM_MOUSEMOVE:
                    {
                        if (_useRaw) break; // movement comes from WM_INPUT instead
                        int x = Marshal.ReadInt32(lParam, 0);
                        int y = Marshal.ReadInt32(lParam, 4);
                        int dx = x - _anchor.x;
                        int dy = y - _anchor.y;
                        if (dx != 0 || dy != 0) EnqueueMove(dx, dy);
                        break;
                    }
                case Native.WM_LBUTTONDOWN: Enqueue(Op.Button, Btn.Left, 1, 0, 0); break;
                case Native.WM_LBUTTONUP: Enqueue(Op.Button, Btn.Left, 0, 0, 0); break;
                case Native.WM_RBUTTONDOWN: Enqueue(Op.Button, Btn.Right, 1, 0, 0); break;
                case Native.WM_RBUTTONUP: Enqueue(Op.Button, Btn.Right, 0, 0, 0); break;
                case Native.WM_MBUTTONDOWN: Enqueue(Op.Button, Btn.Middle, 1, 0, 0); break;
                case Native.WM_MBUTTONUP: Enqueue(Op.Button, Btn.Middle, 0, 0, 0); break;
                case Native.WM_XBUTTONDOWN:
                    Enqueue(Op.Button, Native.HiWord(mouseData) == Native.XBUTTON1 ? Btn.X1 : Btn.X2, 1, 0, 0);
                    break;
                case Native.WM_XBUTTONUP:
                    Enqueue(Op.Button, Native.HiWord(mouseData) == Native.XBUTTON1 ? Btn.X1 : Btn.X2, 0, 0, 0);
                    break;
                case Native.WM_MOUSEWHEEL:
                    Enqueue(Op.Wheel, Native.HiWordSigned(mouseData), 0, 0, 0);
                    break;
                case Native.WM_MOUSEHWHEEL:
                    Enqueue(Op.Wheel, Native.HiWordSigned(mouseData), 1, 0, 0);
                    break;
            }
            return (IntPtr)1;
        }

        /// <summary>Whether Ctrl and Alt are really held. See ChordDetector.</summary>
        private bool CtrlAltHeld()
        {
            int now = Environment.TickCount;
            if (_remote && _cfg.ShareKeyboard)
            {
                // Keys are being swallowed and forwarded, so Windows never sees
                // them and its key state means nothing; rely on our own tracking.
                return _chord.CtrlAltHeld(now, null, null);
            }
            bool sysCtrl = (Native.GetAsyncKeyState(Native.VK_CONTROL) & 0x8000) != 0;
            bool sysAlt = (Native.GetAsyncKeyState(Native.VK_MENU) & 0x8000) != 0;
            return _chord.CtrlAltHeld(now, sysCtrl, sysAlt);
        }

        private IntPtr KeyProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0) return Native.CallNextHookEx(_keyHook, nCode, wParam, lParam);

            IntPtr extra = Marshal.ReadIntPtr(lParam, 16);
            if (extra == Native.MARKER) return Native.CallNextHookEx(_keyHook, nCode, wParam, lParam);

            int msg = (int)wParam;
            bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
            int vk = Marshal.ReadInt32(lParam, 0);
            int scan = Marshal.ReadInt32(lParam, 4);
            uint flags = unchecked((uint)Marshal.ReadInt32(lParam, 8));
            bool extended = (flags & Native.LLKHF_EXTENDED) != 0;
            // Right Shift arrives flagged "extended"; E0 36 is a fake shift that
            // Windows ignores, so the other PC would never see it. See MakeKey.
            if (scan == 0x2A || scan == 0x36) extended = false;

            _chord.OnKey(vk, down, Environment.TickCount);
            if (_held.OnKey(vk, scan | (extended ? 0x10000 : 0), down)) return (IntPtr)1;

            // Ctrl+Alt+S always works locally, even while the keyboard is being
            // forwarded, so there is a way back if the mouse button fails.
            if (down && vk == 0x53 && CtrlAltHeld())
            {
                // The S itself was swallowed; its repeats and release must be too,
                // or holding the chord a moment too long switches back again.
                _held.OrphanKey(vk);
                Toggle("Ctrl+Alt+S");
                return (IntPtr)1;
            }

            // Ctrl+Alt+K turns keyboard forwarding on or off, from either PC.
            // The actual change (and the settings file write) happens on the
            // UI thread afterwards; nothing slow may run inside a hook.
            if (down && vk == 0x4B && CtrlAltHeld())
            {
                LogAsync("[host] 키보드 넘기기 전환: Ctrl+Alt+K");
                _held.OrphanKey(vk);
                if (_cfg.ShareKeyboard) PlayTones(520, 40, 2);   // turning off
                else PlayTones(1000, 40, 2);                     // turning on
                Action h = KeyboardToggleRequested;
                if (h != null) h();
                return (IntPtr)1;
            }

            if (!_remote || !_cfg.ShareKeyboard)
                return Native.CallNextHookEx(_keyHook, nCode, wParam, lParam);

            Enqueue(Op.Key, vk, scan, down ? 1 : 0, extended ? 1 : 0);
            return (IntPtr)1;
        }

        // ------------------------------------------------------------------
        // queue
        // ------------------------------------------------------------------

        private void EnqueueMove(int dx, int dy)
        {
            lock (_qLock)
            {
                int n = _q.Count;
                if (n > 0 && _q[n - 1].op == Op.Move)
                {
                    Ev last = _q[n - 1];
                    last.a += dx;
                    last.b += dy;
                    _q[n - 1] = last;
                    return;
                }
                if (n > 4000) return;
                Ev e = new Ev();
                e.op = Op.Move;
                e.a = dx;
                e.b = dy;
                _q.Add(e);
            }
        }

        private void Enqueue(byte op, int a, int b, int c, int d)
        {
            lock (_qLock)
            {
                if (_q.Count > 4000) return;
                Ev e = new Ev();
                e.op = op;
                e.a = a;
                e.b = b;
                e.c = c;
                e.d = d;
                _q.Add(e);
            }
            _signal.Set();
        }

        // ------------------------------------------------------------------
        // sender
        // ------------------------------------------------------------------

        private void SenderLoop()
        {
            long lastPing = 0;
            PacketWriter w = new PacketWriter();

            while (_running)
            {
                int wait = _cfg.CoalesceMs;
                if (wait < 1) wait = 1;
                _signal.WaitOne(wait);

                List<Ev> batch;
                lock (_qLock)
                {
                    batch = _q;
                    _q = _spare;
                    _spare = batch;
                    _q.Clear();
                }

                int rdx = 0, rdy = 0;
                if (_useRaw)
                {
                    lock (_rawLock)
                    {
                        rdx = _rawDx;
                        rdy = _rawDy;
                        _rawDx = 0;
                        _rawDy = 0;
                    }
                }

                Link link = _link;
                if (link == null)
                {
                    batch.Clear();
                    continue;
                }

                try
                {
                    w.Reset();
                    if (rdx != 0 || rdy != 0) w.Move(rdx, rdy);
                    for (int i = 0; i < batch.Count; i++)
                    {
                        Ev e = batch[i];
                        switch (e.op)
                        {
                            case Op.Move: w.Move(e.a, e.b); break;
                            case Op.Button: w.Button((byte)e.a, e.b != 0); break;
                            case Op.Wheel: w.Wheel(e.a, e.b != 0); break;
                            case Op.Key: w.Key((ushort)e.a, (ushort)e.b, e.c != 0, e.d != 0); break;
                            case Op.Config: w.Config(e.a != 0); break;
                            default: w.Simple(e.op); break;
                        }
                    }
                    batch.Clear();

                    long now = _clock.ElapsedMilliseconds;
                    if (now - lastPing > 1000)
                    {
                        lastPing = now;
                        w.Stamp(Op.Ping, now);
                    }

                    if (w.Length > 0) link.Send(w.ToArray());
                }
                catch (Exception ex)
                {
                    Config.Log("[host] send failed: " + ex.Message);
                    DropLink();
                }

                if (_remote)
                {
                    Native.POINT p;
                    if (Native.GetCursorPos(out p) && (p.x != _anchor.x || p.y != _anchor.y))
                        Native.SetCursorPos(_anchor.x, _anchor.y);
                }
            }
        }

        private void DropLink()
        {
            Link l = _link;
            _link = null;
            LatencyMs = -1;
            if (l != null) l.Dispose();
            if (_remote)
            {
                LogAsync("[host] 연결이 끊겨서 이 PC로 돌아옴");
                SetRemote(false);
            }
        }

        // ------------------------------------------------------------------
        // connection
        // ------------------------------------------------------------------

        private void NetLoop()
        {
            while (_running)
            {
                if (string.IsNullOrEmpty(_cfg.PeerAddress) || !Config.HasKey)
                {
                    Report(Config.HasKey ? "상대 PC 주소가 설정되지 않았습니다" : "페어링이 필요합니다");
                    Thread.Sleep(3000);
                    continue;
                }

                TcpClient tcp = null;
                try
                {
                    Report("연결 중...");
                    tcp = new TcpClient();
                    IAsyncResult ar = tcp.BeginConnect(_cfg.PeerAddress, _cfg.Port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(4000))
                        throw new Exception("응답 없음");
                    tcp.EndConnect(ar);

                    byte[] ltk = Config.LoadKey();
                    if (ltk == null) throw new Exception("저장된 페어링 키가 없습니다");

                    Link link = Link.Handshake(tcp, true, false, ltk, null);
                    _link = link;
                    Report("연결됨 - " + _cfg.PeerAddress);
                    Enqueue(Op.Config, _cfg.ShareKeyboard ? 1 : 0, 0, 0, 0);

                    ReaderLoop(link);
                }
                catch (HandshakeRejected hr)
                {
                    Report(hr.Message);
                    if (tcp != null) try { tcp.Close(); } catch { }
                    Thread.Sleep(5000);
                }
                catch (Exception ex)
                {
                    Config.Log("[host] connect failed: " + ex.Message);
                    Report("연결 대기 중");
                    if (tcp != null) try { tcp.Close(); } catch { }
                }

                DropLink();
                if (_running) Thread.Sleep(2500);
            }
        }

        private void ReaderLoop(Link link)
        {
            while (_running && _link == link)
            {
                byte[] payload = link.Receive();
                PacketReader r = new PacketReader(payload);
                while (r.HasMore)
                {
                    byte op = r.NextOp();
                    if (op == Op.Pong)
                    {
                        long sent = r.I64();
                        LatencyMs = (int)(_clock.ElapsedMilliseconds - sent);
                    }
                    else if (op == Op.Ping)
                    {
                        r.I64();
                    }
                    else if (op == Op.Bye)
                    {
                        throw new Exception("상대가 연결을 종료했습니다");
                    }
                    else
                    {
                        return; // unexpected, resynchronise by reconnecting
                    }
                }
            }
        }
        // ------------------------------------------------------------------
        // raw input
        // ------------------------------------------------------------------

        private void StartRawInput()
        {
            try
            {
                _rawWindow = new RawInputWindow(this);
                Native.RAWINPUTDEVICE[] dev = new Native.RAWINPUTDEVICE[1];
                dev[0].usUsagePage = 0x01;   // generic desktop
                dev[0].usUsage = 0x02;       // mouse
                dev[0].dwFlags = Native.RIDEV_INPUTSINK;
                dev[0].hwndTarget = _rawWindow.Handle;
                _useRaw = Native.RegisterRawInputDevices(
                    dev, 1, (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE)));
                Config.Log("[host] raw input " + (_useRaw ? "on" : "unavailable, using hook coordinates"));
            }
            catch (Exception ex)
            {
                _useRaw = false;
                Config.Log("[host] raw input failed: " + ex.Message);
            }
        }

        private void StopRawInput()
        {
            try
            {
                if (_useRaw)
                {
                    Native.RAWINPUTDEVICE[] dev = new Native.RAWINPUTDEVICE[1];
                    dev[0].usUsagePage = 0x01;
                    dev[0].usUsage = 0x02;
                    dev[0].dwFlags = Native.RIDEV_REMOVE;
                    dev[0].hwndTarget = IntPtr.Zero;
                    Native.RegisterRawInputDevices(
                        dev, 1, (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE)));
                }
                _useRaw = false;
                if (_rawWindow != null)
                {
                    _rawWindow.DestroyHandle();
                    _rawWindow = null;
                }
                if (_rawBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_rawBuffer);
                    _rawBuffer = IntPtr.Zero;
                    _rawBufferSize = 0;
                }
            }
            catch
            {
            }
        }

        private void OnRawInput(IntPtr hRawInput)
        {
            if (!_remote) return;
            try
            {
                uint headerSize = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTHEADER));
                uint size = 0;
                if (Native.GetRawInputData(hRawInput, Native.RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0)
                    return;
                if (size == 0) return;

                if (_rawBuffer == IntPtr.Zero || _rawBufferSize < size)
                {
                    if (_rawBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_rawBuffer);
                    _rawBuffer = Marshal.AllocHGlobal((int)size);
                    _rawBufferSize = size;
                }

                uint got = _rawBufferSize;
                if (Native.GetRawInputData(hRawInput, Native.RID_INPUT, _rawBuffer, ref got, headerSize) == uint.MaxValue)
                    return;

                if ((uint)Marshal.ReadInt32(_rawBuffer, 0) != Native.RIM_TYPEMOUSE) return;

                int data = (int)headerSize;
                ushort flags = (ushort)Marshal.ReadInt16(_rawBuffer, data);
                if ((flags & Native.MOUSE_MOVE_ABSOLUTE) != 0) return; // tablet / remote desktop

                int dx = Marshal.ReadInt32(_rawBuffer, data + 12);
                int dy = Marshal.ReadInt32(_rawBuffer, data + 16);
                if (dx == 0 && dy == 0) return;

                lock (_rawLock)
                {
                    _rawDx += dx;
                    _rawDy += dy;
                }
            }
            catch
            {
            }
        }

        /// <summary>A message only window, created just to receive WM_INPUT.</summary>
        private sealed class RawInputWindow : NativeWindow
        {
            private readonly HostSide _owner;

            public RawInputWindow(HostSide owner)
            {
                _owner = owner;
                CreateParams cp = new CreateParams();
                cp.Caption = "MouseSwitchRawInput";
                cp.X = 0;
                cp.Y = 0;
                cp.Width = 0;
                cp.Height = 0;
                cp.Style = 0;
                cp.ExStyle = 0;
                cp.Parent = new IntPtr(Native.HWND_MESSAGE);
                CreateHandle(cp);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Native.WM_INPUT) _owner.OnRawInput(m.LParam);
                base.WndProc(ref m);
            }
        }
    }
}
