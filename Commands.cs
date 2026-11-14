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
        _ => UserView
    };

    // ---------------- helpers ----------------
    static Command C(string kw, string help, Func<ExecCtx, string[]>? run = null)
        => new Command(kw, help) { Run = run };

    static string[] Err(string msg) => new[] { "Error: " + msg };

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

    static IEnumerable<Candidate> IfKindSug(Device d, string p)
    {
        var kinds = new (string Word, string Help)[]
        {
            ("Ethernet", "Ethernet interface"), ("GigabitEthernet", "GigabitEthernet interface"),
            ("Vlanif", "VLAN interface"), ("NULL0", "NULL interface")
        };
        return kinds.Where(k => k.Word.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    .Select(k => new Candidate(k.Word, k.Help));
    }

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

    static IEnumerable<Candidate> UserSug(Device d, string p) =>
        d.Users.Keys.OrderBy(k => k).Select(k => new Candidate(k, ""));

    static IEnumerable<Candidate> VlanSug(Device d, string p) =>
        d.Vlans.OrderBy(v => v).Select(v => new Candidate(v.ToString(), ""));


    // 接口类型按真机口径要写全称（不接受 Eth/Gi 这种缩写），大小写不限
    static readonly Func<string, bool> IfKindAccept = t =>
        new[] { "Ethernet", "GigabitEthernet", "Vlanif", "NULL0" }
            .Any(k => k.Equals(t, StringComparison.OrdinalIgnoreCase));

    static string CanonIfaceKind(string k)
    {
        foreach (var x in new[] { "Ethernet", "GigabitEthernet", "Vlanif", "NULL0" })
            if (x.Equals(k, StringComparison.OrdinalIgnoreCase)) return x;
        return k;
    }

    static readonly Val ValIf = new("if", "<interface>", IfSug);
    static readonly Val ValIp = new("ip", "<X.X.X.X>");
    static readonly Val ValUser = new("name", "<user-name>", UserSug);

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
        c.T(new Lit("acl", "ACL information"), new Val("aclno", "<2000-3999>", AclSug, s => IsInt(s, out _)));
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
            .T(new Val("name", "<name>")),
        C("aaa", "Enter AAA view", c => { c.S.View = "aaa"; return Array.Empty<string>(); }).T(),
        C("acl", "Enter ACL view", EnterAcl)
            .T(new Lit("number"), new Val("num", "<2000-3999>"))
            .T(new Lit("number"), new Val("num", "<2000-3999>"), new Lit("name"), new Val("nm", "<name>"))
            .T(new Val("num", "<2000-3999>"))
            .T(new Val("num", "<2000-3999>"), new Lit("name"), new Val("nm", "<name>")),
        C("interface", "Enter interface view", EnterIface)
            .T(new Lit("range", "Set interface range"), new Rep(new Val("rw", "<interface1> to <interface2>")))
            .T(new Val("kind", "<interface-type>", IfKindSug, IfKindAccept), new Val("num", "<interface-number>", IfNumSug))
            .T(new Val("if", "<interface>", IfSug)),
        C("port-group", "Enter port group view", EnterPortGroup)
            .T(new Val("pg", "<port-group-name>", (d, p) => d.PortGroups.Keys.OrderBy(k => k).Select(k => new Candidate(k, "")))),
        C("vlan", "Create VLAN", VlanBatch).T(new Lit("batch"), new Rep(new Val("id", "<vlan-id>"))),
        C("rsa", "RSA key pair", RsaRun).T(new Lit("local-key-pair"), new Lit("create")),
        C("stelnet", "Enable SSH service", StelnetRun)
            .T(new Lit("server"), new Lit("enable"))
            .T(new Lit("ipv4"), new Lit("server"), new Lit("enable"))
            .T(new Lit("ipv6"), new Lit("server"), new Lit("enable")),
        C("ssh", "SSH related", _ => new[] { "  Info: The command is executed successfully." })
            .T(new Lit("client"), new Lit("first-time"), new Lit("enable"))
            .T(new Lit("user"), ValUser, new Lit("authentication-type"), new Val("t", "<auth-type>"))
            .T(new Lit("user"), ValUser, new Lit("service-type"), new Val("t", "<service-type>")),
        C("snmp-agent", "SNMP agent", SnmpRun)
            .T()
            .T(new Lit("community"), new Val("dir", "<read|write>", (d, p) => SugOf(new[] { "read", "write" }, p)),
                new Opt(new Lit("cipher")), new Val("pw", "<community>"),
                new Opt(new Lit("acl"), new Val("aclnum", "<2000-3999>", AclSug)))
            .T(new Lit("sys-info"), new Lit("version"), new Rep(new Val("ver", "<v1|v2c|v3|all>", (d, p) => SugOf(new[] { "v1", "v2c", "v3", "all" }, p))))
            .T(new Lit("target-host"), new Lit("trap"), new Lit("address"), new Lit("udp-domain"), ValIp,
                new Opt(new Lit("udp-port"), new Val("port", "<1-65535>")),
                new Lit("params"), new Lit("securityname"), new Opt(new Lit("cipher")), new Val("sec", "<community>"),
                new Opt(new Val("ver", "<v2c|v3>", (d, p) => SugOf(new[] { "v2c", "v3" }, p))))
            .T(new Lit("trap"), new Lit("enable")),
        C("info-center", "Log service", InfoCenterRun)
            .T(new Lit("enable"))
            .T(new Lit("loghost"), new Opt(new Lit("source"), ValIf), ValIp),
        C("clock", "Set clock/timezone", ClockRun)
            .T(new Lit("timezone"), new Val("tz", "<name>"), new Lit("add"), new Val("off", "<HH:MM:SS>"))
            .T(new Lit("datetime"), new Val("time", "<HH:MM:SS>"), new Val("date", "<YYYY-MM-DD>")),
        C("user-interface", "Enter user interface view", EnterUi)
            .T(new Lit("console"), new Val("n", "<0>"))
            .T(new Lit("vty"), new Val("a", "<0-20>"), new Opt(new Val("b", "<0-20>"))),
        UndoSystemCmd,
        Nav("quit", "Exit current view", QuitRun),
        Nav("return", "Return to user view", ReturnRun),
        DisplayCmd, SaveCmd, CheckCmd
    };

    static string[] EnterAcl(ExecCtx c)
    {
        if (!IsInt(c.Cap("num"), out var n) || n < 2000 || n > 3999) return Err("Wrong parameter found at '^' position.");
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
        var kind = CanonIfaceKind(c.Cap("kind"));
        var name = c.Has("kind")
            ? (Eq(kind, "NULL0") ? "NULL0" : kind + c.Cap("num"))
            : c.Cap("if");
        if (!c.Dev.Ifaces.ContainsKey(name))
        {
            if (name.StartsWith("Vlanif", StringComparison.OrdinalIgnoreCase) && IsInt(name.Substring(6), out var vlan))
            {
                if (!c.Dev.Vlans.Contains(vlan)) return Err("The VLAN does not exist.");
                c.Dev.Ifaces[name] = new Iface { Name = name, IsVlanif = true };
            }
            else return Err("Wrong parameter found at '^' position.");
        }
        c.S.IfName = name;
        c.S.View = "iface";
        return Array.Empty<string>();
    }

    static (string Prefix, int Num) SplitIface(string s)
    {
        var slash = s.LastIndexOf('/');
        if (slash >= 0)
            return (s.Substring(0, slash + 1), IsInt(s.Substring(slash + 1), out var n) ? n : -1);
        var i = s.Length - 1;
        while (i >= 0 && char.IsDigit(s[i])) i--;
        return (s.Substring(0, i + 1), IsInt(s.Substring(i + 1), out var m) ? m : -1);
    }

    static string[] EnterRange(ExecCtx c)
    {
        var toks = c.Tokens;
        var idx = toks.FindIndex(x => x.Equals("to", StringComparison.OrdinalIgnoreCase));
        if (idx <= 2) return Err("Wrong parameter found at '^' position.");
        var left = string.Concat(toks.Skip(2).Take(idx - 2));
        var right = string.Concat(toks.Skip(idx + 1));
        var (p1, n1) = SplitIface(left);
        var (p2, n2) = SplitIface(right);
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

    static string[] RsaRun(ExecCtx c) => new[]
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
        if (t.Count > 1 && t[1] == "ipv6") c.Dev.StelnetV6 = true;
        else c.Dev.StelnetV4 = true;
        return new[] { "Info: Succeeded in starting the Stelnet server." };
    }

    static string[] SnmpRun(ExecCtx c)
    {
        var d = c.Dev;
        d.SnmpAgent = true;
        var t = c.Tokens;
        if (t.Count >= 2 && Eq(t[1], "community"))
        {
            var com = new SnmpCommunity { Read = !Eq(c.Cap("dir"), "write"), Password = c.Cap("pw") };
            if (c.Has("aclnum") && IsInt(c.Cap("aclnum"), out var an)) com.Acl = an;
            d.Communities.RemoveAll(x => x.Read == com.Read);
            d.Communities.Add(com);
        }
        else if (t.Count >= 2 && Eq(t[1], "sys-info"))
        {
            d.SnmpVersion = string.Join(" ", t.Skip(3)).ToLowerInvariant();
            if (d.SnmpVersion.Contains("v1") || d.SnmpVersion.Contains("v2c"))
                return new[] { "Warning: SNMPv1/SNMPv2c is not secure, and it is recommended to use SNMPv3." };
        }
        else if (t.Count >= 2 && Eq(t[1], "target-host"))
        {
            var tg = new SnmpTarget { Ip = c.Cap("ip"), Security = c.Cap("sec"), Version = c.Has("ver") ? c.Cap("ver") : "v2c" };
            if (c.Has("port") && IsInt(c.Cap("port"), out var p)) tg.Port = p;
            d.Targets.RemoveAll(x => x.Ip == tg.Ip);
            d.Targets.Add(tg);
        }
        else if (t.Count >= 2 && Eq(t[1], "trap"))
        {
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
            var ip = c.Cap("ip");
            if (!IsIp(ip)) return Err("Wrong parameter found at '^' position.");
            if (!c.Dev.LogHosts.Contains(ip)) c.Dev.LogHosts.Add(ip);
            return new[] { "Warning: There is security risk as this operation enables a non secure syslog protocol." };
        }
        else c.Dev.InfoCenterEnabled = true;
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
            c.Dev.Clock.TzOffsetSec = h * 3600 + mi * 60;
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
        string name;
        if (Eq(t[1], "console")) name = "con " + c.Cap("n");
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
        u.T(new Lit("vlan"), new Lit("batch"), new Rep(new Val("id", "<vlan-id>")));
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
        if (t.Count > 2 && Eq(t[1], "vlan") && Eq(t[2], "batch"))
        {
            foreach (var x in t.Skip(3)) if (IsInt(x, out var v) && v != 1) d.Vlans.Remove(v);
            return Array.Empty<string>();
        }
        return Err("Unrecognized command found at '^' position.");
    }

    // ---------------- AAA view ----------------
    static readonly Command UndoAaaCmd = BuildUndoAaa();

    static readonly List<Command> AaaView = new()
    {
        C("local-user", "Configure local user", LocalUserRun)
            .T(ValUser, new Lit("password"), new Val("mode", "<cipher|irreversible-cipher>", (d, p) => SugOf(new[] { "cipher", "irreversible-cipher" }, p)), new Val("pw", "STRING<8-64>"))
            .T(ValUser, new Lit("password"), new Val("mode", "<cipher|irreversible-cipher>", (d, p) => SugOf(new[] { "cipher", "irreversible-cipher" }, p)), new Val("pw", "STRING<8-64>"),
                new Lit("privilege"), new Lit("level"), new Val("n", "<0-15>"))
            .T(ValUser, new Lit("privilege"), new Lit("level"), new Val("n", "<0-15>"))
            .T(ValUser, new Lit("service-type"), new Rep(new Val("svc", "<service>", (d, p) => SugOf(new[] { "terminal", "ssh", "telnet", "ftp", "http" }, p))))
            .T(ValUser, new Lit("idle-timeout"), new Val("min", "<0-35791>"), new Opt(new Val("sec", "<0-59>"))),
        C("local-aaa-user", "AAA user policy", LocalAaaRun)
            .T(new Lit("wrong-password"), new Lit("retry-interval"), new Val("a", "<1-60>"), new Lit("retry-time"), new Val("b", "<1-10>"), new Lit("block-time"), new Val("c", "<1-120>"))
            .T(new Lit("password"), new Lit("policy"), new Lit("administrator")),
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
            .T(new Val("action", "<permit|deny>", (d, p) => SugOf(new[] { "permit", "deny" }, p)), new Lit("ip"))
            .T(new Val("action", "<permit|deny>", (d, p) => SugOf(new[] { "permit", "deny" }, p)),
                new Lit("source"), ValIp, new Val("wc", "<wildcard>", (d, p) => new[] { new Candidate("0", "Wildcard bits : 0.0.0.0 (a host)"), new Candidate("X.X.X.X", "Wildcard of source") }))
            .T(new Val("action", "<permit|deny>", (d, p) => SugOf(new[] { "permit", "deny" }, p)),
                new Val("proto", "<tcp|udp>", (d, p) => SugOf(new[] { "tcp", "udp" }, p)), new Lit("destination-port"), new Lit("eq"), new Val("port", "<port>"))
            .T(new Val("action", "<permit|deny>", (d, p) => SugOf(new[] { "permit", "deny" }, p)),
                new Val("proto", "<tcp|udp>", (d, p) => SugOf(new[] { "tcp", "udp" }, p)), new Lit("destination-port"), new Lit("range"), new Val("p1", "<start-port>"), new Val("p2", "<end-port>"))
            .T(new Val("action", "<permit|deny>", (d, p) => SugOf(new[] { "permit", "deny" }, p)))
            .T(new Val("id", "<1-65535>"), new Val("action", "<permit|deny>", (d, p) => SugOf(new[] { "permit", "deny" }, p)))
            .T(new Val("id", "<1-65535>"), new Val("action", "<permit|deny>", (d, p) => SugOf(new[] { "permit", "deny" }, p)), new Lit("ip"))
            .T(new Val("id", "<1-65535>"), new Val("action", "<permit|deny>", (d, p) => SugOf(new[] { "permit", "deny" }, p)),
                new Lit("source"), ValIp, new Val("wc", "<wildcard>", (d, p) => new[] { new Candidate("0", "Wildcard bits : 0.0.0.0 (a host)"), new Candidate("X.X.X.X", "Wildcard of source") }))
            .T(new Val("id", "<1-65535>"), new Val("action", "<permit|deny>", (d, p) => SugOf(new[] { "permit", "deny" }, p)),
                new Val("proto", "<tcp|udp>", (d, p) => SugOf(new[] { "tcp", "udp" }, p)), new Lit("destination-port"), new Lit("eq"), new Val("port", "<port>"))
            .T(new Val("id", "<1-65535>"), new Val("action", "<permit|deny>", (d, p) => SugOf(new[] { "permit", "deny" }, p)),
                new Val("proto", "<tcp|udp>", (d, p) => SugOf(new[] { "tcp", "udp" }, p)), new Lit("destination-port"), new Lit("range"), new Val("p1", "<start-port>"), new Val("p2", "<end-port>")),
        C("description", "ACL description", c =>
        {
            if (c.Dev.Acls.TryGetValue(c.S.AclNumber, out var a)) a.Description = string.Join(" ", c.Tokens.Skip(1));
            return Array.Empty<string>();
        }).T(new Rep(new Val("w", "<text>"))),
        UndoAclCmd,
        Nav("quit", "Exit current view", QuitRun),
        Nav("return", "Return to user view", ReturnRun),
        DisplayCmd, SaveCmd, CheckCmd
    };

    static Command BuildUndoAcl()
    {
        var u = new Command("undo", "Negate a command or set its defaults") { Run = AclUndo };
        u.T(new Lit("rule"), new Val("id", "<1-65535>"));
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
        int id;
        int actionIdx;
        if (c.Has("id"))
        {
            if (!IsInt(c.Cap("id"), out id) || id < 1 || id > 65535) return Err("Wrong parameter found at '^' position.");
            actionIdx = 2;
        }
        else
        {
            var maxId = acl.Rules.Count == 0 ? 0 : acl.Rules.Max(x => x.Id);
            id = maxId + 5;
            if (id > 65535) return Err("The rule ID reaches the upper limit.");
            actionIdx = 1;
        }
        var action = c.Cap("action").ToLowerInvariant();
        if (action != "permit" && action != "deny") return Err("Wrong parameter found at '^' position.");
        var r = new AclRule
        {
            Id = id,
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
            else return Err("Incomplete command found at '^' position.");
        }
        else
        {
            var si = t.IndexOf("source");
            if (si >= 2)
            {
                r.Source = c.Cap("ip");
                if (IsInt(c.Cap("wc"), out var wc)) r.SourceWildcard = wc;
                r.Proto = "ip";
            }
            else r.Proto = "ip";
        }
        acl.Rules.RemoveAll(x => x.Id == id);
        acl.Rules.Add(r);
        return Array.Empty<string>();
    }

    static string[] AclUndo(ExecCtx c)
    {
        var d = c.Dev;
        var t = c.Tokens.Select(x => x.ToLowerInvariant()).ToList();
        if (!d.Acls.TryGetValue(c.S.AclNumber, out var acl)) return Err("ACL not found.");
        if (Eq(t[1], "rule") && IsInt(c.Cap("id"), out var id)) { acl.Rules.RemoveAll(x => x.Id == id); return Array.Empty<string>(); }
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
        }).T(new Rep(new Val("w", "<text>"))),
        C("port", "Port configuration", PortRun)
            .T(new Lit("link-type"), new Val("lt", "<access|trunk|hybrid>", (d, p) => SugOf(new[] { "access", "trunk", "hybrid" }, p)))
            .T(new Lit("default"), new Lit("vlan"), new Val("vid", "<vlan-id>", VlanSug))
            .T(new Lit("trunk"), new Lit("allow-pass"), new Lit("vlan"), new Rep(new Val("vid", "<vlan-id>", VlanSug))),
        C("traffic-filter", "Apply ACL on interface", TfRun)
            .T(new Val("dir", "<inbound|outbound>", (d, p) => SugOf(new[] { "inbound", "outbound" }, p)), new Lit("acl"), new Val("num", "<2000-3999>", AclSug)),
        C("shutdown", "Shut down interface", c => { foreach (var i in Targets(c)) i.Shutdown = true; return Array.Empty<string>(); }).T(),
        C("ip", "IP configuration", IpRun).T(new Lit("address"), ValIp, new Val("mask", "<X.X.X.X>")),
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
        u.T(new Lit("port"), new Lit("trunk"), new Lit("allow-pass"), new Lit("vlan"), new Val("vid", "<vlan-id>", VlanSug));
        u.T(new Lit("port"), new Lit("link-type"));
        u.T(new Lit("traffic-filter"), new Val("dir", "<inbound|outbound>", (d, p) => SugOf(new[] { "inbound", "outbound" }, p)), new Lit("acl"), new Val("num", "<2000-3999>", AclSug));
        u.T(new Lit("traffic-filter"), new Val("dir", "<inbound|outbound>", (d, p) => SugOf(new[] { "inbound", "outbound" }, p)));
        u.T(new Lit("description"));
        u.T(new Lit("ip"), new Lit("address"));
        return u;
    }

    static string[] PortRun(ExecCtx c)
    {
        var t = c.Tokens.Select(x => x.ToLowerInvariant()).ToList();
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
        u.T(new Lit("port"), new Lit("trunk"), new Lit("allow-pass"), new Lit("vlan"), new Val("vid", "<vlan-id>", VlanSug));
        u.T(new Lit("port"), new Lit("link-type"));
        u.T(new Lit("traffic-filter"), new Val("dir", "<inbound|outbound>", (d, p) => SugOf(new[] { "inbound", "outbound" }, p)), new Lit("acl"), new Val("num", "<2000-3999>", AclSug));
        u.T(new Lit("traffic-filter"), new Val("dir", "<inbound|outbound>", (d, p) => SugOf(new[] { "inbound", "outbound" }, p)));
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
        var name = c.Cap("if");
        if (!d.Ifaces.ContainsKey(name)) return Err("Wrong parameter found at '^' position.");
        if (c.Has("if2"))
        {
            var name2 = c.Cap("if2");
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
            if (c.Has("if")) members.Remove(c.Cap("if"));
            else members.Clear();
            return Array.Empty<string>();
        }
        return IfaceUndo(c);
    }


    // ---------------- 本地管理员密码策略视图 ----------------
    static readonly Command UndoLuppCmd = BuildUndoLupp();

    static readonly List<Command> LuppView = new()
    {
        C("password", "Local administrator password policy", LuppRun)
            .T(new Lit("expire"), new Val("days", "<0-999>"))
            .T(new Lit("history"), new Lit("record"), new Lit("number"), new Val("num", "<0-5>")),
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
        }).T(new Val("m", "<aaa|password|none>", (d, p) => SugOf(new[] { "aaa", "password", "none" }, p))),
        C("acl", "Apply ACL to VTY", c =>
        {
            if (c.Dev.Uis.TryGetValue(c.S.UiName, out var u) && IsInt(c.Cap("num"), out var n)) u.Acl = n;
            return Array.Empty<string>();
        }).T(new Val("num", "<2000-3999>", AclSug), new Opt(new Lit("inbound"))),
        C("idle-timeout", "Set idle timeout", c =>
        {
            if (!c.Dev.Uis.TryGetValue(c.S.UiName, out var u)) return Array.Empty<string>();
            if (!IsInt(c.Cap("min"), out var m)) return Err("Wrong parameter found at '^' position.");
            u.IdleSet = true;
            u.IdleMin = m;
            u.IdleSec = IsInt(c.Cap("sec"), out var s) ? s : 0;
            return Array.Empty<string>();
        }).T(new Val("min", "<0-35791>"), new Opt(new Val("sec", "<0-59>"))),
        C("protocol", "Protocol inbound", c =>
        {
            if (c.Dev.Uis.TryGetValue(c.S.UiName, out var u)) u.Protocol = c.Cap("p").ToLowerInvariant();
            return Array.Empty<string>();
        }).T(new Lit("inbound"), new Val("p", "<all|ssh|telnet>", (d, p) => SugOf(new[] { "all", "ssh", "telnet" }, p))),
        UndoUiCmd,
        Nav("quit", "Exit current view", QuitRun),
        Nav("return", "Return to user view", ReturnRun),
        DisplayCmd, SaveCmd, CheckCmd
    };

    static Command BuildUndoUi()
    {
        var u = new Command("undo", "Negate a command or set its defaults") { Run = UiUndo };
        u.T(new Lit("acl"), new Val("num", "<2000-3999>", AclSug));
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
}
