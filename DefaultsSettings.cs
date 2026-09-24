using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace UtiCdHelper;

/// <summary>一块识别区域的默认参数。全部可空：没写的项沿用代码里的初值。</summary>
public sealed class RecognitionAreaDefaults
{
    public int? X { get; set; }

    public int? Y { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    /// <summary>放大倍数，有效范围 1-8。</summary>
    public int? Scale { get; set; }

    /// <summary>二值化阈值，有效范围 -1（自动）到 255。</summary>
    public int? Threshold { get; set; }

    public bool? Invert { get; set; }
}

/// <summary>
/// 多帧采样：连续截几帧分别识别，至少两帧一致才采纳。
/// 帧数填 1 就是关掉这套机制（不再有帧间等待），识别快但不再过滤偶发误读。
/// </summary>
public sealed class SamplingDefaults
{
    /// <summary>采样帧数，1-5。</summary>
    public int? Samples { get; set; }

    /// <summary>相邻两帧之间的等待毫秒数，0-500。</summary>
    public int? SampleDelayMs { get; set; }
}

/// <summary>
/// 喊话文本模板。占位符固定两个：{0} = 技能名（没名字就是"N楼"），{1} = 就绪时刻。
/// 少写占位符不会出错，多写或写错会退回内置文本。
/// </summary>
public sealed class ShoutDefaults
{
    /// <summary>已就绪时用的文本。</summary>
    public string? Ready { get; set; }

    /// <summary>还在倒计时时用的文本。</summary>
    public string? Pending { get; set; }
}

/// <summary>悬浮窗的默认摆放。X 留空表示横向居中。</summary>
public sealed class OverlayDefaults
{
    public int? X { get; set; }

    public int? Y { get; set; }

    public int? Spacing { get; set; }

    public int? Size { get; set; }
}

/// <summary>一条默认快捷键。Modifiers / Key 用枚举名填写，如 "Control, Shift"、"F"。</summary>
public sealed class HotKeyDefault
{
    public string? Name { get; set; }

    public string? Group { get; set; }

    public string? Modifiers { get; set; }

    public string? Key { get; set; }
}

/// <summary>
/// 应用的默认配置，和外部的三个配置文件放在一起，界面上不体现。
/// 只在"还没有用户配置"、"点默认按钮"这类场合作为初值来源；用户配置存在时永远以用户配置为准。
/// </summary>
public sealed class DefaultsSettings
{
    private const string FileName = "utiCdHelper默认配置.json";

    /// <summary>默认的跟随进程名：默认配置文件里没填时用它兜底。</summary>
    public const string BuiltInFollowProcessName = "SourceTree";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static DefaultsSettings? _current;

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>进程内共享的一份默认配置，首次访问时才读盘。</summary>
    public static DefaultsSettings Current => _current ??= Load();

    public RecognitionAreaDefaults Initial { get; set; } = new();

    public RecognitionAreaDefaults Timer { get; set; } = new();

    public RecognitionAreaDefaults Name { get; set; } = new();

    public OverlayDefaults Overlay { get; set; } = new();

    public SamplingDefaults Sampling { get; set; } = new();

    public ShoutDefaults Shout { get; set; } = new();

    /// <summary>
    /// 跟随的进程名：该进程有窗口时显示悬浮窗，未运行或最小化时隐藏。
    /// 填游戏进程名即可，不带 .exe。
    /// </summary>
    public string? FollowProcessName { get; set; }

    public List<HotKeyDefault> HotKeys { get; set; } = new();

    /// <summary>读默认配置文件，读不到就按代码内置的初值生成一份落盘，方便直接改。</summary>
    public static DefaultsSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                DefaultsSettings? stored = JsonSerializer.Deserialize<DefaultsSettings>(File.ReadAllText(FilePath));
                if (stored is not null)
                {
                    stored.Normalize();

                    // 旧版本生成的文件缺后来新增的项，按内置值补齐并写回，
                    // 免得用户拿到一份看着"少了一半"的 json
                    if (stored.FillMissing(CreateBuiltIn()))
                    {
                        stored.Save();
                    }

                    return stored;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // 读不出来就用内置的
        }

        var defaults = CreateBuiltIn();
        defaults.Save();
        return defaults;
    }

    /// <summary>
    /// 用内置值补齐缺失的项（只补 null 的，已填的保持原样）。
    /// 返回是否补过东西，补过才需要重新落盘。
    /// </summary>
    private bool FillMissing(DefaultsSettings builtIn)
    {
        bool changed = MergeArea(Initial, builtIn.Initial);
        changed |= MergeArea(Timer, builtIn.Timer);
        changed |= MergeArea(Name, builtIn.Name);

        changed |= MergeValue(Overlay, builtIn.Overlay, (target, source) =>
        {
            bool filled = false;
            if (target.X is null && source.X is not null) { target.X = source.X; filled = true; }
            if (target.Y is null && source.Y is not null) { target.Y = source.Y; filled = true; }
            if (target.Spacing is null && source.Spacing is not null) { target.Spacing = source.Spacing; filled = true; }
            if (target.Size is null && source.Size is not null) { target.Size = source.Size; filled = true; }
            return filled;
        });

        changed |= MergeValue(Sampling, builtIn.Sampling, (target, source) =>
        {
            bool filled = false;
            if (target.Samples is null && source.Samples is not null) { target.Samples = source.Samples; filled = true; }
            if (target.SampleDelayMs is null && source.SampleDelayMs is not null) { target.SampleDelayMs = source.SampleDelayMs; filled = true; }
            return filled;
        });

        changed |= MergeValue(Shout, builtIn.Shout, (target, source) =>
        {
            bool filled = false;
            if (target.Ready is null && source.Ready is not null) { target.Ready = source.Ready; filled = true; }
            if (target.Pending is null && source.Pending is not null) { target.Pending = source.Pending; filled = true; }
            return filled;
        });

        if (FollowProcessName is null && builtIn.FollowProcessName is not null)
        {
            FollowProcessName = builtIn.FollowProcessName;
            changed = true;
        }

        if ((HotKeys is null || HotKeys.Count == 0) && builtIn.HotKeys.Count > 0)
        {
            HotKeys = builtIn.HotKeys;
            changed = true;
        }

        return changed;
    }

    private static bool MergeValue<T>(T target, T source, Func<T, T, bool> fill)
        where T : notnull =>
        target is not null && source is not null && fill(target, source);

    private static bool MergeArea(RecognitionAreaDefaults area, RecognitionAreaDefaults builtIn) =>
        MergeValue(area, builtIn, (target, source) =>
        {
            bool filled = false;
            if (target.X is null && source.X is not null) { target.X = source.X; filled = true; }
            if (target.Y is null && source.Y is not null) { target.Y = source.Y; filled = true; }
            if (target.Width is null && source.Width is not null) { target.Width = source.Width; filled = true; }
            if (target.Height is null && source.Height is not null) { target.Height = source.Height; filled = true; }
            if (target.Scale is null && source.Scale is not null) { target.Scale = source.Scale; filled = true; }
            if (target.Threshold is null && source.Threshold is not null) { target.Threshold = source.Threshold; filled = true; }
            if (target.Invert is null && source.Invert is not null) { target.Invert = source.Invert; filled = true; }
            return filled;
        });

    /// <summary>文件里写 null 的段落补成空对象，调用方不必处处判空。</summary>
    private void Normalize()
    {
        Initial ??= new RecognitionAreaDefaults();
        Timer ??= new RecognitionAreaDefaults();
        Name ??= new RecognitionAreaDefaults();
        Overlay ??= new OverlayDefaults();
        Sampling ??= new SamplingDefaults();
        Shout ??= new ShoutDefaults();
        HotKeys ??= new List<HotKeyDefault>();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SerializerOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // 写不进去不影响使用，全部走代码内置的初值
        }
    }

    /// <summary>把代码里现有的初值整理成一份默认配置。</summary>
    private static DefaultsSettings CreateBuiltIn()
    {
        var probe = new RecognitionSettings();

        return new DefaultsSettings
        {
            Initial = new RecognitionAreaDefaults
            {
                X = probe.CaptureX,
                Y = probe.CaptureY,
                Width = probe.CaptureWidth,
                Height = probe.CaptureHeight,
                Scale = probe.Scale,
                Threshold = probe.Threshold,
                Invert = probe.Invert,
            },
            Timer = new RecognitionAreaDefaults
            {
                X = probe.TimerCaptureX,
                Y = probe.TimerCaptureY,
                Width = probe.TimerCaptureWidth,
                Height = probe.TimerCaptureHeight,
                Scale = probe.TimerScale,
                Threshold = probe.TimerThreshold,
                Invert = probe.TimerInvert,
            },
            Name = new RecognitionAreaDefaults
            {
                X = probe.NameCaptureX,
                Y = probe.NameCaptureY,
                Width = probe.NameCaptureWidth,
                Height = probe.NameCaptureHeight,
                Scale = probe.NameScale,
                Threshold = probe.NameThreshold,
                Invert = probe.NameInvert,
            },
            // X 不写死：默认的做法是横向居中，跟屏幕宽度有关
            Overlay = new OverlayDefaults
            {
                Y = 0,
                Spacing = OverlaySettings.DefaultSpacing,
                Size = OverlaySettings.DefaultSize,
            },
            // 时钟区域稳定、参数又能调，默认只采一帧：不再为等帧付上百毫秒
            Sampling = new SamplingDefaults
            {
                Samples = 1,
                SampleDelayMs = 60,
            },
            Shout = new ShoutDefaults
            {
                Ready = CountdownStarter.DefaultReadyText,
                Pending = CountdownStarter.DefaultPendingText,
            },
            FollowProcessName = BuiltInFollowProcessName,
            HotKeys = HotKeySettings.BuiltInDefaults
                .Select(item => new HotKeyDefault
                {
                    Name = item.Name,
                    Group = item.Group,
                    Modifiers = HotKeySettings.DefaultModifiers.ToString(),
                    Key = item.Key.ToString(),
                })
                .ToList(),
        };
    }
}
