using System.Windows;
using LinkVault.Config;
using LinkVault.Core;
using LinkVault.Popup;
using LinkVault.Services;
using LinkVault.Tray;
using CoreSettings = LinkVault.Core.Settings;

namespace LinkVault;

public partial class App : Application
{
    private const string MutexName = @"Local\LinkVault.SingleInstance";
    private const string ShowSettingsEventName = @"Local\LinkVault.ShowSettings";

    private Mutex? _mutex;
    private MessageWindow? _messages;
    private VaultStorage? _storage;
    private CoreSettings _settings = new();
    private HotkeyManager? _hotkeys;
    private FaviconCache? _icons;
    private LinkPopup? _popup;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out var isFirst);
        var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        if (!isFirst)
        {
            showSettings.Set();   // tell the running instance to open Settings
            Shutdown();
            return;
        }
        ThreadPool.RegisterWaitForSingleObject(showSettings, (_, _) => Dispatcher.BeginInvoke(ShowSettings), null, -1, false);

        _storage = new VaultStorage(VaultStorage.DefaultRoot());
        if (e.Args.Contains("--trace", StringComparer.OrdinalIgnoreCase))
            Trace.Enable(System.IO.Path.Combine(_storage.RootDir, "trace.log"), Dispatcher);

        _settings = _storage.LoadSettings();
        _messages = new MessageWindow();
        _hotkeys = new HotkeyManager(_messages);
        _icons = new FaviconCache(_storage.IconsDir);
        _popup = new LinkPopup(() => _settings, _icons, OpenLink);
        _tray = new TrayIcon(
            // Deferred: the tray click itself must finish (and its foreground change settle) before the popup's hooks go in.
            showPopup: () => Dispatcher.BeginInvoke(() => _popup.Toggle(), System.Windows.Threading.DispatcherPriority.ApplicationIdle),
            showSettings: ShowSettings,
            exit: Shutdown);

        var failures = RegisterHotkeys(_settings, _settings);
        if (failures.Count > 0) _tray.Notify("LinkVault hotkeys", string.Join("\n", failures));
        _icons.Fetch(_settings.AllLinks, force: false);
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, ApplySettings, links => _icons!.Fetch(links, force: true));
        _settingsWindow.Show();
    }

    /// <summary>Applies new settings. Hotkeys that Windows refuses fall back to their previous values or are cleared.</summary>
    private IReadOnlyList<string> ApplySettings(CoreSettings next)
    {
        var failures = RegisterHotkeys(next, _settings);
        _settings = next;
        _storage!.SaveSettings(next);
        StartupRegistry.Set(next.StartWithWindows);
        _icons!.Fetch(next.AllLinks, force: false);
        return failures;
    }

    private List<string> RegisterHotkeys(CoreSettings settings, CoreSettings previous)
    {
        var failures = new List<string>();
        _hotkeys!.UnregisterAll();

        if (!_hotkeys.TryRegister(settings.PopupHotkey, () => _popup!.Toggle()))
        {
            var fallback = previous.PopupHotkey;
            if (!fallback.Equals(settings.PopupHotkey) && _hotkeys.TryRegister(fallback, () => _popup!.Toggle()))
            {
                failures.Add($"Popup hotkey {settings.PopupHotkey} is in use by another application. Kept {fallback}.");
                settings.PopupHotkey = fallback;
            }
            else
            {
                failures.Add($"Popup hotkey {settings.PopupHotkey} is in use by another application. Open Settings from the tray icon to choose another.");
            }
        }

        foreach (var link in settings.AllLinks)
        {
            if (link.Hotkey is not { IsEmpty: false } hotkey) continue;
            if (_hotkeys.TryRegister(hotkey, () => OpenLink(link))) continue;
            failures.Add($"Hotkey {hotkey} for link \"{link.Label}\" is in use by another application. The link has no hotkey now.");
            link.Hotkey = null;
        }
        return failures;
    }

    private void OpenLink(Link link)
    {
        if (LinkOpener.Open(link) is { } problem) _tray?.Notify("LinkVault", problem);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _messages?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
