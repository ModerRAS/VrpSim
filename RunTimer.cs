using System.Diagnostics;

namespace VrpSim;

// 纯后台计时（v1.6.0）：t0 = 登录开始（第一次打印 Username: 前；--no-login/--batch 用进程启动）。
// 只累计"实际运行时长"（单调时钟，改系统时间/时区不影响），关掉再开继续累计；
// 不上屏、不写配置、不参与 check 评分；结果存 vrp-sim-state.json（ElapsedMs / Steps / FirstLoginAt / UpdatedAt）。
public sealed class RunTimer
{
    long baseElapsedMs;          // 上次运行已累计的毫秒（来自 state 文件）
    int baseSteps;               // 上次运行已累计的成功命令数
    long sessionStartTs;         // 本次会话 t0（Stopwatch 时间戳，单调）
    bool started;

    public int SessionSteps { get; private set; }
    public string? FirstLoginAt { get; set; }

    public long ElapsedMs => baseElapsedMs + (started ? (long)((Stopwatch.GetTimestamp() - sessionStartTs) * 1000.0 / Stopwatch.Frequency) : 0);
    public int Steps => baseSteps + SessionSteps;

    // 从 state 文件恢复累计值（每次启动时调用；不重算，直接续上）
    public void Prime(long elapsedMs, int steps, string? firstLoginAt)
    {
        baseElapsedMs = elapsedMs < 0 ? 0 : elapsedMs;
        baseSteps = steps < 0 ? 0 : steps;
        FirstLoginAt = string.IsNullOrEmpty(firstLoginAt) ? null : firstLoginAt;
        SessionSteps = 0;
        started = false;
    }

    // 标记本次会话起点（登录开始/进程启动）；FirstLoginAt 只在第一次记录后保持不变
    public void MarkStart(string firstLoginAt)
    {
        if (string.IsNullOrEmpty(FirstLoginAt)) FirstLoginAt = firstLoginAt;
        sessionStartTs = Stopwatch.GetTimestamp();
        started = true;
        SessionSteps = 0;
    }

    // 每成功执行一条命令记一步（? / 空行 / 纯帮助不算）
    public void NoteStep()
    {
        if (started) SessionSteps++;
    }

    // 给人看的时间戳：本地时间 ISO 8601 带偏移
    public static string LocalIsoNow() => DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz");
}
