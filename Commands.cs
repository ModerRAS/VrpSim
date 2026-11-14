namespace VrpSim;

public static class CommandSets
{
    public static List<Command> For(string view) => view switch
    {
        "user" => UserView,
        "system" => SystemView,
        "aaa" => AaaView,
        "acl" => AclView,
        "iface" => IfaceView,
        "ui" => UiView,
        "lupp" => LuppView,
        "range" => IfaceView,
        "pg" => PgView,
        "vlan" => VlanView,
        _ => UserView
    };

    // ---------------- helpers ----------------
    static Command C(string kw, string help, Func<ExecCtx, string[]>? run = null)
        => new Command(kw, help) { Run = run };

    static string[] Err(string msg) => new[] { "Error: " + msg };

    // 真机口径报错：先打 ^（指向第 tokIdx 个 token 起点），再打 Error 行
    static string[] ErrAt(ExecCtx c, int tokIdx, string msg)
    {
        var off = 0;
        for (var i = 0; i < tokIdx && i < c.Tokens.Count; i++) off += c.Tokens[i].Length + 1;
        return new[] { new string(' ', c.S.Prompt().Length + off) + "^", "Error: " + msg };
    }

    static bool IsInt(string s, out int v) => int.TryParse(s, out v);

    static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static int PortNum(string s) => s.ToLowerInvariant() switch
    {
        "netbios-ns" => 137, "netbios-dgm" => 138, "netbios-ssn" => 139,
        "ftp" => 21, "ssh" => 22, "telnet" => 23, "http" => 80, "https" => 443,
        "domain" => 53, "snmp" => 161, "snmptrap" => 162,
        _ => IsInt(s, out var v) ? v : -1
    };

    static bool IsIp(string s)
    {
        var p = s.Split('.');
        if (p.Length != 4) return false;
        foreach (var x in p) if (!byte.TryParse(x, out _)) return false;
        return true;
    }

    static IEnumerable<Candidate> SugOf(IEnumerable<string> items, string p) =>
        items.Where(x => x.StartsWith(p, StringComparison.OrdinalIgnoreCase)).Select(x => new Candidate(x, ""));

    static IEnumerable<Candidate> IfSug(Device d, string p) =>
        d.Ifaces.Keys.OrderBy(Device.IfOrder).Select(k => new Candidate(k, ""));

    static IEnumerable<Candidate> AclSug(Device d, string p) =>
        d.Acls.Keys.OrderBy(k => k).Select(k => new Candidate(k.ToString(), ""));

    // interface 可输入的类型（真机/eNSP 候选；Ethernet 按用户现场口径保留）
    static readonly string[] IfKinds = { "Eth-Trunk", "Ethernet", "GigabitEthernet", "LoopBack", "NULL", "Tunnel", "Vlanif" };
    // interface range 只用能批量配置的类型（模拟器扩展命令）
    static readonly string[] IfRangeKinds = { "Ethernet", "GigabitEthernet", "Vlanif" };

    static IEnumerable<Candidate> IfKindSug(Device d, string p) => KindSug(IfKinds, p);
    static IEnumerable<Candidate> IfRangeKindSug(Device d, string p) => KindSug(IfRangeKinds, p);

    static string KindHelp(string k) => k switch
    {
        "Eth-Trunk" => "Ethernet-Trunk interface",
        "Vlanif" => "Vlan interface",
        _ => k + " interface"
    };

    static IEnumerable<Candidate> KindSug(string[] kinds, string p) =>
        kinds.Where(k => k.StartsWith(p, StringComparison.OrdinalIgnoreCase))
             .Select(k => new Candidate(k, KindHelp(k)));

    static IEnumerable<Candidate> IfNumSug(Device d, string p)
    {
        var nums = new List<string>();
        foreach (var k in d.Ifaces.Keys)
        {
            var m = System.Text.RegularExpressions.Regex.Match(k, "^(Ethernet|GigabitEthernet|Vlanif|NULL0)(.+)$");
            if (m.Success) nums.Add(m.Groups[2].Value);
        }
        return nums.Distinct().OrderBy(x => x.Length).ThenBy(x => x)
            .Where(x => x.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            .Select(x => new Candidate(x, ""));
    }

    // 接口编号只接受 "0/0/1" 这类数字/斜杠串，防止把 "to" 之类关键字吃成编号
    static bool IsIfNumToken(string t) => t.Length > 0 && t.All(ch => char.IsDigit(ch) || ch == '/');

    static bool IsIfaceKindToken(string t) =>
        IfKinds.Any(k => k.Equals(t, StringComparison.OrdinalIgnoreCase));

    static bool IsRangeKindToken(string t) =>
        IfRangeKinds.Any(k => k.Equals(t, StringComparison.OrdinalIgnoreCase));

    // 类型+编号连写（Ethernet0/0/1、GigabitEthernet0/0/1、Vlanif8）——真机输入不认
    static bool IsConcatIfaceToken(string t)
    {
        foreach (var k in IfKinds.Where(x => x != "NULL"))
        {
            if (t.Length <= k.Length || !t.StartsWith(k, StringComparison.OrdinalIgnoreCase)) continue;
            var ch = t[k.Length];
            if (char.IsDigit(ch) || ch == '/') return true;
        }
        return false;
    }

    // 完整接口名（Ethernet0/0/1 / Vlanif8）——必须带数字后缀
    static bool IsIfaceNameToken(string t) => Device.SplitIfaceName(t).Num >= 0;

    // 按已选接口类型给编号候选：Vlanif -> VLAN 号；物理口 -> 已存在编号（真机 Tab 口径）
    static Func<Device, string, IReadOnlyDictionary<string, string>, IEnumerable<Candidate>> IfNumCtx(string capKey) =>
        (d, p, caps) =>
        {
            var kind = caps.TryGetValue(capKey, out var k) ? k : "";
            return IfNumFor(d, kind);
        };

    static IEnumerable<Candidate> IfNumFor(Device d, string kind)
    {
        if (kind.Length == 0) return Array.Empty<Candidate>();
        // NULL 接口编号只有 0
        if ("NULL".StartsWith(kind, StringComparison.OrdinalIgnoreCase))
            return new[] { new Candidate("0", "") };
        if ("Vlanif".StartsWith(kind, StringComparison.OrdinalIgnoreCase) || kind.StartsWith("Vlanif", StringComparison.OrdinalIgnoreCase))
            return d.Vlans.OrderBy(v => v).Select(v => new Candidate(v.ToString(), ""));
        var known = new[] { "Ethernet", "Eth-Trunk", "GigabitEthernet", "LoopBack", "Tunnel" };
        var prefix = known.FirstOrDefault(k => k.Equals(kind, StringComparison.OrdinalIgnoreCase))
                     ?? known.FirstOrDefault(k => kind.Length >= 3 && k.StartsWith(kind, StringComparison.OrdinalIgnoreCase));
        if (prefix == null) return Array.Empty<Candidate>();
        return d.Ifaces.Keys
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(n => n.Substring(prefix.Length))
            .Distinct().OrderBy(x => x.Length).ThenBy(x => x)
            .Select(x => new Candidate(x, ""));
    }

    // 端口号/常用端口名候选（destination-port eq/range/lt/gt）
    static IEnumerable<Candidate> PortSug(Device d, string p)
    {
        var list = new (string W, string H)[]
        {
            ("135", "MS RPC"), ("137", "NetBIOS name service"), ("138", "NetBIOS datagram service"),
            ("139", "NetBIOS session service"), ("445", "Microsoft-DS"), ("3389", "Remote desktop"),
            ("ftp", "File Transfer Protocol (21)"), ("ssh", "Secure Shell (22)"), ("telnet", "Telnet (23)"),
            ("http", "Hypertext Transfer Protocol (80)"), ("https", "HTTPS (443)"), ("domain", "Domain Name Server (53)"),
            ("snmp", "Simple Network Management Protocol (161)"), ("snmptrap", "SNMP Trap (162)"),
            ("netbios-ns", "NetBIOS name service (137)"), ("netbios-dgm", "NetBIOS datagram service (138)"),
            ("netbios-ssn", "NetBIOS session service (139)")
        };
        return list.Where(x => x.W.StartsWith(p, StringComparison.OrdinalIgnoreCase)).Select(x => new Candidate(x.W, x.H));
    }

    static IEnumerable<Candidate> UserSug(Device d, string p) =>
        d.Users.Keys.OrderBy(k => k).Select(k => new Candidate(k, ""));

    static IEnumerable<Candidate> VlanSug(Device d, string p) =>
        d.Vlans.OrderBy(v => v).Select(v => new Candidate(v.ToString(), ""));


    // 接口类型按真机口径要写全称（不接受 Eth/Gi 这种缩写），大小写不限
    static readonly Func<string, bool> IfKindAccept = t =>
        IfKinds.Any(k => k.Equals(t, StringComparison.OrdinalIgnoreCase));

    static readonly Func<string, bool> IfRangeKindAccept = t =>
        IfRangeKinds.Any(k => k.Equals(t, StringComparison.OrdinalIgnoreCase));

    static string CanonIfaceKind(string k)
    {
        foreach (var x in IfKinds)
            if (x.Equals(k, StringComparison.OrdinalIgnoreCase)) return x;
        return k;
    }

    // 完整接口名的大小写规范化：ethernet0/0/1 -> Ethernet0/0/1（真机拼写）
    static string CanonIfaceName(string name)
    {
        var (prefix, num) = Device.SplitIfaceName(name);
        if (num < 0) return CanonIfaceKind(name);
        var i = 0;
        while (i < prefix.Length && char.IsLetter(prefix[i])) i++;
        return CanonIfaceKind(prefix.Substring(0, i)) + prefix.Substring(i) + num;
    }

    static readonly Val ValIf = new("if", "<interface>", IfSug);
    static readonly Val ValIp = new("ip", "<X.X.X.X>");
    static readonly Val ValUser = new("name", "STRING<1-64>", UserSug);
    static Val VInt(string n, string hint) => new(n, hint, null, s => IsInt(s, out _));
    static readonly Val AnyVal = new("x", "<text>", null, _ => true, null, true);

    static readonly Func<Device, string, IEnumerable<Candidate>> SugPermitDeny = (d, p) => SugOf(new[] { "permit", "deny" }, p);
    static readonly Func<Device, string, IEnumerable<Candidate>> SugTcpUdp = (d, p) => SugOf(new[] { "tcp", "udp" }, p);
    static readonly Func<Device, string, IEnumerable<Candidate>> SugCipherMode = (d, p) => SugOf(new[] { "cipher", "irreversible-cipher", "simple" }, p);
    static readonly Func<Device, string, IEnumerable<Candidate>> SugServices = (d, p) =>
        SugOf(new[] { "8021x", "bind", "ftp", "http", "ppp", "ssh", "telnet", "terminal", "web", "x25-pad" }, p);
    static readonly Val ValAclAction = new("action", "{permit|deny}", SugPermitDeny,
        t => t.Equals("permit", StringComparison.OrdinalIgnoreCase) || t.Equals("deny", StringComparison.OrdinalIgnoreCase));
    static readonly Func<Device, string, IEnumerable<Candidate>> SugProtos = (d, p) =>
        SugOf(new[] { "gre", "icmp", "igmp", "ip", "ipinip", "ospf", "tcp", "udp" }, p);
    static readonly string[] ProtoNames = { "gre", "icmp", "igmp", "ip", "ipinip", "ospf", "tcp", "udp" };
    static readonly Val ValProto = new("proto", "{gre|icmp|igmp|ip|ipinip|ospf|tcp|udp}", SugProtos,
        t => ProtoNames.Any(x => x.Equals(t, StringComparison.OrdinalIgnoreCase))
             || (IsInt(t, out var pn) && pn >= 1 && pn <= 255));
    static readonly Val ValAclId = new("id", "INTEGER<0-4294967294>", null, s => long.TryParse(s, out var v) && v >= 0 && v <= 4294967294);
    static readonly Val ValPort = new("port", "INTEGER<0-65535>", PortSug, s => IsInt(s, out _));
    static readonly Val ValWildcard = new("wc", "<wildcard>",
        (d, p) => new[] { new Candidate("0", "Wildcard bits : 0.0.0.0 (a host)"), new Candidate("X.X.X.X", "Wildcard of source") },
        s => IsInt(s, out _) || IsIp(s));

    // ---------------- display ----------------
    static readonly Command DisplayCmd = BuildDisplay();

    static Command BuildDisplay()
    {
        var c = new Command("display", "Display information") { Run = DisplayRun };
        c.T(new Lit("current-configuration", "Current configuration"));
        c.T(new Lit("current-configuration", "Current configuration"), new Lit("|", "Filter output"), new Val("mode", "include|begin|exclude", (d, p) => SugOf(new[] { "include", "begin", "exclude" }, p)), new Val("pat", "<text>"));
        c.T(new Lit("version", "Version information"));
        c.T(new Lit("clock", "Current date and time"));
        c.T(new Lit("this", "Current view configuration"));
        c.T(new Lit("interface", "Interface information"));
        c.T(new Lit("interface", "Interface information"), new Lit("brief", "Brief interface information"));
        c.T(new Lit("acl", "ACL information"));
        c.T(new Lit("acl", "ACL information"), new Lit("all", "All ACLs"));
        c.T(new Lit("acl", "ACL information"), new Val("aclno", "INTEGER<2000-3999>", AclSug, s => IsInt(s, out _)));
        c.T(new Lit("telnet", "Telnet service"), new Lit("server", "Telnet server"), new Lit("status", "Service status"));
        c.T(new Lit("ftp", "FTP service"), new Lit("server", "FTP server"));
        c.T(new Lit("http", "HTTP service"), new Lit("server", "HTTP server"));
        c.T(new Lit("snmp-agent", "SNMP agent"), new Lit("community", "SNMP community"));
        c.T(new Lit("user-interface", "User interface information"));
        c.T(new Lit("local-user", "Local user information"));
        c.T(new Lit("local-aaa-user", "Local administrator password policy"), new Lit("password", "Password policy"), new Lit("policy", "Password policy"), new Lit("administrator", "Administrator password policy"));
        c.T(new Lit("logbuffer", "Log buffer"));
        c.T(new Lit("cpu-usage", "CPU usage"));
        c.T(new Lit("memory-usage", "Memory usage"));
        c.T(new Lit("power", "Power module status"));
        c.T(new Lit("vlan", "VLAN information"));
        c.T(new Lit("ip", "IP information"), new Lit("interface", "IP interface"), new Lit("brief", "Brief information"));
        c.T(new Lit("stelnet", "STelnet service"), new Lit("server", "STelnet server"));
        c.T(new Lit("ssh", "SSH service"), new Lit("server", "SSH server"), new Lit("status", "Service status"));
        c.T(new Lit("saved-configuration", "Saved configuration"));
        c.T(new Lit("users", "Login users"));
        return c;
    }

    static string[] DisplayRun(ExecCtx c)
    {
        var d = c.Dev;
        var t = c.Tokens;
        var sub = t.Count > 1 ? t[1].ToLowerInvariant() : "";
        List<string> lines = sub switch
        {
            "current-configuration" => d.RenderConfig(),
            "saved-configuration" => SavedConfigLines(),
            "version" => VersionLines(d),
            "clock" => new List<string> { "  " + d.Clock.Text + (d.Clock.Tz != null ? " " + d.Clock.Tz + " (UTC+08:00)" : " (UTC)") },
            "this" => ThisLines(c),
            "interface" => InterfaceBrief(d),
            "acl" => AclLines(d, t),
            "telnet" => new List<string> { "  TELNET server is " + (d.TelnetServer ? "enabled" : "disabled") + "." },
            "ftp" => new List<string> { "  FTP server is " + (d.FtpServer ? "enabled" : "disabled") + "." },
            "http" => new List<string> { "  HTTP server is " + (d.HttpServer ? "enabled" : "disabled") + "." },
            "snmp-agent" => SnmpCommunityLines(d),
            "user-interface" => UiLines(d),
            "local-user" => LocalUserLines(d),
            "local-aaa-user" => AaaPolicyLines(d),
            "logbuffer" => LogBuffer(d),
            "cpu-usage" => new List<string> { "  CPU usage: 12%", "  CPU usage at 5s: 12%; at 1min: 11%; at 5min: 10%" },
            "memory-usage" => new List<string> { "  Memory total: 1024 MB, used: 348 MB, free: 676 MB", "  Memory utilization: 34%" },
            "power" => new List<string> { "  Power 1: Normal", "  Power 2: Normal" },
            "vlan" => VlanLines(d),
            "ip" => IpIfaceLines(d),
            "stelnet" => new List<string> { "  STelnet server is " + (d.StelnetV4 ? "enabled" : "disabled") + ".", "  STelnet IPv6 server is " + (d.StelnetV6 ? "enabled" : "disabled") + "." },
            "ssh" => new List<string> { "  SSH server is " + (d.StelnetV4 ? "enabled" : "disabled") + "." },
            "users" => new List<string> { "  UserIndex UserName   UserIP        UserChannel", "  0         admin      -             CON0" },
            _ => new List<string> { "Error: Unrecognized command found at '^' position." }
        };
        if (c.Has("pat"))
        {
            var pat = c.Cap("pat").ToLowerInvariant();
            var mode = c.Cap("mode").ToLowerInvariant();
            if (mode == "include") lines = lines.Where(l => l.ToLowerInvariant().Contains(pat)).ToList();
            else if (mode == "exclude") lines = lines.Where(l => !l.ToLowerInvariant().Contains(pat)).ToList();
            else if (mode == "begin")
            {
                var idx = lines.FindIndex(l => l.ToLowerInvariant().Contains(pat));
                lines = idx >= 0 ? lines.Skip(idx).ToList() : new List<string>();
            }
        }
        return lines.ToArray();
    }

    static List<string> VersionLines(Device d) => new()
    {
        "Huawei Versatile Routing Platform Software",
        "VRP (R) software, Version 5.170 (S5700 V200R011C10SPC600)",
        "Copyright (C) 2012-2019 Huawei Technologies Co., Ltd.",
        d.SysName + " uptime is 0 days, 0 hours, 12 minutes",
        "  (simulator: not a real VRP implementation, for hardening drill only)"
    };

    static List<string> ThisLines(ExecCtx c)
    {
        var d = c.Dev;
        switch (c.S.View)
        {
            case "aaa":
                return WrapThis(d.RenderAaa());
            case "acl":
                return d.Acls.TryGetValue(c.S.AclNumber, out var a) ? WrapThis(d.RenderAcl(a)) : new List<string> { "Error: ACL not found." };
            case "iface":
                return d.Ifaces.TryGetValue(c.S.IfName, out var i) ? WrapThis(d.RenderIface(i)) : new List<string> { "Error: Interface not found." };
            case "ui":
                return d.Uis.TryGetValue(c.S.UiName, out var u) ? WrapThis(d.RenderUi(u)) : new List<string> { "Error: User interface not found." };
            case "lupp":
                return LuppThis(d);
            case "vlan":
                // 真机：空 VLAN（无 description/name）display this 只有 # + return，不显示 vlan 行
                return d.HasVlanCfg(c.S.VlanId) ? WrapThis(d.RenderVlan(c.S.VlanId)) : new List<string> { "#", "return" };
            case "pg":
                return WrapThis(d.RenderPortGroup(c.S.PgName,
                    d.PortGroups.TryGetValue(c.S.PgName, out var members) ? members : new List<string>()));
            case "range":
            {
                var L = new List<string>();
                foreach (var n in c.S.RangeIfaces.Where(d.Ifaces.ContainsKey))
                {
                    L.Add("#");
                    L.AddRange(d.RenderIface(d.Ifaces[n]));
                }
                L.Add("#");
                L.Add("return");
                return L;
            }
            default:
                return d.RenderConfig();
        }
    }

    // 真机 display this 的格式：配置块前后各一行 #，末尾跟 return
    static List<string> WrapThis(List<string> block)
    {
        var L = new List<string> { "#" };
        L.AddRange(block);
        L.Add("#");
        L.Add("return");
        return L;
    }

    static List<string> LuppThis(Device d)
    {
        var L = new List<string> { "#", "aaa", " local-aaa-user password policy administrator" };
        foreach (var line in AaaPolicyConfigLines(d)) L.Add(" " + line);
        L.Add("#");
        L.Add("return");
        return L;
    }

    static List<string> InterfaceBrief(Device d)
    {
        const string fmt = " {0,-28}{1,-8}{2,-9}{3,6}{4,7}{5,9}{6,10}";
        var L = new List<string>
        {
            string.Format(fmt, "Interface", "PHY", "Protocol", "InUti", "OutUti", "inErrors", "outErrors")
        };
        foreach (var i in d.Ifaces.Values.Where(i => i.IsPort || i.IsVlanif).OrderBy(i => Device.IfOrder(i.Name)))
        {
            string phy;
            if (i.Shutdown) phy = "*down";
            else if (i.IsVlanif) phy = i.Ip != null ? "up" : "down";
            else phy = i.Cabled ? "up" : "down";
            var proto = phy == "up" ? "up" : "down";
            L.Add(string.Format(fmt, i.Name, phy, proto, "0%", "0%", "0", "0"));
        }
        return L;
    }

    static List<string> AclLines(Device d, List<string> t)
    {
        var L = new List<string>();
        if (t.Count > 2 && IsInt(t[2], out var only))
        {
            if (!d.Acls.ContainsKey(only)) return new List<string> { "Error: The ACL does not exist." };
            return AclBlock(d.Acls[only]);
        }
        foreach (var a in d.Acls.Values.OrderBy(a => a.Number))
        {
            L.AddRange(AclBlock(a));
            L.Add("");
        }
        return L;
    }

    static List<string> AclBlock(Acl a)
    {
        var L = new List<string>
        {
            $"{(a.Number >= 3000 ? "Advanced" : "Basic")} ACL {a.Number}, {a.Rules.Count} rule(s)",
            "Acl's step is 5"
        };
        if (!string.IsNullOrEmpty(a.Description)) L.Add($" description {a.Description}");
        foreach (var r in a.Rules.OrderBy(r => r.Id)) L.Add(" " + r.Raw);
        return L;
    }


    static List<string> SnmpCommunityLines(Device d)
    {
        var L = new List<string>();
        foreach (var c in d.Communities)
            L.Add($"  Community: {Device.Cipher(c.Password)}  Access: {(c.Read ? "read" : "write")}  ACL: {(c.Acl?.ToString() ?? "none")}");
        if (d.Communities.Count == 0) L.Add("  No SNMP community configured.");
        return L;
    }

    static List<string> UiLines(Device d)
    {
        var L = new List<string> { " UI           Auth      ACL   Idle(min)  Protocol" };
        foreach (var u in d.Uis.Values)
            L.Add($" {u.Name,-12} {(u.Auth ?? "none"),-9} {(u.Acl?.ToString() ?? "-"),-5} {(u.IdleSet ? u.IdleMin.ToString() : "10"),-10} {u.Protocol ?? "all"}");
        return L;
    }

    static List<string> LocalUserLines(Device d)
    {
        var L = new List<string>();
        foreach (var u in d.Users.Values.OrderBy(u => u.Name))
            L.Add($"  User: {u.Name}  Privilege: {u.Privilege}  Service: {string.Join(" ", u.ServiceTypes)}");
        if (d.Users.Count == 0) L.Add("  No local user.");
        return L;
    }

    static List<string> LogBuffer(Device d)
    {
        var now = d.Clock.Text;
        return new List<string>
        {
            $" {now} HUAWEI DS/4/DATASYNC_CFGCHANGE: configurations have been changed.",
            $" {now} HUAWEI LINE/5/VTYUSERLOGIN: A user login. (UserName=admin, UserChannel=CON0)",
            $" {now} HUAWEI SNMP/4/TRAP: send trap to {d.MonitorIp}"
        };
    }

    static List<string> VlanLines(Device d)
    {
        var L = new List<string>
        {
            " The total number of vlans is " + d.Vlans.Count,
            "",
            " VLAN ID   Name        Status      Ports",
            " --------  ----------  ----------  -------------------------------"
        };
        foreach (var v in d.Vlans.OrderBy(v => v)) L.Add($" {v,-9} VLAN{v:D4}      enable");
        return L;
    }

    static List<string> IpIfaceLines(Device d)
    {
        var L = new List<string> { " Interface                   IP Address     Mask            Physical" };
        foreach (var i in d.Ifaces.Values.Where(i => i.IsVlanif && i.Ip != null).OrderBy(i => Device.IfOrder(i.Name)))
            L.Add($" {i.Name,-27} {i.Ip,-14} {i.Mask,-15} UP");
        return L;
    }

    // ---------------- save / check / nav ----------------
    static readonly Command SaveCmd = new Command("save", "Save configuration") { Run = SaveRun }.T();

    static string[] SaveRun(ExecCtx c)
    {
        var s = c.S;
        var lines = new List<string>();
        if (s.Interactive && s.ReadLine != null)
        {
            // 真机两段式：先提示写入，再单独一行询问
            Console.WriteLine("The current configuration will be written to the device.");
            var ans = s.ReadLine("Are you sure to continue?[Y/N]:") ?? "";
            if (!ans.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase))
                return new[] { "Info: The operation is canceled." };
        }
        else
        {
            lines.Add("The current configuration will be written to the device.");
            lines.Add("Are you sure to continue?[Y/N]:y");
        }
        try { File.WriteAllLines("saved-config.cfg", c.Dev.RenderConfig()); } catch { }
        s.Saved = true;
        lines.Add("Now saving the current configuration to the slot 0.");
        lines.Add("Save the configuration successfully.");
        return lines.ToArray();
    }

    static List<string> SavedConfigLines()
    {
        try
        {
            if (File.Exists("saved-config.cfg")) return File.ReadAllLines("saved-config.cfg").ToList();
        }
        catch { }
        return new List<string> { "Info: The saved configuration file does not exist." };
    }

    static readonly Command CheckCmd = new Command("check", "Run hardening self-check (simulator only)")
    { Run = c => Checks.Format(Checks.Run(c.Dev), c.Dev) }.T();

    static Command Nav(string kw, string help, Func<ExecCtx, string[]> run)
        => new Command(kw, help) { Run = run }.T();

    static string[] QuitRun(ExecCtx c)
    {
        var s = c.S;
        switch (s.View)
        {
            case "user":
                s.ExitRequested = true;
                return new[] { "User interface con0 is available." };
            case "system":
                s.View = "user";
                return Array.Empty<string>();
            case "lupp":
                s.View = "aaa";
                return Array.Empty<string>();
            case "range":
            case "pg":
            case "vlan":
                s.View = "system";
                return Array.Empty<string>();
            default:
                s.View = "system";
                return Array.Empty<string>();
        }
    }

    static string[] ReturnRun(ExecCtx c) { c.S.View = "user"; return Array.Empty<string>(); }

    // ---------------- views ----------------
    static readonly List<Command> UserView = new()
    {
        new Command("system-view", "Enter system view")
        { Run = c => { c.S.View = "system"; return new[] { "Enter system view, return user view with Ctrl+Z." }; } }.T(),
        Nav("quit", "Exit current view", QuitRun),
        new Command("return", "Return to user view") { Run = ReturnRun }.T(),
        new Command("reset", "Reset to factory configuration (simulator only)")
        {
            Run = c =>
            {
                c.S.Dev = Device.Factory();
                c.S.View = "user";
                StateStore.Delete(StateStore.DefaultPath);
                return new[] { "  Info: The device has been reset to factory default." };
            }
        }.T(),
        DisplayCmd, SaveCmd, CheckCmd
    };

    static readonly Command UndoSystemCmd = BuildUndoSystem();

    static readonly List<Command> SystemView = new()
    {
        C("sysname", "Set the system name", c => { c.Dev.SysName = c.Cap("name"); return Array.Empty<string>(); })
            .T(new Val("name", "STRING<1-246>")),
        C("aaa", "Enter AAA view", AaaRun)
            .T(new Lit("abnormal-offline-record"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 4))
            .T(new Lit("offline-record"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 4))
            .T(new Lit("online-fail-record"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 4))
            .T(),
        C("acl", "Enter ACL view", AclRun)
            .T(new Val("num", "INTEGER<2000-2999>", AclSug, s => IsInt(s, out _)))
            .T(new Val("num", "INTEGER<3000-3999>", AclSug, s => IsInt(s, out _)))
            .T(new Val("num", "INTEGER<4000-4999>", AclSug, s => IsInt(s, out _)))
            .T(new Val("num", "INTEGER<5000-5999>", AclSug, s => IsInt(s, out _)))
            .T(new Lit("ipv6", "ACL IPv6"), new Opt(new Val("num", "INTEGER<2000-3999>", null, s => IsInt(s, out _))))
            .T(new Lit("name", "Specify a named ACL"), new Val("nm", "STRING<1-64>"))
            .T(new Lit("number", "Specify a numbered ACL"), new Val("num", "INTEGER<2000-2999>", AclSug, s => IsInt(s, out _)))
            .T(new Lit("number", "Specify a numbered ACL"), new Val("num", "INTEGER<3000-3999>", AclSug, s => IsInt(s, out _)))
            .T(new Lit("number", "Specify a numbered ACL"), new Val("num", "INTEGER<4000-4999>", AclSug, s => IsInt(s, out _)))
            .T(new Lit("number", "Specify a numbered ACL"), new Val("num", "INTEGER<5000-5999>", AclSug, s => IsInt(s, out _))),
        C("interface", "Enter interface view", EnterIface)
            .T(new Lit("range", "Set interface range"),
                new Val("kind1", "<interface-type>", IfRangeKindSug, IfRangeKindAccept, null, true), new Val("num1", "<interface-number>", null, IsIfNumToken, IfNumCtx("kind1")),
                new Lit("to", "End of interface range"),
                new Val("kind2", "<interface-type>", IfRangeKindSug, IfRangeKindAccept, null, true), new Val("num2", "<interface-number>", null, IsIfNumToken, IfNumCtx("kind2")))
            // 宽松模板：只为把"连写/漏写 to"等情况交给 EnterRange 按真机口径报错（合法写法由上面的拆分模板先命中）
            .T(new Lit("range", "Set interface range"),
                new Val("bad0", "<interface>", null, _ => true, null, true),
                new Rep(new Val("badN", "<interface>", null, _ => true, null, true), 7))
            .T(new Val("kind", "<interface-type>", IfKindSug, IfKindAccept, null, true), new Val("num", "<interface-number>", null, IsIfNumToken, IfNumCtx("kind")))
            .T(new Val("if", "<interface>", null, IsIfaceNameToken, null, true)),
        C("port-group", "Enter port group view", EnterPortGroup)
            .T(new Val("pg", "STRING<1-32>", (d, p) => d.PortGroups.Keys.OrderBy(k => k).Select(k => new Candidate(k, "")))),
        C("vlan", "Create VLAN and enter VLAN view", VlanRun)
            .T(new Lit("batch", "Batch process"), new Rep(new Val("id", "INTEGER<1-4094>", VlanSug, s => IsInt(s, out _))))
            .T(new Val("id", "INTEGER<1-4094>", null, s => IsInt(s, out _))),
        C("rsa", "RSA key pair", RsaRun)
            .T(new Lit("local-key-pair"), new Lit("create"))
            .T(new Lit("peer-public-key")),
        C("stelnet", "Stelnet client/server", StelnetRun)
            .T(new Lit("server"), new Lit("enable"))
            .T(new Lit("ipv4"), new Lit("server"), new Lit("enable"))
            .T(new Lit("ipv6"), new Lit("server"), new Lit("enable"))
            .T(new Val("host", "STRING<1-255>")),
        C("ssh", "SSH related", _ => new[] { "  Info: The command is executed successfully." })
            .T(new Lit("client"), new Lit("first-time"), new Lit("enable"))
            .T(new Lit("user"), ValUser, new Lit("authentication-type"), new Val("t", "<auth-type>"))
            .T(new Lit("user"), ValUser, new Lit("service-type"), new Val("t", "<service-type>")),
        C("snmp-agent", "SNMP agent", SnmpRun)
            .T()
            .T(new Lit("community"), new Val("dir", "{read|write}", (d, p) => SugOf(new[] { "read", "write" }, p)),
                new Opt(new Lit("cipher", "Cipher text")), new Val("pw", "STRING<1-32>"),
                new Opt(new Lit("acl"), new Val("aclnum", "INTEGER<2000-3999>", AclSug, s => IsInt(s, out _))))
            .T(new Lit("community"), new Val("dir", "{read|write}", (d, p) => SugOf(new[] { "read", "write" }, p)),
                new Opt(new Lit("simple", "Plain text")), new Val("pw", "STRING<1-32>"),
                new Opt(new Lit("acl"), new Val("aclnum", "INTEGER<2000-3999>", AclSug, s => IsInt(s, out _))))
            .T(new Lit("extend", "SNMP extend configuration"), new Rep(AnyVal, 4))
            .T(new Lit("group", "Set an SNMP group based on USM"), new Rep(AnyVal, 4))
            .T(new Lit("inform", "Configure parameters for notifications in inform"), new Rep(AnyVal, 3))
            .T(new Lit("local-engineid", "Set the engine ID of local SNMP entity"), new Rep(AnyVal, 3))
            .T(new Lit("manner", "SNMP message manner"), new Rep(AnyVal, 3))
            .T(new Lit("mib-view", "Set SNMP MIB view information"), new Rep(AnyVal, 4))
            .T(new Lit("notification-log", "Notification log"), new Rep(AnyVal, 4))
            .T(new Lit("notify-filter-profile", "Define notification filter profile"), new Rep(AnyVal, 4))
            .T(new Lit("packet", "Set SNMP packet's parameters"), new Rep(AnyVal, 3))
            .T(new Lit("packet-priority", "The priority of the SNMP packet"), new Rep(AnyVal, 2))
            .T(new Lit("set-cache", "Cache to store the result of the set operation"), new Rep(AnyVal, 3))
            .T(new Lit("usm-user", "Set a new user for access to SNMP entity"), new Rep(AnyVal, 6))
            .T(new Lit("sys-info"), new Lit("contact", "Set the contact information for system maintenance"), new Val("v", "STRING<1-255>"))
            .T(new Lit("sys-info"), new Lit("location", "Set the physical position"), new Val("v", "STRING<1-255>"))
            .T(new Lit("sys-info"), new Lit("version"), new Rep(new Val("ver", "{v1|v2c|v3|all}", (d, p) => SugOf(new[] { "v1", "v2c", "v3", "all" }, p))))
            .T(new Lit("target-host"), new Lit("inform"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 8))
            .T(new Lit("target-host"), new Lit("trap"), new Lit("address"), new Lit("udp-domain"), ValIp,
                new Opt(new Lit("udp-port"), new Val("port", "INTEGER<1-65535>", null, s => IsInt(s, out _))),
                new Lit("params"), new Lit("securityname"), new Opt(new Lit("cipher", "Cipher text")), new Val("sec", "STRING<1-32>"),
                new Opt(new Val("ver", "{v2c|v3}", (d, p) => SugOf(new[] { "v2c", "v3" }, p))))
            .T(new Lit("target-host"), new Lit("trap"), new Lit("address"), new Lit("udp-domain"), ValIp,
                new Opt(new Lit("udp-port"), new Val("port", "INTEGER<1-65535>", null, s => IsInt(s, out _))),
                new Lit("params"), new Lit("securityname"), new Opt(new Lit("simple", "Plain text")), new Val("sec", "STRING<1-32>"),
                new Opt(new Val("ver", "{v2c|v3}", (d, p) => SugOf(new[] { "v2c", "v3" }, p))))
            .T(new Lit("trap"), new Lit("disable", "SNMP trap/notification disable commands group"))
            .T(new Lit("trap"), new Lit("enable", "SNMP trap/notification enable commands group"))
            .T(new Lit("trap"), new Lit("life"), new Val("n", "INTEGER<0-1000>", null, s => IsInt(s, out _)))
            .T(new Lit("trap"), new Lit("queue-size"), new Val("n", "INTEGER<1-1000>", null, s => IsInt(s, out _)))
            .T(new Lit("trap"), new Lit("source", "Set the source interface of trap packet"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 3))
            .T(new Lit("trap"), new Lit("suppress"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 4))
            .T(new Lit("trap"), new Lit("type"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 4)),
        C("info-center", "Log service", InfoCenterRun)
            .T(new Lit("channel"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 4))
            .T(new Lit("console"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 4))
            .T(new Lit("enable", "Enable the information center"))
            .T(new Lit("filter-id"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 4))
            .T(new Lit("local"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 4))
            .T(new Lit("logbuffer"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 4))
            .T(new Lit("loghost"), new Val("ip", "<X.X.X.X>") { HintHelp = "Logging host IP address" })
            .T(new Lit("loghost"), new Lit("ipv6", "Logging host IPv6 address"), new Val("ip6", "X:X::X:X"))
            .T(new Lit("loghost"), new Lit("source", "Set the source address of packets sent to loghost"), ValIf, ValIp)
            .T(new Lit("monitor"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 4))
            .T(new Lit("rate-limit"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 4))
            .T(new Lit("snmp"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 4))
            .T(new Lit("source"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 4))
            .T(new Lit("statistic-suppress"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 4))
            .T(new Lit("timestamp"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 4))
            .T(new Lit("trapbuffer"), new Rep(new Val("a", "<text>", null, _ => true, null, true), 4)),
        C("clock", "Set clock/timezone", ClockRun)
            .T(new Lit("timezone"), new Val("tz", "STRING<1-32>"), new Lit("add", "Add to the UTC time"), new Val("off", "<HH:MM:SS>"))
            .T(new Lit("timezone"), new Val("tz", "STRING<1-32>"), new Lit("minus", "Subtract from the UTC time"), new Val("off", "<HH:MM:SS>"))
            .T(new Lit("datetime"), new Val("time", "<HH:MM:SS>"), new Val("date", "<YYYY-MM-DD>")),
        C("user-interface", "Enter user interface view", EnterUi)
            .T(new Val("one", "INTEGER<0,34-48,50-54>", null, s => IsInt(s, out _)))
            .T(new Lit("console", "Primary user terminal interface"), new Val("n", "INTEGER<0>", null, s => IsInt(s, out _)))
            .T(new Lit("current", "The current user terminal interface"))
            .T(new Lit("maximum-vty", "The maximum number of VTY users"), new Val("n", "INTEGER<1-1024>", null, s => IsInt(s, out _)))
            .T(new Lit("vty", "Virtual terminal user interface"), new Val("a", "INTEGER<0-20>", null, s => IsInt(s, out _)), new Opt(new Val("b", "INTEGER<0-20>", null, s => IsInt(s, out _)))),
        UndoSystemCmd,
        Nav("quit", "Exit current view", QuitRun),
        Nav("return", "Return to user view", ReturnRun),
        DisplayCmd, SaveCmd, CheckCmd
    };

    static string[] AaaRun(ExecCtx c)
    {
        // 带参数的 aaa 子命令（abnormal-offline-record 等）只做接受，不切视图
        if (c.Tokens.Count > 1) return Array.Empty<string>();
        c.S.View = "aaa";
        return Array.Empty<string>();
    }

    static string[] AclRun(ExecCtx c)
    {
        // ipv6 / name 两种写法本模拟器不做模型，接受不报错
        if (c.Tokens.Count > 1 && (Eq(c.Tokens[1], "ipv6") || Eq(c.Tokens[1], "name"))) return Array.Empty<string>();
        return EnterAcl(c);
    }

    static string[] EnterAcl(ExecCtx c)
    {
        if (!IsInt(c.Cap("num"), out var n) || n < 2000 || n > 5999) return Err("Wrong parameter found at '^' position.");
        if (!c.Dev.Acls.ContainsKey(n)) c.Dev.Acls[n] = new Acl { Number = n };
        c.S.AclNumber = n;
        c.S.View = "acl";
        return Array.Empty<string>();
    }

    static string[] EnterIface(ExecCtx c)
    {
        if (c.Tokens.Count > 1 && Eq(c.Tokens[1], "range"))
        {
            return EnterRange(c);
        }
        // 真机输入只接受"类型 + 编号"分开写；Ethernet0/0/1 / Vlanif8 这类连写直接按真机报错
        // （NULL0 例外：eNSP/真机 interface NULL 0 与 interface NULL0 两种写法都接受）
        if (c.Has("if") && !Eq(c.Cap("if"), "NULL0"))
            return ErrAt(c, 1, "Wrong parameter found at '^' position.");
        var kind = CanonIfaceKind(c.Cap("kind"));
        var num = c.Cap("num");
        if (!c.Has("if") && Eq(kind, "NULL") && !Eq(num, "0"))
            return ErrAt(c, 2, "Wrong parameter found at '^' position.");
        var name = c.Has("if")
            ? "NULL0"
            : (Eq(kind, "NULL") ? "NULL0" : kind + num);
        if (!c.Dev.Ifaces.ContainsKey(name))
        {
            if (Eq(kind, "Vlanif"))
            {
                if (!IsInt(num, out var vlan)) return ErrAt(c, 2, "Wrong parameter found at '^' position.");
                if (!c.Dev.Vlans.Contains(vlan)) return Err("The VLAN does not exist.");
                c.Dev.Ifaces[name] = new Iface { Name = name, IsVlanif = true };
            }
            else if (Eq(kind, "Eth-Trunk") || Eq(kind, "LoopBack") || Eq(kind, "Tunnel"))
            {
                var max = Eq(kind, "Eth-Trunk") ? 127 : 1023;
                if (!IsInt(num, out var n) || n < 0 || n > max) return ErrAt(c, 2, "Wrong parameter found at '^' position.");
                c.Dev.Ifaces[name] = new Iface { Name = name };
            }
            else return Err("Wrong parameter found at '^' position.");
        }
        c.S.IfName = name;
        c.S.View = "iface";
        return Array.Empty<string>();
    }

    static string[] EnterRange(ExecCtx c)
    {
        var t = c.Tokens;
        // 类型+编号连写（Ethernet0/0/1 …）：真机不认，^ 指向该 token
        for (var i = 1; i < t.Count; i++)
            if (IsConcatIfaceToken(t[i])) return ErrAt(c, i, "Wrong parameter found at '^' position.");
        // 合法写法只有：interface range <type> <num> to <type> <num>
        if (t.Count != 7) return ErrAt(c, Math.Min(t.Count, 2), "Wrong parameter found at '^' position.");
        if (!IsRangeKindToken(t[2])) return ErrAt(c, 2, "Wrong parameter found at '^' position.");
        if (!IsIfNumToken(t[3])) return ErrAt(c, 3, "Wrong parameter found at '^' position.");
        if (!Eq(t[4], "to")) return ErrAt(c, 4, "Wrong parameter found at '^' position.");
        if (!IsRangeKindToken(t[5])) return ErrAt(c, 5, "Wrong parameter found at '^' position.");
        if (!IsIfNumToken(t[6])) return ErrAt(c, 6, "Wrong parameter found at '^' position.");
        // 类型大小写不敏感、两端可混用；拼成规范名再查表（字典是大小写敏感的）
        string left = CanonIfaceKind(t[2]) + t[3];
        string right = CanonIfaceKind(t[5]) + t[6];
        var (p1, n1) = Device.SplitIfaceName(left);
        var (p2, n2) = Device.SplitIfaceName(right);
        if (!p1.Equals(p2, StringComparison.OrdinalIgnoreCase) || n1 < 0 || n2 < 0 || n1 > n2 || n2 - n1 + 1 > 64)
            return Err("Wrong parameter found at '^' position.");
        var list = new List<string>();
        for (var n = n1; n <= n2; n++)
        {
            var nm = p1 + n;
            if (!c.Dev.Ifaces.ContainsKey(nm)) return Err("Wrong parameter found at '^' position.");
            list.Add(nm);
        }
        c.S.RangeIfaces = list;
        c.S.View = "range";
        return Array.Empty<string>();
    }

    static List<Iface> Targets(ExecCtx c)
    {
        if (c.S.View == "range" && c.S.RangeIfaces.Count > 0)
            return c.S.RangeIfaces.Where(n => c.Dev.Ifaces.ContainsKey(n)).Select(n => c.Dev.Ifaces[n]).ToList();
        if (c.S.View == "pg" && c.Dev.PortGroups.TryGetValue(c.S.PgName, out var members) && members.Count > 0)
            return members.Where(n => c.Dev.Ifaces.ContainsKey(n)).Select(n => c.Dev.Ifaces[n]).ToList();
        return new List<Iface> { SetIface(c) };
    }

    static string[] VlanBatch(ExecCtx c)
    {
        var ids = c.Tokens.Skip(2).Select(x => IsInt(x, out var v) ? v : -1).Where(v => v > 0 && v <= 4094).ToList();
        if (ids.Count == 0) return Err("Wrong parameter found at '^' position.");
        foreach (var v in ids) c.Dev.Vlans.Add(v);
        return Array.Empty<string>();
    }

    static string[] VlanRun(ExecCtx c)
    {
        if (c.Tokens.Count > 1 && Eq(c.Tokens[1], "batch")) return VlanBatch(c);
        if (!IsInt(c.Cap("id"), out var id) || id < 1 || id > 4094)
            return ErrAt(c, 1, "Wrong parameter found at '^' position.");
        // 已存在则直接进视图，不存在则创建再进（真机行为）
        c.Dev.Vlans.Add(id);
        c.S.VlanId = id;
        c.S.View = "vlan";
        return Array.Empty<string>();
    }

    static string[] RsaRun(ExecCtx c)
    {
        if (c.Tokens.Count > 1 && Eq(c.Tokens[1], "peer-public-key")) return Array.Empty<string>();
        return new[]
        {
        "The key name will be: " + c.Dev.SysName + "_Host",
        "% RSA keys defined for " + c.Dev.SysName + "_Host already exist.",
        "Confirm to replace them? [y/n]:y",
        "The range of public key size is (2048 ~ 2048).",
        "NOTES: If the key modulus is greater than 512,",
        "       it will take a few minutes.",
        "Input the bits in the modulus[default = 2048]:",
        "Generating keys...",
        "............+++++",
        "........................++",
        "....++++",
        "...........++"
        };
    }

    static bool Confirm(ExecCtx c, string warning)
    {
        Console.WriteLine("Warning: " + warning);
        if (!c.S.Interactive || c.S.ReadLine == null) return true;
        var ans = c.S.ReadLine("");
        return ans != null && ans.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
    }

    static string[] StelnetRun(ExecCtx c)
    {
        var t = c.Tokens.Select(x => x.ToLowerInvariant()).ToList();
        if (t.Count > 1 && t[1] == "ipv6") { c.Dev.StelnetV6 = true; return new[] { "Info: Succeeded in starting the Stelnet server." }; }
        if (t.Count > 1 && (t[1] == "server" || t[1] == "ipv4")) { c.Dev.StelnetV4 = true; return new[] { "Info: Succeeded in starting the Stelnet server." }; }
        // stelnet <IP/hostname>：客户端连接（模拟器不回放真实握手）
        return new[] { "Info: The command is executed successfully." };
    }

    static string[] SnmpRun(ExecCtx c)
    {
        var d = c.Dev;
        d.SnmpAgent = true;
        var t = c.Tokens;
        if (t.Count >= 2 && new[] { "extend", "group", "inform", "local-engineid", "manner", "mib-view", "notification-log", "notify-filter-profile", "packet", "packet-priority", "set-cache", "usm-user" }
                .Any(x => Eq(t[1], x)))
            return Array.Empty<string>();
        if (t.Count >= 2 && Eq(t[1], "community"))
        {
            var com = new SnmpCommunity { Read = !Eq(c.Cap("dir"), "write"), Password = c.Cap("pw") };
            if (c.Has("aclnum") && IsInt(c.Cap("aclnum"), out var an)) com.Acl = an;
            d.Communities.RemoveAll(x => x.Read == com.Read);
            d.Communities.Add(com);
        }
        else if (t.Count >= 2 && Eq(t[1], "sys-info"))
        {
            if (t.Count >= 3 && Eq(t[2], "contact")) { d.SnmpContact = string.Join(" ", t.Skip(3)); return Array.Empty<string>(); }
            if (t.Count >= 3 && Eq(t[2], "location")) { d.SnmpLocation = string.Join(" ", t.Skip(3)); return Array.Empty<string>(); }
            d.SnmpVersion = string.Join(" ", t.Skip(3)).ToLowerInvariant();
            if (d.SnmpVersion.Contains("v1") || d.SnmpVersion.Contains("v2c"))
                return new[] { "Warning: SNMPv1/SNMPv2c is not secure, and it is recommended to use SNMPv3." };
        }
        else if (t.Count >= 2 && Eq(t[1], "target-host"))
        {
            if (t.Count >= 3 && Eq(t[2], "inform")) return Array.Empty<string>();   // inform 目标暂不进配置模型
            var tg = new SnmpTarget { Ip = c.Cap("ip"), Security = c.Cap("sec"), Version = c.Has("ver") ? c.Cap("ver") : "v2c" };
            if (c.Has("port") && IsInt(c.Cap("port"), out var p)) tg.Port = p;
            d.Targets.RemoveAll(x => x.Ip == tg.Ip);
            d.Targets.Add(tg);
        }
        else if (t.Count >= 2 && Eq(t[1], "trap"))
        {
            if (t.Count >= 3 && Eq(t[2], "disable")) { d.SnmpTrapEnable = false; return Array.Empty<string>(); }
            if (t.Count >= 3 && !Eq(t[2], "enable")) return Array.Empty<string>();   // life/queue-size/source/suppress/type：接受不报错
            if (!d.SnmpTrapEnable && !Confirm(c, "All switches of SNMP trap/notification will be open. Continue? [Y/N]:"))
                return new[] { "Info: The operation is canceled." };
            d.SnmpTrapEnable = true;
        }
        return Array.Empty<string>();
    }

    static string[] InfoCenterRun(ExecCtx c)
    {
        var t = c.Tokens;
        if (t.Count >= 2 && Eq(t[1], "loghost"))
        {
            if (t.Count >= 3 && Eq(t[2], "ipv6")) return Array.Empty<string>();   // IPv6 日志主机：接受不报错
            var ip = c.Cap("ip");
            if (!IsIp(ip)) return Err("Wrong parameter found at '^' position.");
            if (!c.Dev.LogHosts.Contains(ip)) c.Dev.LogHosts.Add(ip);
            return new[] { "Warning: There is security risk as this operation enables a non secure syslog protocol." };
        }
        if (t.Count == 1 || (t.Count >= 2 && Eq(t[1], "enable"))) c.Dev.InfoCenterEnabled = true;
        // 其余 info-center 子命令（channel/console/filter-id/local/logbuffer/monitor/rate-limit/snmp/source/...）：接受不报错
        return Array.Empty<string>();
    }

    static string[] ClockRun(ExecCtx c)
    {
        var t = c.Tokens;
        if (t.Count >= 2 && Eq(t[1], "timezone"))
        {
            var parts = c.Cap("off").Split(':');
            if (parts.Length != 3 || !IsInt(parts[0], out var h) || !IsInt(parts[1], out var mi)) return Err("Wrong parameter found at '^' position.");
            c.Dev.Clock.Tz = c.Cap("tz");
            var sign = t.Contains("minus") ? -1 : 1;
            c.Dev.Clock.TzOffsetSec = sign * (h * 3600 + mi * 60);
        }
        else if (t.Count >= 2 && Eq(t[1], "datetime"))
        {
            var tp = c.Cap("time").Split(':');
            var dp = c.Cap("date").Split('-');
            if (tp.Length != 3 || dp.Length != 3) return Err("Wrong parameter found at '^' position.");
            var cl = c.Dev.Clock;
            cl.Hour = int.Parse(tp[0]); cl.Minute = int.Parse(tp[1]); cl.Second = int.Parse(tp[2]);
            cl.Year = int.Parse(dp[0]); cl.Month = int.Parse(dp[1]); cl.Day = int.Parse(dp[2]);
        }
        return Array.Empty<string>();
    }

    static string[] EnterUi(ExecCtx c)
    {
        var t = c.Tokens;
        if (Eq(t[1], "maximum-vty")) return Array.Empty<string>();           // 最大 VTY 数：接受不报错
        string name;
        if (Eq(t[1], "current")) name = "con 0";                             // 当前会话就是 con 0
        else if (Eq(t[1], "console")) name = "con " + c.Cap("n");
        else if (c.Has("one")) name = "vty " + c.Cap("one");
        else name = c.Has("b") ? "vty " + c.Cap("a") + " " + c.Cap("b") : "vty " + c.Cap("a");
        if (name.StartsWith("vty") && name.Split(' ').Length == 2 && !c.Has("b"))
            name = "vty " + c.Cap("a") + " " + c.Cap("a");
        if (!c.Dev.Uis.ContainsKey(name)) c.Dev.Uis[name] = new UiCfg { Name = name };
        c.S.UiName = name;
        c.S.View = "ui";
        return Array.Empty<string>();
    }

    static Command BuildUndoSystem()
    {
        var u = new Command("undo", "Negate a command or set its defaults") { Run = SystemUndo };
        u.T(new Lit("telnet"), new Lit("server"), new Lit("enable"));
        u.T(new Lit("ftp"), new Lit("server"));
        u.T(new Lit("http"), new Lit("server"), new Lit("enable"));
        u.T(new Lit("http"), new Lit("secure-server"), new Lit("enable"));
        u.T(new Lit("stelnet"), new Lit("server"), new Lit("enable"));
        u.T(new Lit("stelnet"), new Lit("ipv4"), new Lit("server"), new Lit("enable"));
        u.T(new Lit("stelnet"), new Lit("ipv6"), new Lit("server"), new Lit("enable"));
        u.T(new Lit("snmp-agent"));
        u.T(new Lit("snmp-agent"), new Lit("trap"), new Lit("enable"));
        u.T(new Lit("acl"), new Lit("number"), new Val("num", "<2000-3999>", AclSug));
        u.T(new Lit("sysname"));
        u.T(new Lit("info-center"), new Lit("enable"));
        u.T(new Lit("info-center"), new Lit("loghost"), ValIp);
        u.T(new Lit("clock"), new Lit("timezone"));
        u.T(new Lit("snmp-agent"), new Lit("community"), new Opt(new Val("dir", "<read|write>", (d, p) => SugOf(new[] { "read", "write" }, p))));
        u.T(new Lit("snmp-agent"), new Lit("sys-info"), new Lit("version"));
        u.T(new Lit("snmp-agent"), new Lit("target-host"), new Lit("trap"), new Lit("address"), new Lit("udp-domain"), ValIp);
        u.T(new Lit("vlan"), new Val("id", "INTEGER<1-4094>", VlanSug, s => IsInt(s, out _)));
        u.T(new Lit("vlan"), new Lit("batch"), new Rep(new Val("id", "INTEGER<1-4094>")));
        return u;
    }

    static string[] SystemUndo(ExecCtx c)
    {
        var d = c.Dev;
        var t = c.Tokens.Select(x => x.ToLowerInvariant()).ToList();
        var k = string.Join(" ", t.Skip(1));
        switch (k)
        {
            case "telnet server enable": d.TelnetServer = false; return new[] { "Info: The Telnet server has been disabled." };
            case "ftp server": d.FtpServer = false; return new[] { "Info: The FTP server is already disabled." };
            case "http server enable":
                if (!Confirm(c, "The operation will stop HTTP service. Continue? [Y/N]:")) return new[] { "Info: The operation is canceled." };
                d.HttpServer = false;
                return Array.Empty<string>();
            case "http secure-server enable": d.HttpsServer = false; return Array.Empty<string>();
            case "stelnet server enable":
            case "stelnet ipv4 server enable": d.StelnetV4 = false; return Array.Empty<string>();
            case "stelnet ipv6 server enable": d.StelnetV6 = false; return Array.Empty<string>();
            case "snmp-agent": d.SnmpAgent = false; d.Communities.Clear(); d.Targets.Clear(); d.SnmpTrapEnable = false; return Array.Empty<string>();
            case "snmp-agent trap enable": d.SnmpTrapEnable = false; return Array.Empty<string>();
            case "sysname": d.SysName = "HUAWEI"; return Array.Empty<string>();
            case "clock timezone": d.Clock.Tz = null; d.Clock.TzOffsetSec = 0; return Array.Empty<string>();
            case "info-center enable": d.InfoCenterEnabled = false; return Array.Empty<string>();
            case "snmp-agent community": d.Communities.Clear(); return Array.Empty<string>();
            case "snmp-agent community read": d.Communities.RemoveAll(x => x.Read); return Array.Empty<string>();
            case "snmp-agent community write": d.Communities.RemoveAll(x => !x.Read); return Array.Empty<string>();
            case "snmp-agent sys-info version": d.SnmpVersion = "v3"; return Array.Empty<string>();
        }
        if (IsInt(c.Cap("num"), out var an) && t.Contains("acl")) { d.Acls.Remove(an); return Array.Empty<string>(); }
        if (t.Contains("loghost")) { d.LogHosts.Remove(c.Cap("ip")); return Array.Empty<string>(); }
        if (t.Count > 2 && Eq(t[1], "snmp-agent") && Eq(t[2], "target-host"))
        {
            d.Targets.RemoveAll(x => x.Ip == c.Cap("ip"));
            return Array.Empty<string>();
        }
        if (t.Count == 3 && Eq(t[1], "vlan") && IsInt(t[2], out var vidSingle))
        {
            if (vidSingle == 1) return Err("The default VLAN cannot be deleted.");
            if (!d.Vlans.Contains(vidSingle)) return Err("The VLAN does not exist.");
            RemoveVlan(d, vidSingle);
            return Array.Empty<string>();
        }
        if (t.Count > 2 && Eq(t[1], "vlan") && Eq(t[2], "batch"))
        {
            foreach (var x in t.Skip(3)) if (IsInt(x, out var v) && v != 1) RemoveVlan(d, v);
            return Array.Empty<string>();
        }
        return Err("Unrecognized command found at '^' position.");
    }

    static void RemoveVlan(Device d, int id)
    {
        d.Vlans.Remove(id);
        d.VlanCfgs.Remove(id);
        d.Ifaces.Remove($"Vlanif{id}");
    }

    // ---------------- AAA view ----------------
    static readonly Command UndoAaaCmd = BuildUndoAaa();

    static readonly List<Command> AaaView = new()
    {
        C("local-user", "Configure local user", LocalUserRun)
            .T(ValUser, new Lit("access-limit", "Set access limit of user(s)"), new Rep(AnyVal, 3))
            .T(ValUser, new Lit("ftp-directory", "Set user(s) FTP directory permitted"), new Rep(AnyVal, 3))
            .T(ValUser, new Lit("idle-timeout", "Set the idle timeout"), new Val("min", "INTEGER<0-35791>", null, s => IsInt(s, out _)), new Opt(new Val("sec", "INTEGER<0-59>", null, s => IsInt(s, out _))))
            .T(ValUser, new Lit("password", "Set password"), new Val("mode", "{cipher|irreversible-cipher|simple}", SugCipherMode,
                    t => new[] { "cipher", "irreversible-cipher", "simple" }.Any(x => x.Equals(t, StringComparison.OrdinalIgnoreCase))), new Val("pw", "STRING<8-64>"))
            .T(ValUser, new Lit("password", "Set password"), new Val("mode", "{cipher|irreversible-cipher|simple}", SugCipherMode,
                    t => new[] { "cipher", "irreversible-cipher", "simple" }.Any(x => x.Equals(t, StringComparison.OrdinalIgnoreCase))), new Val("pw", "STRING<8-64>"),
                new Lit("privilege"), new Lit("level", "Set the privilege level"), new Val("n", "INTEGER<0-15>", null, s => IsInt(s, out _)))
            .T(ValUser, new Lit("privilege"), new Lit("level", "Set the privilege level"), new Val("n", "INTEGER<0-15>", null, s => IsInt(s, out _)))
            .T(ValUser, new Lit("service-type", "Set the service type"), new Rep(new Val("svc", "{8021x|bind|ftp|http|ppp|ssh|telnet|terminal|web|x25-pad}", SugServices)))
            .T(ValUser, new Lit("state", "Activate/Block the user(s)"), new Rep(AnyVal, 3)),
        C("local-aaa-user", "AAA user policy", LocalAaaRun)
            .T(new Lit("wrong-password", "Wrong password policy"), new Lit("retry-interval", "Retry interval"), new Val("a", "INTEGER<1-60>", null, s => IsInt(s, out _)),
                new Lit("retry-time", "Maximum retry times"), new Val("b", "INTEGER<1-10>", null, s => IsInt(s, out _)),
                new Lit("block-time", "Block time"), new Val("c", "INTEGER<1-120>", null, s => IsInt(s, out _)))
            .T(new Lit("password", "Password policy"), new Lit("policy", "Password policy"), new Lit("administrator", "Administrator password policy")),
        C("user-password", "Password policy", c => { c.Dev.Aaa.ComplexityCheck = true; return Array.Empty<string>(); })
            .T(new Lit("complexity-check")),
        UndoAaaCmd,
        Nav("quit", "Exit current view", QuitRun),
        Nav("return", "Return to user view", ReturnRun),
        DisplayCmd, SaveCmd, CheckCmd
    };

    static Command BuildUndoAaa()
    {
        var u = new Command("undo", "Negate a command or set its defaults") { Run = AaaUndo };
        u.T(new Lit("local-user"), ValUser);
        u.T(new Lit("user-password"), new Lit("complexity-check"));
        u.T(new Lit("local-aaa-user"), new Lit("wrong-password"));
        u.T(new Lit("local-aaa-user"), new Lit("password"), new Lit("policy"), new Lit("administrator"));
        return u;
    }

    static string[] LocalUserRun(ExecCtx c)
    {
        var d = c.Dev;
        var t = c.Tokens;
        var name = c.Cap("name");
        if (string.IsNullOrEmpty(name)) return Err("Incomplete command found at '^' position.");
        if (t.Count < 3) return Err("Incomplete command found at '^' position.");
        if (Eq(t[2], "password"))
        {
            if (!d.Users.TryGetValue(name, out var u))
            {
                u = new LocalUser { Name = name, Privilege = 0 };
                d.Users[name] = u;
            }
            u.Password = c.Cap("pw");
            u.PasswordInitial = true;
            if (c.Has("n") && IsInt(c.Cap("n"), out var lv0)) u.Privilege = lv0;
            return Array.Empty<string>();
        }
        if (Eq(t[2], "access-limit") || Eq(t[2], "ftp-directory") || Eq(t[2], "state")) return Array.Empty<string>();
        if (!d.Users.TryGetValue(name, out var user)) return Err("The user does not exist.");
        if (Eq(t[2], "privilege"))
        {
            if (IsInt(c.Cap("n"), out var lv)) user.Privilege = lv;
            return Array.Empty<string>();
        }
        if (Eq(t[2], "service-type"))
        {
            user.ServiceTypes.Clear();
            foreach (var s in t.Skip(3)) user.ServiceTypes.Add(s);
            return Array.Empty<string>();
        }
        return Array.Empty<string>();
    }

    static string[] LocalAaaRun(ExecCtx c)
    {
        var d = c.Dev;
        var t = c.Tokens;
        if (t.Count >= 3 && Eq(t[1], "wrong-password"))
        {
            if (!IsInt(c.Cap("a"), out var a) || !IsInt(c.Cap("b"), out var b) || !IsInt(c.Cap("c"), out var cc))
                return Err("Wrong parameter found at '^' position.");
            d.Aaa.WrongPassword = (a, b, cc);
            return Array.Empty<string>();
        }
        if (t.Count >= 4 && Eq(t[1], "password") && Eq(t[3], "administrator"))
        {
            d.Aaa.PasswordPolicyAdmin = true;
            c.S.View = "lupp";
            return Array.Empty<string>();
        }
        return Array.Empty<string>();
    }

    static string[] AaaUndo(ExecCtx c)
    {
        var d = c.Dev;
        var t = c.Tokens.Select(x => x.ToLowerInvariant()).ToList();
        if (Eq(t[1], "local-user"))
        {
            var name = c.Cap("name");
            d.Users.Remove(name);
            if (Eq(name, "admin")) d.AdminDeleted = true;
            return Array.Empty<string>();
        }
        if (Eq(t[1], "user-password")) { d.Aaa.ComplexityCheck = false; return Array.Empty<string>(); }
        if (Eq(t[1], "local-aaa-user") && t.Count >= 3 && Eq(t[2], "wrong-password")) { d.Aaa.WrongPassword = null; return Array.Empty<string>(); }
        if (Eq(t[1], "local-aaa-user") && t.Count >= 3 && Eq(t[2], "password")) { d.Aaa.PasswordPolicyAdmin = false; return Array.Empty<string>(); }
        return Err("Unrecognized command found at '^' position.");
    }

    // ---------------- ACL view ----------------
    static readonly Command UndoAclCmd = BuildUndoAcl();

    static readonly List<Command> AclView = new()
    {
        C("rule", "Configure ACL rule", RuleRun)
            .T(ValAclAction, new Lit("ip", "Any IP protocol"))
            .T(ValAclAction, new Lit("source", "Specify source address"), ValIp, ValWildcard)
            .T(ValAclAction, ValProto, new Lit("destination-port", "Specify destination port"), new Lit("eq", "Equal to given port number"), ValPort)
            .T(ValAclAction, ValProto, new Lit("destination-port", "Specify destination port"), new Lit("gt", "Greater than given port number"), ValPort)
            .T(ValAclAction, ValProto, new Lit("destination-port", "Specify destination port"), new Lit("lt", "Less than given port number"), ValPort)
            .T(ValAclAction, ValProto, new Lit("destination-port", "Specify destination port"), new Lit("range", "Between two port numbers"),
                new Val("p1", "INTEGER<0-65535>", PortSug, s => IsInt(s, out _)), new Val("p2", "INTEGER<0-65535>", PortSug, s => IsInt(s, out _)))
            .T(ValAclAction)
            .T(ValAclId, ValAclAction)
            .T(ValAclId, ValAclAction, new Lit("ip", "Any IP protocol"))
            .T(ValAclId, ValAclAction, new Lit("source", "Specify source address"), ValIp, ValWildcard)
            .T(ValAclId, ValAclAction, ValProto, new Lit("destination-port", "Specify destination port"), new Lit("eq", "Equal to given port number"), ValPort)
            .T(ValAclId, ValAclAction, ValProto, new Lit("destination-port", "Specify destination port"), new Lit("gt", "Greater than given port number"), ValPort)
            .T(ValAclId, ValAclAction, ValProto, new Lit("destination-port", "Specify destination port"), new Lit("lt", "Less than given port number"), ValPort)
            .T(ValAclId, ValAclAction, ValProto, new Lit("destination-port", "Specify destination port"), new Lit("range", "Between two port numbers"),
                new Val("p1", "INTEGER<0-65535>", PortSug, s => IsInt(s, out _)), new Val("p2", "INTEGER<0-65535>", PortSug, s => IsInt(s, out _))),
        C("description", "ACL description", c =>
        {
            if (c.Dev.Acls.TryGetValue(c.S.AclNumber, out var a)) a.Description = string.Join(" ", c.Tokens.Skip(1));
            return Array.Empty<string>();
        }).T(new Rep(new Val("w", "TEXT") { HintHelp = "ACL description (no more than 127 characters)" })),
        UndoAclCmd,
        Nav("quit", "Exit current view", QuitRun),
        Nav("return", "Return to user view", ReturnRun),
        DisplayCmd, SaveCmd, CheckCmd
    };

    static Command BuildUndoAcl()
    {
        var u = new Command("undo", "Negate a command or set its defaults") { Run = AclUndo };
        u.T(new Lit("rule"), new Val("id", "INTEGER<1-65535>", null, s => IsInt(s, out _)));
        u.T(new Lit("description"));
        return u;
    }

    static string[] RuleRun(ExecCtx c)
    {
        var d = c.Dev;
        if (!d.Acls.TryGetValue(c.S.AclNumber, out var acl)) return Err("ACL not found.");
        var t = c.Tokens.Select(x => x.ToLowerInvariant()).ToList();
        if (c.S.AclNumber < 3000 && (t.Contains("ip") || t.Contains("tcp") || t.Contains("udp") || t.Contains("destination-port")))
            return Err("Wrong parameter found at '^' position.");
        if (t.Contains("description")) return Array.Empty<string>();   // rule <id> description <text>：接受不落配置
        long id;
        int actionIdx;
        if (c.Has("id"))
        {
            if (!long.TryParse(c.Cap("id"), out id) || id < 0 || id > 4294967294) return Err("Wrong parameter found at '^' position.");
            actionIdx = 2;
        }
        else
        {
            var maxId = acl.Rules.Count == 0 ? 0L : acl.Rules.Max(x => x.Id);
            id = maxId + 5;
            if (id > 4294967294) return Err("The rule ID reaches the upper limit.");
            actionIdx = 1;
        }
        var action = c.Cap("action").ToLowerInvariant();
        if (action != "permit" && action != "deny") return Err("Wrong parameter found at '^' position.");
        var r = new AclRule
        {
            Id = (int)id,
            Action = action,
            Raw = "rule " + id + " " + action + (c.Tokens.Count > actionIdx + 1 ? " " + string.Join(" ", c.Tokens.Skip(actionIdx + 1)) : "")
        };
        var dp = t.IndexOf("destination-port");
        if (dp >= 2)
        {
            r.Proto = t[dp - 1];
            if (dp + 2 < t.Count && Eq(t[dp + 1], "eq"))
            {
                var pn = PortNum(t[dp + 2]);
                if (pn < 0) return Err("Wrong parameter found at '^' position.");
                r.PortMin = r.PortMax = pn;
            }
            else if (dp + 3 < t.Count && Eq(t[dp + 1], "range"))
            {
                var p1 = PortNum(t[dp + 2]);
                var p2 = PortNum(t[dp + 3]);
                if (p1 < 0 || p2 < 0) return Err("Wrong parameter found at '^' position.");
                r.PortMin = Math.Min(p1, p2);
                r.PortMax = Math.Max(p1, p2);
            }
            else if (dp + 2 < t.Count && Eq(t[dp + 1], "gt"))
            {
                var pn = PortNum(t[dp + 2]);
                if (pn < 0) return Err("Wrong parameter found at '^' position.");
                r.PortMin = pn;
                r.PortMax = 65535;
            }
            else if (dp + 2 < t.Count && Eq(t[dp + 1], "lt"))
            {
                var pn = PortNum(t[dp + 2]);
                if (pn < 0) return Err("Wrong parameter found at '^' position.");
                r.PortMin = 0;
                r.PortMax = pn;
            }
            else return Err("Incomplete command found at '^' position.");
        }
        else
        {
            var si = t.IndexOf("source");
            if (si >= 2)
            {
                if (si + 1 < t.Count && Eq(t[si + 1], "any")) { r.Proto = "ip"; }
                else
                {
                    r.Source = c.Cap("ip");
                    if (IsInt(c.Cap("wc"), out var wc)) r.SourceWildcard = wc;
                    r.Proto = "ip";
                }
            }
            else r.Proto = "ip";
        }
        acl.Rules.RemoveAll(x => x.Id == (int)id);
        acl.Rules.Add(r);
        return Array.Empty<string>();
    }

    static string[] AclUndo(ExecCtx c)
    {
        var d = c.Dev;
        var t = c.Tokens.Select(x => x.ToLowerInvariant()).ToList();
        if (!d.Acls.TryGetValue(c.S.AclNumber, out var acl)) return Err("ACL not found.");
        if (Eq(t[1], "rule") && long.TryParse(c.Cap("id"), out var id)) { acl.Rules.RemoveAll(x => x.Id == (int)Math.Min(id, int.MaxValue)); return Array.Empty<string>(); }
        if (Eq(t[1], "description")) { acl.Description = null; return Array.Empty<string>(); }
        return Err("Unrecognized command found at '^' position.");
    }

    // ---------------- interface view ----------------
    static readonly Command UndoIfaceCmd = BuildUndoIface();

    static readonly List<Command> IfaceView = new()
    {
        C("description", "Interface description", c =>
        {
            var text = string.Join(" ", c.Tokens.Skip(1));
            foreach (var i in Targets(c)) i.Description = text;
            return Array.Empty<string>();
        }).T(new Rep(new Val("w", "TEXT") { HintHelp = "Interface description (no more than 242 characters)" })),
        C("port", "Port configuration", PortRun)
            .T(new Lit("link-type", "Set the link type"), new Val("lt", "{access|dot1q-tunnel|hybrid|trunk}", (d, p) => SugOf(new[] { "access", "dot1q-tunnel", "hybrid", "trunk" }, p)))
            .T(new Lit("add-tag", "Add outer tag based on acl"), new Rep(AnyVal, 4))
            .T(new Lit("discard", "Discard"), new Rep(AnyVal, 2))
            .T(new Lit("hybrid", "Hybrid port"), new Rep(AnyVal, 4))
            .T(new Lit("link-flap", "Link flap"), new Rep(AnyVal, 2))
            .T(new Lit("mux-vlan", "Multiplex vlan"), new Rep(AnyVal, 2))
            .T(new Lit("priority", "Specify current port's priority"), new Rep(AnyVal, 2))
            .T(new Lit("type", "Type"), new Rep(AnyVal, 3))
            .T(new Lit("vlan-mapping", "VLAN Mapping"), new Rep(AnyVal, 4))
            .T(new Lit("vlan-stacking", "VLAN Stacking"), new Rep(AnyVal, 4))
            .T(new Lit("default", "Set the default VLAN"), new Lit("vlan"), new Val("vid", "INTEGER<1-4094>", VlanSug, s => IsInt(s, out _)))
            .T(new Lit("trunk", "Trunk configuration"), new Lit("allow-pass", "Allow the VLAN to pass"), new Lit("vlan"),
                new Rep(new Val("vid", "INTEGER<1-4094>", VlanSug, s => IsInt(s, out _)))),
        C("traffic-filter", "Apply ACL on interface", TfRun)
            .T(new Val("dir", "{inbound|outbound}", (d, p) => SugOf(new[] { "inbound", "outbound" }, p)), new Lit("acl", "Specify an ACL"),
                new Val("num", "INTEGER<2000-3999>", AclSug, s => IsInt(s, out _))),
        C("shutdown", "Shut down interface", c => { foreach (var i in Targets(c)) i.Shutdown = true; return Array.Empty<string>(); }).T(),
        C("ip", "IP configuration", IpRun)
            .T(new Lit("address"), ValIp, new Val("mask", "<X.X.X.X>"))
            .T(new Lit("source", "Source"), new Rep(AnyVal, 3)),
        UndoIfaceCmd,
        Nav("quit", "Exit current view", QuitRun),
        Nav("return", "Return to user view", ReturnRun),
        DisplayCmd, SaveCmd, CheckCmd
    };

    static Iface SetIface(ExecCtx c) => c.Dev.Ifaces[c.S.IfName];

    static Command BuildUndoIface()
    {
        var u = new Command("undo", "Negate a command or set its defaults") { Run = IfaceUndo };
        u.T(new Lit("shutdown"));
        u.T(new Lit("port"), new Lit("default"), new Lit("vlan"));
        u.T(new Lit("port"), new Lit("trunk"), new Lit("allow-pass"), new Lit("vlan"), new Val("vid", "INTEGER<1-4094>", VlanSug, s => IsInt(s, out _)));
        u.T(new Lit("port"), new Lit("link-type"));
        u.T(new Lit("traffic-filter"), new Val("dir", "{inbound|outbound}", (d, p) => SugOf(new[] { "inbound", "outbound" }, p)), new Lit("acl"),
            new Val("num", "INTEGER<2000-3999>", AclSug, s => IsInt(s, out _)));
        u.T(new Lit("traffic-filter"), new Val("dir", "{inbound|outbound}", (d, p) => SugOf(new[] { "inbound", "outbound" }, p)));
        u.T(new Lit("description"));
        u.T(new Lit("ip"), new Lit("address"));
        return u;
    }

    static string[] PortRun(ExecCtx c)
    {
        var t = c.Tokens.Select(x => x.ToLowerInvariant()).ToList();
        if (t.Count >= 2 && new[] { "add-tag", "discard", "hybrid", "link-flap", "mux-vlan", "priority", "type", "vlan-mapping", "vlan-stacking" }
                .Any(x => Eq(t[1], x)))
            return Array.Empty<string>();
        if (t.Count > 2 && Eq(t[1], "link-type"))
        {
            foreach (var i in Targets(c)) i.LinkType = t[2];
            return Array.Empty<string>();
        }
        if (t.Count > 3 && Eq(t[1], "default") && Eq(t[2], "vlan"))
        {
            if (!IsInt(c.Cap("vid"), out var v)) return Err("Wrong parameter found at '^' position.");
            if (!c.Dev.Vlans.Contains(v)) return Err("The VLAN does not exist.");
            foreach (var i in Targets(c)) i.AccessVlan = v;
            return Array.Empty<string>();
        }
        if (t.Count > 3 && Eq(t[1], "trunk"))
        {
            var ids = c.Tokens.Skip(4).Select(x => IsInt(x, out var v) ? v : -1).Where(v => v > 0).ToList();
            foreach (var v in ids) if (!c.Dev.Vlans.Contains(v)) return Err("The VLAN does not exist.");
            foreach (var i in Targets(c)) i.TrunkVlans = ids;
            return Array.Empty<string>();
        }
        return Err("Incomplete command found at '^' position.");
    }

    static string[] TfRun(ExecCtx c)
    {
        var dir = c.Cap("dir").ToLowerInvariant();
        if (!IsInt(c.Cap("num"), out var n)) return Err("Wrong parameter found at '^' position.");
        if (!c.Dev.Acls.ContainsKey(n)) return Err("The ACL does not exist.");
        foreach (var target in Targets(c))
        {
            if (dir == "inbound") target.TfIn = n; else target.TfOut = n;
        }
        return Array.Empty<string>();
    }

    static string[] IpRun(ExecCtx c)
    {
        if (c.Tokens.Count > 1 && Eq(c.Tokens[1], "source")) return Array.Empty<string>();
        if (c.S.View == "range" || c.S.View == "pg") return Err("Unsupported operation on an interface range.");
        var i = SetIface(c);
        if (!i.IsVlanif) return Err("Unsupported operation on a physical port.");
        if (!IsIp(c.Cap("ip")) || !IsIp(c.Cap("mask"))) return Err("Wrong parameter found at '^' position.");
        i.Ip = c.Cap("ip");
        i.Mask = c.Cap("mask");
        return Array.Empty<string>();
    }

    static string[] IfaceUndo(ExecCtx c)
    {
        var t = c.Tokens.Select(x => x.ToLowerInvariant()).ToList();
        var k = string.Join(" ", t.Skip(1));
        if (k.StartsWith("ip address"))
        {
            if (c.S.View == "range" || c.S.View == "pg") return Err("Unsupported operation on an interface range.");
            var iface = SetIface(c);
            if (!iface.IsVlanif) return Err("Unsupported operation on a physical port.");
            iface.Ip = null;
            iface.Mask = null;
            return Array.Empty<string>();
        }
        var handled = false;
        foreach (var i in Targets(c))
        {
            if (k == "shutdown") { i.Shutdown = false; handled = true; continue; }
            if (k == "description") { i.Description = null; handled = true; continue; }
            if (k == "port default vlan") { i.AccessVlan = null; handled = true; continue; }
            if (k == "port link-type") { i.LinkType = null; handled = true; continue; }
            if (k.StartsWith("port trunk allow-pass vlan")) { i.TrunkVlans.Clear(); handled = true; continue; }
            if (Eq(t[1], "traffic-filter"))
            {
                if (Eq(c.Cap("dir"), "inbound")) i.TfIn = null; else i.TfOut = null;
                handled = true;
                continue;
            }
            return Err("Unrecognized command found at '^' position.");
        }
        if (!handled) return Err("Unrecognized command found at '^' position.");
        return Array.Empty<string>();
    }

    // ---------------- port-group 视图（真机批量端口配置） ----------------
    static readonly List<Command> PgView = BuildPgView();

    static List<Command> BuildPgView()
    {
        var L = new List<Command>
        {
            C("group-member", "Add member interface(s) to port group", GroupMemberRun)
                .T(new Val("if", "<interface>", IfSug))
                .T(new Val("if", "<interface>", IfSug), new Lit("to", "End of interface range"), new Val("if2", "<interface>", IfSug)),
            BuildUndoPg(),
        };
        foreach (var c in IfaceView)
            if (c.Kw != "undo") L.Add(c);
        return L;
    }

    static Command BuildUndoPg()
    {
        var u = new Command("undo", "Negate a command or set its defaults") { Run = PgUndo };
        u.T(new Lit("group-member"));
        u.T(new Lit("group-member"), new Val("if", "<interface>", IfSug));
        u.T(new Lit("shutdown"));
        u.T(new Lit("port"), new Lit("default"), new Lit("vlan"));
        u.T(new Lit("port"), new Lit("trunk"), new Lit("allow-pass"), new Lit("vlan"), new Val("vid", "INTEGER<1-4094>", VlanSug, s => IsInt(s, out _)));
        u.T(new Lit("port"), new Lit("link-type"));
        u.T(new Lit("traffic-filter"), new Val("dir", "{inbound|outbound}", (d, p) => SugOf(new[] { "inbound", "outbound" }, p)), new Lit("acl"),
            new Val("num", "INTEGER<2000-3999>", AclSug, s => IsInt(s, out _)));
        u.T(new Lit("traffic-filter"), new Val("dir", "{inbound|outbound}", (d, p) => SugOf(new[] { "inbound", "outbound" }, p)));
        u.T(new Lit("description"));
        u.T(new Lit("ip"), new Lit("address"));
        return u;
    }

    static string[] EnterPortGroup(ExecCtx c)
    {
        var name = c.Cap("pg");
        if (string.IsNullOrEmpty(name) || name.Length > 32) return Err("Wrong parameter found at '^' position.");
        if (!c.Dev.PortGroups.ContainsKey(name)) c.Dev.PortGroups[name] = new List<string>();
        c.S.PgName = name;
        c.S.View = "pg";
        return Array.Empty<string>();
    }

    static string[] GroupMemberRun(ExecCtx c)
    {
        var d = c.Dev;
        if (!d.PortGroups.TryGetValue(c.S.PgName, out var members))
        {
            members = new List<string>();
            d.PortGroups[c.S.PgName] = members;
        }
        var name = CanonIfaceName(c.Cap("if"));
        if (!d.Ifaces.ContainsKey(name)) return Err("Wrong parameter found at '^' position.");
        if (c.Has("if2"))
        {
            var name2 = CanonIfaceName(c.Cap("if2"));
            if (!d.Ifaces.ContainsKey(name2)) return Err("Wrong parameter found at '^' position.");
            var (p1, n1) = Device.SplitIfaceName(name);
            var (p2, n2) = Device.SplitIfaceName(name2);
            if (!p1.Equals(p2, StringComparison.OrdinalIgnoreCase) || n1 < 0 || n2 < 0 || n1 > n2 || n2 - n1 + 1 > 64)
                return Err("Wrong parameter found at '^' position.");
            members.Clear();
            for (var n = n1; n <= n2; n++) members.Add(p1 + n);
        }
        else if (!members.Contains(name))
        {
            members.Add(name);
        }
        return Array.Empty<string>();
    }

    static string[] PgUndo(ExecCtx c)
    {
        var t = c.Tokens.Select(x => x.ToLowerInvariant()).ToList();
        if (t.Count > 1 && Eq(t[1], "group-member"))
        {
            if (!c.Dev.PortGroups.TryGetValue(c.S.PgName, out var members)) return Array.Empty<string>();
            if (c.Has("if")) members.Remove(CanonIfaceName(c.Cap("if")));
            else members.Clear();
            return Array.Empty<string>();
        }
        return IfaceUndo(c);
    }

    // ---------------- VLAN 视图（vlan <id> 进入） ----------------
    static readonly Command UndoVlanViewCmd = BuildUndoVlanView();

    static readonly List<Command> VlanView = new()
    {
        C("description", "VLAN description", c => { GetVlanCfg(c).Description = JoinText(c); return Array.Empty<string>(); })
            .T(new Rep(new Val("w", "TEXT") { HintHelp = "VLAN description(no more than 80 characters)" })),
        C("name", "VLAN name", c => { GetVlanCfg(c).Name = string.Join(" ", c.Tokens.Skip(1)); return Array.Empty<string>(); })
            .T(new Val("nm", "STRING<1-32>")),
        UndoVlanViewCmd,
        Nav("quit", "Exit current view", QuitRun),
        Nav("return", "Return to user view", ReturnRun),
        DisplayCmd, SaveCmd, CheckCmd
    };

    static Command BuildUndoVlanView()
    {
        var u = new Command("undo", "Negate a command or set its defaults") { Run = VlanViewUndo };
        u.T(new Lit("description"));
        u.T(new Lit("name"));
        return u;
    }

    static VlanCfg GetVlanCfg(ExecCtx c)
    {
        if (!c.Dev.VlanCfgs.TryGetValue(c.S.VlanId, out var cfg))
        {
            cfg = new VlanCfg { Id = c.S.VlanId };
            c.Dev.VlanCfgs[c.S.VlanId] = cfg;
        }
        return cfg;
    }

    static string JoinText(ExecCtx c) => string.Join(" ", c.Tokens.Skip(1));

    static string[] VlanViewUndo(ExecCtx c)
    {
        var t = c.Tokens.Select(x => x.ToLowerInvariant()).ToList();
        if (!c.Dev.VlanCfgs.TryGetValue(c.S.VlanId, out var cfg)) return Array.Empty<string>();
        if (t.Count > 1 && Eq(t[1], "description")) { cfg.Description = null; return Array.Empty<string>(); }
        if (t.Count > 1 && Eq(t[1], "name")) { cfg.Name = null; return Array.Empty<string>(); }
        return Err("Unrecognized command found at '^' position.");
    }


    // ---------------- 本地管理员密码策略视图 ----------------
    static readonly Command UndoLuppCmd = BuildUndoLupp();

    static readonly List<Command> LuppView = new()
    {
        C("password", "Local administrator password policy", LuppRun)
            .T(new Lit("expire", "Password expiration time"), new Val("days", "INTEGER<0-999>", null, s => IsInt(s, out _)))
            .T(new Lit("history", "Password history policy"), new Lit("record"), new Lit("number"), new Val("num", "INTEGER<0-5>", null, s => IsInt(s, out _))),
        UndoLuppCmd,
        Nav("quit", "Exit current view", QuitRun),
        Nav("return", "Return to user view", ReturnRun),
        DisplayCmd, SaveCmd, CheckCmd
    };

    static Command BuildUndoLupp()
    {
        var u = new Command("undo", "Negate a command or set its defaults") { Run = LuppUndo };
        u.T(new Lit("password"), new Lit("expire"));
        u.T(new Lit("password"), new Lit("history"), new Lit("record"), new Lit("number"));
        return u;
    }

    static string[] LuppRun(ExecCtx c)
    {
        var d = c.Dev;
        var t = c.Tokens;
        if (t.Count >= 3 && Eq(t[1], "expire"))
        {
            if (!IsInt(c.Cap("days"), out var n) || n < 0 || n > 999) return Err("Wrong parameter found at '^' position.");
            d.Aaa.PasswordExpire = n;
            return Array.Empty<string>();
        }
        if (t.Count >= 5 && Eq(t[1], "history"))
        {
            if (!IsInt(c.Cap("num"), out var n) || n < 0 || n > 5) return Err("Wrong parameter found at '^' position.");
            d.Aaa.PasswordHistory = n;
            return Array.Empty<string>();
        }
        return Err("Incomplete command found at '^' position.");
    }

    static string[] LuppUndo(ExecCtx c)
    {
        var t = c.Tokens.Select(x => x.ToLowerInvariant()).ToList();
        if (t.Count >= 3 && Eq(t[1], "expire")) { c.Dev.Aaa.PasswordExpire = 90; return Array.Empty<string>(); }
        if (t.Count >= 3 && Eq(t[1], "history")) { c.Dev.Aaa.PasswordHistory = 5; return Array.Empty<string>(); }
        return Err("Unrecognized command found at '^' position.");
    }

    static List<string> AaaPolicyConfigLines(Device d)
    {
        var L = new List<string>();
        if (d.Aaa.PasswordExpire != 90) L.Add($" password expire {d.Aaa.PasswordExpire}");
        if (d.Aaa.PasswordHistory != 5) L.Add($" password history record number {d.Aaa.PasswordHistory}");
        return L;
    }

    static List<string> AaaPolicyLines(Device d) => new()
    {
        "  Password policy  : " + (d.Aaa.PasswordPolicyAdmin ? "Enable" : "Disable"),
        "  Password expire  : " + d.Aaa.PasswordExpire + (d.Aaa.PasswordExpire == 0 ? " (never expire)" : " days"),
        "  Password history : " + d.Aaa.PasswordHistory + " record(s)"
    };

    // ---------------- user-interface view ----------------
    static readonly Command UndoUiCmd = BuildUndoUi();

    static readonly List<Command> UiView = new()
    {
        C("authentication-mode", "Set authentication mode", c =>
        {
            if (c.Dev.Uis.TryGetValue(c.S.UiName, out var u)) u.Auth = c.Cap("m").ToLowerInvariant();
            if (Eq(c.Cap("m"), "aaa") && c.S.UiName.StartsWith("vty"))
                return new[] { "Warning: The level of the user-interface(s) will be the default level of AAA users, please check whether it is correct." };
            return Array.Empty<string>();
        }).T(new Val("m", "{aaa|password|none}", (d, p) => SugOf(new[] { "aaa", "password", "none" }, p))),
        C("acl", "Apply ACL to VTY", c =>
        {
            if (c.Tokens.Count > 1 && Eq(c.Tokens[1], "ipv6")) return Array.Empty<string>();
            if (c.Dev.Uis.TryGetValue(c.S.UiName, out var u) && IsInt(c.Cap("num"), out var n)) u.Acl = n;
            return Array.Empty<string>();
        }).T(new Val("num", "INTEGER<2000-3999>", AclSug, s => IsInt(s, out _)), new Opt(new Lit("inbound", "Inbound direction")))
          .T(new Lit("ipv6", "Filter IPv6 addresses"), new Opt(new Val("v6", "INTEGER<2000-3999>", null, s => IsInt(s, out _)))),
        C("idle-timeout", "Set idle timeout", c =>
        {
            if (!c.Dev.Uis.TryGetValue(c.S.UiName, out var u)) return Array.Empty<string>();
            if (!IsInt(c.Cap("min"), out var m)) return Err("Wrong parameter found at '^' position.");
            u.IdleSet = true;
            u.IdleMin = m;
            u.IdleSec = IsInt(c.Cap("sec"), out var s) ? s : 0;
            return Array.Empty<string>();
        }).T(new Val("min", "INTEGER<0-35791>", null, s => IsInt(s, out _)), new Opt(new Val("sec", "INTEGER<0-59>", null, s => IsInt(s, out _)))),
        C("protocol", "Protocol inbound", c =>
        {
            if (c.Dev.Uis.TryGetValue(c.S.UiName, out var u)) u.Protocol = c.Cap("p").ToLowerInvariant();
            return Array.Empty<string>();
        }).T(new Lit("inbound", "Inbound direction"), new Val("p", "{all|ssh|telnet}", (d, p) => SugOf(new[] { "all", "ssh", "telnet" }, p))),
        UndoUiCmd,
        Nav("quit", "Exit current view", QuitRun),
        Nav("return", "Return to user view", ReturnRun),
        DisplayCmd, SaveCmd, CheckCmd
    };

    static Command BuildUndoUi()
    {
        var u = new Command("undo", "Negate a command or set its defaults") { Run = UiUndo };
        u.T(new Lit("acl"), new Val("num", "INTEGER<2000-3999>", AclSug, s => IsInt(s, out _)));
        u.T(new Lit("authentication-mode"));
        u.T(new Lit("protocol"), new Lit("inbound"));
        u.T(new Lit("idle-timeout"));
        return u;
    }

    static string[] UiUndo(ExecCtx c)
    {
        if (!c.Dev.Uis.TryGetValue(c.S.UiName, out var u)) return Err("User interface not found.");
        var t = c.Tokens.Select(x => x.ToLowerInvariant()).ToList();
        var k = string.Join(" ", t.Skip(1));
        if (k.StartsWith("acl")) { u.Acl = null; return Array.Empty<string>(); }
        if (k == "authentication-mode") { u.Auth = null; return Array.Empty<string>(); }
        if (k.StartsWith("protocol inbound")) { u.Protocol = null; return Array.Empty<string>(); }
        if (k == "idle-timeout") { u.IdleSet = false; return Array.Empty<string>(); }
        return Err("Unrecognized command found at '^' position.");
    }

    // v1.5.0 第二批：按 gap 报告补 ACL rule 的协议族/tcp-udp 子关键字（有语义的照旧进规则，其余接受不报错）
    static void AddAclRuleExtras()
    {
        var c = AclView.First(x => x.Kw == "rule");
        void AddProto(string kw, Tok[] rest)
        {
            var pre = new Tok[] { ValAclAction, ValProto, new Lit(kw) };
            c.T(pre.Concat(rest).ToArray());
            c.T(new Tok[] { ValAclId }.Concat(pre.Skip(0)).Concat(rest).ToArray());
        }
        // rule <id> description <text>（真机可敲，配置里记录 rule 描述；本模拟器只接受）
        c.T(ValAclId, new Lit("description"), new Rep(AnyVal, 4));
        // 基础/高级通用：fragment / logging / time-range（无 id / 带 id）
        foreach (var pre in new[] { new Tok[] { ValAclAction }, new Tok[] { ValAclId, ValAclAction } })
        {
            c.T(pre.Concat(new Tok[] { new Lit("fragment") }).ToArray());
            c.T(pre.Concat(new Tok[] { new Lit("logging") }).ToArray());
            c.T(pre.Concat(new Tok[] { new Lit("time-range"), new Val("tr", "STRING<1-32>") }).ToArray());
        }
        // 基础 ACL source any
        c.T(ValAclAction, new Lit("source"), new Lit("any"));
        c.T(ValAclId, ValAclAction, new Lit("source"), new Lit("any"));
        // 高级 ACL：tcp/udp 子关键字
        AddProto("destination", new Tok[] { ValIp, ValWildcard });
        AddProto("source", new Tok[] { ValIp, ValWildcard });
        AddProto("source-port", new Tok[] { new Lit("eq"), ValPort });
        AddProto("source-port", new Tok[] { new Lit("gt"), ValPort });
        AddProto("source-port", new Tok[] { new Lit("lt"), ValPort });
        AddProto("dscp", new Tok[] { VInt("d", "INTEGER<0-63>") });
        AddProto("precedence", new Tok[] { VInt("p", "INTEGER<0-7>") });
        AddProto("tcp-flag", new Tok[] { new Val("f", "{ack|fin|psh|rst|syn|urg}", (d, p) => SugOf(new[] { "ack", "fin", "psh", "rst", "syn", "urg" }, p)), new Opt(new Val("f2", "{ack|fin|psh|rst|syn|urg}", (d, p) => SugOf(new[] { "ack", "fin", "psh", "rst", "syn", "urg" }, p))) });
        AddProto("time-range", new Tok[] { new Val("tr", "STRING<1-32>") });
        AddProto("tos", new Tok[] { VInt("t", "INTEGER<0-255>") });
        AddProto("fragment", Array.Empty<Tok>());
        AddProto("logging", Array.Empty<Tok>());
        AddProto("destination-port", new Tok[] { new Lit("eq"), ValPort });
        AddProto("destination-port", new Tok[] { new Lit("gt"), ValPort });
        AddProto("destination-port", new Tok[] { new Lit("lt"), ValPort });
    }

    // ---------------- 帮助文案（v1.5.0：真机两列 `?`/Tab 第二列说明；词条取自 eNSP LSW transcripts） ----------------
    static CommandSets()
    {
        AddAclRuleExtras();
        FillHelp(UserView, UserHelp);
        FillHelp(SystemView, SystemHelp);
        FillHelp(AaaView, AaaHelp);
        FillHelp(AclView, AclHelp);
        FillHelp(IfaceView, IfaceHelp);
        FillHelp(UiView, UiHelp);
        FillHelp(VlanView, VlanHelp);
        FillHelp(LuppView, LuppHelp);
        Cli.CandHelp = w => CandHelpMap.TryGetValue(w, out var h) ? h : null;
    }

    static readonly Dictionary<string, string> HintHelpMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["INTEGER<1-4094>"] = "VLAN ID",
        ["INTEGER<0-4294967294>"] = "ID of ACL rule",
        ["{cipher|irreversible-cipher|simple}"] = "Password type",
        ["{8021x|bind|ftp|http|ppp|ssh|telnet|terminal|web|x25-pad}"] = "Service type",
        ["{access|dot1q-tunnel|hybrid|trunk}"] = "Port link type",
        ["{inbound|outbound}"] = "Direction",
        ["{v2c|v3}"] = "SNMP version",
        ["{ack|fin|psh|rst|syn|urg}"] = "TCP flag",
        ["INTEGER<0-63>"] = "DSCP value",
        ["INTEGER<0-7>"] = "Precedence",
        ["INTEGER<0-255>"] = "TOS value",
        ["INTEGER<2000-2999>"] = "Basic access-list(add to current using rules)",
        ["INTEGER<3000-3999>"] = "Advanced access-list(add to current using rules)",
        ["INTEGER<4000-4999>"] = "Specify a L2 acl group",
        ["INTEGER<5000-5999>"] = "User defined access-list",
        ["INTEGER<2000-3999>"] = "Basic or advanced ACL",
        ["INTEGER<1-65535>"] = "ID of ACL rule",
        ["INTEGER<0-65535>"] = "Port number",
        ["INTEGER<0,34-48,50-54>"] = "The first user terminal interface to be configured",
        ["INTEGER<1-1024>"] = "The maximum number of VTY users, the default value is 5",
        ["INTEGER<0-20>"] = "The first user terminal interface to be configured",
        ["INTEGER<0>"] = "Console user terminal interface number",
        ["INTEGER<0-35791>"] = "Set the number of minutes before a terminal user times out",
        ["INTEGER<1-246>"] = "The system name",
        ["STRING<1-255>"] = "IP address or host name of a remote system",
        ["STRING<1-64>"] = "User name, in form of 'user@domain'",
        ["STRING<1-63>"] = "Local user name",
        ["STRING<1-32>"] = "Character string, 1-32 characters",
        ["STRING<8-64>"] = "Password, 8-64 characters",
        ["STRING<1-256>"] = "Character string",
        ["<X.X.X.X>"] = "IP address",
        ["<interface>"] = "Interface name",
        ["<interface-type>"] = "Interface type",
        ["<interface-number>"] = "Interface number",
        ["<wildcard>"] = "Wildcard mask",
        ["<text>"] = "Character string",
        ["<auth-type>"] = "Authentication type",
        ["<service-type>"] = "Service type",
        ["<vlan-list>"] = "VLAN list",
        ["{permit|deny}"] = "Action",
        ["{tcp|udp}"] = "Protocol",
        ["{read|write}"] = "Access mode",
        ["{v1|v2c|v3|all}"] = "SNMP version",
        ["{all|ssh|telnet}"] = "Protocol to be allowed",
        ["{access|trunk|hybrid}"] = "Port link type",
        ["{aaa|password|none}"] = "Authentication mode",
        ["{cipher|simple}"] = "Password type",
        ["{add|minus}"] = "Direction",
        ["include|begin|exclude"] = "Filter mode",
    };

    static readonly Dictionary<string, string> CandHelpMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["read"] = "Read-only access for this community in the view",
        ["dot1q-tunnel"] = "QinQ port",
        ["gre"] = "GRE tunneling(47)",
        ["icmp"] = "Internet Control Message Protocol(1)",
        ["igmp"] = "Internet Group Management Protocol(2)",
        ["ipinip"] = "IP in IP tunneling(4)",
        ["ospf"] = "OSPF routing protocol(89)",
        ["write"] = "Read-write access for this community in the view",
        ["access"] = "Access port",
        ["trunk"] = "Trunk port",
        ["hybrid"] = "Hybrid port",
        ["aaa"] = "AAA authentication",
        ["none"] = "Login without checking",
        ["password"] = "Authentication through the password of a user terminal interface",
        ["all"] = "All protocols",
        ["ssh"] = "SSH protocol",
        ["telnet"] = "Telnet protocol",
        ["v1"] = "SNMPv1",
        ["v2c"] = "SNMPv2c",
        ["v3"] = "SNMPv3",
        ["cipher"] = "User password with cipher text",
        ["simple"] = "User password with plain text",
        ["irreversible-cipher"] = "User password with irreversible cipher text",
        ["terminal"] = "Terminal user",
        ["8021x"] = "802.1x user",
        ["bind"] = "Bind authentication user",
        ["ppp"] = "PPP user",
        ["web"] = "Web authentication user",
        ["x25-pad"] = "X25-pad user",
        ["ftp"] = "FTP user",
        ["http"] = "Http user",
        ["permit"] = "Specify matched packet permit",
        ["deny"] = "Specify matched packet deny",
        ["tcp"] = "Transmission Control Protocol(6)",
        ["udp"] = "User Datagram Protocol(17)",
        ["ip"] = "Any IP protocol",
        ["eq"] = "Equal to given port number",
        ["gt"] = "Greater than given port number",
        ["lt"] = "Less than given port number",
        ["range"] = "Between two port numbers",
        ["inbound"] = "Apply the acl on inbound packets",
        ["outbound"] = "Apply the acl on outbound packets",
        ["add"] = "Add to the UTC time",
        ["minus"] = "Subtract from the UTC time",
        ["level"] = "Set admin user(s) level",
        ["first-time"] = "Enable the first-time authentication",
        ["enable"] = "Enable",
        ["disable"] = "Disable",
    };

    static readonly Dictionary<string, string> UserHelp = new(StringComparer.OrdinalIgnoreCase)
    {
        ["system-view"] = "Enter system view",
        ["quit"] = "Exit current view",
        ["return"] = "Return to user view",
        ["save"] = "Save configuration",
        ["check"] = "Run the hardening self-check",
        ["reset"] = "Reset to factory defaults",
        ["current-configuration"] = "Current configuration",
        ["version"] = "Version information",
        ["clock"] = "Current date and time",
        ["this"] = "Current view configuration",
        ["interface"] = "Interface information",
        ["brief"] = "Summary information about the interface status",
        ["acl"] = "Acl status and configuration information",
        ["saved-configuration"] = "Saved configuration",
        ["users"] = "Display information of all user terminals",
        ["logbuffer"] = "Log buffer information",
        ["cpu-usage"] = "CPU usage information",
        ["memory-usage"] = "Memory usage information",
        ["power"] = "Power information",
        ["ip"] = "IP information",
        ["vlan"] = "VLAN information",
        ["|"] = "Matching output",
        ["include"] = "Matching output",
        ["begin"] = "Matching output",
        ["exclude"] = "Matching output",
    };

    static readonly Dictionary<string, string> SystemHelp = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sysname"] = "Set the system name",
        ["aaa"] = "Enter AAA view",
        ["abnormal-offline-record"] = "Abnormal-offline-record",
        ["offline-record"] = "Offline-record",
        ["online-fail-record"] = "Online-fail-record",
        ["acl"] = "Enter ACL view",
        ["number"] = "Specify a numbered ACL",
        ["ipv6"] = "ACL IPv6",
        ["name"] = "Specify a named ACL",
        ["interface"] = "Enter interface view",
        ["range"] = "Set interface range",
        ["to"] = "End of interface range",
        ["port-group"] = "Enter port group view",
        ["vlan"] = "Create VLAN and enter VLAN view",
        ["batch"] = "Batch process",
        ["rsa"] = "RSA key pair",
        ["local-key-pair"] = "Local RSA public key pair operations",
        ["peer-public-key"] = "Remote peer RSA public key configuration",
        ["stelnet"] = "Stelnet client/server",
        ["server"] = "Set Stelnet server",
        ["ipv4"] = "IPv4 protocol",
        ["ssh"] = "SSH related",
        ["client"] = "SSH client",
        ["user"] = "SSH user",
        ["authentication-type"] = "Set authentication type",
        ["service-type"] = "Set service type",
        ["snmp-agent"] = "SNMP agent",
        ["community"] = "Set a community for the access of SNMPv1&SNMPv2c",
        ["extend"] = "SNMP extend configuration",
        ["group"] = "Set an SNMP group based on USM",
        ["inform"] = "Configure parameters for notifications in inform",
        ["local-engineid"] = "Set the engine ID of local SNMP entity",
        ["manner"] = "SNMP message manner",
        ["mib-view"] = "Set SNMP MIB view information",
        ["notification-log"] = "Notification log",
        ["notify-filter-profile"] = "Define notification filter profile",
        ["packet"] = "Set SNMP packet's parameters",
        ["packet-priority"] = "The priority of the SNMP packet",
        ["set-cache"] = "Cache to store the result of the set operation",
        ["usm-user"] = "Set a new user for access to SNMP entity",
        ["sys-info"] = "Set system information of the node",
        ["contact"] = "Set the contact information for system maintenance",
        ["location"] = "Set the physical position information of this node",
        ["version"] = "Enable the SNMP protocol version",
        ["target-host"] = "Set the target hosts to receive SNMP notification/trap",
        ["inform"] = "Specify the target to be inform host",
        ["trap"] = "Set the parameters of SNMP trap/notification",
        ["address"] = "Set the address",
        ["udp-domain"] = "Set the UDP domain",
        ["udp-port"] = "Set the UDP port",
        ["params"] = "Set the parameters",
        ["securityname"] = "Set the security name",
        ["disable"] = "SNMP trap/notification disable commands group",
        ["enable"] = "SNMP trap/notification enable commands group",
        ["life"] = "Set the trap aging time",
        ["queue-size"] = "Length of each TRAP message queue",
        ["source"] = "Set the source address of packets sent to loghost",
        ["suppress"] = "SNMP trap/notification suppress commands group",
        ["type"] = "Set trap type",
        ["info-center"] = "Information center",
        ["channel"] = "Set the name of information channel",
        ["console"] = "Setting of console configuration",
        ["filter-id"] = "Specify the configuration of the ID filtering table",
        ["local"] = "Setting of logging configurations except loghost",
        ["logbuffer"] = "Setting of log buffer configuration",
        ["loghost"] = "Setting of logging host configuration",
        ["monitor"] = "Setting of monitor configuration",
        ["rate-limit"] = "Specify the rate at which the information center",
        ["snmp"] = "Setting of snmp configuration",
        ["statistic-suppress"] = "Suppression that the first occurrence of an event is",
        ["timestamp"] = "Set the time stamp type of information",
        ["trapbuffer"] = "Setting of trap buffer configuration",
        ["timezone"] = "Set the time zone",
        ["datetime"] = "Set the date and time",
        ["add"] = "Add to the UTC time",
        ["minus"] = "Subtract from the UTC time",
        ["user-interface"] = "Enter user interface view",
        ["current"] = "The current user terminal interface",
        ["maximum-vty"] = "The maximum number of VTY users, the default value is 5",
        ["vty"] = "The virtual user terminal interface",
        ["undo"] = "Negate a command or set its defaults",
        ["telnet"] = "Telnet",
        ["http"] = "HTTP",
        ["clock"] = "Clock",
    };

    static readonly Dictionary<string, string> AaaHelp = new(StringComparer.OrdinalIgnoreCase)
    {
        ["local-user"] = "Configure local user",
        ["password"] = "Set password",
        ["privilege"] = "Set admin user(s) level",
        ["level"] = "Set admin user(s) level",
        ["service-type"] = "Service types for authorized user(s)",
        ["idle-timeout"] = "Set the timeout period for terminal user(s)",
        ["access-limit"] = "Set access limit of user(s)",
        ["ftp-directory"] = "Set user(s) FTP directory permitted",
        ["state"] = "Activate/Block the user(s)",
        ["cipher"] = "User password with cipher text",
        ["simple"] = "User password with plain text",
        ["irreversible-cipher"] = "User password with irreversible cipher text",
        ["terminal"] = "Terminal user",
        ["ssh"] = "SSH user",
        ["telnet"] = "Telnet user",
        ["ftp"] = "FTP user",
        ["http"] = "Http user",
        ["local-aaa-user"] = "Set the local administrator password policy",
        ["wrong-password"] = "Set the wrong password policy",
        ["policy"] = "Set password policy",
        ["administrator"] = "Administrator password policy",
        ["user-password"] = "Set user password",
        ["complexity-check"] = "Set password complexity check",
        ["undo"] = "Negate a command or set its defaults",
    };

    static readonly Dictionary<string, string> AclHelp = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rule"] = "Specify an ACL rule",
        ["permit"] = "Specify matched packet permit",
        ["deny"] = "Specify matched packet deny",
        ["description"] = "Specify rule description",
        ["ip"] = "Any IP protocol",
        ["tcp"] = "Transmission Control Protocol(6)",
        ["udp"] = "User Datagram Protocol(17)",
        ["source"] = "Specify source address",
        ["destination"] = "Specify destination address",
        ["destination-port"] = "Specify destination port",
        ["destination"] = "Specify destination address",
        ["dscp"] = "Specify dscp",
        ["fragment"] = "Check fragment packet",
        ["logging"] = "Log matched packet",
        ["precedence"] = "Specify precedence",
        ["source-port"] = "Specify source port",
        ["tcp-flag"] = "Specify tcp flag",
        ["time-range"] = "Specify a special time",
        ["tos"] = "Specify TOS",
        ["any"] = "Any source",
        ["source-port"] = "Specify source port",
        ["eq"] = "Equal to given port number",
        ["gt"] = "Greater than given port number",
        ["lt"] = "Less than given port number",
        ["range"] = "Between two port numbers",
        ["undo"] = "Negate a command or set its defaults",
    };

    static readonly Dictionary<string, string> IfaceHelp = new(StringComparer.OrdinalIgnoreCase)
    {
        ["port"] = "Set port configuration",
        ["link-type"] = "Switch port link type",
        ["default"] = "Set the default VLAN",
        ["trunk"] = "Set the trunk configuration",
        ["allow-pass"] = "Allow the VLAN to pass",
        ["vlan"] = "Set VLAN",
        ["add-tag"] = "Add outer tag based on acl",
        ["discard"] = "Discard",
        ["type"] = "Type",
        ["hybrid"] = "Hybrid port",
        ["priority"] = "Specify current port's priority",
        ["link-flap"] = "Link flap",
        ["mux-vlan"] = "Multiplex vlan",
        ["vlan-mapping"] = "VLAN Mapping",
        ["vlan-stacking"] = "VLAN Stacking",
        ["access"] = "Access port",
        ["traffic-filter"] = "Set ACL-based traffic filtering",
        ["inbound"] = "Apply the acl on inbound packets",
        ["outbound"] = "Apply the acl on outbound packets",
        ["acl"] = "Set the ACL",
        ["shutdown"] = "Shut down the interface",
        ["description"] = "Interface description",
        ["ip"] = "IP configuration",
        ["address"] = "Set the IP address",
        ["undo"] = "Negate a command or set its defaults",
    };

    static readonly Dictionary<string, string> UiHelp = new(StringComparer.OrdinalIgnoreCase)
    {
        ["authentication-mode"] = "Configure the authentication mode for a user terminal",
        ["aaa"] = "AAA authentication",
        ["none"] = "Login without checking",
        ["password"] = "Authentication through the password of a user terminal interface",
        ["acl"] = "ACL-based connection",
        ["ipv6"] = "Filter IPv6 addresses",
        ["protocol"] = "Set the user interface protocol",
        ["inbound"] = "Set the user interface protocol",
        ["all"] = "All protocols",
        ["ssh"] = "SSH protocol",
        ["telnet"] = "Telnet protocol",
        ["idle-timeout"] = "Set the timeout period for a terminal user",
        ["undo"] = "Negate a command or set its defaults",
    };

    static readonly Dictionary<string, string> VlanHelp = new(StringComparer.OrdinalIgnoreCase)
    {
        ["description"] = "VLAN description",
        ["name"] = "Set the VLAN name",
        ["undo"] = "Negate a command or set its defaults",
    };

    static readonly Dictionary<string, string> LuppHelp = new(StringComparer.OrdinalIgnoreCase)
    {
        ["password"] = "Set the password policy",
        ["expire"] = "Set the expiration period of passwords",
        ["history"] = "Password history policy",
        ["record"] = "Record the password history",
        ["number"] = "Number of passwords recorded",
        ["undo"] = "Negate a command or set its defaults",
    };

    static void FillHelp(List<Command> cmds, Dictionary<string, string> map)
    {
        foreach (var c in cmds)
            foreach (var tpl in c.Templates)
                FillToks(tpl, map);
    }

    static void FillToks(IEnumerable<Tok> toks, Dictionary<string, string> map)
    {
        foreach (var t in toks)
        {
            switch (t)
            {
                case Lit l:
                    if (l.Help.Length == 0 && map.TryGetValue(l.W, out var h)) l.Help = h;
                    break;
                case Val v:
                    if (v.HintHelp.Length == 0 && HintHelpMap.TryGetValue(v.Hint, out var hh)) v.HintHelp = hh;
                    break;
                case Opt o:
                    FillToks(o.Inner, map);
                    break;
                case Rep r:
                    FillToks(new[] { r.Inner }, map);
                    break;
            }
        }
    }
}
