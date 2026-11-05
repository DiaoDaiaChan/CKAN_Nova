using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;

namespace CkanUI.Plugin.Assets;

/// <summary>旗帜页进页时序探针：只在 <c>CKANUI_FLAGS_TIMING</c> 指向文件时写盘，生产路径零开销。</summary>
/// <remarks>
/// 分段口径与 NavigationProbe 的 `[nav]` 行对齐：`scan` 是服务层扫描完成（含线程池往返），
/// `read-default` 是读 settings.cfg 的 FLAG 行，`fill` 是把行灌进 ObservableCollection 完成，
/// `render`/`idle` 是从 fill 完成到 UI 线程跑完一次渲染/后台优先级的排队时间——它包含
/// ListBox 实体化 + 缩略图解码 + 布局，正好落在探针报出的 maxGap 上。
/// </remarks>
internal sealed class FlagsTiming
{
    private static readonly string? SinkPath = Environment.GetEnvironmentVariable("CKANUI_FLAGS_TIMING");

    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly List<string> _stages = [];

    private FlagsTiming()
    {
    }

    /// <summary>是否开启（未设置环境变量时所有方法立即返回）。</summary>
    public static bool Enabled => !string.IsNullOrWhiteSpace(SinkPath);

    /// <summary>开始一段计时。</summary>
    public static FlagsTiming Start() => new();

    /// <summary>记一个阶段；<paramref name="count"/> 为 -1 时不打条数。</summary>
    public void Mark(string stage, int count = -1)
    {
        if (!Enabled)
        {
            return;
        }

        _stages.Add(count >= 0
            ? string.Create(CultureInfo.InvariantCulture, $"{stage}={_watch.Elapsed.TotalMilliseconds:F1}ms n={count}")
            : string.Create(CultureInfo.InvariantCulture, $"{stage}={_watch.Elapsed.TotalMilliseconds:F1}ms"));
    }

    /// <summary>量「填充完成 → 渲染 / 空闲」的排队时间，然后把整行写盘。</summary>
    public async Task MarkRenderAndIdleAsync()
    {
        if (!Enabled)
        {
            return;
        }

        var beforeRender = _watch.Elapsed.TotalMilliseconds;
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Render).GetTask().ConfigureAwait(true);
        var renderMs = _watch.Elapsed.TotalMilliseconds - beforeRender;

        var beforeIdle = _watch.Elapsed.TotalMilliseconds;
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background).GetTask().ConfigureAwait(true);
        var idleMs = _watch.Elapsed.TotalMilliseconds - beforeIdle;

        _stages.Add(string.Create(CultureInfo.InvariantCulture, $"fill->render={renderMs:F1}ms"));
        _stages.Add(string.Create(CultureInfo.InvariantCulture, $"fill->idle={idleMs:F1}ms"));
        _stages.Add(string.Create(CultureInfo.InvariantCulture, $"total={_watch.Elapsed.TotalMilliseconds:F1}ms"));

        try
        {
            File.AppendAllText(SinkPath!, string.Create(CultureInfo.InvariantCulture, $"[flags] {string.Join(' ', _stages)}{Environment.NewLine}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 探针写盘失败不能影响页面本身。
        }
    }
}
