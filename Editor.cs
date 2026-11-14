using System.Text;

namespace VrpSim;

public sealed class LineEditor
{
    readonly Session s;
    readonly List<string> history = new();
    int histIdx;

    public LineEditor(Session s) { this.s = s; }

    public string? ReadLine(string prompt)
    {
        var buf = new StringBuilder();
        var pos = 0;
        var prevLen = 0;
        Console.Write(prompt);
        while (true)
        {
            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                var text = buf.ToString();
                if (text.Trim().Length > 0) { history.Add(text); histIdx = history.Count; }
                return text;
            }
            if (k.Key == ConsoleKey.Backspace)
            {
                if (pos > 0) { buf.Remove(pos - 1, 1); pos--; }
                Redraw(prompt, buf, pos, ref prevLen);
                continue;
            }
            if (k.Key == ConsoleKey.Delete)
            {
                if (pos < buf.Length) buf.Remove(pos, 1);
                Redraw(prompt, buf, pos, ref prevLen);
                continue;
            }
            if (k.Key == ConsoleKey.LeftArrow)
            {
                if (pos > 0) pos--;
                MoveCursor(prompt, pos);
                continue;
            }
            if (k.Key == ConsoleKey.RightArrow)
            {
                if (pos < buf.Length) pos++;
                MoveCursor(prompt, pos);
                continue;
            }
            if (k.Key == ConsoleKey.Home) { pos = 0; MoveCursor(prompt, pos); continue; }
            if (k.Key == ConsoleKey.End) { pos = buf.Length; MoveCursor(prompt, pos); continue; }
            if (k.Key == ConsoleKey.UpArrow)
            {
                if (histIdx > 0) histIdx--;
                SetBuf(prompt, buf, histIdx < history.Count ? history[histIdx] : "", ref pos, ref prevLen);
                continue;
            }
            if (k.Key == ConsoleKey.DownArrow)
            {
                if (histIdx < history.Count) histIdx++;
                SetBuf(prompt, buf, histIdx < history.Count ? history[histIdx] : "", ref pos, ref prevLen);
                continue;
            }
            if (k.Key == ConsoleKey.Tab)
            {
                Complete(prompt, buf, ref pos, ref prevLen, false);
                continue;
            }
            if (k.KeyChar == '?')
            {
                Complete(prompt, buf, ref pos, ref prevLen, true);
                continue;
            }
            if (k.KeyChar == '\u0003') { Console.WriteLine(); return ""; }
            if (k.KeyChar == '\u001A') { Console.WriteLine(); return "\u001A"; }
            if (!char.IsControl(k.KeyChar))
            {
                buf.Insert(pos, k.KeyChar);
                pos++;
                Redraw(prompt, buf, pos, ref prevLen);
            }
        }
    }

    static void SetBuf(string prompt, StringBuilder buf, string v, ref int pos, ref int prevLen)
    {
        buf.Clear();
        buf.Append(v);
        pos = buf.Length;
        Redraw(prompt, buf, pos, ref prevLen);
    }

    static void Redraw(string prompt, StringBuilder buf, int pos, ref int prevLen)
    {
        var line = prompt + buf.ToString();
        var pad = Math.Max(0, prevLen - line.Length);
        try { Console.Write("\r" + line + (pad > 0 ? new string(' ', pad) : "")); } catch { }
        try
        {
            var col = prompt.Length + pos;
            var w = Console.BufferWidth > 0 ? Console.BufferWidth : 120;
            if (col < w) Console.SetCursorPosition(col, Console.CursorTop);
        }
        catch { }
        prevLen = line.Length;
    }

    static void MoveCursor(string prompt, int pos)
    {
        try { Console.SetCursorPosition(prompt.Length + pos, Console.CursorTop); } catch { }
    }

    void Complete(string prompt, StringBuilder buf, ref int pos, ref int prevLen, bool help)
    {
        var text = buf.ToString();
        var start = 0;
        for (var i = pos - 1; i >= 0; i--)
        {
            if (text[i] == ' ') { start = i + 1; break; }
        }
        var beforeText = text.Substring(0, start);
        var partial = text.Substring(start, pos - start);
        var before = beforeText.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        var cands = Cli.Complete(s.Dev, s.Commands(), before, partial, help);
        if (cands.Count == 0) return;
        if (!help && cands.Count == 1 && cands[0].Word.Length > 0)
        {
            var insert = cands[0].Word;
            buf.Remove(start, pos - start);
            buf.Insert(start, insert + " ");
            pos = start + insert.Length + 1;
            Console.WriteLine();
            prevLen = 0;
            Redraw(prompt, buf, pos, ref prevLen);
            return;
        }
        Console.WriteLine();
        var w = cands.Max(c => c.Word.Length);
        foreach (var c in cands)
            Console.WriteLine("  " + (c.Word.Length == 0 ? "<cr>" : c.Word).PadRight(Math.Max(w, 4) + 2) + c.Help);
        Console.WriteLine();
        prevLen = 0;
        Redraw(prompt, buf, pos, ref prevLen);
    }
}
