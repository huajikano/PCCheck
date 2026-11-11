using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace PCCheck
{
    public static class SelfTest
    {
        public static int WriteSelfTest(string outputPath)
        {
            ScanSnapshot snapshot = RunProbes();
            if (snapshot == null)
            {
                return 2;
            }
            SummaryView summary = SummaryBuilder.Build(snapshot);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("PCCheck 内部自检记录");
            sb.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy年M月d日 HH:mm:ss"));
            sb.AppendLine("计算机名：" + Environment.MachineName);
            sb.AppendLine("检测耗时：" + snapshot.Elapsed.TotalSeconds.ToString("0.00") + " 秒");
            sb.AppendLine("结论：" + summary.Headline);
            sb.AppendLine("统计：" + snapshot.Counts.Total + " 项，正常 " + snapshot.Counts.Ok +
                "，注意 " + snapshot.Counts.Warning + "，严重 " + snapshot.Counts.Critical +
                "，未知 " + snapshot.Counts.Unknown);
            sb.AppendLine();
            sb.AppendLine("--- 指标 ---");
            foreach (KeyValuePair<string, double> kv in snapshot.Metrics)
                sb.AppendLine(kv.Key + " = " + kv.Value);
            sb.AppendLine();
            sb.AppendLine("--- 检查项 ---");
            foreach (CheckDefinition def in CheckCatalog.All)
            {
                CheckResult result = Find(snapshot, def.Id);
                if (result == null) continue;
                sb.AppendLine(string.Format("[{0}] {1} / {2} / {3} = {4}",
                    StatusText.Of(result.Status), StatusText.GroupTitle(def.Group), def.Section, def.Name, result.Value));
                if (!string.IsNullOrEmpty(result.Detail))
                    sb.AppendLine("    详情：" + result.Detail.Replace("\n", " | "));
            }

            try
            {
                EnsureParent(outputPath);
                File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(true));
                string basePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".",
                    Path.GetFileNameWithoutExtension(outputPath));
                File.WriteAllText(basePath + ".report.txt", ReportWriter.BuildText(snapshot, summary), new UTF8Encoding(true));
                File.WriteAllText(basePath + ".report.html", ReportWriter.BuildHtml(snapshot, summary), new UTF8Encoding(true));
                return 0;
            }
            catch
            {
                return 2;
            }
        }

        public static int ExportReport(string directory)
        {
            ScanSnapshot snapshot = RunProbes();
            if (snapshot == null) return 2;
            SummaryView summary = SummaryBuilder.Build(snapshot);
            try
            {
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                string stem = "PCCheck报告_" + Environment.MachineName + "_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.WriteAllText(Path.Combine(directory, stem + ".txt"),
                    ReportWriter.BuildText(snapshot, summary), new UTF8Encoding(true));
                File.WriteAllText(Path.Combine(directory, stem + ".html"),
                    ReportWriter.BuildHtml(snapshot, summary), new UTF8Encoding(true));
                return 0;
            }
            catch
            {
                return 2;
            }
        }

        public static ScanSnapshot RunProbes()
        {
            ProbeRunner runner = new ProbeRunner();
            ManualResetEvent done = new ManualResetEvent(false);
            ScanSnapshot snapshot = null;
            runner.Completed += delegate(ScanSnapshot value)
            {
                snapshot = value;
                done.Set();
            };
            runner.Start();
            bool completed = done.WaitOne(12000);
            runner.Close();
            done.Close();
            return completed ? snapshot : null;
        }

        private static CheckResult Find(ScanSnapshot snapshot, string id)
        {
            foreach (CheckResult result in snapshot.Results)
                if (string.Equals(result.Id, id, StringComparison.OrdinalIgnoreCase)) return result;
            return null;
        }

        private static void EnsureParent(string path)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }
    }
}
