using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace DynamicIsland.Core.Input;

/// <summary>Modifier keys; values match the Win32 <c>MOD_*</c> flags of <c>RegisterHotKey</c>.</summary>
[Flags]
public enum HotkeyModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Alt.</summary>
    Alt = 0x1,

    /// <summary>Ctrl.</summary>
    Ctrl = 0x2,

    /// <summary>Shift.</summary>
    Shift = 0x4,

    /// <summary>Windows key.</summary>
    Win = 0x8,
}

/// <summary>A global hotkey such as <c>Ctrl+Alt+Space</c>: modifiers plus a Win32 virtual-key code.</summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20,
        ["Enter"] = 0x0D,
        ["Tab"] = 0x09,
        ["Esc"] = 0x1B,
        ["Escape"] = 0x1B,
        ["Insert"] = 0x2D,
        ["Delete"] = 0x2E,
        ["Home"] = 0x24,
        ["End"] = 0x23,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["Left"] = 0x25,
        ["Up"] = 0x26,
        ["Right"] = 0x27,
        ["Down"] = 0x28,
        ["`"] = 0xC0,
    };

    /// <summary>Parses text like <c>Ctrl+Alt+Space</c>, <c>Win+Shift+D</c>, <c>Ctrl+F12</c>.</summary>
    /// <remarks>At least one modifier is required, otherwise the hotkey would swallow normal typing.</remarks>
    public static bool TryParse(string? text, [NotNullWhen(true)] out HotkeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        int? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    modifiers |= HotkeyModifiers.Ctrl;
                    break;
                case "ALT":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "SHIFT":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "WIN" or "WINDOWS":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                default:
                    if (key is not null || !TryParseKey(raw, out var vk))
                    {
                        return false;
                    }

                    key = vk;
                    break;
            }
        }

        if (key is null || modifiers == HotkeyModifiers.None)
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key.Value);
        return true;
    }

    /// <summary>Canonical text form, for example <c>Ctrl+Alt+Space</c>.</summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(KeyName(VirtualKey));
        return string.Join('+', parts);
    }

    private static bool TryParseKey(string text, out int vk)
    {
        vk = 0;
        if (NamedKeys.TryGetValue(text, out vk))
        {
            return true;
        }

        if (text.Length == 1 && char.IsAsciiLetterOrDigit(text[0]))
        {
            vk = char.ToUpperInvariant(text[0]);
            return true;
        }

        if (text.Length is 2 or 3 && (text[0] is 'F' or 'f')
            && int.TryParse(text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 24)
        {
            vk = 0x70 + n - 1;
            return true;
        }

        return false;
    }

    private static string KeyName(int vk)
    {
        foreach (var (name, code) in NamedKeys)
        {
            if (code == vk && name != "Escape")
            {
                return name;
            }
        }

        return vk switch
        {
            >= 0x70 and <= 0x87 => "F" + (vk - 0x70 + 1).ToString(CultureInfo.InvariantCulture),
            _ => ((char)vk).ToString(),
        };
    }
}
