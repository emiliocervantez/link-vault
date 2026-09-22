using System.Runtime.InteropServices;
using LinkVault.Native;
using static LinkVault.Native.NativeMethods;

namespace LinkVault.Services;

internal static class InputSender
{
    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>
    /// Call right after the popup hotkey fired. The popup does not take focus, so the app underneath sees the
    /// hotkey's modifiers go down and up with nothing in between; a lone Alt puts a Win32 app into menu-bar mode
    /// and a lone Win opens Start. Tapping Ctrl while the modifier is still held makes the release not "lone".
    /// </summary>
    public static void MaskHeldModifiers()
    {
        if (!(Down(VK_MENU) || Down(VK_LWIN) || Down(VK_RWIN))) return;
        Send(Key(VK_CONTROL, up: false), Key(VK_CONTROL, up: true));
        Trace.Log("input: masked held Alt/Win with a Ctrl tap");
    }

    private static void Send(params INPUT[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

    private static INPUT Key(int vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                wVk = (ushort)vk,
                wScan = (ushort)MapVirtualKey((uint)vk, 0),
                dwFlags = up ? KEYEVENTF_KEYUP : 0,
            },
        },
    };
}
