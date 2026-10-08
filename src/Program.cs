using System;
using System.Threading;
using System.Windows.Forms;

namespace MouseSwitch
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // "MouseSwitch.exe --autostart on|off" is what the install script
            // calls. Handled before the single instance check so it works
            // while the tray app is already running.
            if (args != null && args.Length >= 2 && args[0] == "--autostart")
            {
                string err = args[1] == "off" ? AutoStart.Disable() : AutoStart.Enable();
                return err == null ? 0 : 1;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\MouseSwitchSingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("MouseSwitch가 이미 실행 중입니다. (작업 표시줄 오른쪽 아래 아이콘 확인)",
                                    "MouseSwitch", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Ask Windows for 1 ms timer resolution so the 2 ms send tick
                // is actually 2 ms and not the default ~15 ms.
                Native.TimeBeginPeriod(1);

                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    Config.Log("UI exception: " + e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Config.Log("fatal: " + e.ExceptionObject);
                };

                try
                {
                    Application.Run(new TrayApp());
                }
                finally
                {
                    Native.TimeEndPeriod(1);
                }
            }
            return 0;
        }
    }
}
