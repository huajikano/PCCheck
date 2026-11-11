using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PCCheck
{
    public sealed class ScanSnapshot
    {
        public List<CheckResult> Results { get; private set; }
        public Dictionary<string, double> Metrics { get; private set; }
        public CheckCounts Counts { get; private set; }
        public TimeSpan Elapsed { get; private set; }
        public DateTime FinishedAt { get; private set; }

        public ScanSnapshot(List<CheckResult> results, Dictionary<string, double> metrics,
            TimeSpan elapsed, DateTime finishedAt)
        {
            Results = results;
            Metrics = metrics;
            Counts = CheckCounts.From(results);
            Elapsed = elapsed;
            FinishedAt = finishedAt;
        }
    }

    public sealed class ProbeRunner : IDisposable
    {
        private readonly Dictionary<string, CheckResult> _results =
            new Dictionary<string, CheckResult>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _metrics =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new object();
        private readonly Stopwatch _watch = new Stopwatch();
        private bool _closed;
        private bool _started;
        private ProbeContext _context;

        public event Action<CheckResult> ItemReady;
        public event Action<ScanSnapshot> Completed;

        public ProbeContext Context
        {
            get { return _context; }
        }

        public bool IsFinished
        {
            get { lock (_gate) return _closed; }
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_started) return;
                _started = true;
                _watch.Start();
                _context = new ProbeContext(this);
            }

            Task[] groups = new Task[5];
            groups[0] = StartGroup(delegate { HardwareProbe.Run(_context); }, CheckGroup.Hardware);
            groups[1] = StartGroup(delegate { SystemProbe.Run(_context); }, CheckGroup.System);
            groups[2] = StartGroup(delegate { DriverProbe.Run(_context); }, CheckGroup.Drivers);
            groups[3] = StartGroup(delegate { EnvironmentProbe.Run(_context); }, CheckGroup.Runtime);
            groups[4] = StartGroup(delegate { NetProbe.Run(_context); }, CheckGroup.Network);

            Task.Factory.StartNew(delegate { WaitAndFinish(groups); }, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private Task StartGroup(Action action, CheckGroup group)
        {
            return Task.Factory.StartNew(delegate
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    _context.FailGroup(group, ErrorTranslator.ToHuman(ex));
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void WaitAndFinish(Task[] groups)
        {
            try
            {
                Task.WaitAll(groups, 8500);
            }
            catch
            {
            }
            FinishAll();
        }

        internal void Publish(string id, string value, string detail, string hint, CheckStatus status)
        {
            CheckDefinition def = CheckCatalog.Get(id);
            if (def == null) return;
            CheckResult result = new CheckResult(id, value, detail, hint, status);
            Action<CheckResult> handler = null;
            lock (_gate)
            {
                if (_closed) return;
                if (_results.ContainsKey(id)) return;
                _results[id] = result;
                handler = ItemReady;
                if (handler != null)
                {
                    try { handler(result); }
                    catch { }
                }
            }
        }

        internal void PublishMetric(string key, double value)
        {
            lock (_gate)
            {
                if (_closed) return;
                _metrics[key] = value;
            }
        }

        internal bool Has(string id)
        {
            lock (_gate) return _results.ContainsKey(id);
        }

        internal void FailGroup(CheckGroup group, string reason)
        {
            foreach (CheckDefinition def in CheckCatalog.All)
            {
                if (def.Group == group && !Has(def.Id))
                {
                    Publish(def.Id, reason, def.Degrade, "该组采集未能完成，其他项目不受影响。", CheckStatus.Unknown);
                }
            }
        }

        private void FinishAll()
        {
            ScanSnapshot snapshot = null;
            lock (_gate)
            {
                if (_closed) return;
                foreach (CheckDefinition def in CheckCatalog.All)
                {
                    if (_results.ContainsKey(def.Id)) continue;
                    CheckResult result = new CheckResult(def.Id, "读取超时",
                        "检测总时长超过 8.5 秒，已跳过此项；可稍后重新检测。",
                        "该项未能及时返回，不影响其他检查结果。", CheckStatus.Unknown);
                    _results[def.Id] = result;
                    Action<CheckResult> handler = ItemReady;
                    if (handler != null)
                    {
                        try { handler(result); }
                        catch { }
                    }
                }

                _watch.Stop();
                List<CheckResult> ordered = new List<CheckResult>();
                foreach (CheckDefinition def in CheckCatalog.All)
                {
                    CheckResult r;
                    if (_results.TryGetValue(def.Id, out r)) ordered.Add(r);
                }
                snapshot = new ScanSnapshot(ordered, new Dictionary<string, double>(_metrics),
                    _watch.Elapsed, DateTime.Now);
                _closed = true;
            }

            Action<ScanSnapshot> completed = Completed;
            if (completed != null)
            {
                try { completed(snapshot); }
                catch { }
            }
        }

        public void Close()
        {
            lock (_gate)
            {
                _closed = true;
                ItemReady = null;
                Completed = null;
            }
        }

        public void Dispose()
        {
            Close();
        }
    }

    public sealed class ProbeContext
    {
        private readonly ProbeRunner _runner;

        internal ProbeContext(ProbeRunner runner)
        {
            _runner = runner;
        }

        public void Emit(string id, string value, CheckStatus status, string detail, string hint)
        {
            _runner.Publish(id, value, detail, hint, status);
        }

        public void Ok(string id, string value, string detail)
        {
            Emit(id, value, CheckStatus.Ok, detail, string.Empty);
        }

        public void Ok(string id, string value)
        {
            Emit(id, value, CheckStatus.Ok, string.Empty, string.Empty);
        }

        public void Warning(string id, string value, string detail, string hint)
        {
            Emit(id, value, CheckStatus.Warning, detail, hint);
        }

        public void Unknown(string id, string value, string detail, string hint)
        {
            Emit(id, value, CheckStatus.Unknown, detail, hint);
        }

        public void Critical(string id, string value, string detail, string hint)
        {
            Emit(id, value, CheckStatus.Critical, detail, hint);
        }

        public void Metric(string key, double value)
        {
            _runner.PublishMetric(key, value);
        }

        public void Slot(string label, string[] ids, Action work)
        {
            Task task = Task.Factory.StartNew(delegate
            {
                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    Fail(ids, ErrorTranslator.ToHuman(ex));
                }
            }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);

            try
            {
                if (!task.Wait(3000))
                {
                    Fail(ids, "读取超时");
                }
                else
                {
                    EnsureMissing(ids);
                }
            }
            catch
            {
                Fail(ids, "暂时无法读取");
            }
        }

        private void EnsureMissing(string[] ids)
        {
            if (ids == null) return;
            foreach (string id in ids)
            {
                CheckDefinition def = CheckCatalog.Get(id);
                if (def == null || _runner.Has(id)) continue;
                _runner.Publish(id, def.Degrade, "该项本次未取得数据。", def.Degrade, CheckStatus.Unknown);
            }
        }

        public void Fail(string[] ids, string reason)
        {
            if (ids == null) return;
            foreach (string id in ids)
            {
                CheckDefinition def = CheckCatalog.Get(id);
                if (def == null) continue;
                if (_runner.Has(id)) continue;
                string detail = "数据来源：" + StatusText.SourceLabel(def.Source);
                _runner.Publish(id, reason, detail, def.Degrade, CheckStatus.Unknown);
            }
        }

        internal void FailGroup(CheckGroup group, string reason)
        {
            _runner.FailGroup(group, reason);
        }
    }

    public static class MachineGrade
    {
        public static string FromMetrics(IDictionary<string, double> metrics)
        {
            int cores = (int)Get(metrics, "cpu.cores");
            double ram = Get(metrics, "mem.total.gb");
            bool nvme = Get(metrics, "disk.nvme") > 0;
            bool discrete = Get(metrics, "gpu.discrete") > 0;

            if ((cores >= 8 && ram >= 31.0) ||
                (cores >= 8 && ram >= 15.0 && nvme && discrete))
                return "高端";
            if (cores >= 6 && ram >= 15.0)
                return "中高端";
            if (cores >= 4 && ram >= 7.0)
                return "中端";
            return "入门";
        }

        private static double Get(IDictionary<string, double> metrics, string key)
        {
            double v;
            return metrics != null && metrics.TryGetValue(key, out v) ? v : 0;
        }
    }
}
