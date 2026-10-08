using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MouseSwitch
{
    internal sealed class SettingsForm : Form
    {
        private readonly TrayApp _app;
        private readonly Config _cfg;

        private RadioButton _rbHost, _rbClient;
        private GroupBox _grpHost, _grpClient;
        private ComboBox _cbPeer, _cbTrigger;
        private TextBox _txtIp;
        private CheckBox _chkKeyboard, _chkAutoStart, _chkSound;
        private NumericUpDown _numCoalesce, _numPort;
        private Label _lblCode, _lblStatus;
        private Button _btnScan, _btnPair, _btnCode;
        private List<DiscoveredPeer> _peers = new List<DiscoveredPeer>();
        private bool _loading;

        public SettingsForm(TrayApp app, Config cfg)
        {
            _app = app;
            _cfg = cfg;
            Build();
            LoadValues();
        }

        private void Build()
        {
            Text = "MouseSwitch 설정";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(430, 470);
            Font = new Font("Malgun Gothic", 9f);

            GroupBox grpRole = new GroupBox();
            grpRole.Text = "이 PC의 역할";
            grpRole.SetBounds(12, 10, 406, 76);
            _rbHost = new RadioButton();
            _rbHost.Text = "마우스·키보드가 꽂혀 있는 PC (조종하는 쪽)";
            _rbHost.SetBounds(16, 22, 370, 22);
            _rbClient = new RadioButton();
            _rbClient.Text = "조종을 받는 PC";
            _rbClient.SetBounds(16, 46, 370, 22);
            grpRole.Controls.Add(_rbHost);
            grpRole.Controls.Add(_rbClient);
            Controls.Add(grpRole);

            _rbHost.CheckedChanged += delegate { UpdateRoleUi(); };

            // ---- host ----
            _grpHost = new GroupBox();
            _grpHost.Text = "호스트 설정";
            _grpHost.SetBounds(12, 94, 406, 230);

            _grpHost.Controls.Add(MakeLabel("상대 PC", 16, 26));
            _cbPeer = new ComboBox();
            _cbPeer.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbPeer.SetBounds(100, 22, 190, 24);
            _cbPeer.SelectedIndexChanged += delegate
            {
                int i = _cbPeer.SelectedIndex;
                if (i >= 0 && i < _peers.Count) _txtIp.Text = _peers[i].Address;
            };
            _grpHost.Controls.Add(_cbPeer);

            _btnScan = new Button();
            _btnScan.Text = "검색";
            _btnScan.SetBounds(298, 21, 90, 26);
            _btnScan.Click += delegate { Scan(); };
            _grpHost.Controls.Add(_btnScan);

            _grpHost.Controls.Add(MakeLabel("IP 주소", 16, 60));
            _txtIp = new TextBox();
            _txtIp.SetBounds(100, 56, 190, 24);
            _grpHost.Controls.Add(_txtIp);

            _grpHost.Controls.Add(MakeLabel("전환 버튼", 16, 94));
            _cbTrigger = new ComboBox();
            _cbTrigger.DropDownStyle = ComboBoxStyle.DropDownList;
            _cbTrigger.SetBounds(100, 90, 190, 24);
            _cbTrigger.Items.Add("마우스 옆 버튼 (뒤로)");
            _cbTrigger.Items.Add("마우스 옆 버튼 (앞으로)");
            _cbTrigger.Items.Add("휠 클릭");
            _cbTrigger.Items.Add("단축키만 사용");
            _grpHost.Controls.Add(_cbTrigger);

            Label trigWarn = MakeLabel("* 고른 버튼은 원래 기능(뒤로/앞으로/휠클릭)을 못 씁니다", 100, 116);
            trigWarn.ForeColor = Color.Gray;
            _grpHost.Controls.Add(trigWarn);

            _chkKeyboard = new CheckBox();
            _chkKeyboard.Text = "키보드도 함께 넘기기";
            _chkKeyboard.SetBounds(100, 138, 260, 22);
            _chkKeyboard.CheckedChanged += delegate
            {
                if (_loading) return;
                _app.SetShareKeyboard(_chkKeyboard.Checked);
                SetStatus(_chkKeyboard.Checked
                    ? "키보드도 함께 넘깁니다. (Ctrl+Alt+K 로 언제든 전환)"
                    : "마우스만 넘깁니다. 키보드는 이 PC에 남습니다. (Ctrl+Alt+K 로 언제든 전환)");
            };
            _grpHost.Controls.Add(_chkKeyboard);

            _grpHost.Controls.Add(MakeLabel("전송 주기", 16, 166));
            _numCoalesce = new NumericUpDown();
            _numCoalesce.Minimum = 0;
            _numCoalesce.Maximum = 20;
            _numCoalesce.SetBounds(100, 162, 60, 24);
            _grpHost.Controls.Add(_numCoalesce);
            Label hint = MakeLabel("ms  (낮을수록 반응 빠름)", 166, 166);
            hint.AutoSize = true;
            hint.ForeColor = Color.Gray;
            _grpHost.Controls.Add(hint);

            _btnPair = new Button();
            _btnPair.Text = "페어링 시작 (상대 PC의 코드 입력)";
            _btnPair.SetBounds(100, 192, 288, 30);
            _btnPair.Click += delegate { DoPair(); };
            _grpHost.Controls.Add(_btnPair);

            Controls.Add(_grpHost);

            // ---- client ----
            _grpClient = new GroupBox();
            _grpClient.Text = "클라이언트 설정";
            _grpClient.SetBounds(12, 94, 406, 230);

            _grpClient.Controls.Add(MakeLabel("포트", 16, 30));
            _numPort = new NumericUpDown();
            _numPort.Minimum = 1;
            _numPort.Maximum = 65535;
            _numPort.SetBounds(100, 26, 90, 24);
            _grpClient.Controls.Add(_numPort);

            _btnCode = new Button();
            _btnCode.Text = "페어링 코드 만들기";
            _btnCode.SetBounds(100, 62, 200, 30);
            _btnCode.Click += delegate { MakeCode(); };
            _grpClient.Controls.Add(_btnCode);

            _lblCode = new Label();
            _lblCode.Text = "- - - - - -";
            _lblCode.Font = new Font("Consolas", 26f, FontStyle.Bold);
            _lblCode.TextAlign = ContentAlignment.MiddleCenter;
            _lblCode.SetBounds(16, 104, 374, 52);
            _grpClient.Controls.Add(_lblCode);

            Label note = MakeLabel("이 코드를 상대 PC의 MouseSwitch에 입력하세요. 3분간 유효합니다.", 16, 164);
            note.AutoSize = false;
            note.SetBounds(16, 164, 374, 40);
            note.ForeColor = Color.Gray;
            _grpClient.Controls.Add(note);

            Controls.Add(_grpClient);

            // ---- common ----
            _chkAutoStart = new CheckBox();
            _chkAutoStart.Text = "윈도우를 켤 때 자동으로 실행";
            _chkAutoStart.SetBounds(20, 334, 300, 22);
            _chkAutoStart.CheckedChanged += delegate
            {
                if (_loading) return;
                ApplyAutoStart(_chkAutoStart.Checked);
            };
            Controls.Add(_chkAutoStart);

            _chkSound = new CheckBox();
            _chkSound.Text = "전환할 때 알림음";
            _chkSound.SetBounds(20, 358, 300, 22);
            _chkSound.CheckedChanged += delegate
            {
                if (_loading) return;
                _cfg.Sound = _chkSound.Checked;
                _cfg.Save();
            };
            Controls.Add(_chkSound);

            _lblStatus = new Label();
            _lblStatus.SetBounds(20, 388, 390, 40);
            _lblStatus.ForeColor = Color.FromArgb(50, 90, 160);
            Controls.Add(_lblStatus);

            Button save = new Button();
            save.Text = "저장";
            save.SetBounds(232, 430, 88, 30);
            save.Click += delegate { SaveValues(true); };
            Controls.Add(save);

            Button close = new Button();
            close.Text = "닫기";
            close.SetBounds(328, 430, 88, 30);
            close.Click += delegate { Close(); };
            Controls.Add(close);

            Dpi.Scale(this);
        }

        private static Label MakeLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            // AutoSize matters: a label wider than its text paints an opaque
            // rectangle over whatever sits to its right, because labels added
            // first sit on top in the z-order.
            l.AutoSize = true;
            l.SetBounds(x, y, 0, 0);
            return l;
        }

        private void LoadValues()
        {
            _loading = true;
            _rbHost.Checked = _cfg.Role == Role.Host;
            _rbClient.Checked = _cfg.Role == Role.Client;
            _txtIp.Text = _cfg.PeerAddress;
            _cbTrigger.SelectedIndex = (int)_cfg.Trigger;
            _chkKeyboard.Checked = _cfg.ShareKeyboard;
            _numCoalesce.Value = _cfg.CoalesceMs;
            _numPort.Value = _cfg.Port;
            _chkSound.Checked = _cfg.Sound;
            _chkAutoStart.Checked = AutoStart.IsEnabled();
            UpdateRoleUi();
            string status = Config.HasKey ? "페어링 완료된 상태입니다." : "아직 페어링하지 않았습니다.";
            if (!TrayApp.IsElevated())
                status += "  ※ 관리자 권한이 아닙니다 — 게임이나 권한 확인 창에서 입력이 막힐 수 있습니다.";
            SetStatus(status);
            _loading = false;
        }

        private void UpdateRoleUi()
        {
            bool host = _rbHost.Checked;
            _grpHost.Visible = host;
            _grpClient.Visible = !host;
        }

        /// <summary>Called by the tray when Ctrl+Alt+K or the menu flips the setting.</summary>
        public void SyncKeyboard(bool on)
        {
            _loading = true;
            _chkKeyboard.Checked = on;
            _loading = false;
        }

        private void ApplyAutoStart(bool on)
        {
            _chkAutoStart.Enabled = false;
            SetStatus(on ? "자동 실행 등록 중... 관리자 권한 확인 창이 뜨면 [예]를 누르세요."
                         : "자동 실행 해제 중...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = on ? AutoStart.Enable() : AutoStart.Disable();
                bool actual = AutoStart.IsEnabled();
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _loading = true;
                        _chkAutoStart.Checked = actual;
                        _loading = false;
                        _chkAutoStart.Enabled = true;
                        if (err != null) SetStatus("자동 실행 설정 실패: " + err);
                        else if (actual) SetStatus("다음 로그인부터 관리자 권한으로 자동 실행됩니다.");
                        else SetStatus("자동 실행을 껐습니다.");
                    });
                }
                catch
                {
                }
            });
        }

        private void SetStatus(string s)
        {
            _lblStatus.Text = s;
        }

        private void SaveValues(bool restart)
        {
            _cfg.Role = _rbHost.Checked ? Role.Host : Role.Client;
            _cfg.PeerAddress = _txtIp.Text.Trim();
            _cfg.Trigger = (SwitchTrigger)Math.Max(0, _cbTrigger.SelectedIndex);
            _cfg.ShareKeyboard = _chkKeyboard.Checked;
            _cfg.CoalesceMs = (int)_numCoalesce.Value;
            _cfg.Port = (int)_numPort.Value;
            _cfg.Sound = _chkSound.Checked;
            int i = _cbPeer.SelectedIndex;
            if (i >= 0 && i < _peers.Count) _cfg.PeerName = _peers[i].Name;
            if (!_cfg.Save()) SetStatus("설정을 저장하지 못했습니다. 로그를 확인하세요.");
            if (restart)
            {
                _app.StartEngine();
                SetStatus("저장했습니다.");
            }
        }

        private void Scan()
        {
            _btnScan.Enabled = false;
            SetStatus("같은 네트워크에서 찾는 중...");
            Cursor = Cursors.WaitCursor;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<DiscoveredPeer> found = DiscoveryProbe.Scan(1500);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _peers = found;
                        _cbPeer.Items.Clear();
                        foreach (DiscoveredPeer p in found) _cbPeer.Items.Add(p.ToString());
                        if (found.Count > 0)
                        {
                            _cbPeer.SelectedIndex = 0;
                            SetStatus(found.Count + "대를 찾았습니다.");
                        }
                        else
                        {
                            SetStatus("찾지 못했습니다. 상대 PC에서 MouseSwitch를 '조종받는 PC'로 실행했는지, 같은 와이파이인지 확인하세요.");
                        }
                        _btnScan.Enabled = true;
                        Cursor = Cursors.Default;
                    });
                }
                catch
                {
                }
            });
        }

        private void MakeCode()
        {
            if (_cfg.Role != Role.Client || _app.Client == null)
            {
                MessageBox.Show(this, "먼저 '조종을 받는 PC'를 선택하고 [저장]을 누르세요.", "MouseSwitch");
                return;
            }
            string code = _app.Client.StartPairing();
            _lblCode.Text = code.Substring(0, 3) + " " + code.Substring(3);
            SetStatus("상대 PC에서 이 코드를 입력하세요. 3분 후 만료됩니다.");
        }

        private void DoPair()
        {
            SaveValues(false);
            if (string.IsNullOrEmpty(_cfg.PeerAddress))
            {
                MessageBox.Show(this, "상대 PC를 먼저 검색하거나 IP를 입력하세요.", "MouseSwitch");
                return;
            }
            string code = Prompt.Ask(this, "페어링 코드", "상대 PC 화면에 표시된 6자리 코드를 입력하세요.");
            if (code == null) return;
            code = code.Replace(" ", "").Trim();
            if (code.Length != 6)
            {
                MessageBox.Show(this, "6자리 숫자를 입력하세요.", "MouseSwitch");
                return;
            }

            Cursor = Cursors.WaitCursor;
            SetStatus("페어링 중...");
            string address = _cfg.PeerAddress;
            int port = _cfg.Port;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = TrayApp.Pair(address, port, code);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        Cursor = Cursors.Default;
                        if (err == null)
                        {
                            SetStatus("페어링 완료. 이제 옆 버튼으로 전환할 수 있습니다.");
                            _app.StartEngine();
                        }
                        else
                        {
                            SetStatus("페어링 실패: " + err);
                        }
                    });
                }
                catch
                {
                }
            });
        }
    }

    internal static class Prompt
    {
        public static string Ask(IWin32Window owner, string title, string message)
        {
            Form f = new Form();
            f.Text = title;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(340, 140);
            f.Font = new Font("Malgun Gothic", 9f);

            Label l = new Label();
            l.Text = message;
            l.SetBounds(16, 14, 310, 40);
            f.Controls.Add(l);

            TextBox t = new TextBox();
            t.SetBounds(16, 58, 310, 32);
            t.Font = new Font("Consolas", 16f);
            t.MaxLength = 7;
            t.TextAlign = HorizontalAlignment.Center;
            f.Controls.Add(t);

            Button ok = new Button();
            ok.Text = "확인";
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(150, 100, 84, 28);
            f.Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "취소";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(242, 100, 84, 28);
            f.Controls.Add(cancel);

            f.AcceptButton = ok;
            f.CancelButton = cancel;
            Dpi.Scale(f);

            using (f)
            {
                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                return t.Text;
            }
        }
    }
    /// <summary>
    /// The layout is laid out by hand in 96 dpi pixels. On a display with
    /// scaling turned on the font comes back larger while the fixed bounds
    /// do not, and the text gets clipped, so scale everything to match.
    /// </summary>
    internal static class Dpi
    {
        public static float Factor()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    return g.DpiX / 96f;
            }
            catch
            {
                return 1f;
            }
        }

        public static void Scale(Form form)
        {
            float k = Factor();
            if (k < 1.01f) return;
            form.AutoScaleMode = AutoScaleMode.None;
            ScaleChildren(form, k);
            form.ClientSize = new Size((int)(form.ClientSize.Width * k),
                                       (int)(form.ClientSize.Height * k));
        }

        private static void ScaleChildren(Control parent, float k)
        {
            foreach (Control c in parent.Controls)
            {
                Rectangle r = c.Bounds;
                int w = c.AutoSize ? r.Width : (int)(r.Width * k);
                int h = c.AutoSize ? r.Height : (int)(r.Height * k);
                c.SetBounds((int)(r.X * k), (int)(r.Y * k), w, h);
                if (c.Controls.Count > 0) ScaleChildren(c, k);
            }
        }
    }
}
