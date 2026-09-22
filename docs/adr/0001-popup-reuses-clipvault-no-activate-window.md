# 1. The popup reuses ClipVault's non-activating window and low-level hooks

Date: 2026-09-23

## Status

Accepted.

## Context

LinkVault's popup (links, inline group headers, collapsed groups with submenus) could be built in two ways:

1. A WPF `ContextMenu` on an invisible anchor window that takes focus. Submenus, keyboard navigation and close-on-click-outside come for free.
2. ClipVault's `PopupWindow`: a plain WPF window with `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST` that never takes focus. While it is open, low-level keyboard and mouse hooks and a foreground win-event hook deliver input to it (ClipVault ADR 3).

ClipVault needs option 2 because it pastes into the previous window, which must keep focus. LinkVault opens a browser and pastes nothing, so option 1 would work too, and it is less code.

## Decision

Option 2: copy ClipVault's `PopupWindow` and `PopupInputHooks`. Submenus for Collapsed Groups are a second `PopupWindow` placed beside the parent row. `LinkPopup` owns navigation between the two windows: Right/Enter/hover open the submenu, and Left/Esc close it.

Reasons:

- The code is already proven in daily use: placement, DPI, hover-after-movement, and hook cleanup on every close path.
- The app underneath keeps focus and keeps its own transient popups (quick-open widgets, autocomplete) while the user looks at the links.
- The popup looks and behaves the same as ClipVault's, so both tools feel alike.

## Consequences

- LinkVault owns submenu placement and keyboard routing, which a `ContextMenu` would provide.
- Link Hotkeys do not fire while the Popup is open: the keyboard hook swallows every key before `RegisterHotKey` sees it. Only the Popup Hotkey is recognised there, and it closes the Popup.
- The Input Window for Parameters does take focus. It opens only after the Popup has closed.
