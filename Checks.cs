namespace VrpSim;

public sealed record CheckRow(int No, string Title, bool? Ok, string Detail);

public static class Checks
{
    public static List<CheckRow> Run(Device d)
    {
        var rows = new List<CheckRow>();
        var con = d.Uis.GetValueOrDefault("con 0") ?? new UiCfg { Name = "con 0" };
        var vty = d.Uis.GetValueOrDefault("vty 0 4") ?? new UiCfg { Name = "vty 0 4" };

        var hasAdmin = d.Users.Values.Any(u => u.Privilege == 15);
        var lockSet = d.Aaa.WrongPassword != null;
        var conOk = con.Auth == "aaa" && con.IdleSet && con.IdleMin <= 5 && hasAdmin && lockSet;
        rows.Add(new CheckRow(1, "账户及本地认证登录", conOk,
            conOk ? "con0=aaa / idle-timeout 5 / 失败锁定 / 有 privilege 15 本地用户"
                  : !hasAdmin ? "缺 privilege 15 本地用户（aaa 下 local-user X privilege level 15）"
                  : con.Auth != "aaa" ? "con 0 未配 authentication-mode aaa"
                  : !(con.IdleSet && con.IdleMin <= 5) ? "con 0 缺 idle-timeout 5 0"
                  : "缺 local-aaa-user wrong-password retry-interval 5 retry-time 5 block-time 10"));

        var acl2000 = d.Acls.GetValueOrDefault(2000);
        var acl2000Ok = acl2000 != null && acl2000.Rules.Any(r => r.IsPermit);
        var vtyOk = vty.Auth == "aaa" && vty.Protocol == "ssh" && vty.IdleSet && vty.Acl != null && acl2000Ok;
        var item2 = d.StelnetV4 && vtyOk;
        rows.Add(new CheckRow(2, "远程 SSH 管理", item2,
            item2 ? "stelnet ipv4 已开；vty 0 4 = acl 2000 + aaa + ssh + idle"
                  : !d.StelnetV4 ? "未开 SSH（stelnet ipv4 server enable）"
                  : vty.Acl == null ? "vty 0 4 未绑定 acl 2000 inbound"
                  : !acl2000Ok ? "acl 2000 缺 permit（先 permit 管理源再 deny）"
                  : vty.Auth != "aaa" ? "vty 0 4 缺 authentication-mode aaa"
                  : vty.Protocol != "ssh" ? "vty 0 4 缺 protocol inbound ssh（否则可明文 telnet）"
                  : "vty 0 4 缺 idle-timeout"));

        var item3 = d.InfoCenterEnabled && d.LogHosts.Count > 0;
        rows.Add(new CheckRow(3, "日志审计", item3,
            item3 ? "info-center loghost " + string.Join(",", d.LogHosts)
                  : "未配 info-center loghost <监测装置IP>"));

        var item4 = d.Aaa.ComplexityCheck && d.Aaa.PasswordPolicyAdmin && lockSet;
        rows.Add(new CheckRow(4, "密码复杂度", item4,
            item4 ? "复杂度检查 + 管理员密码策略 + 失败锁定 均已开"
                  : !d.Aaa.ComplexityCheck ? "缺 user-password complexity-check"
                  : !d.Aaa.PasswordPolicyAdmin ? "缺 local-aaa-user password policy administrator"
                  : "缺失败锁定 wrong-password"));

        var req = new (string Proto, int Min, int Max)[]
        {
            ("tcp", 135, 135), ("tcp", 137, 139), ("tcp", 445, 445), ("tcp", 3389, 3389),
            ("udp", 135, 135), ("udp", 137, 139), ("udp", 445, 445), ("udp", 3389, 3389)
        };
        var acl3000 = d.Acls.GetValueOrDefault(3000);
        var covered = acl3000 != null && req.All(x => acl3000.Rules.Any(r => !r.IsPermit && r.Proto == x.Proto && r.PortMin <= x.Min && r.PortMax >= x.Max));
        var permitAll = acl3000?.Rules.Any(r => r.IsPermit) == true;
        var bound = d.Ifaces.Values.Any(i => i.TfIn == 3000 && i.TfOut == 3000);
        var item5 = covered && permitAll && bound;
        rows.Add(new CheckRow(5, "限制高危端口", item5,
            item5 ? "acl 3000 覆盖 135/137-139/445/3389（tcp+udp）+ rule permit ip + 已双向挂接"
                  : acl3000 == null ? "缺 acl number 3000"
                  : !covered ? "acl 3000 规则未覆盖全部高危端口（tcp+udp 各 4 组）"
                  : !permitAll ? "acl 3000 缺 rule permit ip 兜底（未匹配流量可能被丢，断业务风险）"
                  : "acl 3000 未在业务口 traffic-filter inbound/outbound 双向挂接"));

        var verOk = d.SnmpVersion.Contains("v2c") || d.SnmpVersion.Contains("v3");
        var commOk = d.Communities.Count > 0 && d.Communities.All(c => c.Password.Length > 0 && c.Acl != null);
        var trapOk = d.SnmpTrapEnable && d.Targets.Count > 0;
        var item6 = d.SnmpAgent && verOk && commOk && trapOk;
        rows.Add(new CheckRow(6, "SNMP 服务配置", item6,
            item6 ? $"version={d.SnmpVersion}；团体字已改密文+ACL；trap 已启用并指定目标"
                  : !d.SnmpAgent ? "未配置 snmp-agent"
                  : !verOk ? "sys-info version 未含 v2c/v3"
                  : !commOk ? "团体字缺 ACL 限源或为空（禁 public/private）"
                  : "缺 snmp-agent trap enable 或 target-host"));

        var item7 = !d.TelnetServer && !d.FtpServer && !d.HttpServer;
        rows.Add(new CheckRow(7, "禁用不必要的服务", item7,
            item7 ? "telnet/ftp/http 均已关闭"
                  : "仍未关闭：" + string.Join(" ", new[]
                    {
                        d.TelnetServer ? "telnet" : "", d.FtpServer ? "ftp" : "", d.HttpServer ? "http" : ""
                    }.Where(x => x.Length > 0)) + "（undo telnet server enable / undo ftp server / undo http server enable）"));

        rows.Add(new CheckRow(8, "关闭默认路由", null, "仅路由器项，交换机不适用（本模拟器无路由配置）"));

        var ports = d.Ifaces.Values.Where(i => i.IsPort).ToList();
        var badPorts = ports.Where(p => p.PortConfigured ? p.Shutdown : !p.Shutdown).ToList();
        var item9 = badPorts.Count == 0;
        rows.Add(new CheckRow(9, "关闭空闲端口", item9,
            item9 ? "业务口 up、其余口全部 shutdown"
                  : "待处理：" + string.Join(",", badPorts.Take(6).Select(p => p.Name + (p.PortConfigured ? "(业务口被关)" : "(空闲未关)"))) + (badPorts.Count > 6 ? " ..." : "")));

        var timeOk = d.Clock.Tz != null && (d.Clock.Ntp || d.Clock.Year >= 2025);
        rows.Add(new CheckRow(10, "时区及对时", timeOk,
            timeOk ? "时区已配，时钟 " + d.Clock.Text
                   : d.Clock.Tz == null ? "未配时区（clock timezone BeiJin add 08:00:00）"
                   : "时钟仍是 " + d.Clock.Text + "（出厂默认，日志时间不可用）"));

        rows.Add(new CheckRow(11, "OSPF MD5 认证", null, "仅路由器项，交换机不适用"));

        rows.Add(new CheckRow(12, "设备资源利用率", true, "CPU 12% / 内存 34%（模拟固定值，均低于 70%）"));
        rows.Add(new CheckRow(13, "设备双电源并有效接地", null, "现场检查项：2 个电源模块（模拟正常），接地需现场查看"));

        var logOk = d.LogHosts.Contains(d.MonitorIp);
        var trapIpOk = d.Targets.Any(t => t.Ip == d.MonitorIp);
        var acl2001 = d.Acls.GetValueOrDefault(2001);
        var aclPermitMonitor = acl2001 != null && acl2001.Rules.Any(r => r.IsPermit && r.Source == d.MonitorIp && r.SourceWildcard == 0);
        var vlanOk = d.Ifaces.Values.Any(i => i.IsVlanif && i.Ip != null && i.Mask != null && SameSubnet(i.Ip, i.Mask, d.MonitorIp));
        var item14 = logOk && trapIpOk && aclPermitMonitor && vlanOk;
        rows.Add(new CheckRow(14, "接入网络安全监测装置", item14,
            item14 ? $"loghost/trap/acl 2001 均指向 {d.MonitorIp}，Vlanif 与监测装置同网段"
                   : !logOk ? "loghost 与监测装置 IP 不一致"
                   : !trapIpOk ? "trap 目标不是监测装置 IP"
                   : !aclPermitMonitor ? "acl 2001 未放行监测装置 IP"
                   : "无与监测装置同网段的三层接口（Vlanif + ip address）"));

        return rows;
    }

    static bool SameSubnet(string ip, string mask, string other)
    {
        var a = ip.Split('.');
        var m = mask.Split('.');
        var b = other.Split('.');
        if (a.Length != 4 || m.Length != 4 || b.Length != 4) return false;
        for (int i = 0; i < 4; i++)
        {
            var ai = int.Parse(a[i]) & int.Parse(m[i]);
            var bi = int.Parse(b[i]) & int.Parse(m[i]);
            if (ai != bi) return false;
        }
        return true;
    }

    public static string[] Format(List<CheckRow> rows, Device d)
    {
        var L = new List<string>
        {
            "==================================================================",
            " 加固自查（14 项 · 模拟器口径；check 为模拟器专用命令）",
            "=================================================================="
        };
        int pass = 0, fail = 0, na = 0;
        foreach (var r in rows)
        {
            var tag = r.Ok == null ? "[--]" : r.Ok.Value ? "[OK]" : "[NG]";
            L.Add($" {tag} 项{r.No:D2}  {r.Title}");
            L.Add($"         {r.Detail}");
            if (r.Ok == null) na++; else if (r.Ok.Value) pass++; else fail++;
        }
        L.Add("------------------------------------------------------------------");
        L.Add($" 可考 {rows.Count - na} 项：通过 {pass}，未通过 {fail}，不适用 {na}（项 8/11/13）");

        var tips = new List<string>();
        var vty2 = d.Uis.GetValueOrDefault("vty 16 20");
        if (vty2 != null && (vty2.Acl == null || vty2.Protocol != "ssh" || vty2.Auth != "aaa" || !vty2.IdleSet))
            tips.Add("vty 16 20 未与 vty 0 4 同配（预留给网管的线路，容易漏）");
        if (d.Users.ContainsKey("admin")) tips.Add("默认 admin 用户仍在（undo local-user admin）");
        if (!d.Users.Values.Any(u => u.Name.Length > 6)) tips.Add("本地用户名建议 >6 字符（原表备注口径）");
        if (d.Communities.Any(c => !c.Read)) tips.Add("配了 write 团体字（监测装置无 set 需求可只留 read）");
        if (d.SnmpVersion != "v2c v3") tips.Add("SNMP version 建议 v2c v3（监测装置多为 v2c）");
        if (d.StelnetV6) tips.Add("stelnet ipv6 server enable 白开（无 IPv6 管理就关）");
        if (d.Clock.Tz == null) tips.Add("未设时区：clock timezone BeiJin add 08:00:00");
        if (tips.Count > 0)
        {
            L.Add(" 附加提醒：");
            foreach (var x in tips) L.Add("  - " + x);
        }
        L.Add("==================================================================");
        return L.ToArray();
    }
}
