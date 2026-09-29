using System.Runtime.InteropServices;
using LinkVault.Native;
using static LinkVault.Native.NativeMethods;

namespace LinkVault.Services;

/// <summary>
/// Keeps the first popup after a quiet period fast. Windows slows background processes (EcoQoS power
/// throttling) and trims their idle memory; a hotkey-driven tray app must not pay for that on the next keypress.
/// </summary>
internal static class Responsiveness
{
    /// <summary>Tells Windows to keep scheduling this process at normal speed even when it has been idle.</summary>
    public static void OptOutOfPowerThrottling()
    {
        var state = new PROCESS_POWER_THROTTLING_STATE
        {
            Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
            ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
            StateMask = 0,   // control the flag, and set it to "off"
        };
        var ok = SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        Trace.Log(ok ? "power throttling: opted out" : $"power throttling: opt-out failed (error {Marshal.GetLastWin32Error()})");
    }
}
