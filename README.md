# LinkVault

Link launcher for Windows 10 and 11. Runs in the tray, keeps your links in groups and opens them in the default browser (or the default app for `mailto:`, files and folders) from a popup menu or a hotkey.

## Features

- The popup opens at the mouse cursor with a global hotkey (default `Ctrl+Alt+L`) or a left click on the tray icon.
- Groups either list their links in the popup under a header, or show only the group name, which opens a submenu. A group that lists its links can hide its name ("Hide group name").
- Dividers split the links of a group into sections: select a link in Settings and click `Divider` to insert a line after it, then move it with the arrows.
- Each link can have a name (shown instead of the URL) and its own global hotkey that opens it directly.
- Each link can also have a popup key: a single key (e.g. `Y`) that opens it while the popup is open, shown on the right of its row. Groups can have a popup key too: it selects the group's first link, or opens its submenu when its links are hidden.
- Parameters: `https://www.google.com/search?q={query}` or `{query=default}` asks for the value before opening. Values are URL-encoded. Use `{{` / `}}` for literal braces.
- Website icons (favicons) are fetched in the background and cached in `%LOCALAPPDATA%\LinkVault\icons`. Files and folders show their Windows icon.
- Keyboard in the popup: arrows, `Shift+Down`/`Shift+Up` or `PageDown`/`PageUp` jump to the next/previous group, `Right`/`Enter` open a submenu, `Left`/`Esc` close it, `Enter` opens a link.
- The popup never takes focus, so the app you are working in keeps it.
- Settings (tray icon double-click, or start LinkVault again): groups, links, hotkeys, start with Windows.

Settings are stored in `%LOCALAPPDATA%\LinkVault\settings.json`.

## Link targets

The URL field takes anything Windows can open. It is handed to the shell as is, as if you had typed it into the Run dialog (`Win+R`). A new link starts with `https://`; replace it with any of these:

| Target | Example | Opens in |
|---|---|---|
| Web page | `https://www.youtube.com/` or `http://intranet:8080/wiki` | default browser |
| E-mail | `mailto:someone@example.com?subject=Hello` | default mail app |
| Folder | `C:\Users\me\Documents` or `\\server\share\team` | File Explorer |
| File | `C:\Users\me\notes.md` or `file:///C:/Users/me/notes.md` | app associated with the file type |
| Program | `C:\Windows\System32\notepad.exe` | the program itself |
| Windows settings page | `ms-settings:display` | Settings |
| Any app protocol | `vscode://file/C:/src/app`, `slack://open`, `tel:+3725551234` | app registered for that protocol |

- Use full paths. A relative path is resolved against LinkVault's working folder, not yours.
- Programs cannot get command-line arguments: the whole field is one target.
- Parameters work in every kind of target, but their values are always URL-encoded. That suits web addresses; in a file path, a value like `my notes` becomes `my%20notes`.
- Write `{{` and `}}` for literal braces, because `{` starts a Parameter.
- Icons: web pages show the site's favicon, and files and folders show their Windows icon. Everything else, including any link whose host or path contains a Parameter, shows the generic link icon.

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
