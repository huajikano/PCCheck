using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PCCheck
{
    public static class EnvironmentProbe
    {
        public static void Run(ProbeContext ctx)
        {
            ctx.Slot("运行库", new string[] { "rt.vcppx86", "rt.vcppx64", "rt.netfx", "rt.netcore", "rt.directx" },
                delegate { ProbeRuntimes(ctx); });

            ctx.Slot("Python", new string[] { "dev.python" }, delegate
            {
                ProbeTool(ctx, "dev.python", "python.exe", "--version",
                    CommandProbe.ExpandCommonPaths(
                        "%LocalAppData%\\Programs\\Python\\Python*\\python.exe",
                        "%ProgramFiles%\\Python*\\python.exe",
                        "%ProgramFiles(x86)%\\Python*\\python.exe",
                        "C:\\Python*\\python.exe"),
                    "Python\\s+([0-9][0-9A-Za-z.\\-]*)");
            });

            ctx.Slot("Node.js", new string[] { "dev.node" }, delegate
            {
                ProbeTool(ctx, "dev.node", "node.exe", "-v",
                    new string[] {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs\\node.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs\\node.exe") },
                    "v?([0-9]+\\.[0-9]+\\.[0-9]+)");
            });

            ctx.Slot("Java", new string[] { "dev.java" }, delegate
            {
                List<string> paths = new List<string>();
                string javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
                if (!string.IsNullOrEmpty(javaHome)) paths.Add(Path.Combine(javaHome, "bin\\java.exe"));
                paths.AddRange(CommandProbe.ExpandCommonPaths(
                    "%ProgramFiles%\\Java\\jdk*\\bin\\java.exe",
                    "%ProgramFiles%\\Eclipse Adoptium\\jdk*\\bin\\java.exe",
                    "%ProgramFiles%\\Microsoft\\jdk*\\bin\\java.exe",
                    "%ProgramFiles%\\Zulu\\zulu*\\bin\\java.exe",
                    "%ProgramFiles(x86)%\\Java\\jdk*\\bin\\java.exe"));
                ProbeTool(ctx, "dev.java", "java.exe", "-version", paths.ToArray(),
                    "version\\s+\"?([0-9][0-9A-Za-z._\\-]*)");
            });

            ctx.Slot("Git", new string[] { "dev.git" }, delegate
            {
                ProbeTool(ctx, "dev.git", "git.exe", "--version",
                    new string[] {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git\\cmd\\git.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git\\cmd\\git.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs\\Git\\cmd\\git.exe") },
                    "git version\\s+([0-9][0-9A-Za-z.\\-]*)");
            });

            ctx.Slot("Docker", new string[] { "dev.docker" }, delegate
            {
                ProbeTool(ctx, "dev.docker", "docker.exe", "--version",
                    new string[] {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker\\Docker\\resources\\bin\\docker.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DockerDesktop\\cli-bin\\docker.exe") },
                    "Docker version\\s+([0-9][0-9A-Za-z.\\-]*)");
            });
        }

        private static void ProbeRuntimes(ProbeContext ctx)
        {
            VcResult x86 = ReadVc(true, false);
            VcResult x64 = ReadVc(false, true);
            EmitVc(ctx, "rt.vcppx86", x86, "x86");
            EmitVc(ctx, "rt.vcppx64", x64, "x64");

            object releaseObj = ReadRegistryView(RegistryView.Registry64,
                @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release");
            object versionObj = ReadRegistryView(RegistryView.Registry64,
                @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Version");
            if (releaseObj == null)
            {
                releaseObj = ReadRegistryView(RegistryView.Registry32,
                    @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release");
                versionObj = ReadRegistryView(RegistryView.Registry32,
                    @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Version");
            }
            if (releaseObj == null)
            {
                ctx.Unknown("rt.netfx", "未安装 / 需管理员权限", "未能读取 .NET Framework 版本。",
                    "本程序需要 .NET Framework 4.8；Windows 10 1903 及以上系统已自带。");
            }
            else
            {
                int release = TextUtil.Int(releaseObj, 0);
                string version = TextUtil.Clean(versionObj);
                string name = NetFxName(release);
                string value = name;
                if (version.Length > 0) value += "（" + version + "）";
                bool good = release >= 528040 || version.StartsWith("4.8", StringComparison.OrdinalIgnoreCase);
                ctx.Emit("rt.netfx", value, good ? CheckStatus.Ok : CheckStatus.Critical,
                    "Release 值：" + release.ToString(CultureInfo.InvariantCulture) + "。本程序要求 4.8 或更高版本。",
                    good ? string.Empty : "请通过 Windows 更新安装 .NET Framework 4.8。");
            }

            List<string> coreVersions;
            bool coreReadable;
            DotnetRuntimes(out coreVersions, out coreReadable);
            if (coreVersions.Count > 0)
            {
                string latest = coreVersions[0];
                ctx.Ok("rt.netcore", latest, "已安装的 .NET 运行时：\n" + string.Join("\n", coreVersions.ToArray()));
            }
            else if (coreReadable)
            {
                ctx.Unknown("rt.netcore", "未安装", "未检测到 .NET Core / .NET 5 及以上运行时。", "未安装不影响本程序运行。");
            }
            else
            {
                ctx.Unknown("rt.netcore", "暂时无法读取", "未能读取 .NET 运行时目录。", string.Empty);
            }

            string sys = Environment.SystemDirectory;
            string d3d12 = Path.Combine(sys, "d3d12.dll");
            string d3d11 = Path.Combine(sys, "d3d11.dll");
            string dxgi = Path.Combine(sys, "dxgi.dll");
            string v12 = FileVersion(d3d12);
            string v11 = FileVersion(d3d11);
            string vdxgi = FileVersion(dxgi);
            if (v12.Length > 0)
            {
                ctx.Ok("rt.directx", "DirectX 12（" + v12 + "）",
                    "d3d12.dll：" + v12 + "\nd3d11.dll：" + (v11.Length > 0 ? v11 : "未提供") +
                    "\ndxgi.dll：" + (vdxgi.Length > 0 ? vdxgi : "未提供"));
            }
            else if (v11.Length > 0)
            {
                ctx.Warning("rt.directx", "DirectX 11（" + v11 + "）",
                    "系统未提供 DirectX 12 组件，可能只支持 DirectX 11。",
                    "如游戏提示缺少 DirectX，可通过 Windows 更新补齐系统组件。");
            }
            else
            {
                ctx.Critical("rt.directx", "缺少关键组件", "未找到 DirectX 图形组件。",
                    "部分游戏和图形软件可能无法启动，建议运行 Windows 更新。");
            }
        }

        private static void EmitVc(ProbeContext ctx, string id, VcResult result, string arch)
        {
            if (!result.Readable)
            {
                ctx.Unknown(id, "需管理员权限", "未能读取 " + arch + " 运行库安装记录。", "可尝试以管理员身份运行。");
                return;
            }
            if (result.Entries.Count == 0)
            {
                ctx.Warning(id, "未安装", "未检测到 " + arch + " 版 VC++ 运行库。",
                    "部分软件可能提示缺少运行库；需要时可安装微软官方运行库合集。");
                return;
            }
            string value = "已安装 " + result.Entries.Count + " 个版本";
            ctx.Ok(id, value, string.Join("\n", result.Entries.ToArray()));
        }

        private static VcResult ReadVc(bool includeX86, bool includeX64)
        {
            VcResult result = new VcResult();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<VcEntry> entries = new List<VcEntry>();
            bool anyReadable = false;
            RegistryView[] views = new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 };
            foreach (RegistryView view in views)
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (uninstall == null) continue;
                        anyReadable = true;
                        foreach (string subName in uninstall.GetSubKeyNames())
                        {
                            try
                            {
                                using (RegistryKey sub = uninstall.OpenSubKey(subName))
                                {
                                    if (sub == null) continue;
                                    string displayName = TextUtil.Clean(sub.GetValue("DisplayName"));
                                    if (displayName.Length == 0) continue;
                                    string lower = displayName.ToLowerInvariant();
                                    if (!lower.Contains("visual c++") || !lower.Contains("redistributable")) continue;
                                    string arch;
                                    if (lower.Contains("x64")) arch = "x64";
                                    else if (lower.Contains("x86")) arch = "x86";
                                    else arch = view == RegistryView.Registry64 ? "x64" : "x86";
                                    if (arch == "x86" && !includeX86) continue;
                                    if (arch == "x64" && !includeX64) continue;
                                    string version = TextUtil.Clean(sub.GetValue("DisplayVersion"));
                                    string year = VcYear(displayName);
                                    string label = year;
                                    if (label.Length == 0)
                                    {
                                        Match vmatch = Regex.Match(displayName, "\\bv(1[0-9]|[1-9])\\b", RegexOptions.IgnoreCase);
                                        label = vmatch.Success ? "v" + vmatch.Groups[1].Value : "其他版本";
                                    }
                                    if (version.Length > 0) label += "：" + version;
                                    string key = arch + "|" + label;
                                    if (seen.Contains(key)) continue;
                                    seen.Add(key);
                                    entries.Add(new VcEntry(label, year, version));
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            entries.Sort(delegate(VcEntry a, VcEntry b)
            {
                int ya;
                int yb;
                int.TryParse(a.Year.Length >= 4 ? a.Year.Substring(0, 4) : "0", out ya);
                int.TryParse(b.Year.Length >= 4 ? b.Year.Substring(0, 4) : "0", out yb);
                int cmp = yb.CompareTo(ya);
                return cmp != 0 ? cmp : string.Compare(b.Version, a.Version, StringComparison.OrdinalIgnoreCase);
            });
            foreach (VcEntry e in entries) result.Entries.Add(e.Label);
            result.Readable = anyReadable;
            return result;
        }

        private static string VcYear(string name)
        {
            Match m = Regex.Match(name, "(20[0-9]{2}(?:\\s*-\\s*20[0-9]{2})?)");
            return m.Success ? Regex.Replace(m.Groups[1].Value, "\\s+", string.Empty) : string.Empty;
        }

        private static string NetFxName(int release)
        {
            if (release >= 533320) return "4.8.1";
            if (release >= 528040) return "4.8";
            if (release >= 461808) return "4.7.2";
            if (release >= 460798) return "4.7";
            if (release >= 394806) return "4.6.2";
            if (release >= 393295) return "4.6";
            if (release >= 379893) return "4.5.2";
            if (release >= 378675) return "4.5.1";
            if (release >= 378389) return "4.5";
            return "4.x 或更低";
        }

        private static void DotnetRuntimes(out List<string> versions, out bool readable)
        {
            versions = new List<string>();
            readable = false;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] roots = new string[] {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };
            string[] families = new string[] { "Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App" };
            foreach (string root in roots)
            {
                foreach (string family in families)
                {
                    string dir = Path.Combine(root, "dotnet\\shared\\" + family);
                    try
                    {
                        if (!Directory.Exists(dir)) continue;
                        readable = true;
                        string[] subs = Directory.GetDirectories(dir);
                        foreach (string sub in subs)
                        {
                            string name = Path.GetFileName(sub);
                            if (!Regex.IsMatch(name, "^[0-9]+\\.[0-9]+")) continue;
                            string display = family == "Microsoft.NETCore.App"
                                ? ".NET " + name : ".NET 桌面运行时 " + name;
                            if (seen.Contains(display)) continue;
                            seen.Add(display);
                            versions.Add(display);
                        }
                    }
                    catch
                    {
                        readable = false;
                    }
                }
            }
            versions.Sort(delegate(string a, string b)
            {
                return string.Compare(b, a, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static string FileVersion(string path)
        {
            try
            {
                if (!File.Exists(path)) return string.Empty;
                string version = FileVersionInfo.GetVersionInfo(path).FileVersion;
                if (string.IsNullOrEmpty(version)) return string.Empty;
                int space = version.IndexOf(' ');
                return (space > 0 ? version.Substring(0, space) : version).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void ProbeTool(ProbeContext ctx, string id, string exe, string arguments,
            string[] extraPaths, string pattern)
        {
            CommandVersion cv = CommandProbe.FindAndRun(exe, arguments, extraPaths);
            if (!cv.Found)
            {
                ctx.Unknown(id, "未安装", "在 PATH 和常见安装目录中均未找到。", "未安装不影响本程序运行。");
                return;
            }
            if (cv.StoreAlias)
            {
                ctx.Warning(id, "已安装（商店版）", "系统只提供应用商店别名，未读取到版本号。", "可在命令行中运行以确认版本。");
                return;
            }
            if (string.IsNullOrEmpty(cv.Version))
                cv.Version = ParseVersion(cv.Output, pattern);
            if (!string.IsNullOrEmpty(cv.Version))
            {
                ctx.Ok(id, cv.Version, "安装位置：" + cv.Path);
                return;
            }
            if (cv.ErrorText == "读取超时")
                ctx.Unknown(id, "读取超时", "版本命令超过 2.5 秒未返回。", "可稍后重新检测。");
            else
                ctx.Warning(id, "版本号未提供", "已找到程序，但未能解析出版本号。", "安装位置：" + cv.Path);
        }

        private static string ParseVersion(string output, string pattern)
        {
            if (string.IsNullOrEmpty(output)) return string.Empty;
            Match m = Regex.Match(output, pattern, RegexOptions.IgnoreCase);
            if (!m.Success) return string.Empty;
            string version = m.Groups[1].Value.Trim().TrimEnd('.');
            if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase)) version = version.Substring(1);
            return version;
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

        private sealed class VcResult
        {
            public bool Readable;
            public List<string> Entries = new List<string>();

            public VcResult()
            {
                Readable = false;
            }
        }

        private sealed class VcEntry
        {
            public string Label;
            public string Year;
            public string Version;

            public VcEntry(string label, string year, string version)
            {
                Label = label;
                Year = year;
                Version = version;
            }
        }
    }
}
