using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PCCheck
{
    public static class RuntimeCheck
    {
        public static bool HasNet48()
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
                    Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
                using (RegistryKey key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (key == null) return false;
                    object value = key.GetValue("Release");
                    if (value == null) return false;
                    int release = Convert.ToInt32(value);
                    return release >= 528040;
                }
            }
            catch
            {
                // 读不到注册表时不阻止启动；程序本身能运行，说明 CLR 4.x 已经存在。
                return true;
            }
        }
    }

    public sealed class MissingRuntimeForm : Form
    {
        public MissingRuntimeForm()
        {
            Text = "PCCheck - 需要 .NET Framework 4.8";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = Theme.S(new Size(640, 480));
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            BackColor = Theme.Bg;
            Font = Theme.FontBody;

            Panel header = new Panel();
            header.BackColor = Theme.Navy;
            header.SetBounds(0, 0, ClientSize.Width, Theme.S(86));
            Controls.Add(header);

            Label title = new Label();
            title.Text = "PCCheck 暂时无法启动";
            title.Font = Theme.FontTitle;
            title.ForeColor = Color.White;
            title.BackColor = Color.Transparent;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.SetBounds(Theme.S(24), Theme.S(14), ClientSize.Width - Theme.S(48), Theme.S(36));
            header.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "缺少运行本程序所需的 .NET Framework 4.8";
            sub.Font = Theme.FontSmall;
            sub.ForeColor = Color.FromArgb(190, 211, 228);
            sub.BackColor = Color.Transparent;
            sub.TextAlign = ContentAlignment.MiddleLeft;
            sub.SetBounds(Theme.S(26), Theme.S(52), ClientSize.Width - Theme.S(52), Theme.S(20));
            header.Controls.Add(sub);

            Label body = new Label();
            body.Font = Theme.FontBody;
            body.ForeColor = Theme.TextSecondary;
            body.BackColor = Color.Transparent;
            body.TextAlign = ContentAlignment.TopLeft;
            body.SetBounds(Theme.S(26), Theme.S(108), ClientSize.Width - Theme.S(52), Theme.S(250));
            body.Text =
                "Windows 10 1903 及更高版本通常已经自带 .NET Framework 4.8。\r\n\r\n" +
                "当前系统没有检测到 4.8 或更高版本，所以程序不能继续运行。\r\n\r\n" +
                "请打开「设置 → Windows 更新」，安装全部可用更新；也可以从微软官方网站下载 " +
                ".NET Framework 4.8，安装完成后重新双击 PCCheck.exe。\r\n\r\n" +
                "PCCheck 不会自动安装任何组件，也不会修改系统设置。";
            Controls.Add(body);

            Label tip = new Label();
            tip.Font = Theme.FontSmall;
            tip.ForeColor = Theme.Muted;
            tip.BackColor = Color.Transparent;
            tip.TextAlign = ContentAlignment.TopLeft;
            tip.SetBounds(Theme.S(26), Theme.S(382), ClientSize.Width - Theme.S(52), Theme.S(34));
            tip.Text = "如果系统低于 Windows 10 1903，请先升级系统。";
            Controls.Add(tip);

            RoundedButton close = new RoundedButton();
            close.Text = "退出";
            close.Primary = true;
            close.SetBounds(ClientSize.Width - Theme.S(132), Theme.S(426), Theme.S(104), Theme.S(38));
            close.Click += delegate { Close(); };
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;
        }
    }
}
