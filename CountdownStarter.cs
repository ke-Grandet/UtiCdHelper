using System;
using System.Globalization;
using System.Windows;

namespace UtiCdHelper;

/// <summary>
/// 启动计时的统一入口：主界面的启动按钮和全局快捷键走的是同一条路径。
/// 方法都返回一句状态文本，供主界面提示用户。
/// </summary>
public sealed class CountdownStarter
{
    /// <summary>已就绪时喊的文本，{0} = 技能名。</summary>
    public const string DefaultReadyText = "敌方【{0}】技能已就绪！";

    /// <summary>还在倒计时时喊的文本，{0} = 技能名，{1} = 就绪时刻。</summary>
    public const string DefaultPendingText = "敌方【{0}】技能至多在【{1}】前就绪！";

    private readonly RecognitionSettings _settings;
    private readonly CellValues _cells;
    private readonly CountdownService _countdown;

    public CountdownStarter(RecognitionSettings settings, CellValues cells, CountdownService countdown)
    {
        _settings = settings;
        _cells = cells;
        _countdown = countdown;
    }

    /// <summary>
    /// 启动第 index 路倒计时。总耗时就是该格的数字；为 0 时不响应；
    /// 已经在倒计时中则覆盖，重新对表。返回给界面显示的状态。
    /// </summary>
    public string Start(int index)
    {
        if (index < 0 || index >= _cells.Cells.Count)
        {
            return string.Empty;
        }

        int total = _cells.Cells[index].Value;
        if (total <= 0)
        {
            return $"第 {index + 1} 格初始值为 0，未启动";
        }

        int? baseline = _settings.RecognizeTimer();
        _countdown.Start(index, total, baseline);

        return baseline is null
            ? $"第 {index + 1} 路已启动：未读到表，按真实时钟倒计时 {total} 秒"
            : $"第 {index + 1} 路已启动：对表 {RecognitionSettings.FormatClock(baseline.Value)}，总耗时 {total} 秒";
    }

    /// <summary>
    /// 喊话：识别计时区域拿到当前时刻，加上本格剩余计时得到未来时刻，拼成文本放进剪贴板。
    /// 只有本格正在倒计时才给时刻；未启动或已倒数完都是"已就绪"。
    /// 返回实际生成的文本。
    /// </summary>
    public string Shout(int index)
    {
        if (index < 0 || index >= _cells.Cells.Count)
        {
            return string.Empty;
        }

        // 只有正在倒计时才有"还要多久"；没启动或已倒数完都算已就绪
        int remaining = _countdown.IsRunning(index) ? _countdown.GetRemaining(index) : 0;

        // 有名字用名字，没名字用"第几楼"（格子序号从 1 开始）
        string label = GetLabel(index);

        // 文本模板从默认配置里取，占位符照旧：{0} 技能名，{1} 就绪时刻
        ShoutDefaults defaults = DefaultsSettings.Current.Shout;
        string text;
        if (remaining <= 0)
        {
            text = Format(defaults.Ready, DefaultReadyText, label, string.Empty);
        }
        else
        {
            // 读不到表时按 0 起算，至少保证有文本输出
            int now = _settings.RecognizeTimer() ?? 0;
            text = Format(
                defaults.Pending,
                DefaultPendingText,
                label,
                RecognitionSettings.FormatClock(now + remaining));
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // 剪贴板被别的程序占用时忽略，不影响计时
        }

        return text;
    }

    /// <summary>
    /// 套用模板生成喊话文本。占位符写错（比如多写了一个）会抛异常，
    /// 这种时候退回内置文本，保证喊话功能始终可用。
    /// </summary>
    private static string Format(string? template, string fallback, string label, string clock)
    {
        string used = string.IsNullOrWhiteSpace(template) ? fallback : template;

        try
        {
            return string.Format(CultureInfo.InvariantCulture, used, label, clock);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.InvariantCulture, fallback, label, clock);
        }
    }

    /// <summary>喊话里指代这一格的说法：已记录的名字，没有名字就用"N楼"（格子序号从 1 开始）。</summary>
    private string GetLabel(int index)
    {
        string? name = _cells.Cells[index].Name;

        return string.IsNullOrEmpty(name) ? $"{index + 1}楼" : name;
    }

    /// <summary>中断全部倒计时。</summary>
    public void StopAll() => _countdown.StopAll();

    /// <summary>底层计时服务，识图设置界面要用它的轮询诊断。</summary>
    public CountdownService Countdown => _countdown;

    public Action ForIndex(int index) => () => { Start(index); };

    public Action ShoutForIndex(int index) => () => { Shout(index); };
}
