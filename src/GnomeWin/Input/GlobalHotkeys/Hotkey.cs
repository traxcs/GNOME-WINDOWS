using System.Globalization;

namespace GnomeWin.Input.GlobalHotkeys;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Super = 1,
    Ctrl = 2,
    Alt = 4,
    Shift = 8,
}

public readonly record struct Hotkey(HotkeyModifiers Modifiers, int Key)
{
    public bool IsSuperAlone => Modifiers == HotkeyModifiers.Super && Key == 0;
    public bool IsValid => Key != 0 || IsSuperAlone;

    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tab"] = 0x09, ["Enter"] = 0x0D, ["Return"] = 0x0D, ["Escape"] = 0x1B, ["Esc"] = 0x1B,
        ["Space"] = 0x20, ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["End"] = 0x23, ["Home"] = 0x24,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Backspace"] = 0x08,
        ["Grave"] = 0xC0, ["`"] = 0xC0, ["Minus"] = 0xBD, ["Plus"] = 0xBB, ["Comma"] = 0xBC, ["Period"] = 0xBE,
        ["PrintScreen"] = 0x2C, ["Pause"] = 0x13,
    };

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = HotkeyModifiers.None;
        int key = 0;
        foreach (string raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "super": case "win": case "windows": case "meta": mods |= HotkeyModifiers.Super; continue;
                case "ctrl": case "control": mods |= HotkeyModifiers.Ctrl; continue;
                case "alt": mods |= HotkeyModifiers.Alt; continue;
                case "shift": mods |= HotkeyModifiers.Shift; continue;
            }
            if (key != 0) return false;
            key = ParseKey(raw);
            if (key == 0) return false;
        }
        hotkey = new Hotkey(mods, key);
        return hotkey.IsValid && (key == 0 || mods != HotkeyModifiers.None || IsFunctionKey(key));
    }

    private static bool IsFunctionKey(int vk) => vk >= 0x70 && vk <= 0x87;

    private static int ParseKey(string s)
    {
        if (NamedKeys.TryGetValue(s, out int vk)) return vk;
        if (s.Length == 1)
        {
            char c = char.ToUpperInvariant(s[0]);
            if (c is >= 'A' and <= 'Z' || c is >= '0' and <= '9') return c;
        }
        if ((s[0] == 'F' || s[0] == 'f') && int.TryParse(s.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int f) && f is >= 1 and <= 24)
            return 0x70 + f - 1;
        if (s.StartsWith("vk", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex) && hex is > 0 and < 256)
            return hex;
        return 0;
    }

    public static string KeyName(int vk)
    {
        foreach (var kv in NamedKeys)
            if (kv.Value == vk && kv.Key.Length > 1 && kv.Key != "Return" && kv.Key != "Esc") return kv.Key;
        if (vk is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x70 + 1);
        return "vk" + vk.ToString("X2", CultureInfo.InvariantCulture);
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Super)) parts.Add("Super");
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Key != 0) parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }
}

public readonly record struct HotkeyBinding(Hotkey Hotkey, ShellAction Action);
