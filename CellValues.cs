using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace UtiCdHelper;

/// <summary>数值显示格式：按 #.## 输出，多余的 0 去掉。</summary>
public static class NumberText
{
    public static string Format(decimal value) =>
        Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>
/// 方格里的初始值。统一在这里做约束，手工输入和识图填入走同一条路：
/// 三位数以内的非负整数（0-999）。识图读到的带小数，在填入前已截断取整。
/// </summary>
public sealed class CellValue : INotifyPropertyChanged
{
    public const int MaxValue = 999;

    private int _value;
    private string _name = string.Empty;

    public CellValue(int index)
    {
        Index = index;
    }

    public int Index { get; }

    /// <summary>识图读到的汉字名字，显示在方格上方。空串表示没有记录。</summary>
    public string Name
    {
        get => _name;
        set
        {
            string next = value ?? string.Empty;

            if (_name == next)
            {
                return;
            }

            _name = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasName));
        }
    }

    public bool HasName => _name.Length > 0;

    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, 0, MaxValue);

            if (_value == clamped)
            {
                return;
            }

            _value = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    /// <summary>按 #.## 显示的文本，供选格弹窗等只读场景使用。</summary>
    public string DisplayText => NumberText.Format(Value);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class CellValues
{
    public ObservableCollection<CellValue> Cells { get; } =
        new(Enumerable.Range(0, 5).Select(index => new CellValue(index)));
}
