using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace UT70Dashboard
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [STAThread]
        private static void Main()
        {
            bool firstInstance;
            using (Mutex instance = new Mutex(true, "Local\\UT70PowerLab", out firstInstance))
            {
                if (!firstInstance) { ActivateExistingWindow(); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    ReportError(e.Exception);
                    Application.Exit();
                };
                try { Application.Run(new DashboardForm()); }
                catch (Exception ex) { ReportError(ex); }
            }
        }

        private static void ActivateExistingWindow()
        {
            int currentId = Process.GetCurrentProcess().Id;
            foreach (Process process in Process.GetProcessesByName("UT70Dashboard"))
            {
                using (process)
                {
                    if (process.Id == currentId) continue;
                    try
                    {
                        IntPtr handle = process.MainWindowHandle;
                        if (handle == IntPtr.Zero) continue;
                        if (IsIconic(handle)) ShowWindowAsync(handle, 9);
                        SetForegroundWindow(handle);
                        return;
                    }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            }
        }

        private static void ReportError(Exception error)
        {
            string detail = error.Message;
            try
            {
                string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "error-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log");
                File.WriteAllText(path, error.ToString());
                detail += "\r\n\r\n详细错误已保存：\r\n" + path;
            }
            catch { detail += "\r\n\r\n" + error.StackTrace; }
            MessageBox.Show("程序遇到错误：\r\n\r\n" + detail, "UT70 Power Lab", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
