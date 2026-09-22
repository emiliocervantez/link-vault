using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LinkVault.Core;
using LinkVault.Native;
using LinkVault.Services;
using static LinkVault.Native.NativeMethods;

namespace LinkVault.Popup;

/// <summary>
/// The Popup: Links of Inline Groups under a header, Collapsed Groups as "Name ▸" rows that open a submenu.
/// Neither window takes focus; while the Popup is open a low-level keyboard hook feeds it navigation keys
/// and swallows them (see ADR 1).
/// </summary>
internal sealed class LinkPopup
{
    private const int PageStep = 8;
    private const double IconSize = 16;
    private const double LabelMaxWidth = 600;

    private readonly Func<Settings> _settings;
    private readonly FaviconCache _icons;
    private readonly Action<Link> _onLinkChosen;
    private readonly PopupWindow _main;
    private readonly PopupWindow _sub;
    private readonly PopupInputHooks _hooks;
    private Group? _openGroup;

    public LinkPopup(Func<Settings> settings, FaviconCache icons, Action<Link> onLinkChosen)
    {
        _settings = settings;
        _icons = icons;
        _onLinkChosen = onLinkChosen;

        _main = new PopupWindow();
        _main.RowClicked += Activate;
        _main.RowHovered += row =>
        {
            if (row.Tag is Group g) OpenSub(g, selectFirst: false);
            else CloseSub();
        };

        _sub = new PopupWindow();
        _sub.RowClicked += Activate;

        _hooks = new PopupInputHooks(_main.Dispatcher);
        _hooks.KeyDown += OnKey;
        _hooks.SwitchChord += Close;      // Alt+Tab and friends: close immediately, Windows handles the chord
        _hooks.ClickedOutside += Close;
        _hooks.ForegroundChanged += Close;
    }

    public bool IsOpen => _hooks.Installed;

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    private void Open()
    {
        InputSender.MaskHeldModifiers();   // first thing: the hotkey's Alt/Win must not read as a lone tap to the app underneath
        GetCursorPos(out var pt);
        _main.SetRows(MainRows(), -1);
        _main.SelectFirst();
        _main.ShowAt(pt);
        _hooks.Install(p => _main.Contains(p) || _sub.Contains(p));
        Trace.Log($"popup open at {pt.X},{pt.Y}; foreground {Trace.Foreground()}");
    }

    private void Close()
    {
        if (!IsOpen) return;
        _hooks.Uninstall();
        CloseSub();
        _main.Hide();
    }

    private void OpenSub(Group group, bool selectFirst)
    {
        if (_openGroup != group || !_sub.IsVisible)
        {
            var row = _main.SelectedRow;
            _sub.SetRows(group.Links.Select(LinkRow).ToList(), -1);
            _sub.ShowBeside(_main.ScreenRect(), row is null ? _main.ScreenRect().Top : _main.RowScreenTop(row));
            _openGroup = group;
        }
        if (selectFirst) _sub.SelectFirst();
    }

    private void CloseSub()
    {
        _sub.Hide();
        _openGroup = null;
    }

    // ---- rows ----

    private List<Row> MainRows()
    {
        var rows = new List<Row>();
        var previousInline = false;
        foreach (var group in _settings().Groups.Where(g => g.Links.Count > 0))
        {
            // Inline Groups are set apart by separators; consecutive Collapsed Groups sit together.
            if (rows.Count > 0 && (group.ShowInline || previousInline)) rows.Add(Separator());
            if (group.ShowInline)
            {
                rows.Add(Header(group.Name));
                rows.AddRange(group.Links.Select(LinkRow));
            }
            else
            {
                rows.Add(GroupRow(group));
            }
            previousInline = group.ShowInline;
        }
        if (rows.Count == 0) rows.Add(Info("(no links yet - add some in Settings)"));
        return rows;
    }

    private static Row Info(string text) => new() { Content = new TextBlock { Text = text }, Selectable = false };

    private static Row Separator() => new()
    {
        Content = new Rectangle { Height = 1, Fill = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)) },
        Selectable = false,
        IsSeparator = true,
    };

    private static Row Header(string name) => new()
    {
        Content = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = LabelMaxWidth + IconSize },
        Selectable = false,
        FontWeight = FontWeights.Bold,
    };

    private Row LinkRow(Link link)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Image
        {
            Source = _icons.For(link),
            Width = IconSize,
            Height = IconSize,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        grid.Children.Add(icon);
        var label = new TextBlock
        {
            Text = link.Label,
            MaxWidth = LabelMaxWidth,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        if (link.PopupKey is { } key)
        {
            var keyText = new TextBlock
            {
                Text = KeyNames.Name(key),
                Foreground = Brushes.Gray,
                Margin = new Thickness(24, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(keyText, 2);
            grid.Children.Add(keyText);
        }
        return new Row { Content = grid, Tag = link, ToolTip = link.Url };
    }

    private static Row GroupRow(Group group)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = group.Name,
            Margin = new Thickness(IconSize + 8, 0, 24, 0),
            MaxWidth = LabelMaxWidth,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var arrow = new TextBlock { Text = "▸" };
        Grid.SetColumn(arrow, 1);
        grid.Children.Add(arrow);
        return new Row { Content = grid, Tag = group };
    }

    // ---- input ----

    private bool IsPopupHotkey(int vk)
    {
        var hk = _settings().PopupHotkey;
        if (vk != hk.VirtualKey) return false;
        static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
        var mods = HotkeyModifiers.None;
        if (Down(VK_CONTROL)) mods |= HotkeyModifiers.Control;
        if (Down(VK_MENU)) mods |= HotkeyModifiers.Alt;
        if (Down(VK_SHIFT)) mods |= HotkeyModifiers.Shift;
        if (Down(VK_LWIN) || Down(VK_RWIN)) mods |= HotkeyModifiers.Win;
        return mods == hk.Modifiers;
    }

    private void OnKey(int vk)
    {
        if (!IsOpen) return;
        var inSub = _sub.IsVisible;
        var active = inSub ? _sub : _main;
        switch (vk)
        {
            case 0x1B:                                                    // Esc
                if (inSub) CloseSub(); else Close();
                return;
            case 0x25:                                                    // Left
                if (inSub) CloseSub();
                return;
            case 0x27:                                                    // Right
                if (!inSub && _main.SelectedRow?.Tag is Group g) OpenSub(g, selectFirst: true);
                return;
            case 0x26: active.MoveSelection(-1); return;                  // Up
            case 0x28: active.MoveSelection(+1); return;                  // Down
            case 0x21: active.MoveSelection(-PageStep); return;           // PageUp
            case 0x22: active.MoveSelection(+PageStep); return;           // PageDown
            case 0x24: active.SelectFirst(); return;                      // Home
            case 0x23: active.SelectLast(); return;                       // End
            case 0x0D: case 0x20:                                         // Enter, Space
                if (active.SelectedRow is { } row) Activate(row);
                return;
        }

        if (IsPopupHotkey(vk)) { Close(); return; }

        // Popup Keys work from anywhere in the Popup, including for Links of Collapsed Groups.
        if (!ModifierHeld() && _settings().AllLinks.FirstOrDefault(l => l.PopupKey == vk) is { } link)
        {
            Close();
            _onLinkChosen(link);
        }
        // every other key is swallowed by the hook and ignored here, like a menu would
    }

    /// <summary>Ctrl, Alt or Win held: Popup Keys are bare keys (Shift is ignored).</summary>
    private static bool ModifierHeld()
    {
        static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
        return Down(VK_CONTROL) || Down(VK_MENU) || Down(VK_LWIN) || Down(VK_RWIN);
    }

    private void Activate(Row row)
    {
        switch (row.Tag)
        {
            case Link link:
                Close();
                _onLinkChosen(link);
                break;
            case Group group:
                OpenSub(group, selectFirst: true);
                break;
        }
    }
}
