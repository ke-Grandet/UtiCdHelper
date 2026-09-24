using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace UtiCdHelper;

public partial class MainWindow : Window
{
    private readonly CountdownStarter _starter;
    private readonly RecognitionSettings _recognitionSettings;
    private readonly OverlaySettings _overlaySettings;
    private readonly CellValues _cellValues;
    private readonly Func<string, Action> _actionFactory;
    private readonly MainView _mainView;
    private readonly HotKeyView _hotKeyView;
    private readonly RecognitionView _recognitionView;
    private readonly OverlayPositionView _positionView;
    private readonly HelpView _helpView;

    private OverlayWindow? _overlay;

    public MainWindow(
        HotKeySettings hotKeySettings,
        RecognitionSettings recognitionSettings,
        OverlaySettings overlaySettings,
        CellValues cellValues,
        DotValues dotValues,
        CountdownStarter starter,
        Func<string, Action> actionFactory)
    {
        _starter = starter;
        _recognitionSettings = recognitionSettings;
        _overlaySettings = overlaySettings;
        _cellValues = cellValues;
        _actionFactory = actionFactory;

        InitializeComponent();

        _mainView = new MainView(cellValues, dotValues, starter);
        _hotKeyView = new HotKeyView(hotKeySettings, actionFactory);
        _recognitionView = new RecognitionView(recognitionSettings, starter.Countdown);
        _positionView = new OverlayPositionView(overlaySettings);
        _helpView = new HelpView();

        _mainView.HotKeyRequested += (_, _) => Navigate(_hotKeyView);
        _mainView.RecognitionRequested += (_, _) => Navigate(_recognitionView);
        _mainView.StartRequested += (_, index) => _mainView.SetStatus(_starter.Start(index));
        _mainView.ShoutRequested += (_, index) => _mainView.SetStatus("已复制：" + _starter.Shout(index));
        _mainView.PickRequested += (_, _) => RunPick();
        _mainView.HelpRequested += (_, _) => Navigate(_helpView);
        _mainView.PositionRequested += (_, _) => Navigate(_positionView);
        _mainView.MirrorXRequested += (_, mirror) =>
        {
            _overlaySettings.MirrorX = mirror;
            ApplyOverlay();
        };
        _hotKeyView.BackRequested += (_, _) => Navigate(_mainView);
        _recognitionView.BackRequested += (_, _) => Navigate(_mainView);
        _helpView.BackRequested += (_, _) => Navigate(_mainView);
        _positionView.BackRequested += (_, _) => Navigate(_mainView);
        _positionView.OverlayChanged += (_, _) => ApplyOverlay();

        Host.Content = _mainView;

        Width = SystemParameters.PrimaryScreenWidth / 2;
        Height = SystemParameters.PrimaryScreenHeight / 2;
    }

    public void AttachHotKeyManager(HotKeyManager manager)
    {
        _hotKeyView.Attach(manager);
    }

    /// <summary>把悬浮窗接到"调节位置"界面：一进去先按存档摆好，之后每改一项立刻重排。</summary>
    public void AttachOverlay(OverlayWindow overlay)
    {
        _overlay = overlay;
        _overlay.ApplySettings(_overlaySettings);
        _overlay.OverlayChanged += (_, _) => _positionView.Refresh();

        // 调节时焦点可能落在浮窗上，键盘微调要两边都能收
        _overlay.AdjustKeyDown += (_, args) => TryNudge(args);
    }

    /// <summary>切换界面，顺带决定悬浮窗要不要进入可拖动的调节模式。</summary>
    private void Navigate(UserControl view)
    {
        Host.Content = view;

        if (view == _positionView)
        {
            _overlay?.EnterAdjustMode();
        }
        else
        {
            _overlay?.ExitAdjustMode();
        }
    }

    private void ApplyOverlay() => _overlay?.ApplySettings(_overlaySettings);

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        // 焦点在输入框里时方向键要用来移动光标，不当作微调
        if (Host.Content == _positionView && e.OriginalSource is not TextBox)
        {
            TryNudge(e);
        }
    }

    /// <summary>摆 Arrange 微调：方向键移动，Shift 改间距/大小，Ctrl 加大步长。</summary>
    private void TryNudge(KeyEventArgs e)
    {
        if (_overlay is null)
        {
            return;
        }

        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        int step = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control ? 10 : 1;

        switch (e.Key)
        {
            case Key.Left:
                if (shift)
                {
                    ChangeSpacing(-1);
                }
                else
                {
                    ChangeX(-step);
                }

                break;
            case Key.Right:
                if (shift)
                {
                    ChangeSpacing(1);
                }
                else
                {
                    ChangeX(step);
                }

                break;
            case Key.Up:
                if (shift)
                {
                    ChangeSize(1);
                }
                else
                {
                    ChangeY(-step);
                }

                break;
            case Key.Down:
                if (shift)
                {
                    ChangeSize(-1);
                }
                else
                {
                    ChangeY(step);
                }

                break;
            default:
                return;
        }

        e.Handled = true;
        ApplyOverlay();
        _positionView.Refresh();

        // 长按重发时不必每次都落盘
        if (!e.IsRepeat)
        {
            _overlaySettings.Save();
        }
    }

    private void ChangeX(int delta) =>
        _overlaySettings.X = ClampCoordinate(_overlaySettings.X + delta);

    private void ChangeY(int delta) =>
        _overlaySettings.Y = ClampCoordinate(_overlaySettings.Y + delta);

    private void ChangeSpacing(int delta) =>
        _overlaySettings.Spacing = Math.Clamp(
            _overlaySettings.Spacing + delta,
            OverlaySettings.MinSpacing,
            OverlaySettings.MaxSpacing);

    private void ChangeSize(int delta) =>
        _overlaySettings.Size = Math.Clamp(
            _overlaySettings.Size + delta,
            OverlaySettings.MinSize,
            OverlaySettings.MaxSize);

    private static int ClampCoordinate(int value) =>
        Math.Clamp(value, OverlaySettings.MinCoordinate, OverlaySettings.MaxCoordinate);

    /// <summary>识图：结果直接显示在选格窗口里，主界面提示行不参与。</summary>
    private void RunPick()
    {
        _mainView.SetStatus(string.Empty);
        App.PickRecognitionTarget(_recognitionSettings, _cellValues);
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
