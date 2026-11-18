using Microsoft.JSInterop;
using VrpSim;

namespace VrpSim.Web;

// 网页终端引擎：复用控制台版的命令树/补全/渲染（Cli/Commands/Device/Checks/RunTimer），
// 只把 Console/File 换成 JS 回调与内存存档。
public sealed class EngineService
{
    private readonly Device _dev;
    private readonly Session _sess;
    private readonly List<string> _pending = new();
    private IReadOnlyList<string> _saved = Array.Empty<string>();
    private bool _exited;

    public EngineService()
    {
        _dev = Device.Factory();
        _sess = new Session { Dev = _dev, Interactive = false };
        _sess.Timer.MarkStart(RunTimer.LocalIsoNow());
        Platform.Width = 100;
        Platform.WriteLine = s => _pending.Add(s);
        Platform.WriteSavedConfig = lines => _saved = lines.ToList();
        Platform.ReadSavedConfig = () => _saved;
    }

    public sealed class InitResult
    {
        public string[] Banner { get; set; } = Array.Empty<string>();
        public string Prompt { get; set; } = "";
    }

    public sealed class SubmitResult
    {
        public string[] Lines { get; set; } = Array.Empty<string>();
        public string Prompt { get; set; } = "";
        public bool Exit { get; set; }
    }

    public sealed class CompleteResult
    {
        public string[] Lines { get; set; } = Array.Empty<string>();
        public string Insert { get; set; } = "";
        public bool HasInsert { get; set; }
    }

    [JSInvokable]
    public InitResult Init()
    {
        var banner = new List<string>
        {
            "",
            "  ============================================================",
            "   Huawei VRP 交换机模拟终端  (V200R011C10SPC600, 网页训练版)",
            "  ============================================================",
            "   Tab 补全    ? 帮助    check 加固自查    quit 退出",
            "   视图: <HUAWEI> 用户视图 -> system-view -> [HUAWEI] 系统视图",
            "   Console 登录: 默认用户 admin / admin@huawei.com",
            "  ============================================================",
            "",
            "Login authentication",
            "",
            "Username:admin",
            "Password:",
            "",
            "  （网页版：已自动登录；存档存在当前页面内存，刷新即重置）",
            ""
        };
        return new InitResult { Banner = banner.ToArray(), Prompt = _sess.Prompt() };
    }

    [JSInvokable]
    public SubmitResult Submit(string line)
    {
        _pending.Clear();
        var prompt = _sess.Prompt();
        var result = new SubmitResult();
        if (_exited || string.IsNullOrWhiteSpace(line))
        {
            result.Prompt = _sess.Prompt();
            return result;
        }

        var tokens = Platform.Tokenize(line);

        // display ... | include / begin / exclude（与 console 批处理同口径）
        var pipe = tokens.IndexOf("|");
        if (pipe > 0)
        {
            var main = tokens.Take(pipe).ToList();
            var mode = tokens.Count > pipe + 1 ? tokens[pipe + 1].ToLowerInvariant() : "include";
            var pat = tokens.Count > pipe + 2 ? tokens[pipe + 2].ToLowerInvariant() : "";
            var r0 = Cli.Execute(_dev, _sess, _sess.Commands(), main);
            var lines = new List<string>(_pending);
            if (r0.Error != null)
            {
                var o0 = Platform.TokenOffset(main, r0.ErrTok);
                lines.Add(new string(' ', prompt.Length + o0) + "^");
                lines.Add("Error:" + r0.Error + " found at '^' position.");
            }
            else
            {
                IEnumerable<string> src = r0.Lines;
                if (mode == "exclude") src = src.Where(x => !x.ToLowerInvariant().Contains(pat));
                else if (mode == "begin")
                {
                    var arr = r0.Lines.ToList();
                    var i0 = arr.FindIndex(x => x.ToLowerInvariant().Contains(pat));
                    src = i0 >= 0 ? arr.Skip(i0) : Enumerable.Empty<string>();
                }
                else src = src.Where(x => x.ToLowerInvariant().Contains(pat));
                lines.AddRange(src);
            }
            result.Lines = lines.ToArray();
            result.Prompt = _sess.Prompt();
            return result;
        }

        var res = Cli.Execute(_dev, _sess, _sess.Commands(), tokens);
        var outLines = new List<string>(_pending);
        if (res.Error == null)
        {
            outLines.AddRange(res.Lines);
        }
        else
        {
            var off = Platform.TokenOffset(tokens, res.ErrTok);
            outLines.Add(new string(' ', prompt.Length + off) + "^");
            outLines.Add("Error:" + res.Error + " found at '^' position.");
        }

        if (_sess.ExitRequested)
        {
            _exited = true;
            outLines.Add("");
            outLines.Add("  User interface con0 is available.");
            outLines.Add("  （已退出，刷新页面可重新开始）");
        }

        result.Lines = outLines.ToArray();
        result.Prompt = _sess.Prompt();
        result.Exit = _exited;
        return result;
    }

    [JSInvokable]
    public CompleteResult Complete(string line, bool help)
    {
        var text = line ?? "";
        var start = 0;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] == ' ') { start = i + 1; break; }
        }
        var beforeText = text.Substring(0, start);
        var partial = text.Substring(start);
        var before = Platform.Tokenize(beforeText);
        var cands = Cli.Complete(_dev, _sess.Commands(), before, partial, help).ToList();
        var r = new CompleteResult();
        if (cands.Count == 0)
        {
            r.Lines = new[] { "  (no match)" };
            return r;
        }
        if (!help && cands.Count == 1 && cands[0].Word.Length > 0)
        {
            r.Insert = beforeText + cands[0].Word + " ";
            r.HasInsert = true;
            return r;
        }
        r.Lines = HelpFormat.Format(cands, 100).ToArray();
        return r;
    }
}
