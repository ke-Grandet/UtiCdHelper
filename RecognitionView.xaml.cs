using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace UtiCdHelper;

public partial class RecognitionView : UserControl
{
    private readonly DispatcherTimer _pollInfoTimer;

    public RecognitionView(RecognitionSettings settings, CountdownService countdown)
    {
        InitializeComponent();

        AreaHost.Children.Add(new RecognitionAreaView(settings, RecognitionAreaKind.Name));
        AreaHost.Children.Add(new RecognitionAreaView(settings, RecognitionAreaKind.Initial) { ShowSeparator = true });
        AreaHost.Children.Add(new RecognitionAreaView(settings, RecognitionAreaKind.Timer) { ShowSeparator = true });

        _pollInfoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _pollInfoTimer.Tick += (_, _) => PollInfo.Text = countdown.DescribePoll();

        Loaded += (_, _) =>
        {
            PollInfo.Text = countdown.DescribePoll();
            _pollInfoTimer.Start();
        };
        Unloaded += (_, _) => _pollInfoTimer.Stop();
    }

    public event EventHandler? BackRequested;

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }
}
