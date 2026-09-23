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
/// The Popup: Links of Inline Groups under a header, Collapsed Groups and Subgroups as "Name ▸" rows that open a
/// submenu. Up to three windows cascade: main, a submenu, and a Subgroup's submenu inside a Collapsed Group's.
/// None of them takes focus; while the Popup is open a low-level keyboard hook feeds it navigation keys
/// and swallows them (see ADR 1).
/// </summary>
internal sealed class LinkPopup
{
    private const double IconSize = 16;
    private const double LabelMaxWidth = 600;

    private readonly Func<Settings> _settings;
    private readonly FaviconCache _icons;
    private readonly Action<Link> _onLinkChosen;
    private const int Levels = 3;

    /// <summary>[0] is the main window; [n] shows the items of <see cref="_openGroups"/>[n], beside the selected row of [n - 1].</summary>
    private readonly PopupWindow[] _levels = new PopupWindow[Levels];
    private readonly Group?[] _openGroups = new Group?[Levels];
    private readonly PopupInputHooks _hooks;
    /// <summary>Each shown Group with the main-window index of its first selectable row, in order.</summary>
    private readonly List<(Group Group, int Row)> _groupStarts = new();

    public LinkPopup(Func<Settings> settings, FaviconCache icons, Action<Link> onLinkChosen)
    {
        _settings = settings;
        _icons = icons;
        _onLinkChosen = onLinkChosen;

        for (var i = 0; i < Levels; i++)
        {
            var level = i;
            var window = new PopupWindow();
            window.RowClicked += row => Activate(level, row);
            window.RowHovered += row =>
            {
                if (row.Tag is Group g) OpenSub(level, g, selectFirst: false);
                else CloseFrom(level + 1);
            };
            _levels[i] = window;
        }

        _hooks = new PopupInputHooks(Main.Dispatcher);
        _hooks.KeyDown += OnKey;
        _hooks.SwitchChord += Close;      // Alt+Tab and friends: close immediately, Windows handles the chord
        _hooks.ClickedOutside += Close;
        _hooks.ForegroundChanged += Close;
    }

    private PopupWindow Main => _levels[0];

    public bool IsOpen => _hooks.Installed;

    /// <summary>The deepest visible window: keyboard navigation acts there.</summary>
    private int ActiveLevel
    {
        get
        {
            for (var i = Levels - 1; i > 0; i--)
                if (_levels[i].IsVisible) return i;
            return 0;
        }
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    private void Open()
    {
        InputSender.MaskHeldModifiers();   // first thing: the hotkey's Alt/Win must not read as a lone tap to the app underneath
        GetCursorPos(out var pt);
        Main.SetRows(MainRows(), -1);
        Main.SelectFirst();
        Main.ShowAt(pt);
        _hooks.Install(p => _levels.Any(w => w.Contains(p)));
        Trace.Log($"popup open at {pt.X},{pt.Y}; foreground {Trace.Foreground()}");
    }

    private void Close()
    {
        if (!IsOpen) return;
        _hooks.Uninstall();
        CloseFrom(1);
        Main.Hide();
    }

    /// <summary>Shows the Group's items in the window after <paramref name="level"/>, beside that level's selected row.</summary>
    private void OpenSub(int level, Group group, bool selectFirst)
    {
        var child = level + 1;
        if (child >= Levels) return;
        var window = _levels[child];
        if (_openGroups[child] != group || !window.IsVisible)
        {
            CloseFrom(child);
            var parent = _levels[level];
            var row = parent.SelectedRow;
            window.SetRows(VisibleItems(group).Select(ItemRow).ToList(), -1);
            window.ShowBeside(parent.ScreenRect(), row is null ? parent.ScreenRect().Top : parent.RowScreenTop(row));
            _openGroups[child] = group;
        }
        else
        {
            CloseFrom(child + 1);
        }
        if (selectFirst) window.SelectFirst();
    }

    /// <summary>Hides the submenu windows from <paramref name="level"/> down.</summary>
    private void CloseFrom(int level)
    {
        for (var i = Levels - 1; i >= Math.Max(1, level); i--)
        {
            _levels[i].Hide();
            _openGroups[i] = null;
        }
    }

    // ---- rows ----

    private List<Row> MainRows()
    {
        var rows = new List<Row>();
        _groupStarts.Clear();
        var previousInline = false;
        foreach (var group in _settings().Groups.Where(g => g.HasLinks))
        {
            // Inline Groups are set apart by separators; consecutive Collapsed Groups sit together.
            if (rows.Count > 0 && (group.ShowInline || previousInline)) rows.Add(Separator());
            if (group.ShowInline)
            {
                if (!group.HideName) rows.Add(Header(group));
                var start = -1;
                foreach (var item in VisibleItems(group))
                {
                    var row = ItemRow(item);
                    if (start < 0 && row.Selectable) start = rows.Count;   // skip leading Dividers
                    rows.Add(row);
                }
                _groupStarts.Add((group, start));
            }
            else
            {
                _groupStarts.Add((group, rows.Count));
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

    private static Row Header(Group group)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // Black, not the gray of other non-selectable rows: it looks the same as a Collapsed Group's name.
        grid.Children.Add(new TextBlock { Text = group.Name, Foreground = Brushes.Black, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = LabelMaxWidth + IconSize });
        AddKeyHint(grid, group.PopupKey, column: 1);
        return new Row { Content = grid, Selectable = false, FontWeight = FontWeights.Bold };
    }

    /// <summary>Shows a Popup Key in gray, right-aligned in the given column.</summary>
    private static void AddKeyHint(Grid grid, int? key, int column)
    {
        if (key is not { } vk) return;
        var text = new TextBlock
        {
            Text = KeyNames.Name(vk),
            Foreground = Brushes.Gray,
            FontWeight = FontWeights.Normal,
            Margin = new Thickness(24, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, column);
        grid.Children.Add(text);
    }

    /// <summary>A Group's entries, minus Subgroups without Links.</summary>
    private static IEnumerable<Link> VisibleItems(Group group) => group.Links.Where(l => l.Subgroup is not { HasLinks: false });

    private Row ItemRow(Link item) =>
        item.IsDivider ? Separator() : item.Subgroup is { } sub ? GroupRow(sub) : LinkRow(item);

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
        AddKeyHint(grid, link.PopupKey, column: 2);
        return new Row { Content = grid, Tag = link, ToolTip = link.Url };
    }

    private static Row GroupRow(Group group)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = group.Name,   // aligned and styled like an Inline Group header
            MaxWidth = LabelMaxWidth,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        AddKeyHint(grid, group.PopupKey, column: 1);
        var arrow = new TextBlock { Text = "▸", Margin = new Thickness(24, 0, 0, 0) };
        Grid.SetColumn(arrow, 2);
        grid.Children.Add(arrow);
        return new Row { Content = grid, Tag = group, FontWeight = FontWeights.Bold };
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
        var level = ActiveLevel;
        var active = _levels[level];
        switch (vk)
        {
            case 0x1B:                                                    // Esc
                if (level > 0) CloseFrom(level); else Close();
                return;
            case 0x25:                                                    // Left
                if (level > 0) CloseFrom(level);
                return;
            case 0x27:                                                    // Right
                if (active.SelectedRow?.Tag is Group g) OpenSub(level, g, selectFirst: true);
                return;
            case 0x26 when ShiftHeld(): JumpGroup(forward: false); return; // Shift+Up
            case 0x26: active.MoveSelection(-1); return;                  // Up
            case 0x28 when ShiftHeld(): JumpGroup(forward: true); return;  // Shift+Down
            case 0x28: active.MoveSelection(+1); return;                  // Down
            case 0x21: JumpGroup(forward: false); return;                // PageUp, like Shift+Up
            case 0x22: JumpGroup(forward: true); return;                 // PageDown, like Shift+Down
            case 0x24: active.SelectFirst(); return;                      // Home
            case 0x23: active.SelectLast(); return;                       // End
            case 0x0D: case 0x20:                                         // Enter, Space
                if (active.SelectedRow is { } row) Activate(level, row);
                return;
        }

        if (IsPopupHotkey(vk)) { Close(); return; }

        // Popup Keys work from anywhere in the Popup, including for Links of Collapsed Groups.
        if (ModifierHeld()) return;
        if (_groupStarts.FirstOrDefault(s => s.Group.PopupKey == vk) is { Group: not null } start)
        {
            SelectGroup(start.Group, start.Row);
        }
        else if (_groupStarts.SelectMany(s => s.Group.Subgroups.Select(sub => (Parent: s.Group, Sub: sub)))
                     .FirstOrDefault(p => p.Sub.PopupKey == vk && p.Sub.HasLinks) is { Sub: not null } hit)
        {
            SelectSubgroup(hit.Parent, hit.Sub);
        }
        else if (_settings().AllLinks.FirstOrDefault(l => l.PopupKey == vk) is { } link)
        {
            Close();
            _onLinkChosen(link);
        }
        // every other key is swallowed by the hook and ignored here, like a menu would
    }

    /// <summary>Selects the Group's first row: an Inline Group's first Link, or a Collapsed Group's name with its submenu opened.</summary>
    private void SelectGroup(Group group, int row)
    {
        if (_openGroups[1] != group) CloseFrom(1);
        Main.Select(row);
        if (!group.ShowInline) OpenSub(0, group, selectFirst: true);
    }

    /// <summary>Selects the Subgroup's row, opening its parent's submenu when needed, and opens the Subgroup's submenu.</summary>
    private void SelectSubgroup(Group parent, Group subgroup)
    {
        var level = 0;
        if (parent.ShowInline)
        {
            if (_openGroups[1] != subgroup) CloseFrom(1);
        }
        else
        {
            SelectGroup(parent, _groupStarts.First(s => s.Group == parent).Row);
            level = 1;
        }
        _levels[level].Select(_levels[level].IndexOfTag(subgroup));
        OpenSub(level, subgroup, selectFirst: true);
    }

    private static bool ShiftHeld() => (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

    /// <summary>
    /// Selects the first row of the next (or previous) Group in the main window, wrapping, closing any submenu.
    /// That row is an Inline Group's first Link or a Collapsed Group's name. Inside a Group, "previous" is its own start.
    /// </summary>
    private void JumpGroup(bool forward)
    {
        if (_groupStarts.Count == 0) return;
        CloseFrom(1);
        var current = Main.SelectedIndex;
        var starts = _groupStarts.Select(s => s.Row).ToList();
        var target = forward
            ? starts.FirstOrDefault(i => i > current, starts[0])
            : starts.LastOrDefault(i => i < current, starts[^1]);
        Main.Select(target);
    }

    /// <summary>Ctrl, Alt or Win held: Popup Keys are bare keys (Shift is ignored).</summary>
    private static bool ModifierHeld()
    {
        static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
        return Down(VK_CONTROL) || Down(VK_MENU) || Down(VK_LWIN) || Down(VK_RWIN);
    }

    private void Activate(int level, Row row)
    {
        switch (row.Tag)
        {
            case Link link:
                Close();
                _onLinkChosen(link);
                break;
            case Group group:
                OpenSub(level, group, selectFirst: true);
                break;
        }
    }
}
