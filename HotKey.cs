using System.Text;
using System.Windows.Input;

namespace UtiCdHelper;

public readonly record struct HotKey(ModifierKeys Modifiers, Key Key)
{
    public static HotKey Default => new(ModifierKeys.Control | ModifierKeys.Shift, Key.D1);

    public bool IsEmpty => Key == Key.None;

    public static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin
            or Key.System;

    public override string ToString()
    {
        var text = new StringBuilder();

        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            text.Append("Ctrl+");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            text.Append("Shift+");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            text.Append("Alt+");
        }

        if (Modifiers.HasFlag(ModifierKeys.Windows))
        {
            text.Append("Win+");
        }

        text.Append(KeyText(Key));
        return text.ToString();
    }

    private static string KeyText(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9)
        {
            return ((int)key - (int)Key.D0).ToString();
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return "Num" + ((int)key - (int)Key.NumPad0);
        }

        return key.ToString();
    }
}
