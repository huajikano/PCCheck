using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PCCheck
{
    public static class TextUtil
    {
        public static string Clean(object value)
        {
            if (value == null) return string.Empty;
            string s = value as string;
            if (s == null)
            {
                string[] arr = value as string[];
                if (arr != null) s = string.Join("、", arr);
                else
                {
                    ushort[] codes = value as ushort[];
                    if (codes != null)
                    {
                        StringBuilder sb = new StringBuilder();
                        foreach (ushort c in codes)
                        {
                            if (c != 0) sb.Append((char)c);
                        }
                        s = sb.ToString();
                    }
                    else s = Convert.ToString(value, CultureInfo.InvariantCulture);
                }
            }
            s = (s ?? string.Empty).Replace("\0", string.Empty).Trim();
            return s;
        }

        public static int Int(object value, int fallback)
        {
            if (value == null) return fallback;
            try
            {
                if (value is uint) return (int)(uint)value;
                if (value is ushort) return (int)(ushort)value;
                if (value is long) return (int)(long)value;
                if (value is ulong) return (int)(ulong)value;
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        public static long Long(object value, long fallback)
        {
            if (value == null) return fallback;
            try
            {
                if (value is ulong) return (long)(ulong)value;
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        public static ulong ULong(object value, ulong fallback)
        {
            if (value == null) return fallback;
            try
            {
                if (value is ulong) return (ulong)value;
                return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        public static DateTime? Date(object value)
        {
            if (value == null) return null;
            try
            {
                if (value is DateTime) return (DateTime)value;
                string s = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (string.IsNullOrEmpty(s)) return null;
                if (s.Length == 8 && s[0] >= '0' && s[0] <= '9' && s.IndexOf('T') < 0 && s.IndexOf(':') < 0)
                {
                    int year, month, day;
                    if (int.TryParse(s.Substring(0, 4), out year) &&
                        int.TryParse(s.Substring(4, 2), out month) &&
                        int.TryParse(s.Substring(6, 2), out day))
                    {
                        if (year >= 1980 && month >= 1 && month <= 12 && day >= 1 && day <= 31)
                            return new DateTime(year, month, day);
                    }
                }
                return ManagementDateTimeConverter.ToDateTime(s);
            }
            catch
            {
                DateTime parsed;
                if (DateTime.TryParse(TextUtil.Clean(value), CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed))
                    return parsed;
                return null;
            }
        }

        public static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            int i = text.IndexOfAny(new char[] { '\r', '\n' });
            return (i < 0 ? text : text.Substring(0, i)).Trim();
        }

        public static string JoinNonEmpty(string separator, params string[] values)
        {
            List<string> list = new List<string>();
            foreach (string s in values)
            {
                if (!string.IsNullOrEmpty(s)) list.Add(s);
            }
            return string.Join(separator, list.ToArray());
        }

        public static string Limit(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
            return text.Substring(0, Math.Max(0, max - 1)) + "…";
        }

        public static string YesNo(bool value)
        {
            return value ? "是" : "否";
        }
    }

    public static class FormatUtil
    {
        private const double Giga = 1073741824.0;

        public static string Gb(long bytes)
        {
            if (bytes <= 0) return "—";
            return (bytes / Giga).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        }

        public static string Gb(ulong bytes)
        {
            if (bytes == 0) return "—";
            return (bytes / Giga).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        }

        public static string GbValue(ulong bytes)
        {
            return (bytes / Giga).ToString("0.0", CultureInfo.InvariantCulture);
        }

        public static string Ghz(int mhz)
        {
            if (mhz <= 0) return "—";
            return (mhz / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + " GHz";
        }

        public static string Percent(double value)
        {
            return value.ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        public static string Date(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("yyyy年M月d日", CultureInfo.CurrentCulture) : "—";
        }

        public static string DateTimeText(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("yyyy年M月d日 HH:mm", CultureInfo.CurrentCulture) : "—";
        }

        public static string Duration(TimeSpan span)
        {
            if (span.TotalDays >= 1)
                return string.Format("{0} 天 {1} 小时", (int)span.TotalDays, span.Hours);
            if (span.TotalHours >= 1)
                return string.Format("{0} 小时 {1} 分钟", (int)span.TotalHours, span.Minutes);
            return string.Format("{0} 分钟", Math.Max(1, span.Minutes));
        }

        public static string Speed(long bitsPerSecond)
        {
            if (bitsPerSecond <= 0) return "—";
            if (bitsPerSecond >= 1000000000L)
                return (bitsPerSecond / 1000000000.0).ToString("0.0", CultureInfo.InvariantCulture) + " Gbps";
            return Math.Round(bitsPerSecond / 1000000.0).ToString("0", CultureInfo.InvariantCulture) + " Mbps";
        }

        public static string Latency(long milliseconds)
        {
            return milliseconds.ToString(CultureInfo.InvariantCulture) + " ms";
        }

        public static double ParseNumber(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            StringBuilder sb = new StringBuilder();
            bool started = false;
            foreach (char c in value)
            {
                if (char.IsDigit(c) || (c == '.' && started))
                {
                    sb.Append(c);
                    started = true;
                }
                else if (started)
                {
                    break;
                }
            }
            double n;
            return double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out n) ? n : 0;
        }
    }

    public static class ErrorTranslator
    {
        public static string ToHuman(Exception ex)
        {
            if (ex == null) return "暂时无法读取";
            if (ex is TimeoutException) return "读取超时";
            if (ex is UnauthorizedAccessException) return "需管理员权限";
            if (ex is SecurityException) return "需管理员权限";
            if (ex is ManagementException) return "设备不支持";
            if (ex is Win32Exception)
            {
                int code = ((Win32Exception)ex).NativeErrorCode;
                if (code == 5) return "需管理员权限";
                return "设备不支持";
            }
            if (ex is IOException) return "暂时无法读取";
            if (ex is NotSupportedException) return "设备不支持";
            return "暂时无法读取";
        }

        public static string DeviceError(int code)
        {
            switch (code)
            {
                case 1: return "设备未正确配置（代码 1）";
                case 2: return "无法加载驱动程序（代码 2）";
                case 3: return "驱动程序损坏或内存不足（代码 3）";
                case 4: return "设备文件损坏（代码 4）";
                case 5: return "驱动程序需要重新安装（代码 5）";
                case 6: return "启动配置冲突（代码 6）";
                case 7: return "系统资源分配失败（代码 7）";
                case 8: return "驱动程序加载失败（代码 8）";
                case 9: return "设备信息无效（代码 9）";
                case 10: return "设备无法启动（代码 10）";
                case 11: return "设备未正常工作（代码 11）";
                case 12: return "系统资源不足（代码 12）";
                case 13: return "设备无法识别（代码 13）";
                case 14: return "需要重启电脑（代码 14）";
                case 15: return "设备正在重新枚举（代码 15）";
                case 16: return "设备资源被占用（代码 16）";
                case 17: return "设备类型未知（代码 17）";
                case 18: return "需要重新安装驱动程序（代码 18）";
                case 19: return "注册表配置损坏（代码 19）";
                case 20: return "驱动程序安装不完整（代码 20）";
                case 21: return "系统正在移除设备（代码 21）";
                case 22: return "设备已被禁用（代码 22）";
                case 23: return "设备启动失败（代码 23）";
                case 24: return "设备不存在或未安装（代码 24）";
                case 25: return "设备被其他程序占用（代码 25）";
                case 26: return "设备驱动未加载（代码 26）";
                case 27: return "设备无法分配资源（代码 27）";
                case 28: return "未安装驱动程序（代码 28）";
                case 29: return "固件未提供必需资源（代码 29）";
                case 30: return "设备资源分配冲突（代码 30）";
                case 31: return "无法加载所需驱动（代码 31）";
                case 32: return "驱动程序服务启动失败（代码 32）";
                case 33: return "设备不支持所需操作（代码 33）";
                case 34: return "设备配置错误（代码 34）";
                case 35: return "固件版本不兼容（代码 35）";
                case 36: return "设备中断请求冲突（代码 36）";
                case 37: return "无法初始化设备（代码 37）";
                case 38: return "驱动程序无法加载（代码 38）";
                case 39: return "驱动程序损坏（代码 39）";
                case 40: return "设备信息不完整（代码 40）";
                case 41: return "设备启动参数错误（代码 41）";
                case 42: return "设备正在等待重启（代码 42）";
                case 43: return "设备报告故障（代码 43）";
                case 44: return "设备被意外移除（代码 44）";
                case 45: return "设备连接异常（代码 45）";
                case 46: return "设备初始化超时（代码 46）";
                case 47: return "设备被安全策略阻止（代码 47）";
                case 48: return "设备驱动被阻止（代码 48）";
                case 49: return "设备固件异常（代码 49）";
                case 50: return "设备电源管理异常（代码 50）";
                case 51: return "设备等待资源（代码 51）";
                case 52: return "无法验证驱动程序签名（代码 52）";
                default: return "设备异常（代码 " + code.ToString(CultureInfo.InvariantCulture) + "）";
            }
        }
    }

    public static class Wmi
    {
        public const string CimV2 = "root\\cimv2";

        public static List<Dictionary<string, object>> Query(string wql)
        {
            return Query(CimV2, wql);
        }

        public static List<Dictionary<string, object>> Query(string scopePath, string wql)
        {
            List<Dictionary<string, object>> rows = new List<Dictionary<string, object>>();
            ManagementScope scope = new ManagementScope(scopePath);
            scope.Options.Timeout = TimeSpan.FromMilliseconds(2400);
            scope.Options.EnablePrivileges = false;
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql)))
            {
                searcher.Options.Timeout = TimeSpan.FromMilliseconds(2400);
                searcher.Options.ReturnImmediately = false;
                using (ManagementObjectCollection collection = searcher.Get())
                {
                    foreach (ManagementObject mo in collection)
                    {
                        using (mo)
                        {
                            Dictionary<string, object> row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                            foreach (PropertyData p in mo.Properties)
                            {
                                try { row[p.Name] = p.Value; }
                                catch { row[p.Name] = null; }
                            }
                            try { row["__PATH"] = mo.Path == null ? string.Empty : mo.Path.Path; }
                            catch { row["__PATH"] = string.Empty; }
                            rows.Add(row);
                        }
                    }
                }
            }
            return rows;
        }
    }

    public static class NativeMethods
    {
        public const int SW_RESTORE = 9;
        public const int ENUM_CURRENT_SETTINGS = -1;
        public const uint EDD_GET_DEVICE_INTERFACE_NAME = 1;

        [DllImport("kernel32.dll")]
        public static extern bool GetFirmwareType(out uint firmwareType);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr hWnd, int command);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplayDevices(string device, uint deviceNumber, ref DISPLAY_DEVICE displayDevice, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr tcpTable, ref int size, bool order, int af,
            int tableClass, uint reserved);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        public static bool TryGetMemory(out ulong total, out ulong available)
        {
            MEMORYSTATUSEX status = new MEMORYSTATUSEX();
            status.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref status))
            {
                total = status.ullTotalPhys;
                available = status.ullAvailPhys;
                return true;
            }
            total = 0;
            available = 0;
            return false;
        }

        public static List<TcpListenerInfo> GetTcpListeners()
        {
            List<TcpListenerInfo> list = new List<TcpListenerInfo>();
            ReadTcpTable(2, 24, 8, 20, false, list);
            ReadTcpTable(23, 56, 20, 52, true, list);
            return list;
        }

        private static void ReadTcpTable(int af, int rowSize, int portOffset, int pidOffset,
            bool ipv6, List<TcpListenerInfo> list)
        {
            int size = 0;
            uint result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, 3, 0);
            if (result != 122 && result != 0) return;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                result = GetExtendedTcpTable(buffer, ref size, false, af, 3, 0);
                if (result != 0) return;
                int count = Marshal.ReadInt32(buffer);
                for (int i = 0; i < count; i++)
                {
                    IntPtr row = new IntPtr(buffer.ToInt64() + 4 + (i * rowSize));
                    int state = Marshal.ReadInt32(row, 0);
                    if (state != 2) continue; // LISTEN
                    int rawPort = Marshal.ReadInt32(row, portOffset);
                    int port = ((rawPort & 0xFF) << 8) | ((rawPort >> 8) & 0xFF);
                    int pid = Marshal.ReadInt32(row, pidOffset);
                    list.Add(new TcpListenerInfo(port, pid, ipv6));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    public sealed class TcpListenerInfo
    {
        public int Port { get; private set; }
        public int ProcessId { get; private set; }
        public bool IsIpv6 { get; private set; }

        public TcpListenerInfo(int port, int pid, bool ipv6)
        {
            Port = port;
            ProcessId = pid;
            IsIpv6 = ipv6;
        }
    }

    public sealed class CommandVersion
    {
        public bool Found { get; set; }
        public bool StoreAlias { get; set; }
        public string Path { get; set; }
        public string Version { get; set; }
        public string Output { get; set; }
        public string ErrorText { get; set; }

        public CommandVersion()
        {
            Path = string.Empty;
            Version = string.Empty;
            Output = string.Empty;
            ErrorText = string.Empty;
        }
    }

    public static class CommandProbe
    {
        public static CommandVersion FindAndRun(string exeName, string arguments, string[] extraPaths)
        {
            CommandVersion result = new CommandVersion();
            string path = FindExecutable(exeName, extraPaths);
            if (path == null)
            {
                return result;
            }
            result.Found = true;
            result.Path = path;
            try
            {
                FileInfo fi = new FileInfo(path);
                if (fi.Length < 1024 && path.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.StoreAlias = true;
                    result.ErrorText = "已安装（商店版）";
                    return result;
                }
            }
            catch
            {
            }

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = path;
            psi.Arguments = arguments;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            try { psi.WorkingDirectory = System.IO.Path.GetDirectoryName(path); }
            catch { psi.WorkingDirectory = Environment.CurrentDirectory; }

            Process process = null;
            try
            {
                process = new Process();
                process.StartInfo = psi;
                process.Start();
                Task<string> outTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errTask = process.StandardError.ReadToEndAsync();
                bool exited = process.WaitForExit(2500);
                if (!exited)
                {
                    try { process.Kill(); } catch { }
                    result.ErrorText = "读取超时";
                    return result;
                }
                try { Task.WaitAll(new Task[] { outTask, errTask }, 500); }
                catch { }
                string stdout = outTask.Status == TaskStatus.RanToCompletion ? outTask.Result : string.Empty;
                string stderr = errTask.Status == TaskStatus.RanToCompletion ? errTask.Result : string.Empty;
                result.Output = (stdout + "\n" + stderr).Trim();
                if (string.IsNullOrEmpty(result.Output))
                {
                    result.ErrorText = "版本号未提供";
                }
            }
            catch (Exception ex)
            {
                result.ErrorText = ErrorTranslator.ToHuman(ex);
            }
            finally
            {
                if (process != null)
                {
                    try { process.Dispose(); } catch { }
                }
            }
            return result;
        }

        public static string FindExecutable(string exeName, string[] extraPaths)
        {
            string fileName = exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exeName : exeName + ".exe";
            List<string> candidates = new List<string>();
            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (string raw in pathEnv.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string dir = raw.Trim().Trim('"');
                    if (dir.Length == 0) continue;
                    try { candidates.Add(Path.Combine(dir, fileName)); }
                    catch { }
                }
            }
            if (extraPaths != null)
            {
                foreach (string p in extraPaths)
                {
                    if (!string.IsNullOrEmpty(p)) candidates.Add(p);
                }
            }
            foreach (string path in candidates)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        FileInfo fi = new FileInfo(path);
                        if (fi.Length > 0) return fi.FullName;
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        public static string[] ExpandCommonPaths(params string[] patterns)
        {
            List<string> result = new List<string>();
            foreach (string pattern in patterns)
            {
                string expanded = Environment.ExpandEnvironmentVariables(pattern);
                int star = expanded.IndexOf('*');
                if (star < 0)
                {
                    result.Add(expanded);
                    continue;
                }
                int slash = expanded.LastIndexOf('\\', star);
                if (slash < 0) continue;
                string root = expanded.Substring(0, slash);
                string file = expanded.Substring(expanded.LastIndexOf('\\') + 1);
                try
                {
                    if (!Directory.Exists(root)) continue;
                    string[] dirs = Directory.GetDirectories(root);
                    foreach (string dir in dirs)
                    {
                        try { result.Add(Path.Combine(dir, file)); }
                        catch { }
                    }
                }
                catch
                {
                }
            }
            return result.ToArray();
        }
    }
}
