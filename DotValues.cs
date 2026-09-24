using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace UtiCdHelper;

/// <summary>
/// 浮窗上的单个圆点。与方格数字解耦：这里存的是倒计时剩余值，不是用户设定的初始值。
/// </summary>
public sealed class DotValue : INotifyPropertyChanged
{
    private int _value;
    private bool _isRunning;

    public DotValue(int index)
    {
        Index = index;
    }

    public int Index { get; }

    public int Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning == value)
            {
                return;
            }

            _isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    /// <summary>未在倒计时中：圆点显示绿色且隐藏数字。</summary>
    public bool IsIdle => !IsRunning;

    public string DisplayText => IsRunning ? NumberText.Format(Value) : string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class DotValues
{
    public ObservableCollection<DotValue> Dots { get; } =
        new(Enumerable.Range(0, 5).Select(index => new DotValue(index)));
}
