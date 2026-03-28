#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Interop;

namespace Coclico.Services;

public sealed class KeyboardShortcut
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("modifiers")]
    public string Modifiers { get; set; } = "Ctrl";

    [JsonPropertyName("key")]
    public string Key { get; set; } = "F1";

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("actionType")]
    public string ActionType { get; set; } = "WorkflowPipeline";

    [JsonPropertyName("chainId")]
    public string? ChainId { get; set; }

    [JsonPropertyName("chainName")]
    public string? ChainName { get; set; }

    [JsonIgnore]
    public string DisplayLabel => $"{Modifiers}+{Key}";

    [JsonIgnore]
    public string Description => string.IsNullOrEmpty(ChainName) ? Action : $"Lancer: {ChainName}";
}

public sealed class KeyboardShortcutsService : IDisposable
{
private static readonly string DataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Coclico", "keyboard_shortcuts.json");

    private readonly List<KeyboardShortcut> _shortcuts = new();
    private readonly Dictionary<int, KeyboardShortcut> _registeredIds = new();
    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private int _nextId = 9000;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int WM_HOTKEY = 0x0312;

    public event Action<KeyboardShortcut>? ShortcutTriggered;

    public KeyboardShortcutsService() { }

    public void Initialize(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _hwnd = helper.Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        Load();
        RegisterAll();
    }

    public List<KeyboardShortcut> GetShortcuts() => new(_shortcuts);

    public void AddShortcut(KeyboardShortcut shortcut)
    {
        var existing = _shortcuts.FindIndex(s =>
            s.Modifiers == shortcut.Modifiers && s.Key == shortcut.Key);
        if (existing >= 0)
        {
            UnregisterById(_registeredIds.ContainsKey(existing) ? existing : -1);
            _shortcuts[existing] = shortcut;
        }
        else
        {
            _shortcuts.Add(shortcut);
        }
        Save();
        RegisterAll();
    }

    public void RemoveShortcut(string id)
    {
        var s = _shortcuts.FindIndex(x => x.Id == id);
        if (s < 0) return;
        UnregisterAll();
        _shortcuts.RemoveAt(s);
        Save();
        RegisterAll();
    }

    public void UpdateShortcut(KeyboardShortcut shortcut)
    {
        var idx = _shortcuts.FindIndex(s => s.Id == shortcut.Id);
        if (idx < 0) return;
        UnregisterAll();
        _shortcuts[idx] = shortcut;
        Save();
        RegisterAll();
    }

    private void RegisterAll()
    {
        UnregisterAll();
        foreach (var shortcut in _shortcuts)
        {
            int id = _nextId++;
            uint mods = ParseModifiers(shortcut.Modifiers) | MOD_NOREPEAT;
            uint vk = ParseKey(shortcut.Key);
            if (vk == 0) continue;
            if (RegisterHotKey(_hwnd, id, mods, vk))
                _registeredIds[id] = shortcut;
        }
    }

    private void UnregisterAll()
    {
        foreach (var id in _registeredIds.Keys)
            UnregisterHotKey(_hwnd, id);
        _registeredIds.Clear();
    }

    private void UnregisterById(int id)
    {
        if (_registeredIds.ContainsKey(id))
        {
            UnregisterHotKey(_hwnd, id);
            _registeredIds.Remove(id);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_registeredIds.TryGetValue(id, out var shortcut))
            {
                handled = true;
                Application.Current?.Dispatcher.BeginInvoke(() => ShortcutTriggered?.Invoke(shortcut));
            }
        }
        return IntPtr.Zero;
    }

    private static uint ParseModifiers(string mods)
    {
        uint result = 0;
        var parts = mods.Split('+');
        foreach (var p in parts)
        {
            result |= p.Trim().ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => MOD_CONTROL,
                "ALT" => MOD_ALT,
                "SHIFT" => MOD_SHIFT,
                "WIN" => MOD_WIN,
                _ => 0
            };
        }
        return result;
    }

    private static uint ParseKey(string key)
    {
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c >= 'A' && c <= 'Z') return (uint)c;
            if (c >= '0' && c <= '9') return (uint)c;
        }
        return key.ToUpperInvariant() switch
        {
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
            "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
            "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
            "SPACE" => 0x20, "ENTER" => 0x0D, "TAB" => 0x09,
            "ESCAPE" => 0x1B, "DELETE" => 0x2E, "INSERT" => 0x2D,
            "HOME" => 0x24, "END" => 0x23, "PAGEUP" => 0x21, "PAGEDOWN" => 0x22,
            "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
            _ => 0
        };
    }

    public void Load()
    {
        _shortcuts.Clear();
        try
        {
            if (File.Exists(DataPath))
            {
                var json = File.ReadAllText(DataPath);
                var list = JsonSerializer.Deserialize<List<KeyboardShortcut>>(json);
                if (list != null) _shortcuts.AddRange(list);
            }
        }
        catch (Exception ex) { LoggingService.LogException(ex, "KeyboardShortcutsService.Load"); }
    }

    private static readonly JsonSerializerOptions _saveOptions = new() { WriteIndented = true };

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DataPath)!);
            File.WriteAllText(DataPath, JsonSerializer.Serialize(_shortcuts, _saveOptions));
        }
        catch (Exception ex) { LoggingService.LogException(ex, "KeyboardShortcutsService.Save"); }
    }

    public void Dispose()
    {
        UnregisterAll();
        _hwndSource?.RemoveHook(WndProc);
    }
}
