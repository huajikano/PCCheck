using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PCCheck
{
    public static class DriverProbe
    {
        public static void Run(ProbeContext ctx)
        {
            ctx.Slot("设备", new string[] { "dev.problemcount", "dev.problems", "dev.pnpcount" },
                delegate { ProbeDevices(ctx); });
            ctx.Slot("驱动", new string[] { "dev.drivercount", "dev.keydrivers" },
                delegate { ProbeDrivers(ctx); });
        }

        private static void ProbeDevices(ProbeContext ctx)
        {
            List<Dictionary<string, object>> rows = Wmi.Query(
                "SELECT Name,ConfigManagerErrorCode,Status,PNPClass FROM Win32_PnPEntity");
            int count = rows.Count;
            ctx.Ok("dev.pnpcount", count + " 个", "Windows 识别的即插即用设备总数。");
            ctx.Metric("dev.pnpcount", count);

            List<ProblemDevice> problems = new List<ProblemDevice>();
            foreach (Dictionary<string, object> row in rows)
            {
                int code = TextUtil.Int(row["ConfigManagerErrorCode"], 0);
                if (code == 0) continue;
                string name = TextUtil.Clean(row["Name"]);
                string cls = TextUtil.Clean(row["PNPClass"]);
                if (name.Length == 0) name = "未命名" + ClassName(cls) + "（系统未提供名称）";
                problems.Add(new ProblemDevice(name, code, cls));
            }
            problems.Sort(delegate(ProblemDevice a, ProblemDevice b)
            {
                int cmp = a.Code.CompareTo(b.Code);
                return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
            });

            if (problems.Count == 0)
            {
                ctx.Ok("dev.problemcount", "0 个", "没有发现带错误码的设备。");
                ctx.Ok("dev.problems", "无", "所有即插即用设备均未报告错误。");
                return;
            }

            bool severeCode = false;
            foreach (ProblemDevice p in problems)
            {
                if (p.Code == 28 || p.Code == 31 || p.Code == 43) severeCode = true;
            }
            CheckStatus status = (problems.Count > 5 || (problems.Count >= 3 && severeCode))
                ? CheckStatus.Critical : CheckStatus.Warning;
            List<string> lines = new List<string>();
            foreach (ProblemDevice p in problems)
            {
                lines.Add(string.Format("{0}：{1}", p.Name, ErrorTranslator.DeviceError(p.Code)));
            }
            string countValue = problems.Count + " 个";
            ctx.Emit("dev.problemcount", countValue, status,
                string.Join("\n", lines.ToArray()),
                status == CheckStatus.Critical ? "异常设备较多，可能影响硬件功能，建议逐一处理。" :
                "可在「设备管理器」中查看带黄色感叹号的设备。");
            ctx.Emit("dev.problems", problems.Count + " 个", status,
                string.Join("\n", lines.ToArray()),
                "错误码已翻译为中文原因，原始设备编号不显示。");
        }

        private static void ProbeDrivers(ProbeContext ctx)
        {
            List<Dictionary<string, object>> rows = Wmi.Query(
                "SELECT DeviceName,DriverVersion,DriverDate,DeviceClass FROM Win32_PnPSignedDriver");
            if (rows.Count == 0)
            {
                ctx.Unknown("dev.drivercount", "设备不支持", "系统未返回驱动列表。", string.Empty);
                ctx.Unknown("dev.keydrivers", "需管理员权限", "系统未返回关键驱动信息。", "可尝试以管理员身份运行。");
                return;
            }
            ctx.Ok("dev.drivercount", rows.Count + " 个", "已签名驱动程序数量。");

            List<DriverInfo> display = new List<DriverInfo>();
            List<DriverInfo> network = new List<DriverInfo>();
            List<DriverInfo> storage = new List<DriverInfo>();
            foreach (Dictionary<string, object> row in rows)
            {
                string cls = TextUtil.Clean(row["DeviceClass"]).ToUpperInvariant();
                string name = TextUtil.Clean(row["DeviceName"]);
                string version = TextUtil.Clean(row["DriverVersion"]);
                DateTime? date = TextUtil.Date(row["DriverDate"]);
                if (name.Length == 0 || version.Length == 0) continue;
                DriverInfo d = new DriverInfo(name, version, date);
                if (cls == "DISPLAY") display.Add(d);
                else if (cls == "NET") network.Add(d);
                else if (cls == "SCSIADAPTER" || cls == "HDC" || cls == "STORAGE") storage.Add(d);
            }

            List<string> lines = new List<string>();
            int categories = 0;
            if (display.Count > 0)
            {
                categories++;
                lines.Add("显卡：" + DriverLine(display));
            }
            if (network.Count > 0)
            {
                categories++;
                lines.Add("网卡：" + DriverLine(network));
            }
            if (storage.Count > 0)
            {
                categories++;
                lines.Add("存储控制器：" + DriverLine(storage));
            }
            if (categories == 0)
            {
                ctx.Unknown("dev.keydrivers", "设备不支持", "未找到显卡、网卡或存储控制器驱动。", string.Empty);
                return;
            }
            CheckStatus status = categories >= 3 ? CheckStatus.Ok : CheckStatus.Warning;
            ctx.Emit("dev.keydrivers", categories + " 类已读取", status,
                string.Join("\n", lines.ToArray()),
                status == CheckStatus.Warning ? "部分关键驱动类别未返回版本，硬件功能一般不受影响。" : string.Empty);
        }

        private static string DriverLine(List<DriverInfo> drivers)
        {
            drivers.Sort(delegate(DriverInfo a, DriverInfo b)
            {
                DateTime da = a.Date.HasValue ? a.Date.Value : DateTime.MinValue;
                DateTime db = b.Date.HasValue ? b.Date.Value : DateTime.MinValue;
                return db.CompareTo(da);
            });
            List<string> pieces = new List<string>();
            int count = 0;
            foreach (DriverInfo d in drivers)
            {
                if (count >= 3) break;
                string line = d.Name + "（" + d.Version;
                if (d.Date.HasValue) line += "，" + FormatUtil.Date(d.Date);
                line += "）";
                pieces.Add(line);
                count++;
            }
            if (drivers.Count > count) pieces.Add("另有 " + (drivers.Count - count) + " 个");
            return string.Join("；", pieces.ToArray());
        }

        private static string ClassName(string cls)
        {
            if (string.IsNullOrEmpty(cls)) return "设备";
            switch (cls.ToUpperInvariant())
            {
                case "SYSTEM": return "系统设备";
                case "NET": return "网络适配器";
                case "DISPLAY": return "显示适配器";
                case "USB": return "USB 设备";
                case "MEDIA": return "声音设备";
                case "MOUSE": return "鼠标";
                case "KEYBOARD": return "键盘";
                case "BLUETOOTH": return "蓝牙设备";
                case "SCSIADAPTER": return "存储控制器";
                case "HDC": return "磁盘控制器";
                case "FIRMWARE": return "固件设备";
                case "HIDCLASS": return "人机接口设备";
                default: return "设备";
            }
        }

        private sealed class ProblemDevice
        {
            public string Name;
            public int Code;
            public string ClassName;

            public ProblemDevice(string name, int code, string cls)
            {
                Name = name;
                Code = code;
                ClassName = cls;
            }
        }

        private sealed class DriverInfo
        {
            public string Name;
            public string Version;
            public DateTime? Date;

            public DriverInfo(string name, string version, DateTime? date)
            {
                Name = name;
                Version = version;
                Date = date;
            }
        }
    }
}
