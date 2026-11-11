using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;

namespace PCCheck
{
    public sealed class SummaryView
    {
        public string Headline;
        public string Subline;
        public CheckStatus Tone;
        public string Grade;

        public SummaryView()
        {
            Headline = string.Empty;
            Subline = string.Empty;
            Tone = CheckStatus.Unknown;
            Grade = "未知";
        }
    }

    public static class SummaryBuilder
    {
        public static SummaryView Build(ScanSnapshot snapshot)
        {
            SummaryView view = new SummaryView();
            if (snapshot == null)
            {
                view.Headline = "正在检测本机配置…";
                view.Subline = "正在读取硬件、系统与网络信息，请稍候。";
                view.Tone = CheckStatus.Unknown;
                return view;
            }

            string grade = MachineGrade.FromMetrics(snapshot.Metrics);
            CheckCounts c = snapshot.Counts;
            view.Grade = grade;
            if (c.Critical > 0)
            {
                view.Headline = string.Format("本机配置{0}，发现 {1} 项严重问题", grade, c.Critical);
                if (c.Warning > 0) view.Headline += string.Format("，{0} 项需要注意", c.Warning);
                view.Tone = CheckStatus.Critical;
            }
            else if (c.Warning > 0)
            {
                view.Headline = string.Format("本机配置{0}，运行基本正常，发现 {1} 项需要注意", grade, c.Warning);
                view.Tone = CheckStatus.Warning;
            }
            else if (c.Unknown > 0)
            {
                view.Headline = string.Format("本机配置{0}，整体正常，{1} 项未能读取", grade, c.Unknown);
                view.Tone = CheckStatus.Unknown;
            }
            else
            {
                view.Headline = string.Format("本机配置{0}，运行正常，未发现问题", grade);
                view.Tone = CheckStatus.Ok;
            }
            view.Subline = string.Format("共检查 {0} 项 · 正常 {1} · 注意 {2} · 严重 {3} · 未知 {4} · 用时 {5:0.0} 秒",
                c.Total, c.Ok, c.Warning, c.Critical, c.Unknown, snapshot.Elapsed.TotalSeconds);
            return view;
        }
    }

    public static class ReportWriter
    {
        public static string DefaultFileName(string extension)
        {
            string machine = Environment.MachineName;
            if (string.IsNullOrEmpty(machine)) machine = "本机";
            return "PCCheck报告_" + machine + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + extension;
        }

        public static string BuildText(ScanSnapshot snapshot, SummaryView summary)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("PCCheck 电脑配置与环境检查报告");
            sb.AppendLine("========================================");
            sb.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy年M月d日 HH:mm:ss", CultureInfo.CurrentCulture));
            sb.AppendLine("计算机名：" + Environment.MachineName);
            string os = FindValue(snapshot, "os.name");
            if (!string.IsNullOrEmpty(os)) sb.AppendLine("操作系统：" + os);
            sb.AppendLine("配置档位：" + summary.Grade);
            sb.AppendLine("检测结论：" + summary.Headline);
            sb.AppendLine("检测耗时：" + snapshot.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " 秒");
            sb.AppendLine("检查统计：" + CountsLine(snapshot.Counts));
            sb.AppendLine();
            sb.AppendLine("说明：本报告由本机只读检测生成，不上传任何数据；程序不会修改系统设置。");
            sb.AppendLine();

            CheckGroup? currentGroup = null;
            string currentSection = null;
            foreach (CheckDefinition def in CheckCatalog.All)
            {
                CheckResult result = Find(snapshot, def.Id);
                if (result == null) continue;
                if (!currentGroup.HasValue || currentGroup.Value != def.Group)
                {
                    currentGroup = def.Group;
                    currentSection = null;
                    sb.AppendLine();
                    sb.AppendLine("========== " + GroupHeading(def.Group) + " ==========");
                }
                if (!string.Equals(currentSection, def.Section, StringComparison.Ordinal))
                {
                    currentSection = def.Section;
                    sb.AppendLine();
                    sb.AppendLine("【" + def.Section + "】");
                }
                sb.AppendLine(string.Format("[{0}] {1}：{2}", StatusText.Of(result.Status), def.Name, result.Value));
                if (!string.IsNullOrEmpty(result.Detail))
                {
                    foreach (string line in result.Detail.Split('\n'))
                        sb.AppendLine("        详情：" + line.TrimEnd('\r'));
                }
                if (!string.IsNullOrEmpty(result.Hint) && result.Status != CheckStatus.Ok)
                    sb.AppendLine("        提示：" + result.Hint);
                sb.AppendLine("        来源：" + StatusText.SourceLabel(def.Source));
            }

            sb.AppendLine();
            sb.AppendLine("========================================");
            sb.AppendLine("报告结束。本报告不包含任何上传操作。");
            return sb.ToString();
        }

        public static string BuildHtml(ScanSnapshot snapshot, SummaryView summary)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
            sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            sb.AppendLine("<title>PCCheck 报告 - " + Html(Environment.MachineName) + "</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{margin:0;background:#f3f6fa;color:#0f172a;font-family:'Microsoft YaHei UI','Microsoft YaHei','PingFang SC',sans-serif;line-height:1.65}");
            sb.AppendLine(".page{max-width:980px;margin:0 auto;padding:28px 20px 48px}");
            sb.AppendLine(".hero{background:linear-gradient(120deg,#0b1f33,#164e63);color:#fff;border-radius:18px;padding:28px 32px}");
            sb.AppendLine(".hero h1{margin:0 0 6px;font-size:26px;letter-spacing:.5px}.hero p{margin:0;color:#c7d7e6}");
            sb.AppendLine(".summary{background:#fff;border-radius:16px;padding:22px 26px;margin:16px 0 22px;box-shadow:0 8px 24px rgba(15,23,42,.06);border-left:6px solid #2563eb}");
            sb.AppendLine(".summary.tone-warning{border-left-color:#d97706}.summary.tone-critical{border-left-color:#dc2626}.summary.tone-unknown{border-left-color:#94a3b8}.summary.tone-ok{border-left-color:#16a34a}");
            sb.AppendLine(".headline{font-size:20px;font-weight:700;margin:0 0 6px}.sub{color:#64748b;font-size:14px}");
            sb.AppendLine(".meta{display:flex;flex-wrap:wrap;gap:10px 24px;margin-top:14px;color:#475569;font-size:13px}.meta b{color:#0f172a}");
            sb.AppendLine("h2{font-size:19px;margin:28px 0 12px;color:#0b1f33}");
            sb.AppendLine(".section{background:#fff;border:1px solid #e2e8f0;border-radius:14px;margin:12px 0;overflow:hidden}");
            sb.AppendLine(".section h3{margin:0;padding:13px 18px;background:#f8fafc;border-bottom:1px solid #e2e8f0;font-size:15px;color:#1e293b}");
            sb.AppendLine(".item{padding:12px 18px;border-bottom:1px solid #f1f5f9}.item:last-child{border-bottom:0}");
            sb.AppendLine(".row{display:flex;gap:12px;align-items:baseline}.name{width:210px;min-width:130px;font-weight:600;color:#334155}.value{flex:1;word-break:break-word;font-weight:600;color:#0f172a}");
            sb.AppendLine(".pill{display:inline-block;min-width:44px;text-align:center;padding:1px 8px;border-radius:999px;font-size:12px;font-weight:600;margin-left:8px}");
            sb.AppendLine(".ok{background:#dcfce7;color:#166534}.warning{background:#fef3c7;color:#92400e}.critical{background:#fee2e2;color:#991b1b}.unknown{background:#f1f5f9;color:#475569}");
            sb.AppendLine(".detail{margin:6px 0 0 222px;color:#64748b;font-size:13px;white-space:pre-wrap}.hint{margin:5px 0 0 222px;color:#9a3412;font-size:13px}.source{margin:4px 0 0 222px;color:#94a3b8;font-size:12px}");
            sb.AppendLine(".foot{color:#94a3b8;font-size:12px;text-align:center;margin-top:30px}");
            sb.AppendLine("@media (max-width:720px){.name{width:100%}.row{display:block}.detail,.hint,.source{margin-left:0}.page{padding:14px}.hero{padding:20px}.summary{padding:18px}}");
            sb.AppendLine("@media print{body{background:#fff}.page{max-width:none;padding:0}.hero{border-radius:0}.section{break-inside:avoid}}");
            sb.AppendLine("</style></head><body><div class=\"page\">");
            sb.AppendLine("<div class=\"hero\"><h1>PCCheck 电脑配置与环境检查</h1><p>本机只读检测报告 · 不上传任何数据</p></div>");
            sb.AppendLine("<div class=\"summary tone-" + ToneClass(summary.Tone) + "\">");
            sb.AppendLine("<div class=\"headline\">" + Html(summary.Headline) + "</div>");
            sb.AppendLine("<div class=\"sub\">" + Html(summary.Subline) + "</div>");
            sb.AppendLine("<div class=\"meta\"><span>计算机：<b>" + Html(Environment.MachineName) + "</b></span>");
            string os = FindValue(snapshot, "os.name");
            if (!string.IsNullOrEmpty(os)) sb.AppendLine("<span>系统：<b>" + Html(os) + "</b></span>");
            sb.AppendLine("<span>配置档位：<b>" + Html(summary.Grade) + "</b></span>");
            sb.AppendLine("<span>生成时间：<b>" + Html(DateTime.Now.ToString("yyyy年M月d日 HH:mm", CultureInfo.CurrentCulture)) + "</b></span></div></div>");

            CheckGroup? currentGroup = null;
            string currentSection = null;
            bool sectionOpen = false;
            foreach (CheckDefinition def in CheckCatalog.All)
            {
                CheckResult result = Find(snapshot, def.Id);
                if (result == null) continue;
                if (!currentGroup.HasValue || currentGroup.Value != def.Group)
                {
                    if (sectionOpen) { sb.AppendLine("</div>"); sectionOpen = false; }
                    currentGroup = def.Group;
                    currentSection = null;
                    sb.AppendLine("<h2>" + Html(GroupHeading(def.Group)) + "</h2>");
                }
                if (!string.Equals(currentSection, def.Section, StringComparison.Ordinal))
                {
                    if (sectionOpen) { sb.AppendLine("</div>"); sectionOpen = false; }
                    currentSection = def.Section;
                    sb.AppendLine("<div class=\"section\"><h3>" + Html(def.Section) + "</h3>");
                    sectionOpen = true;
                }
                sb.AppendLine("<div class=\"item\"><div class=\"row\"><div class=\"name\">" + Html(def.Name) +
                    "</div><div class=\"value\">" + Html(result.Value) +
                    "<span class=\"pill " + StatusClass(result.Status) + "\">" + StatusText.Of(result.Status) + "</span></div></div>");
                if (!string.IsNullOrEmpty(result.Detail))
                    sb.AppendLine("<div class=\"detail\">" + Html(result.Detail) + "</div>");
                if (!string.IsNullOrEmpty(result.Hint) && result.Status != CheckStatus.Ok)
                    sb.AppendLine("<div class=\"hint\">提示：" + Html(result.Hint) + "</div>");
                sb.AppendLine("<div class=\"source\">来源：" + Html(StatusText.SourceLabel(def.Source)) + "</div></div>");
            }
            if (sectionOpen) sb.AppendLine("</div>");
            sb.AppendLine("<div class=\"foot\">PCCheck · 单文件绿色版 · 只读检测 · 不支持联网回传</div>");
            sb.AppendLine("</div></body></html>");
            return sb.ToString();
        }

        private static CheckResult Find(ScanSnapshot snapshot, string id)
        {
            if (snapshot == null) return null;
            foreach (CheckResult r in snapshot.Results)
                if (string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        private static string FindValue(ScanSnapshot snapshot, string id)
        {
            CheckResult r = Find(snapshot, id);
            return r == null ? string.Empty : r.Value;
        }

        private static string CountsLine(CheckCounts c)
        {
            return string.Format("共 {0} 项（正常 {1} · 注意 {2} · 严重 {3} · 未知 {4}）",
                c.Total, c.Ok, c.Warning, c.Critical, c.Unknown);
        }

        private static string GroupHeading(CheckGroup group)
        {
            switch (group)
            {
                case CheckGroup.Hardware: return "一、硬件";
                case CheckGroup.System: return "二、系统与安全";
                case CheckGroup.Drivers: return "三、驱动与设备";
                case CheckGroup.Runtime: return "四、运行库与环境";
                default: return "五、网络与端口";
            }
        }

        private static string StatusClass(CheckStatus status)
        {
            switch (status)
            {
                case CheckStatus.Ok: return "ok";
                case CheckStatus.Warning: return "warning";
                case CheckStatus.Critical: return "critical";
                default: return "unknown";
            }
        }

        private static string ToneClass(CheckStatus status)
        {
            switch (status)
            {
                case CheckStatus.Ok: return "ok";
                case CheckStatus.Warning: return "warning";
                case CheckStatus.Critical: return "critical";
                default: return "unknown";
            }
        }

        private static string Html(string value)
        {
            return WebUtility.HtmlEncode(value ?? string.Empty).Replace("\n", "<br>");
        }
    }
}
