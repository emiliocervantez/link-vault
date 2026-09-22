# LinkVault

Link launcher for Windows 10 and 11. Runs in the tray, keeps your links in groups and opens them in the default browser (or the default app for `mailto:`, files and folders) from a popup menu or a hotkey.

## Features

- The popup opens at the mouse cursor with a global hotkey (default `Ctrl+Alt+L`) or a left click on the tray icon.
- Groups either list their links in the popup under a header, or show only the group name, which opens a submenu.
- Each link can have a name (shown instead of the URL) and its own global hotkey that opens it directly.
- Parameters: `https://www.google.com/search?q={query}` or `{query=default}` asks for the value before opening. Values are URL-encoded. Use `{{` / `}}` for literal braces.
- Website icons (favicons) are fetched in the background and cached in `%LOCALAPPDATA%\LinkVault\icons`. Files and folders show their Windows icon.
- Keyboard in the popup: arrows, `Right`/`Enter` open a submenu, `Left`/`Esc` close it, `Enter` opens a link.
- The popup never takes focus, so the app you are working in keeps it.
- Settings (tray icon double-click, or start LinkVault again): groups, links, hotkeys, start with Windows.

Settings are stored in `%LOCALAPPDATA%\LinkVault\settings.json`.

## Build

Requires the .NET 8 SDK or newer.

```
.\build.cmd            # Debug build + tests
.\build.cmd -Release   # Release build + tests
.\build.cmd -NoTest    # skip tests
```

## Publish (single exe)

```
.\publish.cmd                    # .\publish\LinkVault.exe, .NET bundled (runs anywhere)
.\publish.cmd --bundle=false     # small exe, needs the .NET 8 Desktop Runtime
.\publish.cmd -Output C:\tools   # C:\tools\LinkVault.exe
.\publish.cmd -StopRunning       # stop a LinkVault started from the output folder first
```

## Diagnostics

Start with `--trace` to write `trace.log` next to the settings.
