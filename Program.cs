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
        if (cands.Count == 0) { Console.WriteLine("  (no match)"); return; }
        var w = Math.Max(4, cands.Max(c => c.Word.Length));
        foreach (var c in cands)
            Console.WriteLine("  " + (c.Word.Length == 0 ? "<cr>" : c.Word).PadRight(w + 2) + c.Help);
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
        Console.WriteLine(fails.Count == 0 ? "SELFTEST OK" : $"SELFTEST: {fails.Count} item(s) failed");
        return fails.Count == 0 ? 0 : 1;
    }

    static List<string> HardeningScript()
    {
        var L = new List<string>
        {
            "system-view", "sysname SW-JF-01",
            "vlan batch 8",
            "interface Vlanif8", "ip address 192.168.12.4 255.255.255.0", "description GuanLi-WangAn", "quit",
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
            "interface GigabitEthernet0/0/1",
            "port link-type access", "port default vlan 8",
            "traffic-filter inbound acl 3000", "traffic-filter outbound acl 3000",
            "undo shutdown", "quit",
            "interface GigabitEthernet0/0/12",
            "port link-type trunk", "port trunk allow-pass vlan 8",
            "traffic-filter inbound acl 3000", "traffic-filter outbound acl 3000",
            "undo shutdown", "quit"
        };
        for (var i = 2; i <= 11; i++) { L.Add($"interface GigabitEthernet0/0/{i}"); L.Add("shutdown"); L.Add("quit"); }
        for (var i = 1; i <= 16; i++) { L.Add($"interface Ethernet0/0/{i}"); L.Add("shutdown"); L.Add("quit"); }
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
