using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MouseSwitch
{
    /// <summary>
    /// Start at logon through Task Scheduler.
    ///
    /// The registry Run key cannot do this job: Windows silently skips Run
    /// entries that need elevation, so an app set to run as administrator
    /// never starts. A task with RunLevel=HighestAvailable starts elevated
    /// without a prompt.
    ///
    /// The task is written from XML rather than "schtasks /sc onlogon"
    /// because the command line form keeps three defaults that break a tray
    /// app: it will not start on battery (so a laptop never starts it), it is
    /// killed after 72 hours, and it runs at below-normal priority, which
    /// adds input latency.
    /// </summary>
    internal static class AutoStart
    {
        private const string TaskName = "MouseSwitch";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsEnabled()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", "/query /tn \"" + TaskName + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(5000)) return false;
                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <returns>null on success, otherwise a message for the user</returns>
        public static string Enable()
        {
            string xmlPath = Path.Combine(Path.GetTempPath(), "MouseSwitch-task.xml");
            try
            {
                File.WriteAllText(xmlPath, BuildXml(), Encoding.Unicode);
            }
            catch (Exception ex)
            {
                return "작업 파일을 만들 수 없습니다: " + ex.Message;
            }

            string err = RunSchtasks("/create /tn \"" + TaskName + "\" /xml \"" + xmlPath + "\" /f");
            try { File.Delete(xmlPath); }
            catch { }

            if (err == null)
            {
                RemoveRunKey();
                Config.Log("autostart task registered for " + Application.ExecutablePath);
            }
            else
            {
                Config.Log("autostart register failed: " + err);
            }
            return err;
        }

        public static string Disable()
        {
            RemoveRunKey();
            if (!IsEnabled()) return null;
            string err = RunSchtasks("/delete /tn \"" + TaskName + "\" /f");
            Config.Log(err == null ? "autostart task removed" : "autostart remove failed: " + err);
            return err;
        }

        private static string RunSchtasks(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", args);
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                if (TrayApp.IsElevated())
                {
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                }
                else
                {
                    // Creating a highest-privilege task needs admin, so ask once.
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                }

                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(20000)) return "시간 초과";
                    return p.ExitCode == 0 ? null : "schtasks 오류 코드 " + p.ExitCode;
                }
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) return "관리자 권한 요청을 취소했습니다";
                return ex.Message;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>The old way of starting; remove it so there is only one.</summary>
        public static void RemoveRunKey()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k != null) k.DeleteValue("MouseSwitch", false);
                }
            }
            catch
            {
            }
        }

        private static string Esc(string s)
        {
            return SecurityElement.Escape(s);
        }

        private static string BuildXml()
        {
            string exe = Application.ExecutablePath;
            string dir = Path.GetDirectoryName(exe);
            string user;
            using (WindowsIdentity id = WindowsIdentity.GetCurrent()) user = id.Name;

            StringBuilder x = new StringBuilder();
            x.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
            x.AppendLine("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
            x.AppendLine("  <RegistrationInfo>");
            x.AppendLine("    <Description>MouseSwitch - start at logon</Description>");
            x.AppendLine("  </RegistrationInfo>");
            x.AppendLine("  <Triggers>");
            x.AppendLine("    <LogonTrigger>");
            x.AppendLine("      <Enabled>true</Enabled>");
            x.AppendLine("      <UserId>" + Esc(user) + "</UserId>");
            x.AppendLine("      <Delay>PT5S</Delay>");
            x.AppendLine("    </LogonTrigger>");
            x.AppendLine("  </Triggers>");
            x.AppendLine("  <Principals>");
            x.AppendLine("    <Principal id=\"Author\">");
            x.AppendLine("      <UserId>" + Esc(user) + "</UserId>");
            x.AppendLine("      <LogonType>InteractiveToken</LogonType>");
            x.AppendLine("      <RunLevel>HighestAvailable</RunLevel>");
            x.AppendLine("    </Principal>");
            x.AppendLine("  </Principals>");
            x.AppendLine("  <Settings>");
            x.AppendLine("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
            x.AppendLine("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
            x.AppendLine("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
            x.AppendLine("    <AllowHardTerminate>true</AllowHardTerminate>");
            x.AppendLine("    <StartWhenAvailable>false</StartWhenAvailable>");
            x.AppendLine("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>");
            x.AppendLine("    <IdleSettings>");
            x.AppendLine("      <StopOnIdleEnd>false</StopOnIdleEnd>");
            x.AppendLine("      <RestartOnIdle>false</RestartOnIdle>");
            x.AppendLine("    </IdleSettings>");
            x.AppendLine("    <AllowStartOnDemand>true</AllowStartOnDemand>");
            x.AppendLine("    <Enabled>true</Enabled>");
            x.AppendLine("    <Hidden>false</Hidden>");
            x.AppendLine("    <RunOnlyIfIdle>false</RunOnlyIfIdle>");
            x.AppendLine("    <WakeToRun>false</WakeToRun>");
            x.AppendLine("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
            x.AppendLine("    <Priority>4</Priority>");
            x.AppendLine("  </Settings>");
            x.AppendLine("  <Actions Context=\"Author\">");
            x.AppendLine("    <Exec>");
            x.AppendLine("      <Command>" + Esc(exe) + "</Command>");
            x.AppendLine("      <WorkingDirectory>" + Esc(dir) + "</WorkingDirectory>");
            x.AppendLine("    </Exec>");
            x.AppendLine("  </Actions>");
            x.AppendLine("</Task>");
            return x.ToString();
        }
    }
}
