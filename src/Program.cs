using System;
using System.Threading;
using System.Windows.Forms;

namespace PCCheck
{
    internal static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.InitializeDpi();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate
            {
                ShowFatalMessage();
            };
            AppDomain.CurrentDomain.UnhandledException += delegate
            {
                ShowFatalMessage();
            };

            try
            {
                if (args != null && args.Length >= 2)
                {
                    if (string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
                    {
                        Environment.ExitCode = SelfTest.WriteSelfTest(args[1]);
                        return;
                    }
                    if (string.Equals(args[0], "--export", StringComparison.OrdinalIgnoreCase))
                    {
                        Environment.ExitCode = SelfTest.ExportReport(args[1]);
                        return;
                    }
                }

                bool created;
                _mutex = new Mutex(true, @"Local\PCCheck_SingleInstance_2026", out created);
                if (!created)
                {
                    ActivateExisting();
                    return;
                }

                try
                {
                    if (!RuntimeCheck.HasNet48())
                    {
                        Application.Run(new MissingRuntimeForm());
                        return;
                    }
                    Application.Run(new MainForm());
                }
                finally
                {
                    try { _mutex.ReleaseMutex(); } catch { }
                    try { _mutex.Dispose(); } catch { }
                    _mutex = null;
                }
            }
            catch
            {
                ShowFatalMessage();
            }
        }

        private static void ActivateExisting()
        {
            IntPtr handle = NativeMethods.FindWindow(null, MainForm.WindowTitle);
            if (handle != IntPtr.Zero)
            {
                NativeMethods.ShowWindowAsync(handle, NativeMethods.SW_RESTORE);
                NativeMethods.SetForegroundWindow(handle);
                return;
            }
            MessageBox.Show("PCCheck 已经在运行，请在任务栏中查看。", "PCCheck",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void ShowFatalMessage()
        {
            try
            {
                MessageBox.Show("程序遇到一个未预期的问题，但不会修改系统设置。\r\n请关闭后重新打开；如果仍然出现，请重启电脑后再试。",
                    "PCCheck", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch
            {
            }
        }
    }
}
