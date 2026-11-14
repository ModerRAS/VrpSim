namespace VrpSim;

// `?` / Tab 候选排版：真机风格两列（首列宽 = 本表最长候选 + 2，第二列放说明），
// 说明超宽按控制台宽度折行、续行缩进对齐第二列；命令位置不显示 <cr>。
public static class HelpFormat
{
    public static List<string> Format(IEnumerable<Candidate> cands, int bufferWidth = 0)
    {
        var rows = cands.Where(c => c.Word.Length > 0).ToList();   // 真机命令位置不出现 <cr>
        if (rows.Count == 0) return new List<string>();
        var firstCol = rows.Max(c => c.Word.Length) + 2;
        var width = bufferWidth > 0 ? bufferWidth : BufferWidth();
        var lines = new List<string>();
        foreach (var c in rows)
        {
            var help = (c.Help ?? "").Trim();
            if (help.Length == 0) { lines.Add("  " + c.Word); continue; }
            var col = 2 + firstCol;
            var avail = Math.Max(20, width - col);
            var wrapped = Wrap(help, avail);
            lines.Add("  " + c.Word.PadRight(firstCol) + wrapped[0]);
            for (var i = 1; i < wrapped.Count; i++) lines.Add(new string(' ', col) + wrapped[i]);
        }
        return lines;
    }

    public static int BufferWidth()
    {
        try { var w = Console.BufferWidth; return w > 0 ? w : 120; }
        catch { return 120; }
    }

    static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var cur = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (cur.Length == 0) { cur = word; continue; }
            if (cur.Length + 1 + word.Length <= width) cur += " " + word;
            else { lines.Add(cur); cur = word; }
        }
        if (cur.Length > 0) lines.Add(cur);
        if (lines.Count == 0) lines.Add(text);
        return lines;
    }
}
