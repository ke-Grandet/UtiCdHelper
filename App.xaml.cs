using System;
using System.Globalization;
using System.Windows;

namespace UtiCdHelper;

public partial class App : Application
{
    /// <summary>快捷键里"识图"这一项的名字，主界面识图按钮复用同一个动作。</summary>
    public const string PickActionName = "识图";

    private const string TimerActionPrefix = "启动计时";
    private const string ShoutActionPrefix = "喊话";

    private ProcessWatcher? _watcher;
    private HotKeyManager? _hotKeyManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 兜底：打包运行时出异常也别静默退出，先把原因弹出来
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                args.Exception.ToString(),
                "UtiCdHelper 运行时异常",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            args.Handled = true;
        };

        var overlayWindow = new OverlayWindow();

        var hotKeySettings = HotKeySettings.Load();
        var recognitionSettings = RecognitionSettings.Load();
        var overlaySettings = OverlaySettings.Load();
        var cellValues = new CellValues();
        var dotValues = new DotValues();
        var countdown = new CountdownService(dotValues, recognitionSettings);
        var starter = new CountdownStarter(recognitionSettings, cellValues, countdown);
        overlayWindow.Bind(dotValues);

        Func<string, Action> actionFactory = name =>
        {
            if (name == PickActionName)
            {
                return () => PickRecognitionTarget(recognitionSettings, cellValues);
            }

            int timerIndex = ParseIndexedName(name, TimerActionPrefix);
            if (timerIndex >= 0)
            {
                return starter.ForIndex(timerIndex);
            }

            int shoutIndex = ParseIndexedName(name, ShoutActionPrefix);
            if (shoutIndex >= 0)
            {
                return starter.ShoutForIndex(shoutIndex);
            }

            return overlayWindow.ToggleVisibility;
        };

        var mainWindow = new MainWindow(hotKeySettings, recognitionSettings, overlaySettings, cellValues, dotValues, starter, actionFactory);
        MainWindow = mainWindow;
        mainWindow.AttachOverlay(overlayWindow);
        mainWindow.Show();
        overlayWindow.Owner = mainWindow;

        _hotKeyManager = new HotKeyManager(mainWindow);
        mainWindow.AttachHotKeyManager(_hotKeyManager);

        Dispatcher.BeginInvoke(
            new Action(recognitionSettings.WarmUp),
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        // 盯哪个进程由默认配置决定，没填就用内置的兜底名
        string? processName = DefaultsSettings.Current.FollowProcessName;
        _watcher = new ProcessWatcher(
            TimeSpan.FromSeconds(1),
            string.IsNullOrWhiteSpace(processName) ? DefaultsSettings.BuiltInFollowProcessName : processName);

        _watcher.StateChanged += state => ApplyProcessState(overlayWindow, state);
        ApplyProcessState(overlayWindow, _watcher.State);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watcher?.Dispose();
        _hotKeyManager?.Dispose();
        base.OnExit(e);
    }

    /// <summary>识图一次并弹出选格窗口，识别结果由窗口自己显示。</summary>
    public static void PickRecognitionTarget(RecognitionSettings settings, CellValues cellValues)
    {
        var picker = new PickTargetWindow(cellValues) { Owner = Current?.MainWindow };

        // 识别放到后台：窗口先出来，识别完再回填，点击识图不再卡住界面
        RecognizeIntoAsync(settings, picker);

        picker.ShowDialog();
    }

    /// <summary>后台识别，算完回到 UI 线程把结果填进已经显示的选格窗口。</summary>
    private static void RecognizeIntoAsync(RecognitionSettings settings, PickTargetWindow picker)
    {
        Task.Run(() => (Value: settings.Recognize(), Name: settings.RecognizeName()))
            .ContinueWith(
                task =>
                {
                    // 窗口已经关掉了就没必要回填
                    if (task.IsFaulted || !picker.IsLoaded)
                    {
                        return;
                    }

                    picker.SetResult(task.Result.Value, task.Result.Name);
                },
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>把快捷键名（如"启动计时3"、"喊话2"）换算成圆点下标，名字对不上则返回 -1。</summary>
    private static int ParseIndexedName(string name, string prefix)
    {
        if (!name.StartsWith(prefix, StringComparison.Ordinal))
        {
            return -1;
        }

        if (!int.TryParse(
                name.AsSpan(prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int number))
        {
            return -1;
        }

        return number >= 1 && number <= 5 ? number - 1 : -1;
    }

    /// <summary>跟随的进程有窗口才显示悬浮窗，最小化或没在跑就藏起来。</summary>
    private static void ApplyProcessState(OverlayWindow overlay, ProcessState state)
    {
        if (state == ProcessState.Normal)
        {
            overlay.Show();
        }
        else
        {
            overlay.Hide();
        }
    }
}
