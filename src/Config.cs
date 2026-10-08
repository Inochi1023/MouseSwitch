using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MouseSwitch
{
    internal enum Role
    {
        Host = 0,   // the PC the mouse is physically plugged into
        Client = 1  // the PC that receives the input
    }

    internal enum SwitchTrigger
    {
        SideButton1 = 0, // XBUTTON1, usually "back"
        SideButton2 = 1, // XBUTTON2, usually "forward"
        MiddleButton = 2,
        HotkeyOnly = 3
    }

    internal sealed class Config
    {
        public Role Role = Role.Host;
        public string PeerAddress = "";
        public int Port = Link.DefaultPort;
        public SwitchTrigger Trigger = SwitchTrigger.SideButton2;
        public bool ShareKeyboard = true;
        public bool AutoStart = false;
        public int CoalesceMs = 2;
        public bool Sound = true;
        public string PeerName = "";

        private static string Dir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MouseSwitch");
            }
        }

        private static string IniPath { get { return Path.Combine(Dir, "config.ini"); } }
        private static string KeyPath { get { return Path.Combine(Dir, "peer.key"); } }

        public static Config Load()
        {
            Config c = new Config();
            try
            {
                if (!File.Exists(IniPath)) return c;
                foreach (string raw in File.ReadAllLines(IniPath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    switch (k)
                    {
                        case "role": c.Role = v == "client" ? Role.Client : Role.Host; break;
                        case "peer": c.PeerAddress = v; break;
                        case "peerName": c.PeerName = v; break;
                        case "port": int.TryParse(v, out c.Port); break;
                        case "trigger": c.Trigger = ParseTrigger(v); break;
                        case "shareKeyboard": c.ShareKeyboard = v == "1"; break;
                        case "autoStart": c.AutoStart = v == "1"; break;
                        case "coalesceMs": int.TryParse(v, out c.CoalesceMs); break;
                        case "sound": c.Sound = v == "1"; break;
                    }
                }
                if (c.Port <= 0 || c.Port > 65535) c.Port = Link.DefaultPort;
                if (c.CoalesceMs < 0) c.CoalesceMs = 0;
                if (c.CoalesceMs > 20) c.CoalesceMs = 20;
            }
            catch
            {
            }
            return c;
        }

        private static SwitchTrigger ParseTrigger(string v)
        {
            switch (v)
            {
                case "x1": return SwitchTrigger.SideButton1;
                case "x2": return SwitchTrigger.SideButton2;
                case "middle": return SwitchTrigger.MiddleButton;
                default: return SwitchTrigger.HotkeyOnly;
            }
        }

        private static string TriggerText(SwitchTrigger t)
        {
            switch (t)
            {
                case SwitchTrigger.SideButton1: return "x1";
                case SwitchTrigger.SideButton2: return "x2";
                case SwitchTrigger.MiddleButton: return "middle";
                default: return "hotkey";
            }
        }

        public bool Save()
        {
            try
            {
                SaveCore();
                return true;
            }
            catch (Exception ex)
            {
                Log("settings save failed: " + ex.Message);
                return false;
            }
        }

        private void SaveCore()
        {
            Directory.CreateDirectory(Dir);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# MouseSwitch settings");
            sb.AppendLine("role=" + (Role == Role.Client ? "client" : "host"));
            sb.AppendLine("peer=" + PeerAddress);
            sb.AppendLine("peerName=" + PeerName);
            sb.AppendLine("port=" + Port);
            sb.AppendLine("trigger=" + TriggerText(Trigger));
            sb.AppendLine("shareKeyboard=" + (ShareKeyboard ? "1" : "0"));
            sb.AppendLine("autoStart=" + (AutoStart ? "1" : "0"));
            sb.AppendLine("coalesceMs=" + CoalesceMs);
            sb.AppendLine("sound=" + (Sound ? "1" : "0"));
            File.WriteAllText(IniPath, sb.ToString(), Encoding.UTF8);
        }

        // ---- long term pairing key ----
        public static byte[] LoadKey()
        {
            try
            {
                if (!File.Exists(KeyPath)) return null;
                return Crypto.UnprotectLocal(File.ReadAllBytes(KeyPath));
            }
            catch
            {
                return null;
            }
        }

        public static void SaveKey(byte[] key)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(KeyPath, Crypto.ProtectLocal(key));
        }

        public static void ClearKey()
        {
            try
            {
                if (File.Exists(KeyPath)) File.Delete(KeyPath);
            }
            catch
            {
            }
        }

        public static bool HasKey
        {
            get { return File.Exists(KeyPath); }
        }

        public static string LogPath { get { return Path.Combine(Dir, "log.txt"); } }

        private static readonly object LogLock = new object();

        public static void Log(string message)
        {
            try
            {
                lock (LogLock)
                {
                    Directory.CreateDirectory(Dir);
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 512 * 1024)
                        File.Delete(LogPath);
                    File.AppendAllText(LogPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch
            {
            }
        }
    }
}
