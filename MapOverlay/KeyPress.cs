using System.Runtime.InteropServices;

namespace Poe2Map;

/// <summary>
///     Tusa bas-birak (AutoKeys.cs). SendInput, tarama kodu ile: oyun klavyeden gelmis gibi gorur. Ctrl / Shift / Alt
///     secildiyse once onlar basilir, en son birakilir. Basili tutma 30 ms: cok kisa basislari oyun kacirabiliyor.
///     Yalnizca oyun penceresi on plandayken cagriliyor.
/// </summary>
internal static class KeyPress
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventExtended = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScanCode = 0x0008;
    private const uint MapVkToVsc = 0;
    private const int HoldMs = 30;

    internal static void Send(Keys keys)
    {
        var key = keys & Keys.KeyCode;
        var modifiers = new List<Keys>();
        if (keys.HasFlag(Keys.Control)) { modifiers.Add(Keys.ControlKey); }
        if (keys.HasFlag(Keys.Shift)) { modifiers.Add(Keys.ShiftKey); }
        if (keys.HasFlag(Keys.Alt)) { modifiers.Add(Keys.Menu); }

        foreach (var m in modifiers) { Key(m, up: false); }
        Key(key, up: false);
        Thread.Sleep(HoldMs);
        Key(key, up: true);
        foreach (var m in Enumerable.Reverse(modifiers)) { Key(m, up: true); }
    }

    private static void Key(Keys key, bool up)
    {
        var scan = (ushort)MapVirtualKey((uint)key, MapVkToVsc);
        var flags = KeyEventScanCode | (up ? KeyEventKeyUp : 0) | (IsExtended(key) ? KeyEventExtended : 0);
        var input = new Input
        {
            Type = InputKeyboard,
            Keyboard = new KeyboardInput { Vk = 0, Scan = scan, Flags = flags },
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
    }

    // Uzatilmis tuslar (ok tuslari, Insert/Delete/Home/End/PageUp/PageDown, sag Ctrl/Alt): tarama kodu E0 onekli.
    private static bool IsExtended(Keys key) => key is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Insert or
        Keys.Delete or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown or Keys.RControlKey or Keys.RMenu or
        Keys.Divide or Keys.NumLock;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    // INPUT birligi: en buyuk uye MOUSEINPUT (32 bayt, x64); klavye uyesi 24 bayt, dolgu ile boyut 40 olmali.
    [StructLayout(LayoutKind.Explicit)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeyboardInput Keyboard;
        [FieldOffset(8)] private MouseInputPad pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputPad
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
