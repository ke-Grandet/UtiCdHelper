using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace UtiCdHelper;

/// <summary>悬浮圆点窗的摆放参数：整体坐标、圆点间距、圆点大小。</summary>
public sealed class OverlaySettings
{
    private const string FileName = "utiCdHelper浮窗设置.json";

    public const int DotCount = 5;

    public const int MinSize = 12;
    public const int MaxSize = 64;
    public const int MinSpacing = 0;
    public const int MaxSpacing = 80;

    public const int MinCoordinate = -9999;
    public const int MaxCoordinate = 99999;

    public const int DefaultSize = 26;
    public const int DefaultSpacing = 26;

    // 圆点天生是宽 38 高 26 的圆角条，宽高按这个比例联动，改大小时形状不失真
    private const double DotAspect = 38.0 / DefaultSize;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>悬浮窗左上角的横坐标（屏幕像素）。</summary>
    public int X { get; set; }

    /// <summary>悬浮窗左上角的纵坐标（屏幕像素）。</summary>
    public int Y { get; set; }

    /// <summary>相邻两个圆点之间的空白宽度（像素）。</summary>
    public int Spacing { get; set; } = DefaultSpacing;

    /// <summary>单个圆点的高度（像素），宽度按比例跟随。</summary>
    public int Size { get; set; } = DefaultSize;

    /// <summary>
    /// 运行时开关：勾选时浮窗实际横坐标取配置值相对屏幕中心垂线的镜像位置。
    /// 只影响摆放，不写入配置文件，下次启动回到不勾选。
    /// </summary>
    [JsonIgnore]
    public bool MirrorX { get; set; }

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>
    /// 把浮窗左边缘按屏幕中心垂线左右镜像。镜像是自反的：
    /// 配置 X 换算成实际位置、以及实际位置反算回配置 X 都用这一个公式。
    /// </summary>
    public static int MirrorLeft(int left, double overlayWidth) =>
        (int)Math.Round(SystemParameters.PrimaryScreenWidth - overlayWidth - left);

    /// <summary>圆点宽度：跟随高度按比例换算，保持原来的药丸形状。</summary>
    public static int DotWidth(int size) => (int)Math.Round(size * DotAspect);

    /// <summary>五个圆点加四处间距的总宽度。</summary>
    public static int TotalWidth(int size, int spacing) =>
        (DotCount * DotWidth(size)) + ((DotCount - 1) * spacing);

    /// <summary>TotalWidth 的反算：给定总宽和间距倒推圆点高度，拖着改大小时用。</summary>
    public static int SizeFromTotalWidth(double totalWidth, int spacing)
    {
        double dotWidth = (totalWidth - ((DotCount - 1) * spacing)) / DotCount;
        return (int)Math.Round(dotWidth / DotAspect);
    }

    public static OverlaySettings Load()
    {
        var settings = new OverlaySettings();
        bool loaded = false;

        try
        {
            if (File.Exists(FilePath))
            {
                OverlaySettings? stored = JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(FilePath));
                if (stored is not null)
                {
                    settings = stored;
                    loaded = true;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // 读不出来就用默认值
        }

        if (!loaded)
        {
            settings.ResetDefaults();
        }

        settings.Save();
        return settings;
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SerializerOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // 写入失败不影响使用，下次启动回退到默认值
        }
    }

    public void ResetDefaults()
    {
        OverlayDefaults defaults = DefaultsSettings.Current.Overlay;

        Size = Math.Clamp(defaults.Size ?? DefaultSize, MinSize, MaxSize);
        Spacing = Math.Clamp(defaults.Spacing ?? DefaultSpacing, MinSpacing, MaxSpacing);
        Y = Math.Clamp(defaults.Y ?? 0, MinCoordinate, MaxCoordinate);

        // X 没配就按当前宽度横向居中，跟屏幕宽度有关，不适合写死在文件里
        X = defaults.X is int configured
            ? Math.Clamp(configured, MinCoordinate, MaxCoordinate)
            : (int)Math.Round((SystemParameters.PrimaryScreenWidth - TotalWidth(Size, Spacing)) / 2);
    }
}
