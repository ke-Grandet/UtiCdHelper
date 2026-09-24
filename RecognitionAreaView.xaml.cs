using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace UtiCdHelper;

public enum RecognitionAreaKind
{
    /// <summary>名称区域：识别汉字作为格子名字。</summary>
    Name,

    /// <summary>初始值区域：识别秒数填进方格。</summary>
    Initial,

    /// <summary>计时区域：五个启动计时共用，用来对表。</summary>
    Timer,
}

/// <summary>一块识别区域的一行紧凑配置：区域坐标 + 该区域自己的预处理参数 + 测试。</summary>
public partial class RecognitionAreaView : UserControl
{
    private readonly RecognitionSettings _settings;
    private readonly RecognitionAreaKind _kind;

    public RecognitionAreaView(RecognitionSettings settings, RecognitionAreaKind kind)
    {
        _settings = settings;
        _kind = kind;

        InitializeComponent();

        TitleText.Text = _kind switch
        {
            RecognitionAreaKind.Name => "名称区域",
            RecognitionAreaKind.Initial => "初始值区域",
            _ => "计时区域",
        };

        Reload();
    }

    /// <summary>是否在条目上方画一条分组分隔线（列表里除第一项外都开）。</summary>
    public bool ShowSeparator
    {
        get => Separator.Visibility == Visibility.Visible;
        set => Separator.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Reload()
    {
        BoxX.Text = GetX().ToString(CultureInfo.InvariantCulture);
        BoxY.Text = GetY().ToString(CultureInfo.InvariantCulture);
        BoxWidth.Text = GetWidth().ToString(CultureInfo.InvariantCulture);
        BoxHeight.Text = GetHeight().ToString(CultureInfo.InvariantCulture);
        BoxScale.Text = GetScale().ToString(CultureInfo.InvariantCulture);
        BoxThreshold.Text = GetThreshold().ToString(CultureInfo.InvariantCulture);
        UpdateInvertButton();
    }

    private int GetX() => _kind switch
    {
        RecognitionAreaKind.Name => _settings.NameCaptureX,
        RecognitionAreaKind.Initial => _settings.CaptureX,
        _ => _settings.TimerCaptureX,
    };

    private int GetY() => _kind switch
    {
        RecognitionAreaKind.Name => _settings.NameCaptureY,
        RecognitionAreaKind.Initial => _settings.CaptureY,
        _ => _settings.TimerCaptureY,
    };

    private int GetWidth() => _kind switch
    {
        RecognitionAreaKind.Name => _settings.NameCaptureWidth,
        RecognitionAreaKind.Initial => _settings.CaptureWidth,
        _ => _settings.TimerCaptureWidth,
    };

    private int GetHeight() => _kind switch
    {
        RecognitionAreaKind.Name => _settings.NameCaptureHeight,
        RecognitionAreaKind.Initial => _settings.CaptureHeight,
        _ => _settings.TimerCaptureHeight,
    };

    private int GetScale() => _kind switch
    {
        RecognitionAreaKind.Name => _settings.NameScale,
        RecognitionAreaKind.Initial => _settings.Scale,
        _ => _settings.TimerScale,
    };

    private int GetThreshold() => _kind switch
    {
        RecognitionAreaKind.Name => _settings.NameThreshold,
        RecognitionAreaKind.Initial => _settings.Threshold,
        _ => _settings.TimerThreshold,
    };

    private bool GetInvert() => _kind switch
    {
        RecognitionAreaKind.Name => _settings.NameInvert,
        RecognitionAreaKind.Initial => _settings.Invert,
        _ => _settings.TimerInvert,
    };

    private void SetArea(int x, int y, int width, int height)
    {
        switch (_kind)
        {
            case RecognitionAreaKind.Name:
                _settings.NameCaptureX = x;
                _settings.NameCaptureY = y;
                _settings.NameCaptureWidth = width;
                _settings.NameCaptureHeight = height;
                break;

            case RecognitionAreaKind.Initial:
                _settings.CaptureX = x;
                _settings.CaptureY = y;
                _settings.CaptureWidth = width;
                _settings.CaptureHeight = height;
                break;

            default:
                _settings.TimerCaptureX = x;
                _settings.TimerCaptureY = y;
                _settings.TimerCaptureWidth = width;
                _settings.TimerCaptureHeight = height;
                break;
        }
    }

    private void SetPreprocess(int scale, int threshold, bool invert)
    {
        switch (_kind)
        {
            case RecognitionAreaKind.Name:
                _settings.NameScale = scale;
                _settings.NameThreshold = threshold;
                _settings.NameInvert = invert;
                break;

            case RecognitionAreaKind.Initial:
                _settings.Scale = scale;
                _settings.Threshold = threshold;
                _settings.Invert = invert;
                break;

            default:
                _settings.TimerScale = scale;
                _settings.TimerThreshold = threshold;
                _settings.TimerInvert = invert;
                break;
        }
    }

    private void OnSelectRegionClick(object sender, RoutedEventArgs e)
    {
        Window? owner = Window.GetWindow(this);
        if (owner is null)
        {
            return;
        }

        Rect? rect = RegionSelectorWindow.Select(owner);
        if (rect is null)
        {
            return;
        }

        // 框选结果直接落盘：不能再走 ApplySettings，那会用输入框里的旧文本把新区域覆盖掉
        SetArea((int)rect.Value.X, (int)rect.Value.Y, (int)rect.Value.Width, (int)rect.Value.Height);
        _settings.Save();

        Reload();
        ResultText.Text = $"区域已更新：{GetX()}, {GetY()} · {GetWidth()} × {GetHeight()}";
    }

    private void OnInvertClick(object sender, RoutedEventArgs e)
    {
        ApplySettings();
        SetPreprocess(GetScale(), GetThreshold(), !GetInvert());
        _settings.Save();
        UpdateInvertButton();
    }

    private void OnTestClick(object sender, RoutedEventArgs e)
    {
        ApplySettings();

        if (_kind == RecognitionAreaKind.Name)
        {
            string? name = _settings.RecognizeName();
            ResultText.Text = name is null
                ? "未识别到名称" + (_settings.LastError is null ? string.Empty : "：" + _settings.LastError)
                : $"识别结果：{name}";
            return;
        }

        int? seconds = _kind == RecognitionAreaKind.Initial
            ? _settings.Recognize()
            : _settings.RecognizeTimer();

        ResultText.Text = Describe(seconds);
    }

    private void ApplySettings()
    {
        SetArea(
            Parse(BoxX.Text, GetX()),
            Parse(BoxY.Text, GetY()),
            Math.Max(1, Parse(BoxWidth.Text, GetWidth())),
            Math.Max(1, Parse(BoxHeight.Text, GetHeight())));

        SetPreprocess(
            Math.Clamp(Parse(BoxScale.Text, GetScale()), 1, 8),
            Math.Clamp(Parse(BoxThreshold.Text, GetThreshold()), -1, 255),
            GetInvert());

        _settings.Save();
    }

    private string Describe(int? seconds)
    {
        if (seconds is null)
        {
            string samples = string.IsNullOrWhiteSpace(_settings.LastSamples)
                ? string.Empty
                : $"采样 {_settings.LastSamples}（不一致，已丢弃）；";

            return "未识别到数字：" + samples
                + (_settings.LastError is null ? string.Empty : _settings.LastError);
        }

        int total = seconds.Value;

        // 初始值区域拿到的是秒数，直接显示；计时区域拿到的是时刻
        string text = _kind == RecognitionAreaKind.Initial
            ? $"{total}秒"
            : RecognitionSettings.FormatClock(total);

        if (!string.IsNullOrWhiteSpace(_settings.LastSamples))
        {
            text += $"，采样 {_settings.LastSamples}";
        }

        return text;
    }

    private static int Parse(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

    private void UpdateInvertButton()
    {
        InvertButton.Content = GetInvert() ? "反色：开" : "反色：关";
    }
}
