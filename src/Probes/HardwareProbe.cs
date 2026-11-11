using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PCCheck
{
    public static class HardwareProbe
    {
        public static void Run(ProbeContext ctx)
        {
            ctx.Slot("处理器", new string[] { "cpu.model", "cpu.cores", "cpu.threads", "cpu.clock", "cpu.socket" },
                delegate { ProbeCpu(ctx); });
            ctx.Slot("内存", new string[] { "mem.total", "mem.used", "mem.free", "mem.usage", "mem.sticks", "mem.detail" },
                delegate { ProbeMemory(ctx); });
            ctx.Slot("主板与 BIOS", new string[] { "board.vendor", "board.model", "bios.version", "bios.date" },
                delegate { ProbeBoard(ctx); });
            ctx.Slot("显卡", new string[] { "gpu.model", "gpu.vram", "gpu.driver", "gpu.driverdate" },
                delegate { ProbeGpu(ctx); });
            ctx.Slot("磁盘", new string[] { "disk.count", "disk.models", "disk.capacity", "disk.free", "disk.health", "disk.type" },
                delegate { ProbeDisks(ctx); });
            ctx.Slot("网卡", new string[] { "nic.count", "nic.models", "nic.ip", "nic.mac", "nic.speed" },
                delegate { ProbeNetworkAdapters(ctx); });
            ctx.Slot("显示器", new string[] { "display.model", "display.res", "display.hz" },
                delegate { ProbeDisplays(ctx); });
        }

        private static void ProbeCpu(ProbeContext ctx)
        {
            List<Dictionary<string, object>> rows = Wmi.Query(
                "SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed,SocketDesignation,Manufacturer FROM Win32_Processor");
            if (rows.Count == 0)
            {
                ctx.Unknown("cpu.model", "未检测到处理器", "系统未返回处理器信息。", "可能是虚拟机限制或 WMI 不可用。");
                return;
            }

            string name = NormalizeSpaces(TextUtil.Clean(rows[0]["Name"]));
            int cores = 0;
            int threads = 0;
            int clock = 0;
            List<string> sockets = new List<string>();
            foreach (Dictionary<string, object> row in rows)
            {
                cores += Math.Max(0, TextUtil.Int(row.ContainsKey("NumberOfCores") ? row["NumberOfCores"] : null, 0));
                threads += Math.Max(0, TextUtil.Int(row.ContainsKey("NumberOfLogicalProcessors") ? row["NumberOfLogicalProcessors"] : null, 0));
                clock = Math.Max(clock, TextUtil.Int(row.ContainsKey("MaxClockSpeed") ? row["MaxClockSpeed"] : null, 0));
                string socket = TextUtil.Clean(row.ContainsKey("SocketDesignation") ? row["SocketDesignation"] : null);
                if (socket.Length > 0 && !sockets.Contains(socket)) sockets.Add(socket);
            }

            if (name.Length == 0) name = "处理器型号未提供";
            ctx.Ok("cpu.model", name,
                rows.Count > 1 ? string.Format("检测到 {0} 个物理处理器。", rows.Count) : "Windows 报告的处理器信息。");
            ctx.Metric("cpu.cores", cores);

            if (cores > 0)
            {
                CheckStatus status = cores <= 1 ? CheckStatus.Warning : CheckStatus.Ok;
                ctx.Emit("cpu.cores", cores + " 核", status,
                    cores <= 1 ? "核心数较少，日常办公可用，运行大型软件会比较吃力。" : "物理核心数。", string.Empty);
            }
            else
            {
                ctx.Unknown("cpu.cores", "设备不支持", "处理器未提供核心数。", "可尝试以管理员身份运行。");
            }

            if (threads > 0)
            {
                CheckStatus status = threads <= 2 ? CheckStatus.Warning : CheckStatus.Ok;
                ctx.Emit("cpu.threads", threads + " 线程", status,
                    threads <= 2 ? "线程数较少，同时打开多个程序可能卡顿。" : "逻辑处理器数量。", string.Empty);
            }
            else
            {
                ctx.Unknown("cpu.threads", "设备不支持", "处理器未提供逻辑线程数。", "可尝试以管理员身份运行。");
            }

            if (clock > 0)
            {
                CheckStatus status = clock < 1000 ? CheckStatus.Warning : CheckStatus.Ok;
                ctx.Emit("cpu.clock", FormatUtil.Ghz(clock), status,
                    "Windows 报告的处理器标称频率。", string.Empty);
            }
            else
            {
                ctx.Unknown("cpu.clock", "设备不支持", "处理器未提供频率。", string.Empty);
            }

            if (sockets.Count > 0)
                ctx.Ok("cpu.socket", string.Join("、", sockets.ToArray()), "处理器插槽或封装类型。");
            else
                ctx.Unknown("cpu.socket", "设备不支持", "系统未提供插槽信息。", string.Empty);
        }

        private static void ProbeMemory(ProbeContext ctx)
        {
            List<Dictionary<string, object>> cs = TryQuery("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            List<Dictionary<string, object>> os = TryQuery(
                "SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem");
            List<Dictionary<string, object>> sticks = TryQuery(
                "SELECT DeviceLocator,BankLabel,Capacity,Speed,ConfiguredClockSpeed,Manufacturer,PartNumber,SMBIOSMemoryType FROM Win32_PhysicalMemory");

            ulong installed = 0;
            foreach (Dictionary<string, object> row in sticks)
            {
                installed += TextUtil.ULong(row.ContainsKey("Capacity") ? row["Capacity"] : null, 0);
            }
            ulong csTotal = cs.Count > 0 ? TextUtil.ULong(cs[0]["TotalPhysicalMemory"], 0) : 0;
            if (installed == 0) installed = csTotal;

            ulong totalVisible = 0;
            ulong free = 0;
            if (os.Count > 0)
            {
                totalVisible = TextUtil.ULong(os[0]["TotalVisibleMemorySize"], 0) * 1024UL;
                free = TextUtil.ULong(os[0]["FreePhysicalMemory"], 0) * 1024UL;
            }
            if (totalVisible == 0 || free == 0)
            {
                ulong nativeTotal;
                ulong nativeFree;
                if (NativeMethods.TryGetMemory(out nativeTotal, out nativeFree))
                {
                    if (totalVisible == 0) totalVisible = nativeTotal;
                    if (free == 0) free = nativeFree;
                }
            }
            if (installed == 0) installed = totalVisible;

            if (installed == 0)
            {
                ctx.Unknown("mem.total", "需管理员权限", "系统未返回物理内存总量。", "可尝试以管理员身份运行。");
                return;
            }

            double installedGb = installed / 1073741824.0;
            double visibleGb = totalVisible / 1073741824.0;
            double freeGb = free / 1073741824.0;
            double usedGb = Math.Max(0, visibleGb - freeGb);
            double usage = visibleGb > 0 ? usedGb / visibleGb * 100.0 : 0;

            CheckStatus totalStatus = installedGb < 4 ? CheckStatus.Critical
                : installedGb < 8 ? CheckStatus.Warning : CheckStatus.Ok;
            string totalDetail = "已安装内存。";
            if (visibleGb > 0 && installedGb - visibleGb >= 0.5)
            {
                totalDetail = string.Format("系统可见 {0:0.0} GB，其余约 {1:0.0} GB 被核显或硬件保留。",
                    visibleGb, installedGb - visibleGb);
            }
            ctx.Emit("mem.total", FormatUtil.Gb(installed), totalStatus, totalDetail,
                installedGb < 8 ? "内存偏小，建议至少 8 GB。" : string.Empty);
            ctx.Metric("mem.total.gb", installedGb);

            if (visibleGb > 0)
            {
                CheckStatus usedStatus = usage > 95 ? CheckStatus.Critical
                    : usage > 85 ? CheckStatus.Warning : CheckStatus.Ok;
                ctx.Emit("mem.used", usedGb.ToString("0.0", CultureInfo.InvariantCulture) + " GB", usedStatus,
                    string.Format("按系统可见内存计算，当前使用率 {0:0}% 。", usage), string.Empty);

                CheckStatus freeStatus = freeGb < 0.5 ? CheckStatus.Critical
                    : freeGb < 1 ? CheckStatus.Warning : CheckStatus.Ok;
                ctx.Emit("mem.free", freeGb.ToString("0.0", CultureInfo.InvariantCulture) + " GB", freeStatus,
                    "系统当前可用物理内存。", freeGb < 1 ? "可用内存偏低，建议关闭部分程序。" : string.Empty);

                CheckStatus usageStatus = usage > 95 ? CheckStatus.Critical
                    : usage > 85 ? CheckStatus.Warning : CheckStatus.Ok;
                ctx.Emit("mem.usage", FormatUtil.Percent(usage), usageStatus,
                    "使用率超过 85% 时可能出现卡顿。", string.Empty);
            }
            else
            {
                ctx.Unknown("mem.used", "读取超时", "系统未返回可用内存。", string.Empty);
                ctx.Unknown("mem.free", "读取超时", "系统未返回可用内存。", string.Empty);
                ctx.Unknown("mem.usage", "读取超时", "系统未返回内存使用率。", string.Empty);
            }

            if (sticks.Count > 0)
            {
                ctx.Ok("mem.sticks", sticks.Count + " 条", "已安装的内存条数量。");
                List<string> details = new List<string>();
                int index = 0;
                foreach (Dictionary<string, object> row in sticks)
                {
                    index++;
                    ulong cap = TextUtil.ULong(row["Capacity"], 0);
                    int speed = TextUtil.Int(row["ConfiguredClockSpeed"], 0);
                    if (speed <= 0) speed = TextUtil.Int(row["Speed"], 0);
                    string manufacturer = TranslateMemoryVendor(TextUtil.Clean(row["Manufacturer"]));
                    string locator = TextUtil.Clean(row["DeviceLocator"]);
                    string bank = TextUtil.Clean(row["BankLabel"]);
                    string part = TextUtil.Clean(row["PartNumber"]);
                    string type = MemoryType(TextUtil.Int(row["SMBIOSMemoryType"], 0));
                    List<string> pieces = new List<string>();
                    pieces.Add(FormatUtil.Gb(cap));
                    if (type.Length > 0) pieces.Add(type);
                    if (speed > 0) pieces.Add(speed.ToString(CultureInfo.InvariantCulture) + " MHz");
                    if (manufacturer.Length > 0) pieces.Add(manufacturer);
                    if (part.Length > 0) pieces.Add(part);
                    string where = bank.Length > 0 ? bank : (locator.Length > 0 ? locator : "插槽 " + index);
                    if (locator.Length > 0 && bank.Length > 0 && locator.IndexOf(bank, StringComparison.OrdinalIgnoreCase) < 0)
                        where = bank + " / " + locator;
                    details.Add(string.Format("{0}：{1}", where, string.Join(" / ", pieces.ToArray())));
                }
                ctx.Ok("mem.detail", string.Join("；", details.ToArray()), string.Join("\n", details.ToArray()));
            }
            else
            {
                ctx.Unknown("mem.sticks", "需管理员权限", "系统未返回内存条明细。", "可尝试以管理员身份运行。");
                ctx.Unknown("mem.detail", "需管理员权限", "系统未返回内存条明细。", "可尝试以管理员身份运行。");
            }
        }

        private static void ProbeBoard(ProbeContext ctx)
        {
            List<Dictionary<string, object>> boards = Wmi.Query("SELECT Manufacturer,Product,Version FROM Win32_BaseBoard");
            if (boards.Count > 0)
            {
                string vendor = TextUtil.Clean(boards[0]["Manufacturer"]);
                string product = TextUtil.Clean(boards[0]["Product"]);
                string boardVersion = TextUtil.Clean(boards[0]["Version"]);
                if (vendor.Length > 0) ctx.Ok("board.vendor", vendor, "主板制造商。");
                else ctx.Unknown("board.vendor", "设备不支持", "主板未提供厂商。", string.Empty);
                if (product.Length > 0) ctx.Ok("board.model", product,
                    boardVersion.Length > 0 ? "主板版本：" + boardVersion : "主板型号。");
                else ctx.Unknown("board.model", "设备不支持", "主板未提供型号。", string.Empty);
            }
            else
            {
                ctx.Unknown("board.vendor", "设备不支持", "系统未返回主板信息。", string.Empty);
                ctx.Unknown("board.model", "设备不支持", "系统未返回主板信息。", string.Empty);
            }

            List<Dictionary<string, object>> bios = Wmi.Query(
                "SELECT Manufacturer,SMBIOSBIOSVersion,ReleaseDate,Version FROM Win32_BIOS");
            if (bios.Count == 0)
            {
                ctx.Unknown("bios.version", "需管理员权限", "系统未返回 BIOS 信息。", "可尝试以管理员身份运行。");
                ctx.Unknown("bios.date", "设备不支持", "系统未返回 BIOS 日期。", string.Empty);
                return;
            }
            string biosVersion = TextUtil.Clean(bios[0]["SMBIOSBIOSVersion"]);
            string biosMaker = TextUtil.Clean(bios[0]["Manufacturer"]);
            if (biosVersion.Length == 0) biosVersion = TextUtil.Clean(bios[0]["Version"]);
            if (biosVersion.Length > 0)
                ctx.Ok("bios.version", biosVersion, "BIOS 版本号。" + (biosMaker.Length > 0 ? "厂商：" + biosMaker : string.Empty));
            else
                ctx.Unknown("bios.version", "设备不支持", "BIOS 未提供版本号。", string.Empty);

            DateTime? biosDate = TextUtil.Date(bios[0]["ReleaseDate"]);
            if (biosDate.HasValue)
            {
                bool old = biosDate.Value < DateTime.Today.AddYears(-8);
                ctx.Emit("bios.date", FormatUtil.Date(biosDate), old ? CheckStatus.Warning : CheckStatus.Ok,
                    old ? "BIOS 发布日期较早，如无异常不必升级。" : "BIOS 发布日期。", string.Empty);
            }
            else
            {
                ctx.Unknown("bios.date", "设备不支持", "BIOS 未提供有效日期。", string.Empty);
            }
        }

        private sealed class GpuInfo
        {
            public string Name;
            public ulong Vram;
            public string DriverVersion;
            public DateTime? DriverDate;
            public bool Virtual;
            public bool Discrete;
            public int Score;
        }

        private static void ProbeGpu(ProbeContext ctx)
        {
            List<Dictionary<string, object>> rows = Wmi.Query(
                "SELECT Name,AdapterRAM,DriverVersion,DriverDate,PNPDeviceID FROM Win32_VideoController");
            if (rows.Count == 0)
            {
                ctx.Unknown("gpu.model", "设备不支持", "系统未返回显卡信息。", string.Empty);
                return;
            }

            List<GpuInfo> gpus = new List<GpuInfo>();
            foreach (Dictionary<string, object> row in rows)
            {
                string name = NormalizeSpaces(TextUtil.Clean(row["Name"]));
                if (name.Length == 0) continue;
                GpuInfo gpu = new GpuInfo();
                gpu.Name = name;
                gpu.Vram = TextUtil.ULong(row["AdapterRAM"], 0);
                gpu.DriverVersion = TextUtil.Clean(row["DriverVersion"]);
                gpu.DriverDate = TextUtil.Date(row["DriverDate"]);
                string lower = name.ToLowerInvariant();
                gpu.Virtual = lower.Contains("virtual") || lower.Contains("remote") || lower.Contains("gameviewer")
                    || lower.Contains("parsec") || lower.Contains("sunshine");
                gpu.Discrete = !gpu.Virtual && (lower.Contains("nvidia") || lower.Contains("geforce")
                    || lower.Contains("radeon rx") || lower.Contains("radeon pro") || lower.Contains("firepro")
                    || lower.Contains("arc a") || lower.Contains("arc b"));
                if (gpu.Virtual) gpu.Score = -100;
                else if (gpu.Discrete) gpu.Score = 80;
                else if (lower.Contains("radeon")) gpu.Score = 40;
                else if (lower.Contains("intel")) gpu.Score = 30;
                else gpu.Score = 10;
                ulong regVram = ReadGpuMemoryFromRegistry(name);
                if (regVram > gpu.Vram) gpu.Vram = regVram;
                gpus.Add(gpu);
            }
            if (gpus.Count == 0)
            {
                ctx.Unknown("gpu.model", "设备不支持", "系统未返回有效显卡名称。", string.Empty);
                return;
            }

            gpus.Sort(delegate(GpuInfo a, GpuInfo b) { return b.Score.CompareTo(a.Score); });
            GpuInfo primary = gpus[0];
            List<string> modelDetails = new List<string>();
            List<string> vramDetails = new List<string>();
            List<string> driverDetails = new List<string>();
            bool anyDiscrete = false;
            foreach (GpuInfo gpu in gpus)
            {
                string kind = gpu.Virtual ? "虚拟显示器" : (gpu.Discrete ? "独立显卡" : "核心显卡");
                string vram = gpu.Vram > 0 ? FormatUtil.Gb(gpu.Vram) : "未提供";
                modelDetails.Add(string.Format("{0}（{1}，显存 {2}）", gpu.Name, kind, vram));
                vramDetails.Add(gpu.Name + "：" + vram);
                string driverDate = gpu.DriverDate.HasValue ? FormatUtil.Date(gpu.DriverDate) : "日期未提供";
                driverDetails.Add(string.Format("{0}：{1}（{2}）", gpu.Name,
                    gpu.DriverVersion.Length > 0 ? gpu.DriverVersion : "版本未提供", driverDate));
                if (gpu.Discrete) anyDiscrete = true;
            }
            ctx.Metric("gpu.discrete", anyDiscrete ? 1 : 0);

            string modelValue = primary.Name;
            if (gpus.Count > 1) modelValue += " 等 " + gpus.Count + " 个适配器";
            ctx.Ok("gpu.model", modelValue, string.Join("\n", modelDetails.ToArray()));

            if (primary.Vram > 0)
            {
                double vramGb = primary.Vram / 1073741824.0;
                ctx.Emit("gpu.vram", FormatUtil.Gb(primary.Vram),
                    vramGb < 1 ? CheckStatus.Warning : CheckStatus.Ok,
                    string.Join("\n", vramDetails.ToArray()),
                    vramGb < 1 ? "显存较小，大型游戏或图形软件可能受限。" : string.Empty);
            }
            else
            {
                ctx.Unknown("gpu.vram", "设备不支持", string.Join("\n", vramDetails.ToArray()), string.Empty);
            }

            if (primary.DriverVersion.Length > 0)
                ctx.Ok("gpu.driver", primary.DriverVersion, string.Join("\n", driverDetails.ToArray()));
            else
                ctx.Unknown("gpu.driver", "设备不支持", "显卡驱动版本未提供。", string.Empty);

            if (primary.DriverDate.HasValue)
            {
                bool old = primary.DriverDate.Value < DateTime.Today.AddYears(-3);
                ctx.Emit("gpu.driverdate", FormatUtil.Date(primary.DriverDate),
                    old ? CheckStatus.Warning : CheckStatus.Ok,
                    string.Join("\n", driverDetails.ToArray()),
                    old ? "驱动较旧，如显示异常可到显卡厂商官网更新。" : string.Empty);
            }
            else
            {
                ctx.Unknown("gpu.driverdate", "设备不支持", "显卡驱动日期未提供。", string.Empty);
            }
        }

        private static ulong ReadGpuMemoryFromRegistry(string driverDesc)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey classKey = baseKey.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (classKey == null) return 0;
                    foreach (string name in classKey.GetSubKeyNames())
                    {
                        if (name.Length != 4) continue;
                        using (RegistryKey sub = classKey.OpenSubKey(name))
                        {
                            if (sub == null) continue;
                            string desc = TextUtil.Clean(sub.GetValue("DriverDesc"));
                            if (!string.Equals(desc, driverDesc, StringComparison.OrdinalIgnoreCase)) continue;
                            object value = sub.GetValue("HardwareInformation.qwMemorySize");
                            if (value is long) return (ulong)Math.Max(0, (long)value);
                            if (value is int) return (ulong)Math.Max(0, (int)value);
                            if (value is byte[])
                            {
                                byte[] bytes = (byte[])value;
                                if (bytes.Length >= 8) return BitConverter.ToUInt64(bytes, 0);
                                if (bytes.Length >= 4) return BitConverter.ToUInt32(bytes, 0);
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return 0;
        }

        private sealed class VolumeInfo
        {
            public string Drive;
            public long Size;
            public long Free;
        }

        private sealed class StorageInfo
        {
            public string FriendlyName;
            public ulong Size;
            public int MediaType;
            public int BusType;
            public int Health;
            public string Serial;
        }

        private sealed class DiskInfo
        {
            public int Index;
            public string Model;
            public ulong Size;
            public string Interface;
            public string MediaType;
            public string Status;
            public string Serial;
            public StorageInfo Storage;
            public List<VolumeInfo> Volumes = new List<VolumeInfo>();
        }

        private static void ProbeDisks(ProbeContext ctx)
        {
            List<Dictionary<string, object>> rows = Wmi.Query(
                "SELECT Index,Model,Size,InterfaceType,MediaType,SerialNumber,Status,DeviceID FROM Win32_DiskDrive");
            if (rows.Count == 0)
            {
                ctx.Emit("disk.count", "0 块", CheckStatus.Critical,
                    "系统未检测到磁盘，或磁盘读取被限制。", "可尝试以管理员身份运行后重新检测。");
                return;
            }

            List<Dictionary<string, object>> storageRows = new List<Dictionary<string, object>>();
            try
            {
                storageRows = Wmi.Query("root\\Microsoft\\Windows\\Storage",
                    "SELECT DeviceId,FriendlyName,SerialNumber,Size,MediaType,BusType,HealthStatus,OperationalStatus FROM MSFT_PhysicalDisk");
            }
            catch
            {
            }

            List<DiskInfo> disks = new List<DiskInfo>();
            foreach (Dictionary<string, object> row in rows)
            {
                DiskInfo disk = new DiskInfo();
                disk.Index = TextUtil.Int(row["Index"], -1);
                disk.Model = NormalizeSpaces(TextUtil.Clean(row["Model"]));
                disk.Size = TextUtil.ULong(row["Size"], 0);
                disk.Interface = TextUtil.Clean(row["InterfaceType"]);
                disk.MediaType = TextUtil.Clean(row["MediaType"]);
                disk.Status = TextUtil.Clean(row["Status"]);
                disk.Serial = TextUtil.Clean(row["SerialNumber"]);
                if (disk.Model.Length == 0) disk.Model = "型号未提供";
                disk.Storage = MatchStorage(disk, storageRows);
                disks.Add(disk);
            }

            List<VolumeInfo> volumes = new List<VolumeInfo>();
            try
            {
                foreach (Dictionary<string, object> row in Wmi.Query(
                    "SELECT DeviceID,VolumeName,Size,FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3"))
                {
                    VolumeInfo v = new VolumeInfo();
                    v.Drive = TextUtil.Clean(row["DeviceID"]);
                    v.Size = TextUtil.Long(row["Size"], 0);
                    v.Free = TextUtil.Long(row["FreeSpace"], 0);
                    if (v.Drive.Length > 0) volumes.Add(v);
                }
            }
            catch
            {
            }

            try
            {
                MapVolumes(disks, volumes);
            }
            catch
            {
            }

            ctx.Metric("disk.count", disks.Count);
            ulong totalSize = 0;
            long totalFree = 0;
            long totalVolumeSize = 0;
            bool anyNvme = false;
            bool allSsd = true;
            bool anyKnownType = false;
            List<string> modelDetails = new List<string>();
            List<string> capacityDetails = new List<string>();
            List<string> freeDetails = new List<string>();
            List<string> healthDetails = new List<string>();
            List<string> typeDetails = new List<string>();
            CheckStatus healthStatus = CheckStatus.Ok;
            CheckStatus freeStatus = CheckStatus.Ok;
            bool freeUnknown = false;
            double worstFreePercent = 100;

            foreach (DiskInfo disk in disks)
            {
                totalSize += disk.Size;
                string type = DiskTypeText(disk);
                if (type.Contains("NVMe")) anyNvme = true;
                if (!type.Contains("固态")) allSsd = false;
                if (type.Contains("固态") || type.Contains("机械")) anyKnownType = true;
                modelDetails.Add(string.Format("磁盘 {0}：{1}", disk.Index, disk.Model));
                capacityDetails.Add(string.Format("磁盘 {0}：{1}", disk.Index, FormatUtil.Gb(disk.Size)));
                typeDetails.Add(string.Format("磁盘 {0}：{1}", disk.Index, type));

                string healthText;
                CheckStatus oneHealth = HealthStatusOf(disk, out healthText);
                if (oneHealth == CheckStatus.Critical) healthStatus = CheckStatus.Critical;
                else if (oneHealth == CheckStatus.Warning && healthStatus != CheckStatus.Critical) healthStatus = CheckStatus.Warning;
                else if (oneHealth == CheckStatus.Unknown && healthStatus == CheckStatus.Ok) healthStatus = CheckStatus.Unknown;
                healthDetails.Add(string.Format("磁盘 {0}：{1}", disk.Index, healthText));

                if (disk.Volumes.Count == 0)
                {
                    freeUnknown = true;
                    freeDetails.Add(string.Format("磁盘 {0}：未找到已格式化的本地分区", disk.Index));
                    continue;
                }
                long diskFree = 0;
                long diskVolumeSize = 0;
                foreach (VolumeInfo v in disk.Volumes)
                {
                    diskFree += v.Free;
                    diskVolumeSize += v.Size;
                }
                totalFree += diskFree;
                totalVolumeSize += diskVolumeSize;
                double percent = diskVolumeSize > 0 ? diskFree * 100.0 / diskVolumeSize : 0;
                if (percent < worstFreePercent) worstFreePercent = percent;
                if (percent < 5) freeStatus = CheckStatus.Critical;
                else if (percent < 10 && freeStatus != CheckStatus.Critical) freeStatus = CheckStatus.Warning;
                freeDetails.Add(string.Format("磁盘 {0}：剩余 {1} / {2}（{3:0.0}%）",
                    disk.Index, FormatUtil.Gb(diskFree), FormatUtil.Gb(diskVolumeSize), percent));
                foreach (VolumeInfo v in disk.Volumes)
                {
                    double vp = v.Size > 0 ? v.Free * 100.0 / v.Size : 0;
                    freeDetails.Add(string.Format("  {0} 可用 {1}（{2:0.0}%）", v.Drive, FormatUtil.Gb(v.Free), vp));
                    if (vp < 5) freeStatus = CheckStatus.Critical;
                    else if (vp < 10 && freeStatus != CheckStatus.Critical) freeStatus = CheckStatus.Warning;
                }
            }

            ctx.Ok("disk.count", disks.Count + " 块", "系统识别的物理磁盘数量。");
            string firstModel = disks[0].Model;
            if (disks.Count > 1) firstModel += " 等 " + disks.Count + " 块";
            ctx.Ok("disk.models", firstModel, string.Join("\n", modelDetails.ToArray()));
            ctx.Ok("disk.capacity", FormatUtil.Gb(totalSize), string.Join("\n", capacityDetails.ToArray()));

            if (!freeUnknown)
            {
                string freeValue = FormatUtil.Gb(totalFree);
                if (totalVolumeSize > 0)
                    freeValue += string.Format("（{0:0.0}%）", totalFree * 100.0 / totalVolumeSize);
                ctx.Emit("disk.free", freeValue, freeStatus, string.Join("\n", freeDetails.ToArray()),
                    freeStatus == CheckStatus.Critical ? "有分区剩余空间不足 5%，建议尽快清理。" :
                    freeStatus == CheckStatus.Warning ? "有分区剩余空间不足 10%，建议清理。" : string.Empty);
            }
            else
            {
                ctx.Unknown("disk.free", "设备不支持", string.Join("\n", freeDetails.ToArray()),
                    "未找到分区与磁盘的对应关系。");
            }

            ctx.Emit("disk.health", HealthSummary(healthStatus, disks.Count), healthStatus,
                string.Join("\n", healthDetails.ToArray()),
                healthStatus == CheckStatus.Critical ? "磁盘报告异常，请立即备份重要数据。" : string.Empty);

            string typeValue = anyKnownType
                ? (allSsd ? (anyNvme ? "NVMe 固态硬盘" : "固态硬盘") : (anyNvme ? "含 NVMe 固态硬盘" : "混合硬盘"))
                : "未知";
            ctx.Emit("disk.type", typeValue,
                anyKnownType ? CheckStatus.Ok : CheckStatus.Unknown,
                string.Join("\n", typeDetails.ToArray()), string.Empty);
            ctx.Metric("disk.nvme", anyNvme ? 1 : 0);
        }

        private static void MapVolumes(List<DiskInfo> disks, List<VolumeInfo> volumes)
        {
            Dictionary<int, DiskInfo> byIndex = new Dictionary<int, DiskInfo>();
            foreach (DiskInfo d in disks) byIndex[d.Index] = d;
            Dictionary<string, string> partitionToDrive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, VolumeInfo> volumeByDrive = new Dictionary<string, VolumeInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (VolumeInfo v in volumes) volumeByDrive[v.Drive] = v;

            List<Dictionary<string, object>> logicalMap = Wmi.Query(
                "SELECT Antecedent,Dependent FROM Win32_LogicalDiskToPartition");
            foreach (Dictionary<string, object> row in logicalMap)
            {
                string ant = TextUtil.Clean(row["Antecedent"]);
                string dep = TextUtil.Clean(row["Dependent"]);
                string part = PartitionKey(ant);
                string drive = LogicalDriveKey(dep);
                if (part.Length > 0 && drive.Length > 0) partitionToDrive[part] = drive;
            }

            List<Dictionary<string, object>> diskMap = Wmi.Query(
                "SELECT Antecedent,Dependent FROM Win32_DiskDriveToDiskPartition");
            foreach (Dictionary<string, object> row in diskMap)
            {
                string ant = TextUtil.Clean(row["Antecedent"]);
                string dep = TextUtil.Clean(row["Dependent"]);
                int index = PhysicalDriveIndex(ant);
                string part = PartitionKey(dep);
                DiskInfo disk;
                if (index < 0 || part.Length == 0 || !byIndex.TryGetValue(index, out disk)) continue;
                string drive;
                if (!partitionToDrive.TryGetValue(part, out drive)) continue;
                VolumeInfo volume;
                if (!volumeByDrive.TryGetValue(drive, out volume)) continue;
                bool exists = false;
                foreach (VolumeInfo v in disk.Volumes) if (string.Equals(v.Drive, drive, StringComparison.OrdinalIgnoreCase)) exists = true;
                if (!exists) disk.Volumes.Add(volume);
            }

            if (disks.Count == 1 && disks[0].Volumes.Count == 0 && volumes.Count > 0)
            {
                disks[0].Volumes.AddRange(volumes);
            }
        }

        private static StorageInfo MatchStorage(DiskInfo disk, List<Dictionary<string, object>> rows)
        {
            foreach (Dictionary<string, object> row in rows)
            {
                string id = TextUtil.Clean(row["DeviceId"]);
                string serial = TextUtil.Clean(row["SerialNumber"]);
                string friendly = TextUtil.Clean(row["FriendlyName"]);
                ulong size = TextUtil.ULong(row["Size"], 0);
                bool idMatch = id == disk.Index.ToString(CultureInfo.InvariantCulture);
                bool serialMatch = disk.Serial.Length > 0 && string.Equals(serial, disk.Serial, StringComparison.OrdinalIgnoreCase);
                bool nameMatch = friendly.Length > 0 && disk.Model.Length > 0 &&
                    (disk.Model.IndexOf(friendly, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     friendly.IndexOf(disk.Model, StringComparison.OrdinalIgnoreCase) >= 0);
                bool sizeMatch = disk.Size > 0 && size > 0 && Math.Abs((double)size - disk.Size) / disk.Size < 0.02;
                if (!idMatch && !serialMatch && !(nameMatch && sizeMatch)) continue;
                StorageInfo s = new StorageInfo();
                s.FriendlyName = friendly;
                s.Size = size;
                s.MediaType = TextUtil.Int(row["MediaType"], -1);
                s.BusType = TextUtil.Int(row["BusType"], -1);
                s.Health = TextUtil.Int(row["HealthStatus"], -1);
                s.Serial = serial;
                return s;
            }
            return null;
        }

        private static string DiskTypeText(DiskInfo disk)
        {
            if (disk.Storage != null)
            {
                string media = disk.Storage.MediaType == 4 ? "固态硬盘"
                    : disk.Storage.MediaType == 3 ? "机械硬盘"
                    : disk.Storage.MediaType == 5 ? "存储级内存" : "未知类型";
                string bus = BusText(disk.Storage.BusType);
                return bus.Length > 0 ? media + "（" + bus + "）" : media;
            }
            string lower = (disk.Model + " " + disk.MediaType).ToLowerInvariant();
            if (lower.Contains("nvme")) return "固态硬盘（NVMe）";
            if (lower.Contains("ssd") || lower.Contains("solid state")) return "固态硬盘";
            if (lower.Contains("hdd") || lower.Contains("hard disk")) return "机械硬盘";
            return "未知";
        }

        private static string BusText(int busType)
        {
            switch (busType)
            {
                case 17: return "NVMe";
                case 11: return "SATA";
                case 8: return "RAID";
                case 10: return "SAS";
                case 7: return "USB";
                case 6: return "光纤通道";
                case 15: return "文件共享";
                default: return string.Empty;
            }
        }

        private static CheckStatus HealthStatusOf(DiskInfo disk, out string text)
        {
            if (disk.Storage != null)
            {
                switch (disk.Storage.Health)
                {
                    case 0:
                        text = "良好";
                        return CheckStatus.Ok;
                    case 1:
                        text = "预警，建议备份数据";
                        return CheckStatus.Warning;
                    case 2:
                        text = "异常，请立即备份数据";
                        return CheckStatus.Critical;
                    default:
                        text = "未能读取健康状态";
                        return CheckStatus.Unknown;
                }
            }
            string status = disk.Status.ToLowerInvariant();
            if (status == "ok" || status.Length == 0)
            {
                text = "系统报告正常";
                return CheckStatus.Ok;
            }
            if (status.Contains("pred fail") || status.Contains("warning"))
            {
                text = "系统报告预警";
                return CheckStatus.Warning;
            }
            if (status.Contains("error") || status.Contains("degraded") || status.Contains("fail"))
            {
                text = "系统报告异常";
                return CheckStatus.Critical;
            }
            text = "系统未提供状态";
            return CheckStatus.Unknown;
        }

        private static string HealthSummary(CheckStatus status, int count)
        {
            switch (status)
            {
                case CheckStatus.Ok: return count == 1 ? "良好" : "全部良好";
                case CheckStatus.Warning: return "有磁盘预警";
                case CheckStatus.Critical: return "有磁盘异常";
                default: return "部分未知";
            }
        }

        private static void ProbeNetworkAdapters(ProbeContext ctx)
        {
            NetworkInterface[] all = NetworkInterface.GetAllNetworkInterfaces();
            List<NetworkInterface> nics = new List<NetworkInterface>();
            foreach (NetworkInterface nic in all)
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                nics.Add(nic);
            }

            if (nics.Count == 0)
            {
                ctx.Emit("nic.count", "0 个", CheckStatus.Critical,
                    "没有处于连接状态的网卡。", "请检查网线、Wi-Fi 开关或网络适配器状态。");
                return;
            }

            ctx.Ok("nic.count", nics.Count + " 个", "处于连接状态的物理或虚拟网卡数量。");
            List<string> models = new List<string>();
            List<string> ips = new List<string>();
            List<string> macs = new List<string>();
            List<string> speeds = new List<string>();
            bool hasUsableIp = false;
            bool hasOnlyLinkLocal = false;

            foreach (NetworkInterface nic in nics)
            {
                string desc = TextUtil.Clean(nic.Description);
                if (desc.Length == 0) desc = "未命名网卡";
                models.Add(desc);
                IPInterfaceProperties props = nic.GetIPProperties();
                List<string> ipv4 = new List<string>();
                List<string> ipv6 = new List<string>();
                foreach (UnicastIPAddressInformation addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        ipv4.Add(addr.Address.ToString());
                        if (!addr.Address.ToString().StartsWith("169.254.")) hasUsableIp = true;
                        else hasOnlyLinkLocal = true;
                    }
                    else if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 &&
                        !addr.Address.IsIPv6LinkLocal)
                    {
                        ipv6.Add(addr.Address.ToString());
                    }
                }
                if (ipv4.Count > 0) ips.Add(desc + "：" + string.Join("、", ipv4.ToArray()));
                else if (ipv6.Count > 0) ips.Add(desc + "：" + string.Join("、", ipv6.ToArray()));
                else ips.Add(desc + "：未获取到 IP");
                byte[] mac = nic.GetPhysicalAddress().GetAddressBytes();
                macs.Add(desc + "：" + (mac.Length > 0 ? string.Join("-", mac.Select(delegate(byte b) { return b.ToString("X2", CultureInfo.InvariantCulture); }).ToArray()) : "未提供"));
                try { speeds.Add(desc + "：" + FormatUtil.Speed(nic.Speed)); }
                catch { speeds.Add(desc + "：速率未提供"); }
            }

            ctx.Ok("nic.models", string.Join("；", models.ToArray()), string.Join("\n", models.ToArray()));
            ctx.Emit("nic.ip", string.Join("；", ips.ToArray()),
                hasUsableIp ? CheckStatus.Ok : (hasOnlyLinkLocal ? CheckStatus.Warning : CheckStatus.Unknown),
                string.Join("\n", ips.ToArray()),
                hasUsableIp ? string.Empty : "未获取到可用的 IPv4 地址，请检查网络连接。");
            ctx.Ok("nic.mac", string.Join("；", macs.ToArray()), string.Join("\n", macs.ToArray()));
            CheckStatus speedStatus = CheckStatus.Ok;
            foreach (string line in speeds)
            {
                if (line.Contains("Mbps") && !line.Contains("1000 Mbps") && !line.Contains(" Gbps")) speedStatus = CheckStatus.Warning;
            }
            ctx.Emit("nic.speed", string.Join("；", speeds.ToArray()), speedStatus,
                string.Join("\n", speeds.ToArray()), speedStatus == CheckStatus.Warning ? "连接速率低于 1 Gbps，内网传输会偏慢。" : string.Empty);
        }

        private static void ProbeDisplays(ProbeContext ctx)
        {
            List<Dictionary<string, object>> monitors = new List<Dictionary<string, object>>();
            try
            {
                monitors = Wmi.Query("root\\wmi",
                    "SELECT InstanceName,UserFriendlyName,ManufacturerName,ProductCodeID,YearOfManufacture FROM WmiMonitorID");
            }
            catch
            {
            }

            List<string> models = new List<string>();
            bool specific = false;
            foreach (Dictionary<string, object> row in monitors)
            {
                string friendly = TextUtil.Clean(row["UserFriendlyName"]);
                string mfgCode = TextUtil.Clean(row["ManufacturerName"]);
                string product = TextUtil.Clean(row["ProductCodeID"]);
                string instance = TextUtil.Clean(row["InstanceName"]);
                int year = TextUtil.Int(row["YearOfManufacture"], 0);
                string mfg = TranslateMonitorVendor(mfgCode);
                string code = product;
                if (code.Length == 0 && instance.Length > 0)
                {
                    string[] parts = instance.Split('\\');
                    if (parts.Length > 1) code = parts[1];
                }
                if (friendly.Length > 0)
                {
                    models.Add(mfg.Length > 0 ? mfg + " " + friendly : friendly);
                    specific = true;
                }
                else if (code.Length > 0)
                {
                    string label = (mfg.Length > 0 ? mfg + " " : string.Empty) + code;
                    if (year > 0) label += "（" + year + " 年）";
                    models.Add(label);
                    specific = true;
                }
            }

            if (models.Count == 0)
            {
                foreach (Dictionary<string, object> row in TryQuery("SELECT Name,DeviceID FROM Win32_DesktopMonitor"))
                {
                    string name = TextUtil.Clean(row["Name"]);
                    if (name.Length > 0 && !name.Contains("默认") && !name.Contains("通用"))
                    {
                        models.Add(name);
                        specific = true;
                    }
                }
            }
            if (models.Count == 0)
            {
                foreach (Screen screen in Screen.AllScreens)
                {
                    models.Add(screen.Primary ? "主显示器（型号未提供）" : "扩展显示器（型号未提供）");
                }
            }

            ctx.Metric("display.count", models.Count);
            if (models.Count > 0)
            {
                string value = models[0];
                if (models.Count > 1) value += " 等 " + models.Count + " 台";
                ctx.Emit("display.model", value, specific ? CheckStatus.Ok : CheckStatus.Unknown,
                    string.Join("\n", models.ToArray()),
                    specific ? string.Empty : "显示器未提供型号，这在使用转接器或虚拟机时很常见。");
            }
            else
            {
                ctx.Unknown("display.model", "未提供型号", "系统未返回显示器信息。", string.Empty);
            }

            List<Dictionary<string, object>> gpus = TryQuery(
                "SELECT Name,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate FROM Win32_VideoController");
            int width = 0;
            int height = 0;
            int hz = 0;
            List<string> resolutions = new List<string>();
            List<string> refreshRates = new List<string>();
            foreach (Dictionary<string, object> row in gpus)
            {
                string name = TextUtil.Clean(row["Name"]);
                int w = TextUtil.Int(row["CurrentHorizontalResolution"], 0);
                int h = TextUtil.Int(row["CurrentVerticalResolution"], 0);
                int r = TextUtil.Int(row["CurrentRefreshRate"], 0);
                if (w > 0 && h > 0)
                {
                    resolutions.Add(name + "：" + w + " × " + h);
                    if (width == 0) { width = w; height = h; }
                }
                if (r > 0)
                {
                    refreshRates.Add(name + "：" + r + " Hz");
                    if (hz == 0) hz = r;
                }
            }
            if (width == 0 && Screen.PrimaryScreen != null)
            {
                width = Screen.PrimaryScreen.Bounds.Width;
                height = Screen.PrimaryScreen.Bounds.Height;
                resolutions.Add("主显示器：" + width + " × " + height);
            }
            if (width > 0)
            {
                CheckStatus status = width >= 1920 ? CheckStatus.Ok
                    : width < 1366 ? CheckStatus.Warning : CheckStatus.Warning;
                ctx.Emit("display.res", width + " × " + height, status,
                    string.Join("\n", resolutions.ToArray()),
                    width < 1920 ? "分辨率偏低，部分软件的界面可能显示不全。" : string.Empty);
            }
            else
            {
                ctx.Unknown("display.res", "读取超时", "未能读取当前显示分辨率。", string.Empty);
            }

            if (hz > 0)
            {
                CheckStatus status = hz < 50 ? CheckStatus.Warning : CheckStatus.Ok;
                ctx.Emit("display.hz", hz + " Hz", status,
                    string.Join("\n", refreshRates.ToArray()),
                    hz < 50 ? "刷新率偏低，长时间使用可能更容易疲劳。" : string.Empty);
            }
            else
            {
                ctx.Unknown("display.hz", "设备不支持", "显示器未报告刷新率。", string.Empty);
            }
        }

        private static List<Dictionary<string, object>> TryQuery(string wql)
        {
            try { return Wmi.Query(wql); }
            catch { return new List<Dictionary<string, object>>(); }
        }

        private static string NormalizeSpaces(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return Regex.Replace(value, "\\s+", " ").Trim();
        }

        private static string TranslateMemoryVendor(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string lower = value.ToLowerInvariant();
            if (lower.Contains("samsung")) return "三星";
            if (lower.Contains("hynix")) return "SK 海力士";
            if (lower.Contains("micron")) return "美光";
            if (lower.Contains("kingston")) return "金士顿";
            if (lower.Contains("crucial")) return "英睿达";
            if (lower.Contains("adata")) return "威刚";
            if (lower.Contains("corsair")) return "海盗船";
            if (lower.Contains("unknown") || lower.Contains("not specified")) return "厂商未提供";
            return value;
        }

        private static string MemoryType(int code)
        {
            switch (code)
            {
                case 20: return "DDR";
                case 21: return "DDR2";
                case 24: return "DDR3";
                case 26: return "DDR4";
                case 34: return "DDR5";
                default: return string.Empty;
            }
        }

        private static string TranslateMonitorVendor(string code)
        {
            if (string.IsNullOrEmpty(code)) return string.Empty;
            switch (code.ToUpperInvariant())
            {
                case "LEN": return "联想";
                case "DEL": return "戴尔";
                case "SAM": return "三星";
                case "SEC": return "三星";
                case "HWP": return "惠普";
                case "HPN": return "惠普";
                case "AUS": return "华硕";
                case "ACR": return "宏碁";
                case "AOC": return "AOC";
                case "GSM": return "LG";
                case "BNQ": return "明基";
                case "MSI": return "微星";
                case "PHI": return "飞利浦";
                case "IVM": return "冠捷";
                case "VSC": return "优派";
                default: return code;
            }
        }

        private static int PhysicalDriveIndex(string path)
        {
            Match m = Regex.Match(path ?? string.Empty, "PHYSICALDRIVE(\\d+)", RegexOptions.IgnoreCase);
            int index;
            return m.Success && int.TryParse(m.Groups[1].Value, out index) ? index : -1;
        }

        private static string PartitionKey(string path)
        {
            Match m = Regex.Match(path ?? string.Empty, "Disk #(\\d+), Partition #(\\d+)", RegexOptions.IgnoreCase);
            if (!m.Success) return string.Empty;
            return m.Groups[1].Value + ":" + m.Groups[2].Value;
        }

        private static string LogicalDriveKey(string path)
        {
            Match m = Regex.Match(path ?? string.Empty, "Win32_LogicalDisk.DeviceID=\"([A-Za-z]:)\"");
            return m.Success ? m.Groups[1].Value : string.Empty;
        }
    }
}
