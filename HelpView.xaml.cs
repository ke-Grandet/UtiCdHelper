using System;
using System.Windows;
using System.Windows.Controls;

namespace UtiCdHelper;

public partial class HelpView : UserControl
{
    public HelpView()
    {
        InitializeComponent();
    }

    public event EventHandler? BackRequested;

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }
}
