using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Interop;

namespace UtiCdHelper;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;

    private const double HoverOpacity = 0.45;

    // 扫掠检测步长，需小于响应区域的最小边(窗口高 42px)，保证快速划过时不漏检
    private const double SweepStep = 8.0;

    private readonly TextBlock[] _dotTexts;
    private readonly Border[] _dotBorders;
    private readonly Brush _idleBrush;
    private readonly Brush _runningBrush;
    private readonly DispatcherTimer _hoverTimer;

    private Rect _hitRect;
    private bool _isHovered;
    private Point _lastCursor;
    private bool _hasLastCursor;

    // 调节模式：开着的时候浮窗可以接管鼠标，用来拖动摆位
    private IntPtr _handle;
    private OverlaySettings? _appliedSettings;
    private bool _adjusting;
    private bool _resizing;
    private bool _shownByAdjustMode;
    private Point _pressScreen;
    private double _pressWidth;

    public OverlayWindow()
    {
        InitializeComponent();

        _dotTexts = new[] { DotText0, DotText1, DotText2, DotText3, DotText4 };
        _dotBorders = new[] { Dot0, Dot1, Dot2, Dot3, Dot4 };
        _idleBrush = (Brush)Application.Current.Resources["DotIdleBrush"];
        _runningBrush = (Brush)Application.Current.Resources["DotRunningBrush"];

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _hoverTimer.Tick += OnHoverTimerTick;

        Loaded += (_, _) => RefreshHitRect();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                RefreshHitRect();
                _hoverTimer.Start();
            }
            else
            {
                _hoverTimer.Stop();
                ResetHoverState();
            }
        };
        LocationChanged += OnLocationChanged;
        SizeChanged += (_, _) => RefreshHitRect();
        Closed += (_, _) => _hoverTimer.Stop();

        PreviewMouseLeftButtonDown += OnPreviewLeftButtonDown;
        PreviewMouseMove += OnPreviewMouseMove;
        PreviewMouseLeftButtonUp += OnPreviewLeftButtonUp;
        PreviewMouseWheel += OnPreviewMouseWheel;

        // 拖动后焦点可能留在浮窗上，得把按键转给主窗口去微调
        PreviewKeyDown += (_, args) =>
        {
            if (_adjusting)
            {
                AdjustKeyDown?.Invoke(this, args);
            }
        };

        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
    }

    /// <summary>按摆放参数重排圆点：整体坐标取自 (X, Y)，圆点之间留 Spacing 的空隙，圆点高度为 Size。</summary>
    public void ApplySettings(OverlaySettings settings)
    {
        _appliedSettings = settings;

        int size = Math.Clamp(settings.Size, OverlaySettings.MinSize, OverlaySettings.MaxSize);
        int spacing = Math.Clamp(settings.Spacing, OverlaySettings.MinSpacing, OverlaySettings.MaxSpacing);
        int dotWidth = OverlaySettings.DotWidth(size);

        // 内边距沿用原来的 7×2 比例，跟着圆点一起放大缩小
        double padX = size * 7.0 / OverlaySettings.DefaultSize;
        double padY = size * 2.0 / OverlaySettings.DefaultSize;

        for (int i = 0; i < _dotBorders.Length; i++)
        {
            Border dot = _dotBorders[i];

            dot.MinWidth = dotWidth;
            dot.MaxWidth = dotWidth;
            dot.Height = size;
            dot.CornerRadius = new CornerRadius(size / 3.0);
            dot.Padding = new Thickness(padX, padY, padX, padY);

            // 只在圆点之间插空隙，最后一个不留尾部空白
            dot.Margin = new Thickness(0, 0, i == _dotBorders.Length - 1 ? 0 : spacing, 0);
        }

        Width = OverlaySettings.TotalWidth(size, spacing);
        Height = size + 16;

        Left = settings.MirrorX ? OverlaySettings.MirrorLeft(settings.X, Width) : settings.X;
        Top = settings.Y;
    }

    /// <summary>摆放参数被鼠标或键盘改过了，界面上的数字要跟着变。</summary>
    public event EventHandler? OverlayChanged;

    /// <summary>调节模式下收到的按键，交给主窗口做方向键微调。</summary>
    public event EventHandler<KeyEventArgs>? AdjustKeyDown;

    /// <summary>进入调节模式：接管鼠标，浮窗可以拖着摆位置。</summary>
    public void EnterAdjustMode()
    {
        if (_adjusting)
        {
            return;
        }

        if (_handle == IntPtr.Zero)
        {
            _handle = new WindowInteropHelper(this).EnsureHandle();
        }

        _adjusting = true;
        SetInputTransparent(false);
        IsHitTestVisible = true;

        // 调节时鼠标一直在浮窗上，悬停淡出会把浮窗压暗，这里先停掉并复位透明度
        _hoverTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1.0;

        Root.Cursor = Cursors.SizeAll;
        AdjustFrame.Visibility = Visibility.Visible;

        // 调节时看不见就没得调，先把浮窗显示出来，退出时还原成原来的状态
        if (!IsVisible)
        {
            _shownByAdjustMode = true;
            Show();
        }
    }

    /// <summary>退出调节模式：恢复鼠标穿透，浮窗重新变成只能看的覆盖层。</summary>
    public void ExitAdjustMode()
    {
        if (!_adjusting)
        {
            return;
        }

        _adjusting = false;
        _resizing = false;
        ReleaseMouseCapture();

        SetInputTransparent(true);
        IsHitTestVisible = false;
        AdjustFrame.Visibility = Visibility.Collapsed;
        Root.Cursor = null;

        _appliedSettings?.Save();

        if (_shownByAdjustMode)
        {
            _shownByAdjustMode = false;
            Hide();
        }
        else if (IsVisible)
        {
            _hoverTimer.Start();
        }
    }

    /// <summary>WS_EX_TRANSPARENT：加上鼠标直接穿过，去掉才能接收点击。</summary>
    private void SetInputTransparent(bool transparent)
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        int exStyle = GetWindowLong(_handle, GwlExStyle);
        exStyle = transparent ? exStyle | WsExTransparent : exStyle & ~WsExTransparent;
        SetWindowLong(_handle, GwlExStyle, exStyle);
    }

    private void OnPreviewLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_adjusting)
        {
            return;
        }

        e.Handled = true;
        _pressScreen = PointToScreen(e.GetPosition(this));
        _pressWidth = ActualWidth;
        _resizing = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (_resizing)
        {
            // 改大小靠自己算位移：DragMove 会把整窗带着走
            CaptureMouse();
            Root.Cursor = Cursors.SizeWE;
            return;
        }

        DragMove();
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_adjusting || !_resizing || _appliedSettings is null)
        {
            return;
        }

        Point current = PointToScreen(e.GetPosition(this));
        int size = OverlaySettings.SizeFromTotalWidth(_pressWidth + (current.X - _pressScreen.X), _appliedSettings.Spacing);
        _appliedSettings.Size = Math.Clamp(size, OverlaySettings.MinSize, OverlaySettings.MaxSize);

        ApplySettings(_appliedSettings);
        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPreviewLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_adjusting)
        {
            return;
        }

        e.Handled = true;
        _resizing = false;
        ReleaseMouseCapture();
        Root.Cursor = Cursors.SizeAll;
        _appliedSettings?.Save();
    }

    /// <summary>滚轮改间距：一次 1 像素，按住 Shift 一次 5 像素。</summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_adjusting || _appliedSettings is null)
        {
            return;
        }

        e.Handled = true;

        int step = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? 5 : 1;
        _appliedSettings.Spacing = Math.Clamp(
            _appliedSettings.Spacing + (e.Delta > 0 ? step : -step),
            OverlaySettings.MinSpacing,
            OverlaySettings.MaxSpacing);

        _appliedSettings.Save();
        ApplySettings(_appliedSettings);
        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        RefreshHitRect();

        if (!_adjusting || _appliedSettings is null)
        {
            return;
        }

        // 拖动过程中只改内存里的值并回播，存盘留到松手或退出调节时做，避免边拖边写文件
        // 镜像开着时要先把实际位置反算回配置值，否则每拖一次就把配置值翻到另一边
        int left = (int)Math.Round(Left);
        _appliedSettings.X = _appliedSettings.MirrorX ? OverlaySettings.MirrorLeft(left, ActualWidth) : left;
        _appliedSettings.Y = (int)Math.Round(Top);
        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 把浮窗的五个圆点绑到倒计时数据上：倒计时中显示剩余秒数，空闲时绿色且不显示数字。
    /// </summary>
    public void Bind(DotValues dots)
    {
        foreach (DotValue dot in dots.Dots)
        {
            Apply(dot);
            dot.PropertyChanged += (_, _) => Apply(dot);
        }
    }

    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    private void Apply(DotValue dot)
    {
        if (dot.Index < 0 || dot.Index >= _dotTexts.Length)
        {
            return;
        }

        // 圆点显示的是剩余秒数，位数不定，不做截断；空闲时 DisplayText 就是空串
        _dotTexts[dot.Index].Text = dot.DisplayText;

        _dotBorders[dot.Index].Background = dot.IsRunning ? _runningBrush : _idleBrush;
    }

    private void RefreshHitRect()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        Point topLeft = PointToScreen(new Point(0, 0));
        Point bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
        _hitRect = new Rect(topLeft, bottomRight);
    }

    private void OnHoverTimerTick(object? sender, EventArgs e)
    {
        if (!GetCursorPos(out POINT cursor))
        {
            return;
        }

        var current = new Point(cursor.X, cursor.Y);
        Point from = _hasLastCursor ? _lastCursor : current;
        _lastCursor = current;
        _hasLastCursor = true;

        bool hovered = IsHit(_hitRect, from, current);
        if (hovered == _isHovered)
        {
            return;
        }

        _isHovered = hovered;
        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(hovered ? HoverOpacity : 1.0, TimeSpan.FromMilliseconds(150)));
    }

    private void ResetHoverState()
    {
        if (!_isHovered)
        {
            return;
        }

        _isHovered = false;
        BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, TimeSpan.Zero));
    }

    private static bool IsHit(Rect rect, Point from, Point to)
    {
        if (rect.Contains(to))
        {
            return true;
        }

        double distance = (to - from).Length;
        if (distance < SweepStep)
        {
            return false;
        }

        int steps = (int)(distance / SweepStep);
        for (int i = 1; i < steps; i++)
        {
            double t = (double)i / steps;
            var point = new Point(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
            if (rect.Contains(point))
            {
                return true;
            }
        }

        return false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _handle = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(_handle, GwlExStyle);

        // 调节模式会临时摘掉 WS_EX_TRANSPARENT，这里跟着当前状态决定要不要加回去
        bool transparent = !_adjusting;
        exStyle = transparent ? exStyle | WsExTransparent : exStyle & ~WsExTransparent;

        SetWindowLong(_handle, GwlExStyle, exStyle | WsExToolWindow);
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
