using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shapes;

namespace UtiCdHelper;

public partial class RegionSelectorWindow : Window
{
    private Point _start;
    private bool _selecting;

    public RegionSelectorWindow()
    {
        InitializeComponent();

        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        KeyDown += OnKeyDown;
    }

    public Rect? SelectedRect { get; private set; }

    public static Rect? Select(Window owner)
    {
        var selector = new RegionSelectorWindow { Owner = owner };
        selector.ShowDialog();
        return selector.SelectedRect;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(this);
        _selecting = true;

        Selection.Visibility = Visibility.Visible;
        UpdateSelection(_start);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_selecting)
        {
            return;
        }

        UpdateSelection(e.GetPosition(this));
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_selecting)
        {
            return;
        }

        _selecting = false;
        UpdateSelection(e.GetPosition(this));

        Rect rect = CurrentRect();
        if (rect.Width < 2 || rect.Height < 2)
        {
            SelectedRect = null;
            Close();
            return;
        }

        Point topLeft = PointToScreen(rect.TopLeft);
        Point bottomRight = PointToScreen(rect.BottomRight);
        SelectedRect = new Rect(topLeft, bottomRight);
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SelectedRect = null;
            Close();
        }
    }

    private void UpdateSelection(Point current)
    {
        Rect rect = new(_start, current);

        System.Windows.Controls.Canvas.SetLeft(Selection, rect.X);
        System.Windows.Controls.Canvas.SetTop(Selection, rect.Y);
        Selection.Width = rect.Width;
        Selection.Height = rect.Height;

        Hint.Text = $"按住鼠标左键框选区域，松开确认，Esc 取消    ({Math.Abs((int)rect.Width)} x {Math.Abs((int)rect.Height)})";
    }

    private Rect CurrentRect() => new(_start, Mouse.GetPosition(this));
}
