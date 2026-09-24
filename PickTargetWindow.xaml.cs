using System;
using System.Windows;
using System.Windows.Input;

namespace UtiCdHelper;

public partial class PickTargetWindow : Window
{
    private int _newValue;
    private bool _hasValue;
    private string? _newName;

    /// <summary>识别还没回来：窗口先弹出来，避免点击识图后界面卡住。</summary>
    private bool _pending = true;

    public PickTargetWindow(CellValues cells)
    {
        InitializeComponent();

        CellList.ItemsSource = cells.Cells;
        Refresh();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    /// <summary>识别完成后回填结果，后台线程算完再回到 UI 线程调用。</summary>
    public void SetResult(int? newValue, string? newName)
    {
        _pending = false;
        _hasValue = newValue.HasValue;
        _newValue = newValue ?? 0;
        _newName = newName;

        Refresh();
    }

    private void Refresh()
    {
        TitleText.Text = _pending
            ? "识别中…"
            : _hasValue
                ? $"识别结果：{_newValue}秒"
                    + (_newName is null ? string.Empty : $"，名称「{_newName}」")
                : "未识别到数字，请检查区域与模板";

        CellList.IsEnabled = _hasValue;
    }

    private void OnCellClick(object sender, RoutedEventArgs e)
    {
        if (!_hasValue)
        {
            Close();
            return;
        }

        if (sender is FrameworkElement element && element.DataContext is CellValue cell)
        {
            cell.Value = _newValue;

            // 读到了新名字就覆盖，读不到就保留格子原有的名字
            if (_newName is not null)
            {
                cell.Name = _newName;
            }
        }

        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
