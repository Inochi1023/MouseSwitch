using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MouseSwitch
{
    internal static class Injector
    {
        private static readonly HashSet<int> HeldKeys = new HashSet<int>();
        private static readonly HashSet<byte> HeldButtons = new HashSet<byte>();
        private static readonly object Gate = new object();

        private static readonly int InputSize =
            System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.INPUT));
        private static bool _blocked;

        /// <summary>
        /// SendInput returns 0 when Windows refuses the injection. The usual
        /// reason is UIPI: a process at normal integrity cannot send input to
        /// an elevated foreground window, so the cursor appears to freeze the
        /// moment such a window is focused. Log it once so the cause is
        /// visible instead of looking like a hang.
        /// </summary>
        private static void Send(Native.INPUT[] inp)
        {
            uint sent = Native.SendInput((uint)inp.Length, inp, InputSize);
            if (sent == 0)
            {
                if (!_blocked)
                {
                    _blocked = true;
                    int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    Config.Log("[client] SendInput blocked (error " + err + ")" +
                               (err == 5 ? " - 앞에 있는 창이 관리자 권한입니다. MouseSwitch도 관리자로 실행하세요." : ""));
                }
            }
            else if (_blocked)
            {
                _blocked = false;
                Config.Log("[client] SendInput working again");
            }
        }

        public static void Move(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return;
            Native.INPUT[] inp = new Native.INPUT[1];
            inp[0].type = Native.INPUT_MOUSE;
            inp[0].u.mi.dx = dx;
            inp[0].u.mi.dy = dy;
            inp[0].u.mi.dwFlags = Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_MOVE_NOCOALESCE;
            inp[0].u.mi.dwExtraInfo = Native.MARKER;
            Send(inp);
        }

        public static void Button(byte button, bool down)
        {
            uint flags = 0;
            uint data = 0;
            switch (button)
            {
                case Btn.Left: flags = down ? Native.MOUSEEVENTF_LEFTDOWN : Native.MOUSEEVENTF_LEFTUP; break;
                case Btn.Right: flags = down ? Native.MOUSEEVENTF_RIGHTDOWN : Native.MOUSEEVENTF_RIGHTUP; break;
                case Btn.Middle: flags = down ? Native.MOUSEEVENTF_MIDDLEDOWN : Native.MOUSEEVENTF_MIDDLEUP; break;
                case Btn.X1: flags = down ? Native.MOUSEEVENTF_XDOWN : Native.MOUSEEVENTF_XUP; data = Native.XBUTTON1; break;
                case Btn.X2: flags = down ? Native.MOUSEEVENTF_XDOWN : Native.MOUSEEVENTF_XUP; data = Native.XBUTTON2; break;
                default: return;
            }

            lock (Gate)
            {
                if (down) HeldButtons.Add(button);
                else HeldButtons.Remove(button);
            }

            Native.INPUT[] inp = new Native.INPUT[1];
            inp[0].type = Native.INPUT_MOUSE;
            inp[0].u.mi.mouseData = data;
            inp[0].u.mi.dwFlags = flags;
            inp[0].u.mi.dwExtraInfo = Native.MARKER;
            Send(inp);
        }

        public static void Wheel(int delta, bool horizontal)
        {
            Native.INPUT[] inp = new Native.INPUT[1];
            inp[0].type = Native.INPUT_MOUSE;
            inp[0].u.mi.mouseData = unchecked((uint)delta);
            inp[0].u.mi.dwFlags = horizontal ? Native.MOUSEEVENTF_HWHEEL : Native.MOUSEEVENTF_WHEEL;
            inp[0].u.mi.dwExtraInfo = Native.MARKER;
            Send(inp);
        }

        public static void Key(ushort vk, ushort scan, bool down, bool extended)
        {
            // Same rule as MakeKey, applied before the id so a press and its
            // release always match even if an older sender set the flag.
            if (scan == 0x2A || scan == 0x36) extended = false;

            int id = scan | (extended ? 0x10000 : 0) | (scan == 0 ? (vk << 20) : 0);
            lock (Gate)
            {
                if (down) HeldKeys.Add(id);
                else HeldKeys.Remove(id);
            }

            Send(new Native.INPUT[] { Native.MakeKey(vk, scan, extended, !down) });
        }

        /// <summary>
        /// Safety net: if the link drops while a key or button is held down,
        /// the receiving PC would be stuck with it pressed forever.
        /// </summary>
        public static void ReleaseAll()
        {
            List<int> keys;
            List<byte> buttons;
            lock (Gate)
            {
                keys = new List<int>(HeldKeys);
                buttons = new List<byte>(HeldButtons);
                HeldKeys.Clear();
                HeldButtons.Clear();
            }

            foreach (int id in keys)
            {
                ushort scan = (ushort)(id & 0xFFFF);
                bool ext = (id & 0x10000) != 0;
                ushort vk = (ushort)((id >> 20) & 0xFFFF);
                Send(new Native.INPUT[] { Native.MakeKey(vk, scan, ext, true) });
            }
            foreach (byte b in buttons) Button(b, false);
        }
    }

    /// <summary>
    /// Runs on the PC that receives the input. Listens, authenticates, and
    /// replays whatever the host sends.
    /// </summary>
    internal sealed class ClientSide : IDisposable
    {
        private readonly Config _cfg;
        private TcpListener _listener;
        private Thread _acceptThread;
        private DiscoveryResponder _discovery;
        private volatile bool _running;
        private volatile Link _link;

        private string _pairCode;
        private DateTime _pairUntil = DateTime.MinValue;
        private int _pairFails;
        private readonly object _pairLock = new object();

        public event Action<string> Status;
        public event Action<bool> ControlChanged;
        public event Action PairingDone;

        public bool IsConnected { get { return _link != null; } }

        public ClientSide(Config cfg)
        {
            _cfg = cfg;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _discovery = new DiscoveryResponder(_cfg.Port);
            _discovery.Start();
            _acceptThread = new Thread(AcceptLoop);
            _acceptThread.IsBackground = true;
            _acceptThread.Start();
        }

        public void Dispose()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); }
            catch { }
            Link l = _link;
            _link = null;
            if (l != null) l.Dispose();
            if (_discovery != null) _discovery.Dispose();
            Injector.ReleaseAll();
        }

        private void Report(string s)
        {
            Config.Log("[client] " + s);
            Action<string> h = Status;
            if (h != null) h(s);
        }

        // ---- pairing window ----

        public string StartPairing()
        {
            lock (_pairLock)
            {
                _pairCode = Crypto.NewPairingCode();
                _pairUntil = DateTime.UtcNow.AddMinutes(3);
                _pairFails = 0;
                return _pairCode;
            }
        }

        public void CancelPairing()
        {
            lock (_pairLock)
            {
                _pairCode = null;
                _pairUntil = DateTime.MinValue;
            }
        }

        private byte[] ResolveSecret(bool pairMode)
        {
            if (pairMode)
            {
                lock (_pairLock)
                {
                    if (_pairCode == null || DateTime.UtcNow > _pairUntil) return null;
                    if (_pairFails >= 3) return null;
                    return Encoding.ASCII.GetBytes(_pairCode);
                }
            }
            return Config.LoadKey();
        }

        // ---- connection ----

        private void AcceptLoop()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, _cfg.Port);
                _listener.Start();
                Report("대기 중 (포트 " + _cfg.Port + ")");
            }
            catch (Exception ex)
            {
                Report("포트를 열 수 없습니다: " + ex.Message);
                return;
            }

            while (_running)
            {
                TcpClient tcp = null;
                try
                {
                    tcp = _listener.AcceptTcpClient();
                }
                catch
                {
                    if (!_running) break;
                    Thread.Sleep(500);
                    continue;
                }

                try
                {
                    Link link = Link.Handshake(tcp, false, false, null, ResolveSecret);
                    if (link.NewLongTermKey != null)
                    {
                        Config.SaveKey(link.NewLongTermKey);
                        CancelPairing();
                        Action done = PairingDone;
                        if (done != null) done();
                        Report("페어링 완료");
                    }

                    Link old = _link;
                    _link = link;
                    if (old != null) old.Dispose();

                    Report("연결됨 - " + ((IPEndPoint)tcp.Client.RemoteEndPoint).Address);
                    Serve(link);
                }
                catch (HandshakeRejected hr)
                {
                    lock (_pairLock)
                    {
                        if (_pairCode != null) _pairFails++;
                    }
                    Report(hr.Message);
                    try { tcp.Close(); }
                    catch { }
                }
                catch (Exception ex)
                {
                    Config.Log("[client] accept failed: " + ex.Message);
                    try { tcp.Close(); }
                    catch { }
                }
                finally
                {
                    Link l = _link;
                    _link = null;
                    if (l != null) l.Dispose();
                    Injector.ReleaseAll();
                    NotifyControl(false);
                    if (_running) Report("대기 중 (포트 " + _cfg.Port + ")");
                }
            }
        }

        private void NotifyControl(bool active)
        {
            Action<bool> h = ControlChanged;
            if (h != null) h(active);
        }

        private void Serve(Link link)
        {
            PacketWriter reply = new PacketWriter();
            while (_running && _link == link)
            {
                byte[] payload = link.Receive();
                PacketReader r = new PacketReader(payload);
                reply.Reset();

                while (r.HasMore)
                {
                    byte op = r.NextOp();
                    switch (op)
                    {
                        case Op.Move:
                            {
                                int dx = r.I16();
                                int dy = r.I16();
                                Injector.Move(dx, dy);
                                break;
                            }
                        case Op.Button:
                            {
                                byte b = r.U8();
                                bool down = r.U8() != 0;
                                Injector.Button(b, down);
                                break;
                            }
                        case Op.Wheel:
                            {
                                int d = r.I16();
                                bool h = r.U8() != 0;
                                Injector.Wheel(d, h);
                                break;
                            }
                        case Op.Key:
                            {
                                ushort vk = r.U16();
                                ushort scan = r.U16();
                                bool down = r.U8() != 0;
                                bool ext = r.U8() != 0;
                                Injector.Key(vk, scan, down, ext);
                                break;
                            }
                        case Op.Enter:
                            NotifyControl(true);
                            break;
                        case Op.Leave:
                            Injector.ReleaseAll();
                            NotifyControl(false);
                            break;
                        case Op.ReleaseAll:
                            Injector.ReleaseAll();
                            break;
                        case Op.Config:
                            r.U8();
                            break;
                        case Op.Ping:
                            reply.Stamp(Op.Pong, r.I64());
                            break;
                        case Op.Pong:
                            r.I64();
                            break;
                        case Op.Bye:
                            return;
                        default:
                            return;
                    }
                }

                if (reply.Length > 0) link.Send(reply.ToArray());
            }
        }
    }
}
