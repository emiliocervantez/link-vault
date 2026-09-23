# LinkVault Context

LinkVault is a Windows tray tool that keeps a user's links in groups and opens them from a popup menu or a hotkey.

## Glossary

| Term | Meaning |
|---|---|
| **Link** | A stored target that can be opened: a web address, a `mailto:` or other URI, or a local file or folder path. Has a URL, an optional Name and an optional Link Hotkey. Belongs to exactly one Group. |
| **Name** | Optional text shown for a Link instead of its URL. A Link without a Name is shown by its URL. |
| **Group** | A named, ordered list of Links. Groups are one level deep: a Group never contains another Group. Groups themselves are ordered. |
| **Divider** | A line placed between Links of a Group, in the Group's order, shown in the Popup wherever the Group's Links are listed. Not a Link: it has no URL, hotkey or key and cannot be chosen. A Group with only Dividers counts as empty. |
| **Inline Group** | A Group whose Links are listed in the Popup itself, under a header with the Group's name, or without that header when the Group's name is hidden. |
| **Collapsed Group** | A Group shown in the Popup only by its name with an arrow; the name opens a submenu listing its Links. |
| **Popup** | The menu listing the Groups and Links, opened at the mouse cursor with the Popup Hotkey or a click on the tray icon. Empty Groups are not shown. Rows show the site's icon. |
| **Popup Hotkey** | The global key combination that opens and closes the Popup. |
| **Link Hotkey** | A global key combination that Opens one Link directly, without the Popup. |
| **Popup Key** | A single key without modifiers (e.g. `Y`) that acts while the Popup is open. On a Link it Opens the Link, whether its Group is Inline or Collapsed. On a Group it selects the Group: the first Link of an Inline Group, or the name of a Collapsed Group with its submenu opened. Unique across all Links and Groups; shown on the row. Navigation keys cannot be Popup Keys. |
| **Url Template** | A Link's URL when it contains Parameters. |
| **Parameter** | A placeholder in a Url Template, written `{name}` or `{name=default}`. The same name used twice is one Parameter. `{{` and `}}` are literal braces. |
| **Default** | The value a Parameter's field starts with in the Input Window. |
| **Input Window** | The window that asks for the value of every Parameter before a Link with Parameters is Opened. Enter opens, Esc cancels. Entered values are URL-encoded and not remembered. |
| **Open** | Handing the Link, with its Parameters filled in, to Windows, which starts the default handler (the default browser for web links). |
