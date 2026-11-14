namespace VrpSim;

public interface Tok { }

// Lit/Val 用可变类：帮助文案由 CommandSets 的静态构造按视图统一补全（填 Lit.Help / Val.HintHelp）
public sealed class Lit : Tok
{
    public string W;
    public string Help;
    public Lit(string w, string help = "") { W = w; Help = help; }
}

public sealed class Val : Tok
{
    public string Name;
    public string Hint;
    public Func<Device, string, IEnumerable<Candidate>>? Sug;
    public Func<string, bool>? Accept;
    public Func<Device, string, IReadOnlyDictionary<string, string>, IEnumerable<Candidate>>? CSug;
    public bool NoHint;
    public string HintHelp = "";

    public Val(string name, string hint, Func<Device, string, IEnumerable<Candidate>>? sug = null, Func<string, bool>? accept = null,
        Func<Device, string, IReadOnlyDictionary<string, string>, IEnumerable<Candidate>>? csug = null, bool noHint = false)
    {
        Name = name; Hint = hint; Sug = sug; Accept = accept; CSug = csug; NoHint = noHint;
    }
}
public sealed record Opt(params Tok[] Inner) : Tok;
public sealed record Rep(Tok Inner, int Max = 64) : Tok;
public sealed record Candidate(string Word, string Help);

public sealed class Command
{
    public string Kw;
    public string Help;
    public List<List<Tok>> Templates = new();
    public Func<ExecCtx, string[]>? Run;
    public Command(string kw, string help) { Kw = kw; Help = help; }
    public Command T(params Tok[] ts) { Templates.Add(ts.ToList()); return this; }
}

public sealed class ExecCtx
{
    public Device Dev = null!;
    public Session S = null!;
    public List<string> Tokens = new();
    public Dictionary<string, string> Caps = new();
    public string Cap(string n) => Caps.TryGetValue(n, out var v) ? v : "";
    public bool Has(string n) => Caps.ContainsKey(n);
}

public sealed class State
{
    public List<Tok> Seq = new();
    public List<List<Tok>> Stack = new();
    public Dictionary<string, string> Caps = new();
    public int Pos = 1;
    public Dictionary<int, string> Lits = new();
}

public sealed class ExecResult
{
    public string[] Lines = Array.Empty<string>();
    public string? Error;
    public int ErrTok;
}

public static class Cli
{
    public static List<State> Start(List<Tok> tpl) => new()
    {
        new State { Seq = new List<Tok>(tpl), Stack = new(), Caps = new(), Pos = 1 }
    };

    public static List<State> Advance(Device dev, List<State> states, string tok)
    {
        var outp = new List<State>();
        foreach (var s in states) Step(s, tok, outp, 0);
        return Dedup(outp);
    }

    static void Step(State s, string tok, List<State> outp, int depth)
    {
        if (depth > 48) return;
        if (s.Seq.Count == 0)
        {
            if (s.Stack.Count == 0) return;
            var top = s.Stack[^1];
            var st = new State { Seq = top, Stack = s.Stack.Take(s.Stack.Count - 1).ToList(), Caps = s.Caps, Pos = s.Pos, Lits = s.Lits };
            Step(st, tok, outp, depth + 1);
            return;
        }
        var t = s.Seq[0];
        var rest = s.Seq.Skip(1).ToList();
        switch (t)
        {
            case Lit l:
                if (l.W.Length > 0 && l.W.StartsWith(tok, StringComparison.OrdinalIgnoreCase))
                {
                    var lits = new Dictionary<int, string>(s.Lits) { [s.Pos] = l.W };
                    outp.Add(new State { Seq = rest, Stack = s.Stack, Caps = s.Caps, Pos = s.Pos + 1, Lits = lits });
                }
                break;
            case Val v:
                if (v.Accept != null && !v.Accept(tok)) break;
                var caps = new Dictionary<string, string>(s.Caps) { [v.Name] = tok };
                outp.Add(new State { Seq = rest, Stack = s.Stack, Caps = caps, Pos = s.Pos + 1, Lits = s.Lits });
                break;
            case Opt o:
                Step(new State { Seq = new List<Tok>(o.Inner).Concat(rest).ToList(), Stack = s.Stack, Caps = s.Caps, Pos = s.Pos, Lits = s.Lits }, tok, outp, depth + 1);
                Step(new State { Seq = rest, Stack = s.Stack, Caps = s.Caps, Pos = s.Pos, Lits = s.Lits }, tok, outp, depth + 1);
                break;
            case Rep r:
            {
                var frame = new List<Tok> { r };
                frame.AddRange(rest);
                var st2 = new List<List<Tok>>(s.Stack) { frame };
                Step(new State { Seq = new List<Tok> { r.Inner }, Stack = st2, Caps = s.Caps, Pos = s.Pos, Lits = s.Lits }, tok, outp, depth + 1);
                Step(new State { Seq = rest, Stack = s.Stack, Caps = s.Caps, Pos = s.Pos, Lits = s.Lits }, tok, outp, depth + 1);
                break;
            }
        }
    }

    static List<State> Dedup(List<State> states)
    {
        var seen = new HashSet<string>();
        var res = new List<State>();
        foreach (var s in states)
        {
            var key = string.Join(" ", s.Seq.Select(TokKey)) + "|" + s.Stack.Count + "|" + s.Pos + "|" +
                      string.Join(",", s.Caps.OrderBy(k => k.Key).Select(kv => kv.Key + "=" + kv.Value)) + "|" +
                      string.Join(",", s.Lits.OrderBy(k => k.Key).Select(kv => kv.Key + "=" + kv.Value));
            if (seen.Add(key)) res.Add(s);
        }
        return res;
    }

    // 结构化 key：两个只剩 Opt/Rep 内部关键字不同的模板不能被误判成同一状态
    static string TokKey(Tok t) => t switch
    {
        Lit l => "L:" + l.W,
        Val v => "V:" + v.Name,
        Opt o => "O(" + string.Join(",", o.Inner.Select(TokKey)) + ")",
        Rep r => "R(" + TokKey(r.Inner) + ")",
        _ => "?"
    };

    // 候选说明兜底：Lit/Val 未写帮助时按候选词查（CommandSets 静态构造里注册）
    public static Func<string, string?>? CandHelp;
    static string HelpOr(string own, string word) => own.Length > 0 ? own : (CandHelp?.Invoke(word) ?? "");

    static Candidate WithHelp(Candidate c) => c.Help.Length > 0 ? c : c with { Help = CandHelp?.Invoke(c.Word) ?? "" };

    static void Next(Device dev, State s, string partial, bool help, List<Candidate> outp, int depth)
    {
        if (depth > 48) return;
        if (s.Seq.Count == 0)
        {
            if (s.Stack.Count == 0) { if (help) outp.Add(new Candidate("", "")); return; }
            var top = s.Stack[^1];
            Next(dev, new State { Seq = top, Stack = s.Stack.Take(s.Stack.Count - 1).ToList(), Caps = s.Caps }, partial, help, outp, depth + 1);
            return;
        }
        var t = s.Seq[0];
        var rest = s.Seq.Skip(1).ToList();
        switch (t)
        {
            case Lit l:
                if (l.W.StartsWith(partial, StringComparison.OrdinalIgnoreCase)) outp.Add(new Candidate(l.W, HelpOr(l.Help, l.W)));
                break;
            case Val v:
                if (help && partial.Length == 0 && !v.NoHint) outp.Add(new Candidate(v.Hint, HelpOr(v.HintHelp, v.Hint)));
                if (v.CSug != null)
                {
                    foreach (var c in v.CSug(dev, partial, s.Caps))
                        if (c.Word.StartsWith(partial, StringComparison.OrdinalIgnoreCase)) outp.Add(WithHelp(c));
                }
                else if (v.Sug != null)
                {
                    foreach (var c in v.Sug(dev, partial))
                        if (c.Word.StartsWith(partial, StringComparison.OrdinalIgnoreCase)) outp.Add(WithHelp(c));
                }
                break;
            case Opt o:
                Next(dev, new State { Seq = new List<Tok>(o.Inner).Concat(rest).ToList(), Stack = s.Stack, Caps = s.Caps }, partial, help, outp, depth + 1);
                Next(dev, new State { Seq = rest, Stack = s.Stack, Caps = s.Caps }, partial, help, outp, depth + 1);
                break;
            case Rep r:
                Next(dev, new State { Seq = new List<Tok> { r.Inner }.Concat(rest).ToList(), Stack = s.Stack, Caps = s.Caps }, partial, help, outp, depth + 1);
                Next(dev, new State { Seq = rest, Stack = s.Stack, Caps = s.Caps }, partial, help, outp, depth + 1);
                break;
        }
    }

    public static List<Candidate> Complete(Device dev, List<Command> cmds, List<string> before, string partial, bool help)
    {
        var outp = new List<Candidate>();
        if (before.Count == 0)
        {
            foreach (var c in cmds)
                if (c.Kw.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    outp.Add(new Candidate(c.Kw, HelpOr(c.Help, c.Kw)));
            return DedupCands(outp);
        }
        var matched = cmds.Where(c => c.Kw.StartsWith(before[0], StringComparison.OrdinalIgnoreCase)).ToList();
        if (matched.Count == 0) return outp;
        var states = new List<State>();
        foreach (var c in matched)
            foreach (var tpl in c.Templates)
                states.AddRange(Start(tpl));
        foreach (var tok in before.Skip(1))
            states = Advance(dev, states, tok);
        if (states.Count == 0) return outp;
        foreach (var st in states) Next(dev, st, partial, help, outp, 0);
        return DedupCands(outp);
    }

    static List<Candidate> DedupCands(List<Candidate> items)
    {
        var seen = new HashSet<string>();
        var res = new List<Candidate>();
        foreach (var c in items)
            if (seen.Add(c.Word)) res.Add(c);
        return res;
    }

    public static ExecResult Execute(Device dev, Session session, List<Command> cmds, List<string> tokens)
    {
        var res = new ExecResult();
        if (tokens.Count == 0) return res;
        var matched = cmds.Where(c => c.Kw.StartsWith(tokens[0], StringComparison.OrdinalIgnoreCase)).ToList();
        if (matched.Count == 0) { res.Error = "Unrecognized command"; res.ErrTok = 0; return res; }
        if (matched.Count > 1)
        {
            var exact = matched.FirstOrDefault(c => c.Kw.Equals(tokens[0], StringComparison.OrdinalIgnoreCase));
            if (exact == null) { res.Error = "Ambiguous command"; res.ErrTok = 0; return res; }
            matched = new List<Command> { exact };
        }
        var cmd = matched[0];
        var states = new List<State>();
        foreach (var tpl in cmd.Templates) states.AddRange(Start(tpl));
        for (int i = 1; i < tokens.Count; i++)
        {
            states = Advance(dev, states, tokens[i]);
            if (states.Count == 0) { res.Error = "Unrecognized command"; res.ErrTok = i; return res; }
        }
        var finished = states.Where(IsFinished).ToList();
        if (finished.Count == 0) { res.Error = "Incomplete command"; res.ErrTok = tokens.Count; return res; }
        var norm = new List<string>(tokens);
        foreach (var kv in finished[0].Lits)
            if (kv.Key >= 1 && kv.Key < norm.Count) norm[kv.Key] = kv.Value;
        var ctx = new ExecCtx { Dev = dev, S = session, Tokens = norm, Caps = finished[0].Caps };
        res.Lines = cmd.Run?.Invoke(ctx) ?? Array.Empty<string>();
        if (res.Error == null && !res.Lines.Any(l => l.StartsWith("Error:", StringComparison.Ordinal)))
            session.Timer.NoteStep();
        return res;
    }

    public static bool IsFinished(State s)
    {
        var seq = new List<Tok>(s.Seq);
        var stack = s.Stack.Select(x => new List<Tok>(x)).ToList();
        while (true)
        {
            if (seq.Count == 0)
            {
                if (stack.Count == 0) return true;
                seq = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                continue;
            }
            if (seq[0] is Rep) { seq.RemoveAt(0); continue; }
            return false;
        }
    }
}
