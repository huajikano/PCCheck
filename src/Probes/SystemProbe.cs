using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Principal;
using Microsoft.Win32;

namespace PCCheck
{
    public static class SystemProbe
    {
        public static void Run(ProbeContext ctx)
        {
            ctx.Slot("操作系统", new string[] {
                "os.name", "os.version", "os.build", "os.install", "os.arch",
                "os.computer", "os.boot", "os.uptime" }, delegate { ProbeOs(ctx); });
            ctx.Slot("补丁", new string[] { "patch.recent", "patch.count" }, delegate { ProbePatches(ctx); });
            ctx.Slot("安全与权限", new string[] { "sec.uac", "sec.secureboot", "sec.firmware", "sec.priv" },
                delegate { ProbeSecurity(ctx); });
        }

        private static void ProbeOs(ProbeContext ctx)
        {
            List<Dictionary<string, object>> rows = TryQuery(
                "SELECT Caption,Version,BuildNumber,OSArchitecture,InstallDate,LastBootUpTime FROM Win32_OperatingSystem");
            Dictionary<string, object> os = rows.Count > 0 ? rows[0] : null;

            object displayVersionObj = ReadRegistry(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
            object productNameObj = ReadRegistry(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName");
            object currentBuildObj = ReadRegistry(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild");
            object ubrObj = ReadRegistry(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR");
            object installDateObj = ReadRegistry(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "InstallDate");

            string caption = os != null ? TextUtil.Clean(os["Caption"]) : string.Empty;
            if (caption.Length == 0) caption = TextUtil.Clean(productNameObj);
            string build = os != null ? TextUtil.Clean(os["BuildNumber"]) : TextUtil.Clean(currentBuildObj);
            int buildNo;
            int.TryParse(build, out buildNo);
            if (buildNo >= 22000 && caption.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0)
                caption = ReplaceIgnoreCase(caption, "Windows 10", "Windows 11");
            caption = NormalizeWindowsName(caption, buildNo);

            string version = os != null ? TextUtil.Clean(os["Version"]) : string.Empty;
            string displayVersion = TextUtil.Clean(displayVersionObj);
            if (caption.Length > 0)
                ctx.Ok("os.name", caption, "Windows 系统名称与版本。");
            else
                ctx.Unknown("os.name", "设备不支持", "系统未返回名称。", string.Empty);

            if (version.Length > 0)
            {
                string value = version;
                if (displayVersion.Length > 0) value += "（" + displayVersion + "）";
                ctx.Ok("os.version", value, "主版本号与功能更新版本。");
            }
            else
            {
                ctx.Unknown("os.version", "设备不支持", "系统未返回版本号。", string.Empty);
            }

            int ubr = TextUtil.Int(ubrObj, 0);
            if (build.Length > 0)
            {
                string buildValue = build;
                if (ubr > 0) buildValue += "." + ubr.ToString(CultureInfo.InvariantCulture);
                ctx.Ok("os.build", buildValue, "Windows 内部版本号。");
            }
            else
            {
                ctx.Unknown("os.build", "设备不支持", "系统未返回内部版本。", string.Empty);
            }

            DateTime? install = os != null ? TextUtil.Date(os["InstallDate"]) : null;
            if (!install.HasValue && installDateObj != null)
            {
                int seconds = TextUtil.Int(installDateObj, 0);
                if (seconds > 0) install = new DateTime(1970, 1, 1).AddSeconds(seconds).ToLocalTime();
            }
            if (install.HasValue)
                ctx.Ok("os.install", FormatUtil.Date(install), "系统安装日期。");
            else
                ctx.Unknown("os.install", "设备不支持", "系统未提供安装日期。", string.Empty);

            string arch = os != null ? TextUtil.Clean(os["OSArchitecture"]) : string.Empty;
            if (arch.Length == 0) arch = Environment.Is64BitOperatingSystem ? "64 位" : "32 位";
            bool is32 = arch.Contains("32");
            ctx.Emit("os.arch", arch, is32 ? CheckStatus.Warning : CheckStatus.Ok,
                is32 ? "32 位系统最多只能使用约 4 GB 内存。" : "操作系统架构。", string.Empty);

            ctx.Ok("os.computer", Environment.MachineName, "当前计算机名称。");

            DateTime? boot = os != null ? TextUtil.Date(os["LastBootUpTime"]) : null;
            if (boot.HasValue)
            {
                ctx.Ok("os.boot", FormatUtil.DateTimeText(boot), "本次开机时间。");
                TimeSpan span = DateTime.Now - boot.Value;
                if (span.TotalSeconds < 0) span = TimeSpan.Zero;
                bool longRun = span.TotalDays >= 30;
                ctx.Emit("os.uptime", FormatUtil.Duration(span), longRun ? CheckStatus.Warning : CheckStatus.Ok,
                    longRun ? "连续运行超过 30 天，建议择机重启。" : "自本次开机以来的运行时长。", string.Empty);
            }
            else
            {
                ctx.Unknown("os.boot", "设备不支持", "系统未提供开机时间。", string.Empty);
                ctx.Unknown("os.uptime", "设备不支持", "系统未提供运行时长。", string.Empty);
            }
        }

        private static void ProbePatches(ProbeContext ctx)
        {
            List<Dictionary<string, object>> rows = Wmi.Query(
                "SELECT HotFixID,Description,InstalledOn FROM Win32_QuickFixEngineering");
            if (rows.Count == 0)
            {
                ctx.Emit("patch.recent", "0 条", CheckStatus.Warning,
                    "系统未返回补丁记录；也可能是 Windows 更新由组织统一管理。",
                    "建议检查 Windows 更新。");
                ctx.Warning("patch.count", "0 条", "系统未返回补丁记录。", string.Empty);
                return;
            }

            List<PatchInfo> patches = new List<PatchInfo>();
            foreach (Dictionary<string, object> row in rows)
            {
                PatchInfo p = new PatchInfo();
                p.Id = TextUtil.Clean(row["HotFixID"]);
                p.Date = TextUtil.Date(row["InstalledOn"]);
                if (p.Id.Length == 0 && !p.Date.HasValue) continue;
                patches.Add(p);
            }
            patches.Sort(delegate(PatchInfo a, PatchInfo b)
            {
                DateTime da = a.Date.HasValue ? a.Date.Value : DateTime.MinValue;
                DateTime db = b.Date.HasValue ? b.Date.Value : DateTime.MinValue;
                int cmp = db.CompareTo(da);
                return cmp != 0 ? cmp : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
            });

            List<string> lines = new List<string>();
            for (int i = 0; i < patches.Count && i < 5; i++)
            {
                PatchInfo p = patches[i];
                lines.Add(p.Date.HasValue ? p.Id + " · " + FormatUtil.Date(p.Date) : p.Id + " · 日期未提供");
            }
            string recentValue = patches.Count > 0 ? patches[0].Id + " 等 " + patches.Count + " 条" : "0 条";
            CheckStatus recentStatus = patches.Count >= 5 ? CheckStatus.Ok : CheckStatus.Warning;
            ctx.Emit("patch.recent", recentValue, recentStatus,
                lines.Count > 0 ? string.Join("\n", lines.ToArray()) : "未提供补丁日期。",
                recentStatus == CheckStatus.Warning ? "近期补丁记录较少，建议检查 Windows 更新。" : string.Empty);
            ctx.Emit("patch.count", patches.Count + " 条", patches.Count > 0 ? CheckStatus.Ok : CheckStatus.Warning,
                "由系统补丁记录统计，可能不包含功能更新。", string.Empty);
        }

        private static void ProbeSecurity(ProbeContext ctx)
        {
            object uac = ReadRegistry(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA");
            if (uac == null)
            {
                ctx.Unknown("sec.uac", "需管理员权限", "未能读取 UAC 设置。", "可尝试以管理员身份运行。");
            }
            else
            {
                int value = TextUtil.Int(uac, 1);
                if (value == 1)
                    ctx.Ok("sec.uac", "已开启", "用户账户控制已启用，安装软件时会请求确认。");
                else
                    ctx.Warning("sec.uac", "已关闭", "UAC 已关闭，程序可直接获得管理员权限，安全风险较高。",
                        "除非有特殊需要，建议在系统设置中重新开启 UAC。");
            }

            string firmware = "未知";
            uint fw;
            try
            {
                if (NativeMethods.GetFirmwareType(out fw))
                {
                    if (fw == 1) firmware = "传统 BIOS";
                    else if (fw == 2) firmware = "UEFI";
                }
            }
            catch
            {
            }
            if (firmware == "未知")
                ctx.Unknown("sec.firmware", "设备不支持", "未能识别启动模式。", string.Empty);
            else
                ctx.Emit("sec.firmware", firmware, firmware == "UEFI" ? CheckStatus.Ok : CheckStatus.Warning,
                    firmware == "UEFI" ? "系统使用 UEFI 启动。" : "系统使用传统 BIOS 启动，部分安全功能不可用。",
                    firmware == "UEFI" ? string.Empty : "较新的电脑通常使用 UEFI；如无特殊需要，不建议自行改动。");

            object secureBoot = ReadRegistry(
                @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
            if (secureBoot != null)
            {
                int enabled = TextUtil.Int(secureBoot, -1);
                if (enabled == 1)
                    ctx.Ok("sec.secureboot", "已开启", "安全启动已开启，可阻止未签名的启动组件。");
                else if (enabled == 0)
                    ctx.Warning("sec.secureboot", "未开启", "安全启动未开启，系统启动阶段防护较弱。",
                        "如使用双系统或特殊硬件，请确认后再调整 BIOS 设置。");
                else
                    ctx.Unknown("sec.secureboot", "无法读取", "系统未返回安全启动状态。", string.Empty);
            }
            else
            {
                if (firmware == "传统 BIOS")
                    ctx.Unknown("sec.secureboot", "不适用（传统 BIOS）", "传统 BIOS 启动不支持安全启动。", string.Empty);
                else
                    ctx.Unknown("sec.secureboot", "无法读取（可能为传统启动）", "系统未提供安全启动状态。", string.Empty);
            }

            bool admin = IsAdministrator();
            if (admin)
                ctx.Ok("sec.priv", "管理员", "当前以管理员权限运行，可读取更多硬件信息。");
            else
                ctx.Unknown("sec.priv", "普通用户", "当前以普通用户权限运行；标有「需管理员权限」的项目会降级显示，程序仍可正常使用。",
                    "需要完整信息时，可右键选择「以管理员身份运行」。");
        }

        private static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        private static List<Dictionary<string, object>> TryQuery(string wql)
        {
            try { return Wmi.Query(wql); }
            catch { return new List<Dictionary<string, object>>(); }
        }

        private static object ReadRegistry(string path, string name)
        {
            object value = ReadRegistryView(RegistryView.Registry64, path, name);
            if (value != null) return value;
            return ReadRegistryView(RegistryView.Registry32, path, name);
        }

        private static object ReadRegistryView(RegistryView view, string path, string name)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (RegistryKey key = baseKey.OpenSubKey(path))
                {
                    return key == null ? null : key.GetValue(name);
                }
            }
            catch
            {
                return null;
            }
        }

        private sealed class PatchInfo
        {
            public string Id;
            public DateTime? Date;
        }

        private static string NormalizeWindowsName(string caption, int build)
        {
            if (string.IsNullOrEmpty(caption)) return string.Empty;
            string text = caption.Replace("Microsoft ", string.Empty).Trim();
            if (build >= 22000 && text.IndexOf("Windows 11", StringComparison.OrdinalIgnoreCase) < 0)
            {
                if (text.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0)
                    text = ReplaceIgnoreCase(text, "Windows 10", "Windows 11");
                else
                    text = "Windows 11 " + text;
            }
            text = ReplaceIgnoreCase(text, "Professional Workstation", "专业工作站版");
            text = ReplaceIgnoreCase(text, "Pro for Workstations", "专业工作站版");
            text = ReplaceIgnoreCase(text, "Professional", "专业版");
            text = ReplaceIgnoreCase(text, "Enterprise", "企业版");
            text = ReplaceIgnoreCase(text, "Education", "教育版");
            text = ReplaceIgnoreCase(text, "Home", "家庭版");
            text = ReplaceIgnoreCase(text, "Pro", "专业版");
            text = ReplaceIgnoreCase(text, "Windows 11 Pro", "Windows 11 专业版");
            text = ReplaceIgnoreCase(text, "Windows 10 Pro", "Windows 10 专业版");
            return RegexReplaceSpaces(text);
        }

        private static string ReplaceIgnoreCase(string input, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(input)) return input;
            int index = input.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return input;
            return input.Substring(0, index) + newValue + input.Substring(index + oldValue.Length);
        }

        private static string RegexReplaceSpaces(string input)
        {
            string result = input;
            while (result.Contains("  ")) result = result.Replace("  ", " ");
            return result.Trim();
        }
    }
}
