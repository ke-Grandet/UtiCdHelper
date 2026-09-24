using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Tesseract;

namespace UtiCdHelper;

public sealed class RecognitionSettings
{
    private const string FileName = "utiCdHelper识图设置.json";
    private const string TessDataDirectoryName = "tessdata";

    private const string EnglishDataFileName = "eng.traineddata";
    private const string ChineseDataFileName = "chi_sim.traineddata";

    // 帧数为 1 时不做采样，也没有帧间等待。
    // 这两个值要等真正识别时再取：默认配置文件的生成过程会构造本类，
    // 写成字段初始值就会和 DefaultsSettings.Current 互相递归。
    private int StableSamples => Math.Clamp(DefaultsSettings.Current.Sampling.Samples ?? 1, 1, 5);

    private int SampleDelayMs => Math.Clamp(DefaultsSettings.Current.Sampling.SampleDelayMs ?? 60, 0, 500);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly object EngineLock = new();

    private static TesseractEngine? _engine;

    // 名称识别用中文引擎，单独一个实例：不能套数字白名单，否则汉字全被过滤掉
    private static TesseractEngine? _nameEngine;

    public int CaptureX { get; set; } = 100;

    public int CaptureY { get; set; } = 100;

    public int CaptureWidth { get; set; } = 120;

    public int CaptureHeight { get; set; } = 48;

    // 启动计时专用的识别区域，只有一个，五个启动计时共用
    public int TimerCaptureX { get; set; } = 100;

    public int TimerCaptureY { get; set; } = 100;

    public int TimerCaptureWidth { get; set; } = 120;

    public int TimerCaptureHeight { get; set; } = 48;

    // 名称区域：识别格子名字用的汉字区域，有自己的一套参数
    public int NameCaptureX { get; set; } = 100;

    public int NameCaptureY { get; set; } = 100;

    public int NameCaptureWidth { get; set; } = 160;

    public int NameCaptureHeight { get; set; } = 48;

    public int NameScale { get; set; } = 3;

    public int NameThreshold { get; set; } = -1;

    public bool NameInvert { get; set; }

    // 初始值区域用的预处理参数
    public int Scale { get; set; } = 3;

    public int Threshold { get; set; } = -1;

    public bool Invert { get; set; }

    // 计时区域单独一套：两块区域的字号、底色往往不一样，参数不该共用
    public int TimerScale { get; set; } = 3;

    public int TimerThreshold { get; set; } = -1;

    public bool TimerInvert { get; set; }

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static string TessDataPath => EnsureTessData();

    private static string EnsureTessData()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UtiCdHelper",
            TessDataDirectoryName);

        Extract(directory, EnglishDataFileName);
        Extract(directory, ChineseDataFileName);

        return directory;
    }

    /// <summary>把内置的训练数据释放到本地目录，已存在就跳过。</summary>
    private static void Extract(string directory, string fileName)
    {
        string filePath = Path.Combine(directory, fileName);
        if (File.Exists(filePath))
        {
            return;
        }

        Directory.CreateDirectory(directory);

        using Stream? stream = typeof(RecognitionSettings).Assembly
            .GetManifestResourceStream($"UtiCdHelper.tessdata.{fileName}");

        if (stream is null)
        {
            throw new InvalidOperationException($"未找到内置的 OCR 训练数据：{fileName}");
        }

        using FileStream file = File.Create(filePath);
        stream.CopyTo(file);
    }

    public string? LastError { get; private set; }

    /// <summary>上一次识别到的原始文本，便于判断 OCR 到底读到了什么。</summary>
    public string? LastText { get; private set; }

    /// <summary>上一次多帧采样各自的结果，便于判断识别稳不稳。</summary>
    public string? LastSamples { get; private set; }

    public static RecognitionSettings Load()
    {
        var settings = new RecognitionSettings();

        // 先套外部默认配置，再让用户配置覆盖它
        settings.ApplyDefaults(DefaultsSettings.Current);

        try
        {
            if (File.Exists(FilePath))
            {
                RecognitionSettings? stored = JsonSerializer.Deserialize<RecognitionSettings>(File.ReadAllText(FilePath));
                if (stored is not null)
                {
                    settings = stored;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
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
        }
    }

    /// <summary>用外部默认配置补齐初值，没写 overrides 的项保持原样。</summary>
    private void ApplyDefaults(DefaultsSettings defaults)
    {
        RecognitionAreaDefaults initial = defaults.Initial;
        CaptureX = initial.X ?? CaptureX;
        CaptureY = initial.Y ?? CaptureY;
        CaptureWidth = initial.Width ?? CaptureWidth;
        CaptureHeight = initial.Height ?? CaptureHeight;
        Scale = Math.Clamp(initial.Scale ?? Scale, 1, 8);
        Threshold = Math.Clamp(initial.Threshold ?? Threshold, -1, 255);
        Invert = initial.Invert ?? Invert;

        RecognitionAreaDefaults timer = defaults.Timer;
        TimerCaptureX = timer.X ?? TimerCaptureX;
        TimerCaptureY = timer.Y ?? TimerCaptureY;
        TimerCaptureWidth = timer.Width ?? TimerCaptureWidth;
        TimerCaptureHeight = timer.Height ?? TimerCaptureHeight;
        TimerScale = Math.Clamp(timer.Scale ?? TimerScale, 1, 8);
        TimerThreshold = Math.Clamp(timer.Threshold ?? TimerThreshold, -1, 255);
        TimerInvert = timer.Invert ?? TimerInvert;

        RecognitionAreaDefaults name = defaults.Name;
        NameCaptureX = name.X ?? NameCaptureX;
        NameCaptureY = name.Y ?? NameCaptureY;
        NameCaptureWidth = name.Width ?? NameCaptureWidth;
        NameCaptureHeight = name.Height ?? NameCaptureHeight;
        NameScale = Math.Clamp(name.Scale ?? NameScale, 1, 8);
        NameThreshold = Math.Clamp(name.Threshold ?? NameThreshold, -1, 255);
        NameInvert = name.Invert ?? NameInvert;
    }

    public void WarmUp()
    {
        try
        {
            // 两个引擎都提前建好：中文模型较大，留到第一次识图再建会有可感知的停顿
            GetEngine();
            GetNameEngine();
        }
        catch (Exception exception)
        {
            // 预热失败不该让整个程序挂掉，把原因记下来，识别界面里能看到
            LastError = exception.Message;
        }
    }

    /// <summary>识别初始值区域，返回秒数（用于给方格填值）。</summary>
    public int? Recognize() =>
        RecognizeArea(CaptureX, CaptureY, CaptureWidth, CaptureHeight, Scale, Threshold, Invert, asClock: false);

    /// <summary>识别计时区域，返回的是时刻（转成秒计），用的是计时区域自己的参数。</summary>
    public int? RecognizeTimer() => RecognizeArea(
        TimerCaptureX,
        TimerCaptureY,
        TimerCaptureWidth,
        TimerCaptureHeight,
        TimerScale,
        TimerThreshold,
        TimerInvert,
        asClock: true);

    /// <summary>秒数按时钟格式显示：mm:ss，满一小时用 h:mm:ss。显示精度到秒。</summary>
    public static string FormatClock(decimal totalSeconds)
    {
        int seconds = (int)Math.Floor(totalSeconds);

        return seconds >= 3600
            ? $"{seconds / 3600}:{seconds / 60 % 60:D2}:{seconds % 60:D2}"
            : $"{seconds / 60}:{seconds % 60:D2}";
    }

    private int? RecognizeArea(
        int x,
        int y,
        int width,
        int height,
        int scale,
        int threshold,
        bool invert,
        bool asClock)
    {
        // 倒计时轮询会在后台线程识别，这里串行化，避免和界面上的测试按钮抢同一个引擎
        lock (EngineLock)
        {
            return RecognizeAreaCore(x, y, width, height, scale, threshold, invert, asClock);
        }
    }

    /// <summary>
    /// 连续截几帧分别识别，至少两帧给出相同结果才采纳。
    /// 帧与帧互不相同说明这一刻识别不稳定，宁可返回 null 让调用方保持原值，也不用错值。
    /// </summary>
    private int? RecognizeAreaCore(
        int x,
        int y,
        int width,
        int height,
        int scale,
        int threshold,
        bool invert,
        bool asClock)
    {
        LastError = null;
        LastText = null;
        LastSamples = null;

        var texts = new List<string>();
        var values = new List<int>();

        for (int i = 0; i < StableSamples; i++)
        {
            int? value = RecognizeOnce(x, y, width, height, scale, threshold, invert, asClock);

            // 计时区域采样按时刻显示，初始值区域按秒数显示
            texts.Add(value is null ? "-" : asClock ? FormatClock(value.Value) : value.Value.ToString());
            if (value is int parsed)
            {
                values.Add(parsed);
            }

            if (i < StableSamples - 1)
            {
                Thread.Sleep(SampleDelayMs);
            }
        }

        LastSamples = string.Join(" / ", texts);

        if (values.Count == 0)
        {
            return null;
        }

        int best = values
            .GroupBy(value => value)
            .OrderByDescending(group => group.Count())
            .First()
            .Key;

        // 只采一帧时没有投票可言，直接用这一帧的结果
        if (values.Count == 1)
        {
            return best;
        }

        // 帧之间互不相同：判定为不稳定，不采纳
        return values.Count(value => value == best) >= 2 ? best : null;
    }

    private int? RecognizeOnce(
        int x,
        int y,
        int width,
        int height,
        int scale,
        int threshold,
        bool invert,
        bool asClock)
    {
        LastError = null;
        LastText = null;

        try
        {
            GrayImage image = ScreenCapture.Capture(x, y, width, height);
            GrayImage processed = ImagePreprocessor.Apply(image, scale, threshold, invert);
            byte[] png = ImagePreprocessor.ToPng(processed);

            TesseractEngine engine = GetEngine();

            using Pix pix = Pix.LoadFromMemory(png);
            using Page page = engine.Process(pix, PageSegMode.SingleLine);

            LastText = page.GetText()?.Trim();
            return ParseDuration(LastText, asClock);
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            return null;
        }
    }

    private static TesseractEngine GetEngine()
    {
        lock (EngineLock)
        {
            return GetEngineCore();
        }
    }

    private static TesseractEngine GetEngineCore()
    {
        if (_engine is not null)
        {
            return _engine;
        }

        _engine = new TesseractEngine(TessDataPath, "eng", EngineMode.Default);
        _engine.SetVariable("tessedit_char_whitelist", "0123456789:.");
        return _engine;
    }

    private static TesseractEngine GetNameEngine()
    {
        lock (EngineLock)
        {
            if (_nameEngine is not null)
            {
                return _nameEngine;
            }

            _nameEngine = new TesseractEngine(TessDataPath, "chi_sim", EngineMode.Default);
            return _nameEngine;
        }
    }

    /// <summary>
    /// 识别名称区域的汉字，作为格子的名字。
    /// 识别不到返回 null，调用方据此保留格子原有的名字。
    /// </summary>
    public string? RecognizeName()
    {
        LastError = null;

        lock (EngineLock)
        {
            try
            {
                GrayImage image = ScreenCapture.Capture(
                    NameCaptureX,
                    NameCaptureY,
                    NameCaptureWidth,
                    NameCaptureHeight);

                GrayImage processed = ImagePreprocessor.Apply(image, NameScale, NameThreshold, NameInvert);
                byte[] png = ImagePreprocessor.ToPng(processed);

                TesseractEngine engine = GetNameEngine();

                using Pix pix = Pix.LoadFromMemory(png);
                using Page page = engine.Process(pix, PageSegMode.SingleLine);

                string text = page.GetText() ?? string.Empty;

                // 中文名里不该有空白，顺手去掉换行和空格
                string cleaned = new string(text.Where(character => !char.IsWhiteSpace(character)).ToArray());

                return cleaned.Length == 0 ? null : cleaned;
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                return null;
            }
        }
    }

    /// <summary>
    /// 把 OCR 文本解析成秒数。
    /// asClock=true（计时区域）：按 mm:ss / hh:mm:ss 时刻解析，冒号被漏读时按定长补："1943"→19:43。
    /// asClock=false（初始值区域）：读到的就是秒数，允许两位小数，如 "34.25"。
    /// </summary>
    public static int? ParseDuration(string? text, bool asClock)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = new StringBuilder();
        bool hasDecimalPoint = false;

        foreach (char character in text)
        {
            if (character is >= '0' and <= '9')
            {
                normalized.Append(character);
            }
            else if (asClock && character is ':' or '：' or '.' or ',' or ';' or '·' or '-')
            {
                // 时刻里的冒号常被识别成这些字符，统一当分隔符
                normalized.Append(':');
            }
            else if (!asClock && character is '.' or '。' && !hasDecimalPoint)
            {
                // 秒数里的小数点，只允许一个
                normalized.Append('.');
                hasDecimalPoint = true;
            }
        }

        string value = normalized.ToString();
        if (value.Length == 0)
        {
            return null;
        }

        // 从右往左依次是秒、分、时，兼容 mm:ss 与 hh:mm:ss
        string[] parts = value.Split(':', StringSplitOptions.RemoveEmptyEntries);

        // 整串都是分隔符（OCR 读出一串标点）时，Split 出来是空数组，不能往下走
        if (parts.Length == 0)
        {
            return null;
        }

        if (parts.Length >= 2)
        {
            if (!int.TryParse(parts[^1], out int seconds) || seconds >= 100)
            {
                return null;
            }

            if (!int.TryParse(parts[^2], out int minutes) || minutes >= 1000)
            {
                return null;
            }

            int hours = 0;
            if (parts.Length >= 3 && (!int.TryParse(parts[^3], out hours) || hours >= 1000))
            {
                return null;
            }

            return (hours * 3600) + (minutes * 60) + seconds;
        }

        string digits = parts[0];

        // 初始值区域：读到的就是秒数，可能带小数，填入时舍弃小数部分
        if (!asClock)
        {
            return decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal secondsValue)
                ? (int)Math.Truncate(secondsValue)
                : null;
        }

        if (!int.TryParse(digits, out int number))
        {
            return null;
        }

        // 计时区域：冒号被漏读时按定长补回来 ss / mmss / hhmmss
        if (digits.Length <= 2)
        {
            return number;
        }

        if (digits.Length <= 4)
        {
            int seconds = number % 100;
            int minutes = number / 100;
            return seconds < 60 ? (minutes * 60) + seconds : null;
        }

        if (digits.Length == 6)
        {
            int seconds = number % 100;
            int minutes = (number / 100) % 100;
            int hours = number / 10000;
            return seconds < 60 && minutes < 60 ? (hours * 3600) + (minutes * 60) + seconds : null;
        }

        return null;
    }
}
