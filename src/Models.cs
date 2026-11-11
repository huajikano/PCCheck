using System;
using System.Collections.Generic;
using System.Globalization;

namespace PCCheck
{
    public enum CheckStatus
    {
        Ok = 0,
        Warning = 1,
        Unknown = 2,
        Critical = 3
    }

    public enum CheckGroup
    {
        Hardware = 0,
        System = 1,
        Drivers = 2,
        Runtime = 3,
        Network = 4
    }

    public sealed class CheckDefinition
    {
        public string Id { get; private set; }
        public CheckGroup Group { get; private set; }
        public string Section { get; private set; }
        public string Name { get; private set; }
        public string Unit { get; private set; }
        public string Source { get; private set; }
        public string SourceDetail { get; private set; }
        public string Degrade { get; private set; }

        public CheckDefinition(string id, CheckGroup group, string section, string name, string unit,
            string source, string sourceDetail, string degrade)
        {
            Id = id;
            Group = group;
            Section = section;
            Name = name;
            Unit = unit;
            Source = source;
            SourceDetail = sourceDetail;
            Degrade = degrade;
        }
    }

    public sealed class CheckResult
    {
        public string Id { get; private set; }
        public string Value { get; private set; }
        public string Detail { get; private set; }
        public string Hint { get; private set; }
        public CheckStatus Status { get; private set; }

        public CheckResult(string id, string value, string detail, string hint, CheckStatus status)
        {
            Id = id;
            Value = string.IsNullOrEmpty(value) ? "—" : value;
            Detail = detail ?? string.Empty;
            Hint = hint ?? string.Empty;
            Status = status;
        }
    }

    public sealed class CheckCounts
    {
        public int Total { get; private set; }
        public int Ok { get; private set; }
        public int Warning { get; private set; }
        public int Critical { get; private set; }
        public int Unknown { get; private set; }

        public int Problems
        {
            get { return Warning + Critical; }
        }

        public static CheckCounts From(IEnumerable<CheckResult> results)
        {
            CheckCounts c = new CheckCounts();
            foreach (CheckResult r in results)
            {
                c.Total++;
                switch (r.Status)
                {
                    case CheckStatus.Ok:
                        c.Ok++;
                        break;
                    case CheckStatus.Warning:
                        c.Warning++;
                        break;
                    case CheckStatus.Critical:
                        c.Critical++;
                        break;
                    default:
                        c.Unknown++;
                        break;
                }
            }
            return c;
        }
    }

    public static class StatusText
    {
        public static string Of(CheckStatus status)
        {
            switch (status)
            {
                case CheckStatus.Ok: return "正常";
                case CheckStatus.Warning: return "注意";
                case CheckStatus.Critical: return "严重";
                default: return "未知";
            }
        }

        public static string GroupTitle(CheckGroup group)
        {
            switch (group)
            {
                case CheckGroup.Hardware: return "硬件";
                case CheckGroup.System: return "系统与安全";
                case CheckGroup.Drivers: return "驱动与设备";
                case CheckGroup.Runtime: return "运行库与环境";
                default: return "网络与端口";
            }
        }

        public static string SourceLabel(string source)
        {
            if (string.IsNullOrEmpty(source)) return "只读检测";
            switch (source)
            {
                case "WMI": return "系统信息";
                case "WMI + 注册表": return "系统信息 + 注册表";
                case "系统 API": return "系统接口";
                case "注册表": return "系统注册表";
                case "命令": return "版本命令";
                case "文件": return "系统文件";
                case "网络": return "网络测试";
                default: return "只读检测";
            }
        }
    }

    public static class CheckCatalog
    {
        public static readonly List<CheckDefinition> All = new List<CheckDefinition>();
        private static readonly Dictionary<string, CheckDefinition> ById =
            new Dictionary<string, CheckDefinition>(StringComparer.OrdinalIgnoreCase);

        public static int Count
        {
            get { return All.Count; }
        }

        public static CheckDefinition Get(string id)
        {
            CheckDefinition d;
            return ById.TryGetValue(id, out d) ? d : null;
        }

        static CheckCatalog()
        {
            // 硬件：处理器
            Add("cpu.model", CheckGroup.Hardware, "处理器", "CPU 型号", "文本", "WMI",
                "Win32_Processor.Name", "未检测到处理器");
            Add("cpu.cores", CheckGroup.Hardware, "处理器", "CPU 物理核心", "核", "WMI",
                "Win32_Processor.NumberOfCores", "设备不支持");
            Add("cpu.threads", CheckGroup.Hardware, "处理器", "CPU 逻辑线程", "线程", "WMI",
                "Win32_Processor.NumberOfLogicalProcessors", "设备不支持");
            Add("cpu.clock", CheckGroup.Hardware, "处理器", "CPU 基准频率", "GHz", "WMI",
                "Win32_Processor.MaxClockSpeed", "设备不支持");
            Add("cpu.socket", CheckGroup.Hardware, "处理器", "CPU 插槽", "文本", "WMI",
                "Win32_Processor.SocketDesignation", "设备不支持");

            // 内存
            Add("mem.total", CheckGroup.Hardware, "内存", "内存总容量", "GB", "WMI",
                "Win32_ComputerSystem.TotalPhysicalMemory", "需管理员权限 / 设备不支持");
            Add("mem.used", CheckGroup.Hardware, "内存", "内存已用", "GB", "WMI",
                "Win32_OperatingSystem 内存计数", "读取超时");
            Add("mem.free", CheckGroup.Hardware, "内存", "内存可用", "GB", "WMI",
                "Win32_OperatingSystem.FreePhysicalMemory", "读取超时");
            Add("mem.usage", CheckGroup.Hardware, "内存", "内存使用率", "%", "WMI",
                "已用 ÷ 物理内存总量", "读取超时");
            Add("mem.sticks", CheckGroup.Hardware, "内存", "内存条数量", "条", "WMI",
                "Win32_PhysicalMemory 行数", "需管理员权限 / 设备不支持");
            Add("mem.detail", CheckGroup.Hardware, "内存", "内存条明细", "每条一行", "WMI",
                "Win32_PhysicalMemory 容量、频率、插槽", "需管理员权限 / 设备不支持");

            // 主板与 BIOS
            Add("board.vendor", CheckGroup.Hardware, "主板与 BIOS", "主板厂商", "文本", "WMI",
                "Win32_BaseBoard.Manufacturer", "设备不支持");
            Add("board.model", CheckGroup.Hardware, "主板与 BIOS", "主板型号", "文本", "WMI",
                "Win32_BaseBoard.Product", "设备不支持");
            Add("bios.version", CheckGroup.Hardware, "主板与 BIOS", "BIOS 版本", "文本", "WMI",
                "Win32_BIOS.SMBIOSBIOSVersion", "需管理员权限 / 设备不支持");
            Add("bios.date", CheckGroup.Hardware, "主板与 BIOS", "BIOS 发布日期", "日期", "WMI",
                "Win32_BIOS.ReleaseDate", "设备不支持");

            // 显卡
            Add("gpu.model", CheckGroup.Hardware, "显卡", "显卡型号", "文本", "WMI",
                "Win32_VideoController.Name", "设备不支持");
            Add("gpu.vram", CheckGroup.Hardware, "显卡", "显存容量", "GB", "WMI + 注册表",
                "AdapterRAM / HardwareInformation.qwMemorySize", "设备不支持");
            Add("gpu.driver", CheckGroup.Hardware, "显卡", "显卡驱动版本", "版本号", "WMI",
                "Win32_VideoController.DriverVersion", "设备不支持");
            Add("gpu.driverdate", CheckGroup.Hardware, "显卡", "显卡驱动日期", "日期", "WMI",
                "Win32_VideoController.DriverDate", "设备不支持");

            // 磁盘
            Add("disk.count", CheckGroup.Hardware, "磁盘", "磁盘数量", "块", "WMI",
                "Win32_DiskDrive 行数", "设备不支持");
            Add("disk.models", CheckGroup.Hardware, "磁盘", "磁盘型号（逐块）", "每块一行", "WMI",
                "Win32_DiskDrive.Model / SerialNumber", "需管理员权限 / 设备不支持");
            Add("disk.capacity", CheckGroup.Hardware, "磁盘", "磁盘总容量（逐块）", "GB", "WMI",
                "Win32_DiskDrive.Size", "设备不支持");
            Add("disk.free", CheckGroup.Hardware, "磁盘", "磁盘剩余空间（逐块）", "GB / %", "WMI",
                "Win32_DiskDrive 关联逻辑分区", "设备不支持");
            Add("disk.health", CheckGroup.Hardware, "磁盘", "磁盘健康状态", "中文状态", "WMI",
                "MSFT_PhysicalDisk.HealthStatus / Win32_DiskDrive.Status", "需管理员权限 / 设备不支持");
            Add("disk.type", CheckGroup.Hardware, "磁盘", "磁盘类型", "SSD / HDD", "WMI",
                "MSFT_PhysicalDisk.MediaType / BusType", "设备不支持");

            // 网卡
            Add("nic.count", CheckGroup.Hardware, "网卡", "网卡数量", "个", "系统 API",
                "NetworkInterface.GetAllNetworkInterfaces", "设备不支持");
            Add("nic.models", CheckGroup.Hardware, "网卡", "网卡型号", "每块一行", "系统 API",
                "NetworkInterface.Description", "设备不支持");
            Add("nic.ip", CheckGroup.Hardware, "网卡", "网卡 IP 地址", "每块一行", "系统 API",
                "UnicastAddresses", "未连接 / 无 IP");
            Add("nic.mac", CheckGroup.Hardware, "网卡", "网卡 MAC 地址", "每块一行", "系统 API",
                "NetworkInterface.PhysicalAddress", "设备不支持");
            Add("nic.speed", CheckGroup.Hardware, "网卡", "网卡连接速率", "Mbps", "系统 API",
                "NetworkInterface.Speed", "未连接");

            // 显示器
            Add("display.model", CheckGroup.Hardware, "显示器", "显示器型号", "每台一行", "WMI + 系统 API",
                "WmiMonitorID / EnumDisplayDevices", "未提供型号");
            Add("display.res", CheckGroup.Hardware, "显示器", "显示器分辨率", "宽×高", "系统 API",
                "EnumDisplaySettings dmPelsWidth/Height", "读取超时");
            Add("display.hz", CheckGroup.Hardware, "显示器", "显示器刷新率", "Hz", "系统 API",
                "EnumDisplaySettings dmDisplayFrequency", "设备不支持");

            // 操作系统
            Add("os.name", CheckGroup.System, "操作系统", "系统名称", "文本", "WMI",
                "Win32_OperatingSystem.Caption", "需管理员权限 / 设备不支持");
            Add("os.version", CheckGroup.System, "操作系统", "版本号", "文本", "WMI + 注册表",
                "Version / DisplayVersion", "设备不支持");
            Add("os.build", CheckGroup.System, "操作系统", "内部版本", "文本", "注册表",
                "CurrentBuild + UBR", "设备不支持");
            Add("os.install", CheckGroup.System, "操作系统", "安装日期", "日期", "WMI",
                "Win32_OperatingSystem.InstallDate", "设备不支持");
            Add("os.arch", CheckGroup.System, "操作系统", "系统架构", "文本", "WMI",
                "OSArchitecture", "设备不支持");
            Add("os.computer", CheckGroup.System, "操作系统", "计算机名", "文本", "系统 API",
                "Environment.MachineName", "未知");
            Add("os.boot", CheckGroup.System, "操作系统", "本次开机时间", "日期时间", "WMI",
                "Win32_OperatingSystem.LastBootUpTime", "设备不支持");
            Add("os.uptime", CheckGroup.System, "操作系统", "已运行时长", "天/小时", "WMI",
                "当前时间 − LastBootUpTime", "设备不支持");
            Add("patch.recent", CheckGroup.System, "补丁", "最近安装的补丁", "最近 5 条", "WMI",
                "Win32_QuickFixEngineering", "需管理员权限 / 设备不支持");
            Add("patch.count", CheckGroup.System, "补丁", "已安装补丁总数", "条", "WMI",
                "Win32_QuickFixEngineering 行数", "设备不支持");
            Add("sec.uac", CheckGroup.System, "安全与权限", "UAC 状态", "文本", "注册表",
                "Policies\\System\\EnableLUA", "设备不支持");
            Add("sec.secureboot", CheckGroup.System, "安全与权限", "安全启动", "文本", "注册表",
                "SecureBoot\\State\\UEFISecureBootEnabled", "无法读取（可能为传统启动）");
            Add("sec.firmware", CheckGroup.System, "安全与权限", "启动模式", "文本", "系统 API",
                "GetFirmwareType", "设备不支持");
            Add("sec.priv", CheckGroup.System, "安全与权限", "当前运行权限", "文本", "系统 API",
                "WindowsPrincipal.IsInRole", "未知");

            // 驱动与设备
            Add("dev.problemcount", CheckGroup.Drivers, "异常设备", "异常设备数量", "个", "WMI",
                "Win32_PnPEntity.ConfigManagerErrorCode", "需管理员权限 / 设备不支持");
            Add("dev.problems", CheckGroup.Drivers, "异常设备", "异常设备清单", "每行一台", "WMI",
                "Win32_PnPEntity 名称与错误码", "需管理员权限 / 设备不支持");
            Add("dev.drivercount", CheckGroup.Drivers, "驱动概览", "驱动总数", "个", "WMI",
                "Win32_PnPSignedDriver 行数", "设备不支持");
            Add("dev.keydrivers", CheckGroup.Drivers, "驱动概览", "关键驱动版本", "每类一行", "WMI",
                "显卡、网卡、存储控制器驱动", "需管理员权限 / 设备不支持");
            Add("dev.pnpcount", CheckGroup.Drivers, "驱动概览", "即插即用设备总数", "个", "WMI",
                "Win32_PnPEntity 行数", "设备不支持");

            // 运行库
            Add("rt.vcppx86", CheckGroup.Runtime, "运行库", "VC++ 运行库（x86）", "各版本一行", "注册表",
                "卸载项 Visual C++ Redistributable（32 位视图）", "未安装");
            Add("rt.vcppx64", CheckGroup.Runtime, "运行库", "VC++ 运行库（x64）", "各版本一行", "注册表",
                "卸载项 Visual C++ Redistributable（64 位视图）", "未安装");
            Add("rt.netfx", CheckGroup.Runtime, "运行库", ".NET Framework", "版本号", "注册表",
                "NDP\\v4\\Full Release", "未安装 / 需管理员权限");
            Add("rt.netcore", CheckGroup.Runtime, "运行库", ".NET 运行时（Core/5+）", "各版本一行", "文件",
                "dotnet\\shared 目录", "未安装");
            Add("rt.directx", CheckGroup.Runtime, "运行库", "DirectX", "版本号", "文件",
                "d3d12.dll / d3d11.dll / dxgi.dll", "设备不支持");

            // 开发环境
            Add("dev.python", CheckGroup.Runtime, "开发环境", "Python", "版本号", "命令",
                "python --version", "未安装");
            Add("dev.node", CheckGroup.Runtime, "开发环境", "Node.js", "版本号", "命令",
                "node -v", "未安装");
            Add("dev.java", CheckGroup.Runtime, "开发环境", "Java", "版本号", "命令",
                "java -version", "未安装");
            Add("dev.git", CheckGroup.Runtime, "开发环境", "Git", "版本号", "命令",
                "git --version", "未安装");
            Add("dev.docker", CheckGroup.Runtime, "开发环境", "Docker", "版本号", "命令",
                "docker --version", "未安装");

            // 网络
            Add("net.gateway", CheckGroup.Network, "连接状态", "默认网关", "IPv4", "系统 API",
                "NetworkInterface GatewayAddresses", "未配置");
            Add("net.gatewayping", CheckGroup.Network, "连接状态", "网关连通性", "毫秒", "网络",
                "ICMP Ping 网关", "无法连通 / 读取超时");
            Add("net.dns", CheckGroup.Network, "外网测试", "外网 DNS 解析", "文本", "网络",
                "Dns.GetHostAddresses(www.baidu.com)", "DNS 解析失败");
            Add("net.latency", CheckGroup.Network, "外网测试", "外网延迟", "毫秒", "网络",
                "Ping 解析地址", "无法连通");
            Add("net.ports", CheckGroup.Network, "端口", "监听端口 Top 12", "每行一个", "系统 API",
                "GetExtendedTcpTable + 进程映射", "需管理员权限 / 设备不支持");
            Add("net.active", CheckGroup.Network, "连接状态", "活动网络连接数", "个", "系统 API",
                "NetworkInterface.OperationalStatus", "设备不支持");

            foreach (CheckDefinition d in All)
            {
                ById[d.Id] = d;
            }
        }

        private static void Add(string id, CheckGroup group, string section, string name, string unit,
            string source, string sourceDetail, string degrade)
        {
            All.Add(new CheckDefinition(id, group, section, name, unit, source, sourceDetail, degrade));
        }
    }
}
