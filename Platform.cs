namespace VrpSim;

// 平台抽象：控制台项目用默认实现（Console/File），网页版（Blazor WASM）在启动时覆盖成 JS 回调 / localStorage。
public static class Platform
{
    // 输出一行（真机回显/提示）
    public static Action<string> WriteLine = s => Console.WriteLine(s);

    // 终端宽度（? / Tab 两列排版用）
    public static int Width = 120;

    // save / display saved-configuration 的读写（控制台是工作目录文件，网页是 localStorage）
    public static Action<IReadOnlyList<string>> WriteSavedConfig = lines =>
    {
        try { File.WriteAllLines("saved-config.cfg", lines); } catch { }
    };

    public static Func<IReadOnlyList<string>> ReadSavedConfig = () =>
    {
        try { return File.Exists("saved-config.cfg") ? File.ReadAllLines("saved-config.cfg") : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    };

    // reset 命令：控制台删状态文件，网页清内存存档
    public static Action ResetState = () => { };

    public static List<string> Tokenize(string line) =>
        line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();

    public static int TokenOffset(List<string> toks, int idx)
    {
        var off = 0;
        for (var i = 0; i < idx && i < toks.Count; i++) off += toks[i].Length + 1;
        return off;
    }
}
