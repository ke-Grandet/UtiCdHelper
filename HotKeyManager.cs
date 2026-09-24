using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace UtiCdHelper;

public sealed class HotKeyManager : IDisposable
{
    private const int WmHotKey = 0x0312;

    private const int ModAlt = 0x0001;
    private const int ModControl = 0x0002;
    private const int ModShift = 0x0004;
    private const int ModWin = 0x0008;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 0x1000;

    public HotKeyManager(Window window)
    {
        _source = (HwndSource)PresentationSource.FromVisual(window)
                  ?? throw new InvalidOperationException("窗口句柄尚未创建，请在 Show() 之后再创建 HotKeyManager。");

        _source.AddHook(WndProc);
    }

    public bool TryRegister(HotKey hotKey, Action action, out int id)
    {
        id = 0;

        if (hotKey.IsEmpty)
        {
            return false;
        }

        int candidate = _nextId++;
        if (!RegisterHotKey(_source.Handle, candidate, ToModifiers(hotKey.Modifiers), KeyInterop.VirtualKeyFromKey(hotKey.Key)))
        {
            return false;
        }

        _actions[candidate] = action;
        id = candidate;
        return true;
    }

    public void Unregister(int id)
    {
        if (id == 0)
        {
            return;
        }

        UnregisterHotKey(_source.Handle, id);
        _actions.Remove(id);
    }

    public void Dispose()
    {
        foreach (int id in new List<int>(_actions.Keys))
        {
            Unregister(id);
        }

        _source.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && _actions.TryGetValue(wParam.ToInt32(), out Action? action))
        {
            action();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static int ToModifiers(ModifierKeys modifiers)
    {
        int result = 0;

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            result |= ModAlt;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            result |= ModControl;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            result |= ModShift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            result |= ModWin;
        }

        return result;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
