using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PCCheck
{
    public static class NetProbe
    {
        public static void Run(ProbeContext ctx)
        {
            ctx.Slot("连接状态", new string[] { "net.gateway", "net.active" }, delegate { ProbeGateways(ctx); });
            ctx.Slot("网关连通性", new string[] { "net.gatewayping" }, delegate { ProbeGatewayPing(ctx); });
            ctx.Slot("DNS", new string[] { "net.dns" }, delegate { ProbeDns(ctx); });
            ctx.Slot("外网延迟", new string[] { "net.latency" }, delegate { ProbeLatency(ctx); });
            ctx.Slot("监听端口", new string[] { "net.ports" }, delegate { ProbePorts(ctx); });
        }

        private static void ProbeGateways(ProbeContext ctx)
        {
            List<NetworkInterface> nics = ActiveNics();
            ctx.Emit("net.active", nics.Count + " 个", nics.Count > 0 ? CheckStatus.Ok : CheckStatus.Warning,
                nics.Count > 0 ? string.Join("\n", nics.Select(delegate(NetworkInterface n) { return n.Description; }).ToArray())
                : "没有处于连接状态的网络接口。", string.Empty);

            List<string> gateways = new List<string>();
            List<string> lines = new List<string>();
            foreach (NetworkInterface nic in nics)
            {
                try
                {
                    foreach (GatewayIPAddressInformation gw in nic.GetIPProperties().GatewayAddresses)
                    {
                        if (gw.Address == null) continue;
                        if (gw.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string address = gw.Address.ToString();
                        if (address == "0.0.0.0") continue;
                        if (!gateways.Contains(address)) gateways.Add(address);
                        string line = nic.Description + "：" + address;
                        if (!lines.Contains(line)) lines.Add(line);
                    }
                }
                catch
                {
                }
            }

            if (gateways.Count == 0)
            {
                ctx.Warning("net.gateway", "未配置", "没有从活动网卡读取到默认网关。",
                    "如果仍能上网，可能是网络由系统自动管理；否则请检查路由器或网络设置。");
                return;
            }
            ctx.Ok("net.gateway", string.Join("、", gateways.ToArray()), string.Join("\n", lines.ToArray()));
        }

        private static void ProbeGatewayPing(ProbeContext ctx)
        {
            string gateway = FirstGateway();
            if (gateway.Length == 0)
            {
                ctx.Unknown("net.gatewayping", "无网关", "未配置默认网关，无法测试连通性。", string.Empty);
                return;
            }

            int success = 0;
            long total = 0;
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    using (Ping ping = new Ping())
                    {
                        PingReply reply = ping.Send(gateway, 800);
                        if (reply != null && reply.Status == IPStatus.Success)
                        {
                            success++;
                            total += reply.RoundtripTime;
                        }
                    }
                }
                catch
                {
                }
            }
            if (success > 0)
            {
                long average = total / success;
                ctx.Ok("net.gatewayping", FormatUtil.Latency(average),
                    string.Format("网关 {0}，2 次测试成功 {1} 次。", gateway, success));
            }
            else
            {
                ctx.Critical("net.gatewayping", "不可达", "网关 " + gateway + " 未响应，可能无法访问局域网或互联网。",
                    "请检查网线、Wi-Fi 连接和路由器电源。");
            }
        }

        private static void ProbeDns(ProbeContext ctx)
        {
            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses("www.baidu.com");
                List<string> ipv4 = new List<string>();
                foreach (IPAddress address in addresses)
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork) ipv4.Add(address.ToString());
                }
                if (ipv4.Count == 0)
                {
                    ContextWarning(ctx, "net.dns", "解析异常", "域名解析没有返回 IPv4 地址。", "请检查 DNS 设置。");
                    return;
                }
                ctx.Ok("net.dns", "正常", "www.baidu.com 解析为：" + string.Join("、", ipv4.Take(3).ToArray()));
            }
            catch (Exception ex)
            {
                ContextWarning(ctx, "net.dns", "解析失败", ErrorTranslator.ToHuman(ex),
                    "请检查网络连接和 DNS 设置；如果使用代理，请确认代理正常。");
            }
        }

        private static void ProbeLatency(ProbeContext ctx)
        {
            IPAddress target = null;
            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses("www.baidu.com");
                foreach (IPAddress address in addresses)
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        target = address;
                        break;
                    }
                }
            }
            catch
            {
            }
            if (target == null)
            {
                ctx.Unknown("net.latency", "无法测试", "域名解析不可用，无法进行延迟测试。", string.Empty);
                return;
            }

            int success = 0;
            long total = 0;
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    using (Ping ping = new Ping())
                    {
                        PingReply reply = ping.Send(target, 800);
                        if (reply != null && reply.Status == IPStatus.Success)
                        {
                            success++;
                            total += reply.RoundtripTime;
                        }
                    }
                }
                catch
                {
                }
            }
            if (success == 0)
            {
                ctx.Unknown("net.latency", "无法连通", "外网地址未响应 ICMP，可能被防火墙拦截。", "浏览器能上网时，此项不影响使用。");
                return;
            }
            long average = total / success;
            CheckStatus status = average < 100 ? CheckStatus.Ok : CheckStatus.Warning;
            ctx.Emit("net.latency", FormatUtil.Latency(average), status,
                string.Format("测试地址 {0}，2 次成功 {1} 次。", target, success),
                average >= 100 ? "外网延迟偏高，在线游戏或视频会议可能受影响。" : string.Empty);
        }

        private static void ProbePorts(ProbeContext ctx)
        {
            List<TcpListenerInfo> listeners = NativeMethods.GetTcpListeners();
            Dictionary<string, TcpListenerInfo> unique = new Dictionary<string, TcpListenerInfo>();
            foreach (TcpListenerInfo l in listeners)
            {
                string key = l.Port.ToString(CultureInfo.InvariantCulture) + "|" + l.ProcessId.ToString(CultureInfo.InvariantCulture);
                if (!unique.ContainsKey(key)) unique[key] = l;
            }
            List<TcpListenerInfo> ordered = unique.Values.ToList();
            ordered.Sort(delegate(TcpListenerInfo a, TcpListenerInfo b)
            {
                int cmp = a.Port.CompareTo(b.Port);
                return cmp != 0 ? cmp : a.ProcessId.CompareTo(b.ProcessId);
            });

            if (ordered.Count == 0)
            {
                ctx.Unknown("net.ports", "无监听端口", "没有读取到 TCP 监听端口。", "可能被安全软件限制。");
                return;
            }

            List<string> lines = new List<string>();
            List<string> shown = new List<string>();
            foreach (TcpListenerInfo l in ordered)
            {
                if (shown.Count >= 12) break;
                string process = ProcessName(l.ProcessId);
                string line = string.Format("端口 {0} · {1}（PID {2}）", l.Port, process, l.ProcessId);
                lines.Add(line);
                shown.Add(line);
            }
            string value = shown.Count + " 个";
            if (ordered.Count > shown.Count) value += "（共 " + ordered.Count + " 个）";
            ctx.Ok("net.ports", value, string.Join("\n", lines.ToArray()));
        }

        private static string ProcessName(int pid)
        {
            if (pid <= 0) return "系统空闲进程";
            if (pid == 4) return "系统进程";
            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    string name = process.ProcessName;
                    return string.IsNullOrEmpty(name) ? "未知进程" : name;
                }
            }
            catch
            {
                return "系统进程（权限受限）";
            }
        }

        private static List<NetworkInterface> ActiveNics()
        {
            List<NetworkInterface> result = new List<NetworkInterface>();
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                result.Add(nic);
            }
            return result;
        }

        private static string FirstGateway()
        {
            foreach (NetworkInterface nic in ActiveNics())
            {
                try
                {
                    foreach (GatewayIPAddressInformation gw in nic.GetIPProperties().GatewayAddresses)
                    {
                        if (gw.Address != null && gw.Address.AddressFamily == AddressFamily.InterNetwork &&
                            gw.Address.ToString() != "0.0.0.0")
                            return gw.Address.ToString();
                    }
                }
                catch
                {
                }
            }
            return string.Empty;
        }

        private static void ContextWarning(ProbeContext ctx, string id, string value, string detail, string hint)
        {
            ctx.Warning(id, value, detail, hint);
        }
    }
}
