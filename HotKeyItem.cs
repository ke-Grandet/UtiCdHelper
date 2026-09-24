using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Input;

namespace UtiCdHelper;

public sealed class HotKeyItem : INotifyPropertyChanged
{
    private HotKey _hotKey;
    private bool _isRegistered;

    public HotKeyItem(string name, string group, HotKey hotKey)
    {
        Name = name;
        Group = group;
        _hotKey = hotKey;
    }

    public string Name { get; }

    public string Group { get; }

    public bool IsGroupStart { get; set; }

    public HotKey HotKey
    {
        get => _hotKey;
        set
        {
            _hotKey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public string DisplayText => _hotKey.IsEmpty ? "未设置" : _hotKey.ToString();

    public bool IsRegistered
    {
        get => _isRegistered;
        set
        {
            _isRegistered = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText => _isRegistered ? string.Empty : "已被占用";

    public int RegistrationId { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class HotKeySettings
{
    public const ModifierKeys DefaultModifiers = ModifierKeys.Control | ModifierKeys.Shift;

    private const string FileName = "utiCdHelper快捷键设置.json";

    private static readonly (string Name, string Group, Key Key)[] Defaults =
    {
        ("识图", "识图", Key.F),
        ("启动计时1", "启动计时", Key.D1),
        ("启动计时2", "启动计时", Key.D2),
        ("启动计时3", "启动计时", Key.D3),
        ("启动计时4", "启动计时", Key.D4),
        ("启动计时5", "启动计时", Key.D5),
        ("喊话1", "喊话", Key.Q),
        ("喊话2", "喊话", Key.W),
        ("喊话3", "喊话", Key.E),
        ("喊话4", "喊话", Key.R),
        ("喊话5", "喊话", Key.T),
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public ObservableCollection<HotKeyItem> Items { get; } = new();

    /// <summary>代码内置的默认快捷键，外部默认配置文件为空时用它兜底。</summary>
    public static IReadOnlyList<(string Name, string Group, Key Key)> BuiltInDefaults => Defaults;

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static HotKeySettings Load()
    {
        var settings = new HotKeySettings();

        foreach ((string name, string group, ModifierKeys modifiers, Key key) in ResolveDefaults())
        {
            settings.Items.Add(new HotKeyItem(name, group, new HotKey(modifiers, key)));
        }

        settings.LoadFromFile();
        settings.MarkGroupStarts();
        return settings;
    }

    private void MarkGroupStarts()
    {
        for (int i = 1; i < Items.Count; i++)
        {
            if (Items[i].Group != Items[i - 1].Group)
            {
                Items[i].IsGroupStart = true;
            }
        }
    }

    public void Save()
    {
        try
        {
            var configs = Items
                .Select(item => new HotKeyConfig(item.Name, item.HotKey.Modifiers.ToString(), item.HotKey.Key.ToString()))
                .ToList();

            File.WriteAllText(FilePath, JsonSerializer.Serialize(configs, SerializerOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // 写入失败不影响使用，下次启动回退到默认值
        }
    }

    public void RegisterAll(HotKeyManager manager, Func<string, Action> actionFactory)
    {
        foreach (HotKeyItem item in Items)
        {
            Apply(manager, item, actionFactory);
        }
    }

    public void Apply(HotKeyManager manager, HotKeyItem item, Func<string, Action> actionFactory)
    {
        manager.Unregister(item.RegistrationId);
        item.RegistrationId = 0;

        Action action = actionFactory(item.Name);

        if (manager.TryRegister(item.HotKey, action, out int id))
        {
            item.RegistrationId = id;
            item.IsRegistered = true;
        }
        else
        {
            item.IsRegistered = false;
        }
    }

    public static HotKey GetDefault(string name)
    {
        foreach ((string itemName, string _, ModifierKeys modifiers, Key key) in ResolveDefaults())
        {
            if (itemName == name)
            {
                return new HotKey(modifiers, key);
            }
        }

        return new HotKey(ModifierKeys.None, Key.None);
    }

    /// <summary>默认快捷键来自外部默认配置文件；那边没配就退回代码内置的那套。</summary>
    private static List<(string Name, string Group, ModifierKeys Modifiers, Key Key)> ResolveDefaults()
    {
        var result = new List<(string Name, string Group, ModifierKeys Modifiers, Key Key)>();

        foreach (HotKeyDefault item in DefaultsSettings.Current.HotKeys)
        {
            if (string.IsNullOrWhiteSpace(item.Name) || !Enum.TryParse(item.Key, out Key key))
            {
                continue;
            }

            ModifierKeys modifiers = Enum.TryParse(item.Modifiers, out ModifierKeys parsed) ? parsed : DefaultModifiers;
            result.Add((item.Name, string.IsNullOrWhiteSpace(item.Group) ? item.Name : item.Group, modifiers, key));
        }

        if (result.Count > 0)
        {
            return result;
        }

        foreach ((string name, string group, Key key) in Defaults)
        {
            result.Add((name, group, DefaultModifiers, key));
        }

        return result;
    }

    private void LoadFromFile()
    {
        try
        {
            string path = FilePath;
            if (!File.Exists(path))
            {
                Save();
                return;
            }

            List<HotKeyConfig>? configs = JsonSerializer.Deserialize<List<HotKeyConfig>>(File.ReadAllText(path));
            if (configs is null)
            {
                return;
            }

            foreach (HotKeyConfig config in configs)
            {
                HotKeyItem? item = Items.FirstOrDefault(candidate => candidate.Name == config.Name);
                if (item is null)
                {
                    continue;
                }

                if (Enum.TryParse(config.Modifiers, out ModifierKeys modifiers) && Enum.TryParse(config.Key, out Key key))
                {
                    item.HotKey = new HotKey(modifiers, key);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // 文件缺失或损坏时使用默认值
        }
    }
}

public sealed record HotKeyConfig(string Name, string Modifiers, string Key);
