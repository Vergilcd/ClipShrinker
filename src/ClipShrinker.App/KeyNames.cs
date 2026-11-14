using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using ClipShrinker.Core;

namespace ClipShrinker.App;

/// <summary>WPF keys as the key names 1.0.8 saved in settings.json / presets.json (Tk keysyms, lower case), so
/// shortcuts and preset hotkeys chosen in 1.0.8 keep working. A key that types a character is named after the
/// character on the current keyboard layout, like Tk does (Shift+1 = "exclam" on a US layout).</summary>
public static class KeyNames
{
    static readonly Dictionary<Key, string> Special = new()
    {
        [Key.Space] = "space", [Key.Return] = "return", [Key.Tab] = "tab", [Key.Escape] = "escape",
        [Key.Back] = "backspace", [Key.Delete] = "delete", [Key.Insert] = "insert", [Key.Home] = "home", [Key.End] = "end",
        [Key.PageUp] = "prior", [Key.PageDown] = "next", [Key.Left] = "left", [Key.Right] = "right", [Key.Up] = "up",
        [Key.Down] = "down", [Key.Add] = "kp_add", [Key.Subtract] = "kp_subtract", [Key.Multiply] = "kp_multiply",
        [Key.Divide] = "kp_divide", [Key.Decimal] = "kp_decimal", [Key.Pause] = "pause", [Key.PrintScreen] = "print",
        [Key.Apps] = "app",
    };

    static readonly HashSet<Key> Modifiers =
    [
        Key.LeftShift, Key.RightShift, Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt, Key.LWin, Key.RWin,
        Key.CapsLock, Key.NumLock, Key.Scroll, Key.ImeProcessed, Key.DeadCharProcessed,
    ];

    [DllImport("user32.dll")] static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, StringBuilder buf, int n, uint flags, IntPtr layout);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);

    /// <summary>The character key k types with Shift (or not) on the current layout, without changing dead-key state.</summary>
    static char? CharOf(Key k, bool shift)
    {
        int vk = KeyInterop.VirtualKeyFromKey(k);
        if (vk == 0) return null;
        var state = new byte[256];
        if (shift) state[0x10] = 0x80;
        var sb = new StringBuilder(8);
        // flag 4: do not change the keyboard state (Windows 10 1607+), so dead keys keep working in text fields
        int n = ToUnicodeEx((uint)vk, MapVirtualKey((uint)vk, 0), state, sb, sb.Capacity, 4, GetKeyboardLayout(0));
        return n == 1 && !char.IsControl(sb[0]) ? sb[0] : null;
    }

    /// <summary>The key name without modifiers ("s", "1", "f2", "comma", "exclam"); null for a bare modifier key.</summary>
    public static string? Name(KeyEventArgs e)
    {
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Modifiers.Contains(k)) return null;
        if (k is >= Key.NumPad0 and <= Key.NumPad9) return ((int)(k - Key.NumPad0)).ToString(CultureInfo.InvariantCulture);   // KP_1 -> 1
        if (k is >= Key.F1 and <= Key.F24) return k.ToString().ToLowerInvariant();
        if (Special.TryGetValue(k, out var s)) return s;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (CharOf(k, shift) is { } c && Text.KeysymForChar(c) is { } name) return name;
        return k switch
        {
            >= Key.A and <= Key.Z => k.ToString().ToLowerInvariant(),
            >= Key.D0 and <= Key.D9 => ((int)(k - Key.D0)).ToString(CultureInfo.InvariantCulture),
            _ => k.ToString().ToLowerInvariant(),
        };
    }

    /// <summary>"ctrl+alt+shift+key" like the reference chord_from_event (null for a bare modifier).</summary>
    public static string? Chord(KeyEventArgs e)
    {
        if (Name(e) is not { } name) return null;
        var mods = new List<string>();
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) mods.Add("ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) mods.Add("alt");
        if (m.HasFlag(ModifierKeys.Shift)) mods.Add("shift");
        return string.Join("+", mods.Append(name));
    }
}
