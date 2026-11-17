using System;
using System.Reflection;
using System.Security.Principal;

namespace Nook
{
    /// <summary>开机自启：登录时启动的计划任务，绕开 Windows 对启动应用的延迟排队。</summary>
    internal static class Startup
    {
        private const string TaskName = "Nook";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    var folder = GetTaskFolder();
                    if (folder == null) return false;
                    folder.GetType().InvokeMember("GetTask",
                        BindingFlags.InvokeMethod, null, folder, new object[] { TaskName });
                    return true;
                }
                catch { return false; }
            }
        }

        public static bool SetEnabled(bool on)
        {
            try
            {
                var folder = GetTaskFolder();
                if (folder == null) return false;

                if (on)
                {
                    string exe = Environment.ProcessPath ?? "";
                    if (exe.Length == 0) return false;
                    folder.GetType().InvokeMember("RegisterTask", BindingFlags.InvokeMethod, null,
                        folder, new object[] { TaskName, TaskXml(exe), 6, null, null, 3 });
                }
                else
                {
                    folder.GetType().InvokeMember("DeleteTask", BindingFlags.InvokeMethod, null,
                        folder, new object[] { TaskName, 0 });
                }

                Log.Write($"autostart {(on ? "on" : "off")}: ok");
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("autostart failed: " + ex.Message);
                return false;
            }
        }

        private static object? GetTaskFolder()
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type == null) return null;

            var svc = Activator.CreateInstance(type);
            if (svc == null) return null;

            svc.GetType().InvokeMember("Connect", BindingFlags.InvokeMethod, null, svc, null);
            return svc.GetType().InvokeMember("GetFolder", BindingFlags.InvokeMethod, null,
                svc, new object[] { "\\" });
        }

        private static string TaskXml(string exe)
        {
            var who = WindowsIdentity.GetCurrent();
            string sid = who.User?.Value ?? "";
            string user = who.Name;

            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
                "<Task version=\"1.3\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                "<RegistrationInfo><Description>Nook 开机自启</Description><URI>\\Nook</URI></RegistrationInfo>" +
                $"<Principals><Principal id=\"Author\"><UserId>{sid}</UserId>" +
                "<LogonType>InteractiveToken</LogonType></Principal></Principals>" +
                "<Settings><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" +
                "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>" +
                "<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>" +
                "<StartWhenAvailable>true</StartWhenAvailable></Settings>" +
                $"<Triggers><LogonTrigger><UserId>{user}</UserId></LogonTrigger></Triggers>" +
                $"<Actions Context=\"Author\"><Exec><Command>{exe}</Command></Exec></Actions>" +
                "</Task>";
        }
    }
}
