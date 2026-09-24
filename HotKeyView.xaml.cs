using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace UtiCdHelper;

public partial class HotKeyView : UserControl
{
    private readonly HotKeySettings _settings;
    private readonly Func<string, Action> _actionFactory;
    private HotKeyManager? _manager;

    public HotKeyView(HotKeySettings settings, Func<string, Action> actionFactory)
    {
        _settings = settings;
        _actionFactory = actionFactory;

        InitializeComponent();
        ItemList.ItemsSource = settings.Items;
    }

    public event EventHandler? BackRequested;

    public void Attach(HotKeyManager manager)
    {
        _manager = manager;
        _settings.RegisterAll(manager, _actionFactory);
    }

    private void Apply(HotKeyItem item)
    {
        if (_manager is null)
        {
            return;
        }

        _settings.Apply(_manager, item, _actionFactory);
        _settings.Save();
    }

    private void OnHotKeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        if (_manager is null || sender is not FrameworkElement element || element.DataContext is not HotKeyItem item)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.Back or Key.Delete)
        {
            item.HotKey = new HotKey(ModifierKeys.None, Key.None);
            Apply(item);
            return;
        }

        if (key is Key.Escape || HotKey.IsModifierKey(key))
        {
            return;
        }

        item.HotKey = new HotKey(Keyboard.Modifiers, key);
        Apply(item);
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not HotKeyItem item)
        {
            return;
        }

        item.HotKey = HotKeySettings.GetDefault(item.Name);
        Apply(item);
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }
}
