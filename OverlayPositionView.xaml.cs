using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace UtiCdHelper;

public partial class OverlayPositionView : UserControl
{
    private readonly OverlaySettings _settings;

    public OverlayPositionView(OverlaySettings settings)
    {
        _settings = settings;

        InitializeComponent();
        Loaded += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>请求返回主界面。</summary>
    public event EventHandler? BackRequested;

    /// <summary>摆放参数已写入设置，悬浮窗需要按新值重排。</summary>
    public event EventHandler? OverlayChanged;

    /// <summary>把设置里的值回写到输入框（正在编辑的框不打断）。</summary>
    public void Refresh()
    {
        SetText(BoxX, _settings.X);
        SetText(BoxY, _settings.Y);
        SetText(BoxSpacing, _settings.Spacing);
        SetText(BoxSize, _settings.Size);
    }

    private static void SetText(TextBox box, int value)
    {
        if (box.IsKeyboardFocused)
        {
            return;
        }

        box.Text = value.ToString(CultureInfo.InvariantCulture);
    }

    private void OnValueChanged(object sender, RoutedEventArgs e)
    {
        _settings.X = Parse(BoxX.Text, _settings.X, OverlaySettings.MinCoordinate, OverlaySettings.MaxCoordinate);
        _settings.Y = Parse(BoxY.Text, _settings.Y, OverlaySettings.MinCoordinate, OverlaySettings.MaxCoordinate);
        _settings.Spacing = Parse(BoxSpacing.Text, _settings.Spacing, OverlaySettings.MinSpacing, OverlaySettings.MaxSpacing);
        _settings.Size = Parse(BoxSize.Text, _settings.Size, OverlaySettings.MinSize, OverlaySettings.MaxSize);

        _settings.Save();
        Refresh();
        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        _settings.ResetDefaults();
        _settings.Save();
        Refresh();
        OverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>坐标允许负号开头，其余只允许数字。</summary>
    private void OnNumberPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        bool accepted = e.Text.All(char.IsDigit)
            || (sender is TextBox box && e.Text == "-" && box.SelectionStart == 0 && !box.Text.Contains('-'));

        e.Handled = !accepted;
    }

    private static int Parse(string text, int fallback, int min, int max) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? Math.Clamp(value, min, max)
            : fallback;
}
