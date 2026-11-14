using System.Text;

namespace VrpSim;

public static class Program
{
    public static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        var s = new Session();
        var batch = Console.IsInputRedirected || args.Contains("--batch") || args.Contains("--selftest");
        var selftest = args.Contains("--selftest");
        s.Interactive = !batch;
        if (selftest) return SelfTest(s);
        var ci = Array.IndexOf(args, "--complete");
        if (ci >= 0 && ci + 1 < args.Length)
        {
            var viewArg = Array.IndexOf(args, "--view");
            if (viewArg >= 0 && viewArg + 1 < args.Length) s.View = args[viewArg + 1];
            PrintCompletion(s, args[ci + 1]);
            return 0;
        }
        var fresh = args.Contains("--fresh");
        var persist = args.Contains("--persist") || s.Interactive;
        Banner(s);
        if (persist)
        {
            if (fresh)
            {
                StateStore.Delete(StateStore.DefaultPath);
                Console.WriteLine("  [进度] 已按新开局开始（--fresh），退出时自动存档。");
            }
            else
            {
                var loaded = StateStore.Load(StateStore.DefaultPath, out var lerr);
                if (loaded != null)
                {
                    s.Dev = loaded;
                    Console.WriteLine("  [进度] 已恢复上次配置：" + StateStore.DefaultPath);
                }
                else
                {
                    Console.WriteLine((lerr != null ? "  [进度] 存档读取失败：" + lerr + "；" : "  [进度] 新开局：") +
                                      "退出时自动存到 " + StateStore.DefaultPath);
                }
            }
            Console.WriteLine();
        }
        if (!batch && !args.Contains("--no-login")) Login(s);
        Run(s, batch, persist);
        if (persist) StateStore.Save(s.Dev, StateStore.DefaultPath, out _);
        return 0;
    }

    static void Banner(Session s)
    {
        Console.WriteLine();
        Console.WriteLine("  ============================================================");
        Console.WriteLine("   Huawei VRP 交换机模拟终端  (V200R011C10SPC600, 训练用)");
        Console.WriteLine("  ============================================================");
        Console.WriteLine("   Tab 补全    ? 帮助    check 加固自查    quit 退出");
        Console.WriteLine("   视图: <HUAWEI> 用户视图 → system-view → [HUAWEI] 系统视图");
        Console.WriteLine("   Console 登录: 默认用户 admin / admin@huawei.com");
        Console.WriteLine("  ============================================================");
        Console.WriteLine();
    }

    static void Login(Session s)
    {
        Console.WriteLine("  User interface con0 is available.");
        Console.WriteLine();
        Console.WriteLine("Please Press ENTER.");
        Console.ReadLine();
        var failures = 0;
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("Login authentication");
            Console.WriteLine();
            Console.Write("Username:");
            var u = (Console.ReadLine() ?? "").Trim();
            Console.Write("Password:");
            var p = ReadSecret();
            if (s.Dev.Users.Count == 0)
            {
                Console.WriteLine();
                Console.WriteLine("Warning: no local user configured, entering recovery mode.");
                return;
            }
            if (s.Dev.Users.TryGetValue(u, out var user) && user.Password == p)
            {
                Console.WriteLine();
                if (RunInitialPasswordFlow(s, user)) return;
                Console.WriteLine("Error: The initial password must be changed before login.");
                continue;
            }
            Console.WriteLine();
            Console.WriteLine("Error: Authentication fail");
            failures++;
            if (failures >= 3)
            {
                Console.WriteLine("Error: authentication failure exceeded, session closed.");
                Environment.Exit(1);
            }
        }
    }

    static bool RunInitialPasswordFlow(Session s, LocalUser user)
    {
        if (!user.PasswordInitial) return true;
        Console.WriteLine("Warning: The initial password poses security risks.");
        Console.WriteLine("The password needs to be changed. Change now? [Y/N]:");
        var ans = (Console.ReadLine() ?? "").Trim();
        if (!ans.StartsWith("y", StringComparison.OrdinalIgnoreCase)) return false;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Console.Write("Please enter old password:");
            var oldPwd = ReadSecret();
            if (oldPwd != user.Password)
            {
                Console.WriteLine("Error: The old password is incorrect.");
                if (attempt == 3) Console.WriteLine("Error: Too many failed attempts, the password is not changed.");
                continue;
            }
            Console.Write("Please enter new password:");
            var np = ReadSecret();
            if (np.Length == 0)
            {
                Console.WriteLine("Error: The new password cannot be empty.");
                return false;
            }
            if (np == oldPwd)
            {
                Console.WriteLine("Error: The new password must be different from the old password.");
                return false;
            }
            Console.Write("Please confirm new password:");
            var np2 = ReadSecret();
            if (np != np2)
            {
                Console.WriteLine("Error: The two passwords are different.");
                return false;
            }
            user.Password = np;
            user.PasswordInitial = false;
            Console.WriteLine("The password has been changed successfully.");
            return true;
        }
        return false;
    }

    static string ReadSecret()
    {
        var sb = new StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Enter) { Console.WriteLine(); return sb.ToString(); }
            if (k.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0)
                {
                    sb.Length--;
                    Console.Write("\b \b");
                }
                continue;
            }
            if (k.KeyChar == '\u0003') { Console.WriteLine(); return ""; }
            if (!char.IsControl(k.KeyChar)) { sb.Append(k.KeyChar); Console.Write('*'); }
        }
    }

    static void Run(Session s, bool batch, bool persist)
    {
        var editor = new LineEditor(s);
        if (!batch) s.ReadLine = prompt => editor.ReadLine(prompt);
        while (!s.ExitRequested)
        {
            string? line;
            if (batch)
            {
                Console.Write(s.Prompt());
                line = Console.ReadLine();
                if (line == null) { Console.WriteLine(); break; }
                Console.WriteLine(line);
            }
            else
            {
                try { line = editor.ReadLine(s.Prompt()); }
                catch (InvalidOperationException)
                {
                    batch = true;
                    continue;
                }
            }
            if (line == null) break;
            if (line == "\u001A") { s.View = "user"; continue; }
            if (line.Trim().Length == 0) continue;
            Process(s, line, s.Prompt().Length);
            if (persist) StateStore.Save(s.Dev, StateStore.DefaultPath, out _);
            if (s.ExitRequested) break;
        }
    }

    static void Process(Session s, string line, int promptLen)
    {
        var q = line.IndexOf('?');
        if (q >= 0) { PrintHelp(s, line, q); return; }
        var tokens = Tokenize(line);
        if (tokens.Count == 0) return;
        var pipe = tokens.IndexOf("|");
        if (pipe > 0)
        {
            var main = tokens.Take(pipe).ToList();
            var mode = tokens.Count > pipe + 1 ? tokens[pipe + 1].ToLowerInvariant() : "include";
            var pat = tokens.Count > pipe + 2 ? tokens[pipe + 2].ToLowerInvariant() : "";
            var r0 = Cli.Execute(s.Dev, s, s.Commands(), main);
            if (r0.Error != null)
            {
                var o0 = TokenOffset(main, r0.ErrTok);
                Console.WriteLine(new string(' ', promptLen + o0) + "^");
                Console.WriteLine("Error:" + r0.Error + " found at '^' position.");
                return;
            }
            IEnumerable<string> lines = r0.Lines;
            if (mode == "exclude") lines = lines.Where(x => !x.ToLowerInvariant().Contains(pat));
            else if (mode == "begin")
            {
                var arr = r0.Lines.ToList();
                var i0 = arr.FindIndex(x => x.ToLowerInvariant().Contains(pat));
                lines = i0 >= 0 ? arr.Skip(i0) : Enumerable.Empty<string>();
            }
            else lines = lines.Where(x => x.ToLowerInvariant().Contains(pat));
            foreach (var l in lines) Console.WriteLine(l);
            return;
        }
        var res = Cli.Execute(s.Dev, s, s.Commands(), tokens);
        if (res.Error == null)
        {
            foreach (var l in res.Lines) Console.WriteLine(l);
            return;
        }
        var off = TokenOffset(tokens, res.ErrTok);
        Console.WriteLine(new string(' ', promptLen + off) + "^");
        Console.WriteLine("Error:" + res.Error + " found at '^' position.");
    }

    static void PrintHelp(Session s, string line, int q)
    {
        var before = line.Substring(0, q);
        var toks = Tokenize(before);
        List<string> prior;
        string partial;
        if (before.Length == 0 || before.EndsWith(" "))
        {
            prior = toks;
            partial = "";
        }
        else
        {
            partial = toks.Count > 0 ? toks[^1] : "";
            prior = toks.Take(Math.Max(0, toks.Count - 1)).ToList();
        }
        var cands = Cli.Complete(s.Dev, s.Commands(), prior, partial, true);
        PrintCandidates(cands);
    }

    static void PrintCandidates(List<Candidate> cands)
    {
        if (cands.Count == 0 || cands.All(c => c.Word.Length == 0)) { Console.WriteLine("  (no match)"); return; }
        foreach (var line in HelpFormat.Format(cands)) Console.WriteLine(line);
    }

    static void PrintCompletion(Session s, string text)
    {
        var start = 0;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] == ' ') { start = i + 1; break; }
        }
        var before = Tokenize(text.Substring(0, start));
        var partial = text.Substring(start);
        var cands = Cli.Complete(s.Dev, s.Commands(), before, partial, false);
        Console.WriteLine($"view={s.View} text=[{text}] partial=[{partial}]");
        foreach (var c in cands)
        {
            var line = start > 0 ? text.Substring(0, start) + c.Word : c.Word;
            Console.WriteLine($"  insert: [{line} ]");
        }
        if (cands.Count == 0) Console.WriteLine("  (no match)");
    }

    static List<string> Tokenize(string line) =>
        line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();

    static int TokenOffset(List<string> toks, int idx)
    {
        var off = 0;
        for (var i = 0; i < idx && i < toks.Count; i++) off += toks[i].Length + 1;
        return off;
    }

    static int SelfTest(Session s)
    {
        var script = HardeningScript();
        foreach (var line in script)
        {
            var tokens = Tokenize(line);
            if (tokens.Count == 0) continue;
            var res = Cli.Execute(s.Dev, s, s.Commands(), tokens);
            if (res.Error != null)
            {
                Console.WriteLine($"SELFTEST FAIL: [{line}] -> {res.Error}");
                return 2;
            }
            foreach (var l in res.Lines)
            {
                if (l.StartsWith("Error:", StringComparison.Ordinal))
                {
                    Console.WriteLine($"SELFTEST FAIL: [{line}] -> {l}");
                    return 2;
                }
            }
        }
        var rows = Checks.Run(s.Dev);
        foreach (var l in Checks.Format(rows, s.Dev)) Console.WriteLine(l);
        var fails = rows.Where(r => r.Ok == false).ToList();
        var candFailed = CompletionSelfTest(s) + CompletionOrderSelfTest(s) + RangeExecSelfTest()
                         + IfaceFormSelfTest() + VlanExecSelfTest() + NullIfaceSelfTest() + ConfigIfaceSelfTest(s)
                         + NoErrorSelfTest() + NewIfaceSelfTest() + HelpFormatSelfTest();
        if (fails.Count > 0 || candFailed > 0)
        {
            Console.WriteLine($"SELFTEST: {fails.Count} check item(s) failed, {candFailed} completion/range case(s) failed");
            return 1;
        }
        Console.WriteLine("SELFTEST OK");
        return 0;
    }

    // 逐 token 补全快照回归：演练脚本关键路径的 Tab/? 候选（>=20 条路径）
    static int CompletionSelfTest(Session s)
    {
        var cases = new (string View, string Text, bool Help, string[] Expect, string[]? Absent)[]
        {
            // ---- 用户视图 ----
            ("user", "disp", false, new[] { "display" }, null),
            ("user", "sys", false, new[] { "system-view" }, null),
            ("user", "display ", true, new[] { "current-configuration", "version", "clock", "this", "interface", "acl", "saved-configuration", "users" }, null),
            ("user", "display acl ", true, new[] { "all", "INTEGER<2000-3999>", "2000", "3000", "3999" }, null),
            ("user", "save ", true, new[] { "<cr>" }, null),
            ("user", "show", false, Array.Empty<string>(), new[] { "*" }),
            // ---- 系统视图 ----
            ("system", "int", false, new[] { "interface" }, null),
            ("system", "sysname ", true, new[] { "STRING<1-246>" }, null),
            ("system", "vlan batch ", true, new[] { "INTEGER<1-4094>", "8" }, null),
            ("system", "vlan batch 8 ", true, new[] { "<cr>" }, null),
            ("system", "vlan ", true, new[] { "batch", "INTEGER<1-4094>" }, null),
            ("system", "undo vlan ", true, new[] { "INTEGER<1-4094>", "1", "8" }, null),
            ("system", "interface ", true, new[] { "range", "Eth-Trunk", "Ethernet", "GigabitEthernet", "LoopBack", "NULL", "Tunnel", "Vlanif" },
                new[] { "Ethernet0/0/1", "GigabitEthernet0/0/1", "Vlanif1", "Vlanif8", "NULL0", "<interface-type>", "<interface>" }),
            ("system", "interface ethernet", false, new[] { "Ethernet" }, new[] { "Ethernet0/0/1" }),
            ("system", "interface NULL ", false, new[] { "0" }, null),
            ("system", "interface Vlanif ", false, new[] { "1", "8" }, null),
            ("system", "interface GigabitEthernet ", false, new[] { "0/0/1", "0/0/12" }, null),
            ("system", "interface Ethernet ", false, new[] { "0/0/1", "0/0/16" }, null),
            ("system", "interface range ", true, new[] { "Ethernet", "GigabitEthernet", "Vlanif" },
                new[] { "Ethernet0/0/1", "GigabitEthernet0/0/1", "Vlanif1", "Vlanif8", "NULL0" }),
            ("system", "interface range ethernet ", false, new[] { "0/0/1", "0/0/16" }, new[] { "ethernet0/0/1" }),
            ("system", "interface range Ethernet 0/0/1 ", true, new[] { "to" }, null),
            ("system", "interface range Ethernet 0/0/1 to Ethernet ", false, new[] { "0/0/2", "0/0/16" }, null),
            ("system", "acl ", true, new[] { "INTEGER<2000-2999>", "INTEGER<3000-3999>", "INTEGER<4000-4999>", "INTEGER<5000-5999>", "ipv6", "name", "number", "2000", "3000" }, null),
            ("system", "acl number ", false, new[] { "2000", "2001", "3000", "3999" }, null),
            ("system", "undo ", true, new[] { "telnet", "ftp", "http", "stelnet", "snmp-agent", "acl", "sysname", "info-center", "clock", "vlan" }, null),
            ("system", "undo telnet ", true, new[] { "server" }, null),
            ("system", "undo telnet server ", true, new[] { "enable" }, null),
            ("system", "stelnet ", true, new[] { "server", "ipv4", "ipv6", "STRING<1-255>" }, null),
            ("system", "clock timezone BeiJin ", true, new[] { "add", "minus" }, null),
            ("system", "clock timezone BeiJin add ", true, new[] { "<HH:MM:SS>" }, null),
            ("system", "clock datetime ", true, new[] { "<HH:MM:SS>" }, null),
            ("system", "user-interface ", true, new[] { "INTEGER<0,34-48,50-54>", "console", "current", "maximum-vty", "vty" }, null),
            ("system", "user-interface vty ", true, new[] { "INTEGER<0-20>" }, null),
            ("system", "snmp-agent ", true, new[] { "community", "extend", "group", "inform", "local-engineid", "manner", "mib-view", "notification-log", "notify-filter-profile", "packet", "packet-priority", "set-cache", "usm-user", "sys-info", "target-host", "trap" }, null),
            ("system", "snmp-agent community ", true, new[] { "read", "write" }, null),
            ("system", "snmp-agent community read ", true, new[] { "cipher", "simple", "STRING<1-32>" }, null),
            ("system", "snmp-agent sys-info ", true, new[] { "contact", "location", "version" }, null),
            ("system", "snmp-agent sys-info version ", true, new[] { "v1", "v2c", "v3", "all" }, null),
            ("system", "snmp-agent target-host ", true, new[] { "inform", "trap" }, null),
            ("system", "snmp-agent target-host trap address udp-domain ", true, new[] { "<X.X.X.X>" }, null),
            ("system", "snmp-agent trap ", true, new[] { "disable", "enable", "life", "queue-size", "source", "suppress", "type" }, null),
            ("system", "port-group ", true, new[] { "STRING<1-32>" }, null),
            ("system", "info-center ", true, new[] { "channel", "console", "enable", "filter-id", "local", "logbuffer", "loghost", "monitor", "rate-limit", "snmp", "source", "statistic-suppress", "timestamp", "trapbuffer" }, null),
            ("system", "info-center loghost ", true, new[] { "<X.X.X.X>", "ipv6", "source" }, null),
            ("system", "aaa ", true, new[] { "abnormal-offline-record", "offline-record", "online-fail-record", "<cr>" }, null),
            ("system", "rsa ", true, new[] { "local-key-pair", "peer-public-key" }, null),
            // ---- ACL 视图 ----
            ("acl", "rule ", true, new[] { "permit", "deny", "INTEGER<0-4294967294>" }, null),
            ("acl", "rule 5 ", true, new[] { "permit", "deny", "description" }, null),
            ("acl", "rule 5 deny tcp destination-port ", true, new[] { "eq", "gt", "lt", "range" }, null),
            ("acl", "rule 5 deny tcp destination-port eq ", true, new[] { "INTEGER<0-65535>", "135", "445", "3389" }, null),
            ("acl", "rule 5 deny tcp destination-port range ", true, new[] { "INTEGER<0-65535>", "137", "139" }, null),
            ("acl", "rule 5 permit source 192.168.12.15 ", true, new[] { "0", "X.X.X.X" }, null),
            ("acl", "undo ", true, new[] { "rule", "description" }, null),
            ("acl", "undo rule ", true, new[] { "INTEGER<1-65535>" }, null),
            // ---- AAA 视图 ----
            ("aaa", "local-user ", true, new[] { "STRING<1-64>", "opsadmin" }, null),
            ("aaa", "local-user opsadmin ", true, new[] { "access-limit", "ftp-directory", "idle-timeout", "password", "privilege", "service-type", "state" }, null),
            ("aaa", "local-user opsadmin password ", true, new[] { "cipher", "irreversible-cipher", "simple" }, null),
            ("aaa", "local-user opsadmin password irreversible-cipher ", true, new[] { "STRING<8-64>" }, null),
            ("aaa", "local-user opsadmin privilege level ", true, new[] { "INTEGER<0-15>" }, null),
            ("aaa", "local-user opsadmin service-type ", true, new[] { "8021x", "bind", "ftp", "http", "ppp", "ssh", "telnet", "terminal", "web", "x25-pad" }, null),
            ("aaa", "local-aaa-user ", true, new[] { "wrong-password", "password" }, null),
            ("aaa", "local-aaa-user wrong-password ", true, new[] { "retry-interval" }, null),
            ("aaa", "local-aaa-user wrong-password retry-interval ", true, new[] { "INTEGER<1-60>" }, null),
            ("aaa", "undo ", true, new[] { "local-user", "user-password", "local-aaa-user" }, null),
            ("aaa", "undo local-user ", true, new[] { "opsadmin" }, null),
            // ---- 接口视图 ----
            ("iface", "port ", true, new[] { "link-type", "add-tag", "discard", "hybrid", "link-flap", "mux-vlan", "priority", "type", "vlan-mapping", "vlan-stacking", "default", "trunk" }, null),
            ("iface", "port link-type ", true, new[] { "access", "dot1q-tunnel", "hybrid", "trunk" }, null),
            ("iface", "port default vlan ", true, new[] { "INTEGER<1-4094>", "1", "8" }, null),
            ("iface", "description ", true, new[] { "TEXT" }, null),
            ("iface", "ip ", true, new[] { "address", "source" }, null),
            ("iface", "port hybrid ", false, Array.Empty<string>(), null),
            ("vlan", "description ", true, new[] { "TEXT" }, null),
            ("acl", "description ", true, new[] { "TEXT" }, null),
            ("acl", "rule 5 deny tcp ", true, new[] { "destination", "destination-port", "dscp", "fragment", "logging", "precedence", "source", "source-port", "tcp-flag", "time-range", "tos" }, null),
            ("acl", "rule 5 permit source ", true, new[] { "<X.X.X.X>" }, null),
            ("acl", "rule 5 deny fragment ", true, new[] { "<cr>" }, null),
            ("ui", "acl ipv6 ", true, new[] { "INTEGER<2000-3999>" }, null),
            ("system", "snmp-agent extend ", false, Array.Empty<string>(), null),
            ("iface", "port trunk allow-pass vlan ", true, new[] { "INTEGER<1-4094>", "8" }, null),
            ("iface", "traffic-filter ", true, new[] { "inbound", "outbound" }, null),
            ("iface", "traffic-filter inbound ", true, new[] { "acl" }, null),
            ("iface", "traffic-filter inbound acl ", true, new[] { "INTEGER<2000-3999>", "2000", "3000" }, null),
            ("iface", "ip address ", true, new[] { "<X.X.X.X>" }, null),
            ("iface", "undo ", true, new[] { "shutdown", "port", "traffic-filter", "description", "ip" }, null),
            // ---- user-interface 视图 ----
            ("ui", "authentication-mode ", true, new[] { "aaa", "password", "none" }, null),
            ("ui", "acl ", true, new[] { "INTEGER<2000-3999>", "2000", "ipv6" }, null),
            ("ui", "protocol inbound ", true, new[] { "all", "ssh", "telnet" }, null),
            ("ui", "idle-timeout ", true, new[] { "INTEGER<0-35791>" }, null),
            // ---- 本地管理员密码策略视图 ----
            ("lupp", "password ", true, new[] { "expire", "history" }, null),
            ("lupp", "password history record number ", true, new[] { "INTEGER<0-5>" }, null),
        };
        var failed = 0;
        foreach (var tc in cases)
        {
            var words = CompleteWords(s, tc.View, tc.Text, tc.Help);
            var lacks = tc.Expect.Where(e => !ContainsWord(words, e)).ToList();
            var bad = lacks.Count > 0;
            if (!bad && tc.Absent != null)
            {
                if (tc.Absent.Length == 1 && tc.Absent[0] == "*") bad = words.Count > 0;
                else bad = tc.Absent.Any(a => ContainsWord(words, a));
            }
            if (!bad) continue;
            failed++;
            Console.WriteLine($"  COMPLETION FAIL: [{tc.View}] \"{tc.Text}\" ({(tc.Help ? "?" : "Tab")})");
            if (lacks.Count > 0) Console.WriteLine("    missing: " + string.Join(", ", lacks));
            Console.WriteLine("    got: " + string.Join(" | ", words.Take(14)));
        }
        Console.WriteLine(failed == 0
            ? $"COMPLETION SNAPSHOT OK: {cases.Length}/{cases.Length} cases"
            : $"COMPLETION SNAPSHOT: {cases.Length - failed}/{cases.Length} cases passed");
        return failed;
    }

    static List<string> CompleteWords(Session s, string view, string text, bool help)
    {
        var toks = Tokenize(text);
        List<string> before;
        string partial;
        if (text.Length == 0 || text[^1] == ' ')
        {
            before = toks;
            partial = "";
        }
        else if (toks.Count == 0) { before = new List<string>(); partial = ""; }
        else { before = toks.Take(toks.Count - 1).ToList(); partial = toks[^1]; }
        return Cli.Complete(s.Dev, CommandSets.For(view), before, partial, help).Select(c => c.Word).ToList();
    }

    static bool ContainsWord(List<string> words, string expect) =>
        expect == "<cr>" ? words.Any(w => w.Length == 0)
                         : words.Any(w => string.Equals(w, expect, StringComparison.OrdinalIgnoreCase));

    // interface 候选顺序/纯净度回归（用户真机反馈：只出类型 + range，不出完整接口名）
    static int CompletionOrderSelfTest(Session s)
    {
        var cases = new (string View, string Text, bool Help, string[] Order, string[] Absent, int Count)[]
        {
            ("system", "interface ", true, new[] { "range", "Eth-Trunk", "Ethernet", "GigabitEthernet", "LoopBack", "NULL", "Tunnel", "Vlanif" },
                new[] { "Ethernet0/0/1", "GigabitEthernet0/0/1", "Vlanif1", "NULL0", "<interface-type>", "<interface>" }, 8),
            ("system", "interface range ", true, new[] { "Ethernet", "GigabitEthernet", "Vlanif" },
                new[] { "Ethernet0/0/1", "GigabitEthernet0/0/1", "Vlanif1", "NULL0", "<interface-type>", "<interface>" }, 3),
            ("system", "interface ethernet", false, new[] { "Ethernet" }, new[] { "Ethernet0/0/1" }, 1),
            ("system", "vlan ", true, new[] { "batch", "INTEGER<1-4094>" }, new[] { "1", "8" }, 2),
        };
        var failed = 0;
        foreach (var tc in cases)
        {
            var words = CompleteWords(s, tc.View, tc.Text, tc.Help);
            var bad = words.Count != tc.Count;
            var detail = $"count={words.Count} want={tc.Count}";
            if (!bad)
                for (var i = 0; i < tc.Order.Length; i++)
                    if (!string.Equals(words[i], tc.Order[i], StringComparison.OrdinalIgnoreCase))
                    {
                        bad = true;
                        detail = $"pos{i}: got [{words[i]}] want [{tc.Order[i]}]";
                        break;
                    }
            if (!bad)
                foreach (var a in tc.Absent)
                    if (ContainsWord(words, a)) { bad = true; detail = $"unexpected candidate: {a}"; break; }
            if (!bad) continue;
            failed++;
            Console.WriteLine($"  COMPLETION ORDER FAIL: [{tc.View}] \"{tc.Text}\" -> {detail}");
            Console.WriteLine("    got: " + string.Join(" | ", words));
        }
        Console.WriteLine(failed == 0
            ? $"COMPLETION ORDER OK: {cases.Length}/{cases.Length} cases"
            : $"COMPLETION ORDER: {cases.Length - failed}/{cases.Length} cases passed");
        return failed;
    }

    // interface range 大小写混用执行回归（用户真机反馈：合法写法必须能进 port-group、配置按规范拼写渲染）
    static int RangeExecSelfTest()
    {
        var cases = new (string Label, string[] Lines, bool WantError, string First, string Last, int Count)[]
        {
            ("lower-first", new[] { "system-view", "interface range ethernet 0/0/1 to Ethernet 0/0/16", "shutdown" }, false, "Ethernet0/0/1", "Ethernet0/0/16", 16),
            ("lower-last", new[] { "system-view", "interface range Ethernet 0/0/2 to ethernet 0/0/3", "shutdown" }, false, "Ethernet0/0/2", "Ethernet0/0/3", 2),
            ("concat-range", new[] { "system-view", "interface range Ethernet0/0/4 to Ethernet0/0/6" }, true, "", "", 0),
            ("type-mismatch", new[] { "system-view", "interface range Ethernet 0/0/1 to GigabitEthernet 0/0/2" }, true, "", "", 0),
            ("number-out-of-range", new[] { "system-view", "interface range Ethernet 0/0/1 to Ethernet 0/0/99" }, true, "", "", 0),
            ("concat-single", new[] { "system-view", "interface Ethernet0/0/1" }, true, "", "", 0),
        };
        var failed = 0;
        foreach (var tc in cases)
        {
            var dev = Device.Factory();
            var sess = new Session { Dev = dev };
            var last = new ExecResult();
            foreach (var line in tc.Lines)
            {
                last = Cli.Execute(dev, sess, sess.Commands(), Tokenize(line));
                if (last.Error != null) break;
            }
            var gotErr = last.Error != null || last.Lines.Any(l => l.StartsWith("Error:", StringComparison.Ordinal));
            var ok = gotErr == tc.WantError;
            var detail = "";
            if (ok && !tc.WantError)
            {
                var names = sess.RangeIfaces;
                ok = names.Count == tc.Count && names[0] == tc.First && names[^1] == tc.Last
                     && sess.View == "range" && sess.Prompt() == "[HUAWEI-port-group]"
                     && names.All(n => dev.Ifaces.TryGetValue(n, out var i) && i.Shutdown);
                if (!ok) detail = $"count={names.Count} first={(names.Count > 0 ? names[0] : "-")} last={(names.Count > 0 ? names[^1] : "-")} view={sess.View} prompt={sess.Prompt()}";
                var cfg = dev.RenderConfig();
                if (ok && (!cfg.Contains("interface " + tc.First) || cfg.Any(l => l.StartsWith("ethernet0/0", StringComparison.Ordinal))))
                {
                    ok = false;
                    detail = "config render not canonical";
                }
            }
            if (ok) continue;
            failed++;
            Console.WriteLine($"  RANGE CASE FAIL: {tc.Label} -> {detail}");
        }
        Console.WriteLine(failed == 0
            ? $"RANGE MIXED-CASE OK: {cases.Length}/{cases.Length} cases"
            : $"RANGE MIXED-CASE: {cases.Length - failed}/{cases.Length} cases passed");
        return failed;
    }

    // interface 写法回归（用户真机反馈）：只接受"类型 编号"分开写；连写报 Wrong parameter 且不进视图
    static int IfaceFormSelfTest()
    {
        var cases = new (string Label, string[] Lines, bool WantError, string View, string Prompt)[]
        {
            ("spaced-eth", new[] { "system-view", "interface Ethernet 0/0/1" }, false, "iface", "[HUAWEI-Ethernet0/0/1]"),
            ("spaced-ge", new[] { "system-view", "interface GigabitEthernet 0/0/1" }, false, "iface", "[HUAWEI-GigabitEthernet0/0/1]"),
            ("spaced-vlanif", new[] { "system-view", "vlan batch 8", "interface Vlanif 8" }, false, "iface", "[HUAWEI-Vlanif8]"),
            ("concat-eth", new[] { "system-view", "interface Ethernet0/0/1" }, true, "system", "[HUAWEI]"),
            ("concat-ge", new[] { "system-view", "interface GigabitEthernet0/0/1" }, true, "system", "[HUAWEI]"),
            ("concat-vlanif", new[] { "system-view", "interface Vlanif8" }, true, "system", "[HUAWEI]"),
        };
        var failed = 0;
        foreach (var tc in cases)
        {
            var dev = Device.Factory();
            var sess = new Session { Dev = dev };
            var last = new ExecResult();
            foreach (var line in tc.Lines)
            {
                last = Cli.Execute(dev, sess, sess.Commands(), Tokenize(line));
                if (last.Error != null) break;
            }
            var gotErr = last.Error != null || last.Lines.Any(l => l.StartsWith("Error:", StringComparison.Ordinal));
            var ok = gotErr == tc.WantError && sess.View == tc.View && sess.Prompt() == tc.Prompt;
            var detail = $"err={gotErr} view={sess.View} prompt={sess.Prompt()}";
            if (ok && tc.WantError)
            {
                // 连写报错：^ 指向该 token 起点（[HUAWEI] + "interface " = 18 列）
                var caret = new string(' ', 18) + "^";
                ok = last.Lines.Contains(caret) && last.Lines.Any(l => l.Contains("Wrong parameter found at '^' position."));
                detail += ok ? "" : " caret/error 文案不符";
            }
            if (ok) continue;
            failed++;
            Console.WriteLine($"  IFACE FORM FAIL: {tc.Label} -> {detail}");
            if (last.Lines.Length > 0) Console.WriteLine("    lines: " + string.Join(" / ", last.Lines.Take(4)));
        }
        Console.WriteLine(failed == 0
            ? $"IFACE FORM OK: {cases.Length}/{cases.Length} cases"
            : $"IFACE FORM: {cases.Length - failed}/{cases.Length} cases passed");
        return failed;
    }

    // NULL 接口写法回归（eNSP/真机：候选是 NULL；interface NULL 0 与 interface NULL0 都接受）
    static int NullIfaceSelfTest()
    {
        var cases = new (string Label, string[] Lines)[]
        {
            ("NULL 0", new[] { "system-view", "interface NULL 0" }),
            ("NULL0", new[] { "system-view", "interface NULL0" }),
        };
        var failed = 0;
        foreach (var tc in cases)
        {
            var dev = Device.Factory();
            var sess = new Session { Dev = dev };
            var last = new ExecResult();
            foreach (var line in tc.Lines)
            {
                last = Cli.Execute(dev, sess, sess.Commands(), Tokenize(line));
                if (last.Error != null) break;
            }
            var ok = last.Error == null && sess.View == "iface" && sess.IfName == "NULL0"
                     && sess.Prompt() == "[HUAWEI-NULL0]";
            var thisRes = Cli.Execute(dev, sess, sess.Commands(), Tokenize("display this"));
            ok = ok && thisRes.Error == null && thisRes.Lines.SequenceEqual(new[] { "#", "interface NULL0", "#", "return" });
            // 接口块渲染（display this / display current-configuration 的块首行）必须是 interface NULL0
            ok = ok && dev.Ifaces.TryGetValue("NULL0", out var nullIface)
                     && dev.RenderIface(nullIface).FirstOrDefault() == "interface NULL0";
            if (ok) continue;
            failed++;
            Console.WriteLine($"  NULL IFACE FAIL: {tc.Label}");
        }
        {
            // 非法编号：interface NULL 1 必须报错且留在系统视图
            var dev = Device.Factory();
            var sess = new Session { Dev = dev };
            Cli.Execute(dev, sess, sess.Commands(), Tokenize("system-view"));
            var r = Cli.Execute(dev, sess, sess.Commands(), Tokenize("interface NULL 1"));
            var ok = r.Error != null || r.Lines.Any(l => l.StartsWith("Error:", StringComparison.Ordinal));
            ok = ok && sess.View == "system";
            if (ok) failed += 0;
            else
            {
                failed++;
                Console.WriteLine("  NULL IFACE FAIL: 非法编号 NULL 1 未报错或进了视图");
            }
        }
        Console.WriteLine(failed == 0
            ? "NULL IFACE OK: 3/3 cases"
            : $"NULL IFACE: {3 - failed}/3 cases passed");
        return failed;
    }

    // vlan <id> / VLAN 视图回归（用户真机反馈）
    static int VlanExecSelfTest()
    {
        var dev = Device.Factory();
        var sess = new Session { Dev = dev };
        var failed = 0;
        var checks = new List<(string Label, bool Ok)>();
        void Step(params string[] lines)
        {
            foreach (var line in lines)
            {
                var r = Cli.Execute(dev, sess, sess.Commands(), Tokenize(line));
                if (r.Error != null) { failed++; Console.WriteLine($"  VLAN CASE FAIL: [{line}] -> {r.Error}"); return; }
            }
        }
        Step("system-view");
        var before = dev.Vlans.Count;
        Step("vlan 200");
        checks.Add(("vlan 200 进视图", sess.View == "vlan" && sess.VlanId == 200 && sess.Prompt() == "[HUAWEI-vlan200]"));
        checks.Add(("vlan 200 已创建", dev.Vlans.Contains(200) && dev.Vlans.Count == before + 1));
        Step("description GuanLi-Vlan", "name Manage");
        checks.Add(("description/name 生效", dev.VlanCfgs.TryGetValue(200, out var c200) && c200.Description == "GuanLi-Vlan" && c200.Name == "Manage"));
        var thisRes = Cli.Execute(dev, sess, sess.Commands(), Tokenize("display this"));
        var expectThis = new[] { "#", "vlan 200", " description GuanLi-Vlan", " name Manage", "#", "return" };
        checks.Add(("display this 格式", thisRes.Error == null && thisRes.Lines.SequenceEqual(expectThis)));
        Step("quit");
        checks.Add(("quit 回系统视图", sess.View == "system"));
        var cfg = dev.RenderConfig();
        checks.Add(("配置渲染 vlan 块", cfg.Contains("vlan 200") && cfg.Contains(" description GuanLi-Vlan") && cfg.Contains(" name Manage")));
        checks.Add(("vlan batch 不含 200", !cfg.Any(l => l.StartsWith("vlan batch") && l.Split(' ').Contains("200"))));
        Step("vlan batch 201 202 203");
        cfg = dev.RenderConfig();
        checks.Add(("无描述 VLAN 合并/压缩", cfg.Any(l => l.StartsWith("vlan batch") && l.Contains("201 to 203"))));
        var cnt = dev.Vlans.Count;
        Step("vlan 201");
        checks.Add(("已存在 VLAN 直接进视图", sess.View == "vlan" && sess.VlanId == 201 && dev.Vlans.Count == cnt));
        Step("quit");
        var vlanRes = Cli.Execute(dev, sess, sess.Commands(), Tokenize("display vlan"));
        checks.Add(("display vlan 计数", vlanRes.Lines.Length > 0 && vlanRes.Lines[0].Contains("The total number of vlans is " + dev.Vlans.Count)));
        Step("undo vlan 200");
        cfg = dev.RenderConfig();
        checks.Add(("undo vlan 200 移除", !dev.Vlans.Contains(200) && !cfg.Contains("vlan 200") && !cfg.Any(l => l.StartsWith("vlan batch") && l.Split(' ').Contains("200"))));
        var e1 = Cli.Execute(dev, sess, sess.Commands(), Tokenize("undo vlan 999"));
        checks.Add(("undo 不存在 VLAN 报错", e1.Lines.Any(l => l.StartsWith("Error:", StringComparison.Ordinal) && l.Contains("does not exist"))));
        var e2 = Cli.Execute(dev, sess, sess.Commands(), Tokenize("undo vlan 1"));
        checks.Add(("VLAN 1 不可删", e2.Lines.Any(l => l.StartsWith("Error:", StringComparison.Ordinal) && l.Contains("default VLAN"))));
        Step("vlan 300");
        checks.Add(("vlan 300 prompt", sess.Prompt() == "[HUAWEI-vlan300]"));
        var t3 = Cli.Execute(dev, sess, sess.Commands(), Tokenize("display this"));
        checks.Add(("空 VLAN display this 只有 #/return", t3.Lines.SequenceEqual(new[] { "#", "return" })));
        Step("quit");
        Step("vlan 301", "description Only-Dsc");
        var t4 = Cli.Execute(dev, sess, sess.Commands(), Tokenize("display this"));
        checks.Add(("仅 description 的 VLAN block", t4.Lines.SequenceEqual(new[] { "#", "vlan 301", " description Only-Dsc", "#", "return" })));
        Step("quit");
        foreach (var (label, ok) in checks)
        {
            if (ok) continue;
            failed++;
            Console.WriteLine($"  VLAN CASE FAIL: {label}");
        }
        Console.WriteLine(failed == 0
            ? $"VLAN VIEW OK: {checks.Count}/{checks.Count} cases"
            : $"VLAN VIEW: {checks.Count - failed}/{checks.Count} cases passed");
        return failed;
    }

    // display current-configuration 全接口回归（16 百兆 + 12 千兆 + Vlanif + NULL0，空口也出块）
    static int ConfigIfaceSelfTest(Session s)
    {
        var cfg = s.Dev.RenderConfig();
        var expected = new List<string>();
        for (var i = 1; i <= 16; i++) expected.Add($"interface Ethernet0/0/{i}");
        for (var i = 1; i <= 12; i++) expected.Add($"interface GigabitEthernet0/0/{i}");
        expected.Add("interface Vlanif1");
        expected.Add("interface Vlanif8");   // 演练脚本里创建的
        expected.Add("interface NULL0");
        var failed = 0;
        void Check(string label, bool ok, string detail = "")
        {
            if (ok) return;
            failed++;
            Console.WriteLine($"  CONFIG IFACE FAIL: {label}" + (detail.Length > 0 ? " -> " + detail : ""));
        }
        var ifaceLines = cfg.Where(l => l.StartsWith("interface ", StringComparison.Ordinal)).ToList();
        Check("接口行/顺序", ifaceLines.SequenceEqual(expected),
            "count=" + ifaceLines.Count + " got=" + string.Join(",", ifaceLines.Take(40)));
        // 空口块格式：上一行 #、下一行 #（Vlanif1 在加固后仍为空口）
        var idx = cfg.IndexOf("interface Vlanif1");
        Check("空口块格式", idx > 0 && idx + 1 < cfg.Count && cfg[idx - 1] == "#" && cfg[idx + 1] == "#",
            idx < 0 ? "not found" : $"prev={cfg[idx - 1]} next={cfg[idx + 1]}");
        // | include interface 过滤（真机 include 是子串匹配，会连带 user-interface，只要接口行全在即可）
        var filtered = cfg.Where(l => l.Contains("interface", StringComparison.OrdinalIgnoreCase)).ToList();
        Check("include interface 过滤", expected.All(e => filtered.Contains(e)),
            "missing=" + string.Join(",", expected.Where(e => !filtered.Contains(e))));
        // 28 个物理口 + NULL0 都在
        Check("28 物理口齐全",
            cfg.Count(l => l.StartsWith("interface Ethernet0/0/", StringComparison.Ordinal)) == 16 &&
            cfg.Count(l => l.StartsWith("interface GigabitEthernet0/0/", StringComparison.Ordinal)) == 12 &&
            cfg.Contains("interface NULL0"));
        Console.WriteLine(failed == 0 ? "CONFIG IFACES OK: 4/4 cases" : $"CONFIG IFACES: {4 - failed}/4 cases passed");
        return failed;
    }

    // "看得到就能敲"：? 里出现的候选，敲下去不得报错（用户口径：不能 ? 有、一敲就报错）
    static int NoErrorSelfTest()
    {
        var cases = new (string Nav, string Cmd)[]
        {
            ("system-view", "aaa abnormal-offline-record"),
            ("system-view", "aaa offline-record"),
            ("system-view", "aaa online-fail-record"),
            ("system-view", "acl ipv6 3000"),
            ("system-view", "acl name test1"),
            ("system-view", "acl 4000"),
            ("system-view", "acl 5000"),
            ("system-view", "info-center channel 0 name chan0"),
            ("system-view", "info-center console"),
            ("system-view", "info-center filter-id 1"),
            ("system-view", "info-center local"),
            ("system-view", "info-center logbuffer"),
            ("system-view", "info-center monitor"),
            ("system-view", "info-center rate-limit 10"),
            ("system-view", "info-center snmp"),
            ("system-view", "info-center source"),
            ("system-view", "info-center statistic-suppress"),
            ("system-view", "info-center timestamp"),
            ("system-view", "info-center trapbuffer"),
            ("system-view", "info-center loghost ipv6 2001:db8::1"),
            ("system-view", "snmp-agent sys-info contact ops"),
            ("system-view", "snmp-agent sys-info location room1"),
            ("system-view", "snmp-agent target-host inform address udp-domain 192.168.12.1 params securityname cipher Public@123 v2c"),
            ("system-view", "snmp-agent trap disable"),
            ("system-view", "snmp-agent trap life 60"),
            ("system-view", "snmp-agent trap queue-size 100"),
            ("system-view", "snmp-agent trap source"),
            ("system-view", "snmp-agent trap suppress"),
            ("system-view", "snmp-agent trap type"),
            ("system-view", "snmp-agent extend packet-priority 1"),
            ("system-view", "snmp-agent group v3 g1 privacy"),
            ("system-view", "snmp-agent inform address udp-domain 192.168.12.1"),
            ("system-view", "snmp-agent local-engineid 800007DB03000000000000"),
            ("system-view", "snmp-agent manner alarm"),
            ("system-view", "snmp-agent mib-view view1 include 1.3.6.1"),
            ("system-view", "snmp-agent notification-log"),
            ("system-view", "snmp-agent notify-filter-profile p1"),
            ("system-view", "snmp-agent packet max-size 1200"),
            ("system-view", "snmp-agent set-cache enable"),
            ("system-view", "snmp-agent usm-user v3 u1 group g1"),
            ("system-view", "stelnet 192.168.12.1"),
            ("system-view", "rsa peer-public-key"),
            ("system-view", "user-interface 5"),
            ("system-view", "user-interface current"),
            ("system-view", "user-interface maximum-vty 10"),
            ("system-view", "interface Eth-Trunk 1"),
            ("system-view", "interface LoopBack 0"),
            ("system-view", "interface Tunnel 5"),
            ("system-view", "interface NULL 0"),
            ("system-view", "vlan 200"),
            ("system-view\naaa", "local-user u1 access-limit max-user-number 8"),
            ("system-view\naaa", "local-user u1 ftp-directory flash:"),
            ("system-view\naaa", "local-user u1 state block"),
            ("system-view\naaa", "local-user u1 password simple Str0ng@Pass1"),
            ("system-view\naaa\nlocal-user u1 password simple Str0ng@Pass1", "local-user u1 service-type ppp web bind"),
            ("system-view\ninterface GigabitEthernet 0/0/1", "port hybrid pvid vlan 8"),
            ("system-view\ninterface GigabitEthernet 0/0/1", "port discard tagged"),
            ("system-view\ninterface GigabitEthernet 0/0/1", "port type access"),
            ("system-view\ninterface GigabitEthernet 0/0/1", "port link-type dot1q-tunnel"),
            ("system-view\ninterface GigabitEthernet 0/0/1", "ip source 192.168.12.15 0.0.0.0"),
            ("system-view\ninterface GigabitEthernet 0/0/1", "description TEXT text"),
            ("system-view\nvlan 8", "description TEXT text"),
            ("system-view\nacl number 3000", "description TEXT text"),
            ("system-view\nacl number 3000", "rule 5 deny fragment"),
            ("system-view\nacl number 3000", "rule 5 deny logging"),
            ("system-view\nacl number 3000", "rule 5 deny time-range t1"),
            ("system-view\nacl number 3000", "rule 5 deny tcp destination 1.1.1.1 0.0.0.0"),
            ("system-view\nacl number 3000", "rule 5 deny tcp source 1.1.1.1 0.0.0.0"),
            ("system-view\nacl number 3000", "rule 5 deny tcp source-port eq 80"),
            ("system-view\nacl number 3000", "rule 5 deny tcp dscp 46"),
            ("system-view\nacl number 3000", "rule 5 deny tcp precedence 5"),
            ("system-view\nacl number 3000", "rule 5 deny tcp tcp-flag syn ack"),
            ("system-view\nacl number 3000", "rule 5 deny tcp tos 0"),
            ("system-view\nacl number 3000", "rule 5 description rule-desc"),
            ("system-view\nacl number 2000", "rule 5 permit source any"),
            ("system-view\nuser-interface vty 0 4", "acl ipv6 3000"),
        };
        var failed = 0;
        foreach (var tc in cases)
        {
            var dev = Device.Factory();
            var sess = new Session { Dev = dev };
            var res = new ExecResult();
            foreach (var nav in tc.Nav.Split('\n'))
            {
                res = Cli.Execute(dev, sess, sess.Commands(), Tokenize(nav));
                if (res.Error != null) break;
            }
            if (res.Error == null) res = Cli.Execute(dev, sess, sess.Commands(), Tokenize(tc.Cmd));
            var bad = res.Error != null || res.Lines.Any(l => l.StartsWith("Error:", StringComparison.Ordinal));
            if (!bad) continue;
            failed++;
            var why = res.Error ?? res.Lines.FirstOrDefault(l => l.StartsWith("Error:", StringComparison.Ordinal)) ?? "";
            Console.WriteLine($"  NO-ERROR FAIL: {tc.Cmd} -> {why}");
        }
        Console.WriteLine(failed == 0
            ? $"NO-ERROR SMOKE OK: {cases.Length}/{cases.Length} cases"
            : $"NO-ERROR SMOKE: {cases.Length - failed}/{cases.Length} cases passed");
        return failed;
    }

    // 新接口类型最小行为：Eth-Trunk / LoopBack / Tunnel（能进视图、description/shutdown、进 dis cu、display this）
    static int NewIfaceSelfTest()
    {
        var cases = new (string Label, string[] Lines, string IfName, string Desc)[]
        {
            ("eth-trunk", new[] { "system-view", "interface Eth-Trunk 1", "description trunk1", "shutdown" }, "Eth-Trunk1", "trunk1"),
            ("loopback", new[] { "system-view", "interface LoopBack 0", "description lo0" }, "LoopBack0", "lo0"),
            ("tunnel", new[] { "system-view", "interface Tunnel 5", "description tun5" }, "Tunnel5", "tun5"),
        };
        var failed = 0;
        foreach (var tc in cases)
        {
            var dev = Device.Factory();
            var sess = new Session { Dev = dev };
            var last = new ExecResult();
            foreach (var line in tc.Lines) { last = Cli.Execute(dev, sess, sess.Commands(), Tokenize(line)); if (last.Error != null) break; }
            var ok = last.Error == null && sess.View == "iface" && sess.Prompt() == "[HUAWEI-" + tc.IfName + "]";
            var cfg = dev.RenderConfig();
            ok = ok && cfg.Contains("interface " + tc.IfName) && cfg.Contains(" description " + tc.Desc);
            var dt = Cli.Execute(dev, sess, sess.Commands(), Tokenize("display this"));
            ok = ok && dt.Error == null && dt.Lines.Contains("interface " + tc.IfName);
            if (ok) continue;
            failed++;
            Console.WriteLine($"  NEW IFACE FAIL: {tc.Label} -> err={last.Error} view={sess.View} prompt={sess.Prompt()} cfg={(cfg.Contains("interface " + tc.IfName))}");
        }
        // 非法编号必须报错
        var dev2 = Device.Factory();
        var sess2 = new Session { Dev = dev2 };
        Cli.Execute(dev2, sess2, sess2.Commands(), Tokenize("system-view"));
        var bad = Cli.Execute(dev2, sess2, sess2.Commands(), Tokenize("interface Eth-Trunk 200"));
        if (bad.Error == null && !bad.Lines.Any(l => l.StartsWith("Error:", StringComparison.Ordinal)))
        {
            failed++;
            Console.WriteLine("  NEW IFACE FAIL: Eth-Trunk 200 应当报错");
        }
        Console.WriteLine(failed == 0
            ? $"NEW IFACES OK: {cases.Length + 1}/{cases.Length + 1} cases"
            : $"NEW IFACES: {cases.Length + 1 - failed}/{cases.Length + 1} cases passed");
        return failed;
    }

    // `?` 两列排版回归（v1.5.0）：列宽 = 最长候选 + 2、说明对齐第二列、命令位置无 <cr>、超宽折行
    static int HelpFormatSelfTest()
    {
        var cases = new (string View, string Text, string Word, string Help)[]
        {
            ("system", "interface ", "Eth-Trunk", "Ethernet-Trunk interface"),
            ("system", "acl ", "number", "Specify a numbered ACL"),
            ("system", "acl ", "INTEGER<2000-2999>", "Basic access-list"),
            ("system", "info-center ", "channel", "Set the name of information channel"),
            ("system", "user-interface ", "INTEGER<0,34-48,50-54>", "The first user terminal interface"),
            ("system", "vlan ", "batch", "Batch process"),
            ("system", "snmp-agent trap ", "life", "Set the trap aging time"),
            ("aaa", "local-user u ", "password", "Set password"),
            ("acl", "rule 5 deny tcp destination-port ", "eq", "Equal to given port number"),
            ("iface", "port link-type ", "access", "Access port"),
            ("ui", "authentication-mode ", "aaa", "AAA authentication"),
            ("system", "aaa ", "<cr>", ""),
        };
        var failed = 0;
        var dev = Device.Factory();
        for (var ci = 0; ci < cases.Length; ci++)
        {
            var tc = cases[ci];
            // 与 PrintHelp 相同的拆分：命令前缀 + 最后一段 partial
            var before = tc.Text.Substring(0, tc.Text.Length - (tc.Text.EndsWith(" ") ? 0 : 1));
            var toks = Tokenize(before);
            List<string> prior; string partial;
            if (tc.Text.EndsWith(" ")) { prior = toks; partial = ""; }
            else { partial = toks.Count > 0 ? toks[^1] : ""; prior = toks.Take(Math.Max(0, toks.Count - 1)).ToList(); }
            var cands = Cli.Complete(dev, CommandSets.For(tc.View), prior, partial, true);
            var rows = cands.Where(c => c.Word.Length > 0).ToList();
            if (tc.Word == "<cr>")
            {
                // 命令位置可回车时，数据层有 <cr>，排版层必须剥掉
                var crLines = HelpFormat.Format(cands, 120);
                if (!cands.Any(c => c.Word.Length == 0) || crLines.Any(l => l.Contains("<cr>")))
                {
                    failed++;
                    Console.WriteLine($"  HELP FORMAT FAIL: [{tc.View}] '{tc.Text}' -> <cr> 处理不对");
                }
                continue;
            }
            var lines = HelpFormat.Format(cands, 120);
            var detail = "";
            if (rows.Count == 0) detail = "no candidates";
            else if (lines.Any(l => l.Contains("<cr>"))) detail = "<cr> 不应出现";
            else if (lines.Any(l => l.Length > 2 && l[0] == ' ' && l[1] == ' ' && l[2] == ' ')) detail = "首列前导空格不对";
            else
            {
                var firstCol = rows.Max(c => c.Word.Length) + 2;
                var col = 2 + firstCol;
                // 每行：无说明时整行等于 "  word"（不留尾空格）；有说明时说明从第二列开始
                for (var i = 0; i < rows.Count; i++)
                {
                    var c = rows[i];
                    var help = (c.Help ?? "").Trim();
                    if (help.Length == 0)
                    {
                        if (lines[i] != "  " + c.Word) { detail = $"空说明行有尾空格: [{lines[i]}]"; break; }
                        continue;
                    }
                    if (lines[i].IndexOf(help, StringComparison.Ordinal) != col)
                    {
                        detail = $"说明未对齐第二列({col}): [{lines[i]}] help=[{help}]";
                        break;
                    }
                }
                if (detail.Length == 0)
                {
                    var idx = rows.FindIndex(c => c.Word == tc.Word);
                    if (idx < 0) detail = "找不到候选 " + tc.Word;
                    else if (tc.Help.Length > 0 && !lines[idx].Contains(tc.Help)) detail = $"期望说明[{tc.Help}] 实际[{lines[idx]}]";
                }
            }
            if (detail.Length > 0)
            {
                failed++;
                Console.WriteLine($"  HELP FORMAT FAIL: [{tc.View}] '{tc.Text}' -> {detail}");
                continue;
            }
            // 窄缓冲区折行：续行缩进到第二列
            if (ci == 3)   // info-center：statistic-suppress 说明较长
            {
                var narrow = HelpFormat.Format(cands, 60);
                var firstCol = rows.Max(c => c.Word.Length) + 2;
                var col = 2 + firstCol;
                var wrappedIdx = rows.FindIndex(c => c.Word == "statistic-suppress");
                if (wrappedIdx < 0) { failed++; Console.WriteLine("  HELP FORMAT FAIL: info-center 缺 statistic-suppress"); }
                else
                {
                    var lineIdx = narrow.FindIndex(l => l.Contains("statistic-suppress"));
                    var cont = narrow.Skip(lineIdx + 1).Take(3).ToList();
                    if (lineIdx < 0 || cont.Count == 0 || !cont[0].StartsWith(new string(' ', col)))
                    {
                        failed++;
                        Console.WriteLine($"  HELP FORMAT FAIL: 折行续行未对齐第二列(col={col})");
                    }
                }
            }
        }
        Console.WriteLine(failed == 0
            ? $"HELP FORMAT OK: {cases.Length}/{cases.Length} cases"
            : $"HELP FORMAT: {cases.Length - failed}/{cases.Length} cases passed");
        return failed;
    }

    static List<string> HardeningScript()
    {
        var L = new List<string>
        {
            "system-view", "sysname SW-JF-01",
            "vlan batch 8",
            "interface Vlanif 8", "ip address 192.168.12.4 255.255.255.0", "description GuanLi-WangAn", "quit",
            "acl number 2000", "description Manager", "rule 5 permit source 192.168.12.15 0", "rule 10 deny", "quit",
            "acl number 2001", "description WangJian", "rule 5 permit source 192.168.12.1 0", "rule 10 deny", "quit",
            "acl number 3000", "description Normal",
            "rule 5 deny tcp destination-port eq 135",
            "rule 10 deny tcp destination-port range 137 139",
            "rule 15 deny tcp destination-port eq 445",
            "rule 20 deny tcp destination-port eq 3389",
            "rule 25 deny udp destination-port range 137 139",
            "rule 30 deny udp destination-port eq 135",
            "rule 35 deny udp destination-port eq 445",
            "rule 40 deny udp destination-port eq 3389",
            "rule 45 permit ip",
            "quit",
            "acl 3999",
            "rule permit ip",
            "rule deny tcp destination-port eq 445",
            "quit",
            "interface GigabitEthernet 0/0/1",
            "port link-type access", "port default vlan 8",
            "traffic-filter inbound acl 3000", "traffic-filter outbound acl 3000",
            "undo shutdown", "quit",
            "interface GigabitEthernet 0/0/12",
            "port link-type trunk", "port trunk allow-pass vlan 8",
            "traffic-filter inbound acl 3000", "traffic-filter outbound acl 3000",
            "undo shutdown", "quit"
        };
        for (var i = 2; i <= 11; i++) { L.Add($"interface GigabitEthernet 0/0/{i}"); L.Add("shutdown"); L.Add("quit"); }
        for (var i = 1; i <= 16; i++) { L.Add($"interface Ethernet 0/0/{i}"); L.Add("shutdown"); L.Add("quit"); }
        L.AddRange(new[]
        {
            "aaa",
            "local-aaa-user wrong-password retry-interval 5 retry-time 5 block-time 10",
            "user-password complexity-check",
            "local-aaa-user password policy administrator",
            "quit",
            "local-user opsadmin password irreversible-cipher Str0ng@Pass1",
            "local-user opsadmin privilege level 15",
            "local-user opsadmin service-type terminal ssh",
            "undo local-user admin",
            "quit",
            "user-interface con 0", "authentication-mode aaa", "idle-timeout 5 0", "quit",
            "user-interface vty 0 4", "acl 2000 inbound", "authentication-mode aaa", "protocol inbound ssh", "idle-timeout 5 0", "quit",
            "user-interface vty 16 20", "acl 2000 inbound", "authentication-mode aaa", "protocol inbound ssh", "idle-timeout 5 0", "quit",
            "snmp-agent community read cipher Public@123 acl 2001",
            "snmp-agent sys-info version v2c v3",
            "snmp-agent target-host trap address udp-domain 192.168.12.1 params securityname cipher Public@123 v2c",
            "snmp-agent trap enable",
            "info-center loghost 192.168.12.1",
            "undo telnet server enable",
            "undo ftp server",
            "undo http server enable",
            "stelnet server enable",
            "clock timezone BeiJin add 08:00:00",
            "clock datetime 14:30:00 2026-11-14",
            "return"
        });
        return L;
    }
}
