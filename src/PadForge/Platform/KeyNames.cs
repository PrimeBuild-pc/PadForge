using PadForge.Core.Input;

namespace PadForge.Platform;

public static class KeyNames
{
    public static string Describe(int virtualKey)
    {
        if (virtualKey == 0)
            return "Unbound";

        return virtualKey switch
        {
            VirtualKeyCodes.MouseLeft => "Mouse left",
            VirtualKeyCodes.MouseRight => "Mouse right",
            VirtualKeyCodes.MouseMiddle => "Mouse middle",
            VirtualKeyCodes.MouseX1 => "Mouse back",
            VirtualKeyCodes.MouseX2 => "Mouse forward",
            VirtualKeyCodes.Tab => "Tab",
            VirtualKeyCodes.Enter => "Enter",
            VirtualKeyCodes.Escape => "Escape",
            VirtualKeyCodes.Space => "Space",
            VirtualKeyCodes.Left => "Left",
            VirtualKeyCodes.Up => "Up",
            VirtualKeyCodes.Right => "Right",
            VirtualKeyCodes.Down => "Down",
            VirtualKeyCodes.LeftShift => "Left Shift",
            VirtualKeyCodes.LeftControl => "Left Ctrl",
            0x08 => "Backspace",
            0x10 => "Shift",
            0x11 => "Ctrl",
            0x12 => "Alt",
            0x14 => "Caps Lock",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x23 => "End",
            0x24 => "Home",
            0x2D => "Insert",
            0x2E => "Delete",
            0xA1 => "Right Shift",
            0xA3 => "Right Ctrl",
            0xA4 => "Left Alt",
            0xA5 => "Right Alt",
            >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
            >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
            >= 0x70 and <= 0x87 => "F" + (virtualKey - 0x6F),
            _ => "Key " + virtualKey.ToString("X2"),
        };
    }
}
