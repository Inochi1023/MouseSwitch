using System;
using System.Drawing;
using System.Net.Sockets;
using System.Windows.Forms;
using System.Security.Principal;
using Microsoft.Win32;

namespace MouseSwitch
{
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon _tray;
        private readonly Config _cfg;
        private HostSide _host;
        private ClientSide _client;
        private SettingsForm _settings;
        private readonly Timer _uiTimer;
        private string _status = "시작 중";
        private readonly Control _marshal;
        private volatile bool _quitting;

        private readonly Icon _iconIdle;
        private readonly Icon _iconReady;
        private readonly Icon _iconRemote;

        public TrayApp()
        {
            _cfg = Config.Load();

            _marshal = new Control();
            _marshal.CreateControl();
            IntPtr dummy = _marshal.Handle;

            _iconIdle = MakeIcon(Color.FromArgb(120, 120, 120));
            _iconReady = MakeIcon(Color.FromArgb(60, 160, 90));
            _iconRemote = MakeIcon(Color.FromArgb(230, 140, 40));

            _tray = new NotifyIcon();
            _tray.Icon = _iconIdle;
            _tray.Visible = true;
            _tray.Text = "MouseSwitch";
            _tray.DoubleClick += delegate { ShowSettings(); };
            _tray.ContextMenuStrip = BuildMenu();

            StartEngine();

            _uiTimer = new Timer();
            _uiTimer.Interval = 700;
            _uiTimer.Tick += delegate { RefreshUi(); };
            _uiTimer.Start();

            if (!Config.HasKey)
            {
                _marshal.BeginInvoke((MethodInvoker)delegate { ShowSettings(); });
            }
        }

        // ------------------------------------------------------------------
        // engine
        // ------------------------------------------------------------------

        public Config Settings { get { return _cfg; } }
        public ClientSide Client { get { return _client; } }

        public void StartEngine()
        {
            StopEngine();
            if (_cfg.Role == Role.Host)
            {
                _host = new HostSide(_cfg);
                _host.Status += OnStatus;
                _host.RemoteChanged += OnRemoteChanged;
                _host.KeyboardToggleRequested += OnKeyboardHotkey;
                _host.Start();
            }
            else
            {
                _client = new ClientSide(_cfg);
                _client.Status += OnStatus;
                _client.ControlChanged += OnControlChanged;
                _client.Start();
            }
        }

        private void OnKeyboardHotkey()
        {
            try
            {
                _marshal.BeginInvoke((MethodInvoker)delegate { SetShareKeyboard(!_cfg.ShareKeyboard); });
            }
            catch
            {
            }
        }

        /// <summary>Single place that changes keyboard forwarding: saves, applies, syncs UI.</summary>
        public void SetShareKeyboard(bool on)
        {
            if (_cfg.ShareKeyboard == on) return;
            _cfg.ShareKeyboard = on;
            _cfg.Save();
            Config.Log("keyboard forwarding " + (on ? "on" : "off"));
            if (_host != null) _host.OnShareKeyboardChanged();
            if (_settings != null && !_settings.IsDisposed) _settings.SyncKeyboard(on);
            RefreshUi();
        }

        public void StopEngine()
        {
            if (_host != null) { _host.Dispose(); _host = null; }
            if (_client != null) { _client.Dispose(); _client = null; }
        }

        private void OnStatus(string s)
        {
            _status = s;
        }

        private void OnRemoteChanged(bool remote)
        {
            try
            {
                _marshal.BeginInvoke((MethodInvoker)delegate
                {
                    if (_quitting) return;
                    _tray.Icon = remote ? _iconRemote : _iconReady;
                });
            }
            catch
            {
            }
        }

        private void OnControlChanged(bool active)
        {
            try
            {
                _marshal.BeginInvoke((MethodInvoker)delegate
                {
                    if (_quitting) return;
                    _tray.Icon = active ? _iconRemote : _iconReady;
                });
            }
            catch
            {
            }
        }

        private void RefreshUi()
        {
            if (_quitting) return;
            string role = _cfg.Role == Role.Host ? "호스트" : "클라이언트";
            bool connected = (_host != null && _host.IsConnected) || (_client != null && _client.IsConnected);
            string latency = "";
            if (_host != null && _host.LatencyMs >= 0) latency = "  |  " + _host.LatencyMs + "ms";

            string admin = IsElevated() ? "" : "  |  일반 권한";
            string kb = _cfg.Role == Role.Host ? (_cfg.ShareKeyboard ? "  |  키보드 O" : "  |  키보드 X") : "";
            string text = "MouseSwitch (" + role + ")\n" + _status + latency + kb + admin;
            if (text.Length > 127) text = text.Substring(0, 127);
            _tray.Text = text;

            if (_host != null)
                _tray.Icon = _host.IsRemote ? _iconRemote : (connected ? _iconReady : _iconIdle);
            else if (_client != null && !connected)
                _tray.Icon = _iconIdle;
        }

        // ------------------------------------------------------------------
        // menu
        // ------------------------------------------------------------------

        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();

            ToolStripMenuItem settings = new ToolStripMenuItem("설정 / 페어링...");
            settings.Click += delegate { ShowSettings(); };
            m.Items.Add(settings);

            ToolStripMenuItem kbItem = new ToolStripMenuItem("키보드도 넘기기 (Ctrl+Alt+K)");
            kbItem.Click += delegate { SetShareKeyboard(!_cfg.ShareKeyboard); };

            ToolStripMenuItem toggle = new ToolStripMenuItem("지금 전환 (Ctrl+Alt+S)");
            toggle.Click += delegate { if (_host != null) _host.Toggle(); };
            m.Items.Add(toggle);
            m.Items.Add(kbItem);

            m.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem log = new ToolStripMenuItem("로그 폴더 열기");
            log.Click += delegate
            {
                try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + Config.LogPath + "\""); }
                catch { }
            };
            m.Items.Add(log);

            ToolStripMenuItem quit = new ToolStripMenuItem("종료");
            quit.Click += delegate { Quit(); };
            m.Items.Add(quit);

            m.Opening += delegate
            {
                toggle.Enabled = _host != null && _host.IsConnected;
                kbItem.Visible = _cfg.Role == Role.Host;
                kbItem.Checked = _cfg.ShareKeyboard;
            };
            return m;
        }

        private void ShowSettings()
        {
            if (_settings != null && !_settings.IsDisposed)
            {
                _settings.Activate();
                return;
            }
            _settings = new SettingsForm(this, _cfg);
            _settings.Show();
            _settings.Activate();
            _settings.BringToFront();
        }

        private void Quit()
        {
            _quitting = true;
            _uiTimer.Stop();
            StopEngine();
            _tray.Visible = false;
            _tray.Dispose();
            Application.Exit();
        }

        public void Notify(string title, string body)
        {
            try
            {
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = body;
                _tray.ShowBalloonTip(3000);
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        private static Icon MakeIcon(Color color)
        {
            using (Bitmap bmp = new Bitmap(16, 16))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (SolidBrush b = new SolidBrush(color))
                        g.FillEllipse(b, 1, 0, 13, 15);
                    using (Pen p = new Pen(Color.FromArgb(230, 255, 255, 255), 1.4f))
                        g.DrawLine(p, 7.5f, 3.5f, 7.5f, 7.5f);
                }
                IntPtr h = bmp.GetHicon();
                return (Icon)Icon.FromHandle(h).Clone();
            }
        }

        private static int _elevated = -1;

        public static bool IsElevated()
        {
            if (_elevated >= 0) return _elevated == 1;
            bool ok = false;
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    ok = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
            }
            _elevated = ok ? 1 : 0;
            return ok;
        }

        /// <summary>Host side: run a one off pairing exchange with the peer.</summary>
        public static string Pair(string address, int port, string code)
        {
            TcpClient tcp = null;
            try
            {
                tcp = new TcpClient();
                IAsyncResult ar = tcp.BeginConnect(address, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(5000)) return "상대 PC에 연결할 수 없습니다 (방화벽/IP 확인)";
                tcp.EndConnect(ar);

                Link link = Link.Handshake(tcp, true, true, System.Text.Encoding.ASCII.GetBytes(code), null);
                if (link.NewLongTermKey == null) return "페어링 키를 받지 못했습니다";
                Config.SaveKey(link.NewLongTermKey);
                link.Dispose();
                return null;
            }
            catch (HandshakeRejected hr)
            {
                return hr.Message;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            finally
            {
                try { if (tcp != null) tcp.Close(); }
                catch { }
            }
        }
    }
}
