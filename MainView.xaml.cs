using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace UtiCdHelper;

public partial class MainView : UserControl
{
    private readonly CellValues _cellValues;
    private readonly CountdownStarter _starter;

    public MainView(CellValues cellValues, DotValues dotValues, CountdownStarter starter)
    {
        _cellValues = cellValues;
        _starter = starter;

        InitializeComponent();
        CellList.ItemsSource = cellValues.Cells;
        StartList.ItemsSource = dotValues.Dots;
        VersionText.Text = "v" + ReadVersion();
    }

    public event EventHandler? HotKeyRequested;

    public event EventHandler? RecognitionRequested;

    /// <summary>请求启动第 index 路倒计时。</summary>
    public event EventHandler<int>? StartRequested;

    /// <summary>请求把第 index 格的就绪时刻复制到剪贴板。</summary>
    public event EventHandler<int>? ShoutRequested;

    /// <summary>请求打开使用说明。</summary>
    public event EventHandler? HelpRequested;

    /// <summary>请求打开悬浮窗调节位置界面。</summary>
    public event EventHandler? PositionRequested;

    /// <summary>勾选态变化：参数为是否镜像悬浮窗横坐标。</summary>
    public event EventHandler<bool>? MirrorXRequested;

    /// <summary>请求执行一次识图（识别初始值区域并选格填入）。</summary>
    public event EventHandler? PickRequested;

    /// <summary>版本号取程序集的信息版本，去掉末尾的 +提交哈希 之类后缀。</summary>
    private static string ReadVersion()
    {
        string? informational = typeof(MainView).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrEmpty(informational))
        {
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? informational[..plus] : informational;
        }

        return typeof(MainView).Assembly.GetName().Version?.ToString() ?? "1.0.0";
    }

    /// <summary>在提示行显示一条操作反馈。</summary>
    public void SetStatus(string text) => StatusText.Text = text;

    /// <summary>方格只接受数字：三位数以内的非负整数。</summary>
    private void OnCellPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        foreach (CellValue cell in _cellValues.Cells)
        {
            cell.Value = 0;
            cell.Name = string.Empty;
        }

        // 复位同时中断所有正在进行的倒计时
        _starter.StopAll();

        SetStatus("已清零五个数字与名字，并中断所有倒计时");
    }

    private void OnHotKeyClick(object sender, RoutedEventArgs e)
    {
        HotKeyRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnRecognitionClick(object sender, RoutedEventArgs e)
    {
        RecognitionRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is DotValue dot)
        {
            StartRequested?.Invoke(this, dot.Index);
        }
    }

    private void OnShoutClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is DotValue dot)
        {
            ShoutRequested?.Invoke(this, dot.Index);
        }
    }

    private void OnPickClick(object sender, RoutedEventArgs e)
    {
        PickRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnHelpClick(object sender, RoutedEventArgs e)
    {
        HelpRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnPositionClick(object sender, RoutedEventArgs e)
    {
        PositionRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnMirrorClick(object sender, RoutedEventArgs e)
    {
        MirrorXRequested?.Invoke(this, MirrorBox.IsChecked == true);
    }
}
