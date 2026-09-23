using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LinkVault.Core;
using CoreSettings = LinkVault.Core.Settings;

namespace LinkVault.Config;

/// <summary>Edits a copy of the settings; OK validates and applies it, Cancel drops it.</summary>
public partial class SettingsWindow : Window
{
    /// <summary>An entry of the link editor's Group list; Subgroups are indented under their parent.</summary>
    private sealed record GroupChoice(Group Group, string Display);

    private CoreSettings _draft;
    private readonly Func<CoreSettings, IReadOnlyList<string>> _apply;
    private readonly Action<IEnumerable<Link>> _refreshIcons;
    /// <summary>The Group or Subgroup selected in the tree; its entries are listed in the Links pane.</summary>
    private Group? _group;
    /// <summary>The parent of <see cref="_group"/> when it is a Subgroup, else null.</summary>
    private Group? _parent;
    private Link? _link;
    private bool _loading;

    /// <param name="apply">Applies the settings and returns hotkey problems, if any.</param>
    /// <param name="refreshIcons">Refetches the favicons of the given links.</param>
    internal SettingsWindow(CoreSettings current, Func<CoreSettings, IReadOnlyList<string>> apply, Action<IEnumerable<Link>> refreshIcons)
    {
        InitializeComponent();
        _draft = current.Clone();
        _apply = apply;
        _refreshIcons = refreshIcons;

        PopupHotkeyBox.Value = _draft.PopupHotkey;
        StartupBox.IsChecked = _draft.StartWithWindows;
        LinkHotkeyBox.ValueChanged += () => { if (_link is not null) _link.Hotkey = LinkHotkeyBox.Value; };
        LinkPopupKeyBox.ValueChanged += () => { if (_link is not null) _link.PopupKey = LinkPopupKeyBox.Value; };
        GroupPopupKeyBox.ValueChanged += () => { if (_group is not null) _group.PopupKey = GroupPopupKeyBox.Value; };
        RefreshGroups(_draft.Groups.FirstOrDefault());
    }

    private Group? ParentOf(Group group) => _draft.Groups.FirstOrDefault(p => p.Subgroups.Contains(group));

    // ---- groups ----

    /// <summary>Rebuilds the tree (Groups with their Subgroups, all expanded) and the link editor's Group list, then selects <paramref name="select"/>.</summary>
    private void RefreshGroups(Group? select)
    {
        GroupTree.Items.Clear();
        var choices = new List<GroupChoice>();
        TreeViewItem? selected = null;
        foreach (var g in _draft.Groups)
        {
            var item = new TreeViewItem { Header = g.Name, Tag = g, IsExpanded = true };
            choices.Add(new GroupChoice(g, g.Name));
            if (g == select) selected = item;
            foreach (var sub in g.Subgroups)
            {
                var child = new TreeViewItem { Header = sub.Name, Tag = sub };
                choices.Add(new GroupChoice(sub, "      " + sub.Name));
                if (sub == select) selected = child;
                item.Items.Add(child);
            }
            GroupTree.Items.Add(item);
        }
        LinkGroupBox.ItemsSource = choices;
        if (selected is not null) selected.IsSelected = true;
        ShowGroup(select);
    }

    private void GroupTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (GroupTree.SelectedItem is TreeViewItem { Tag: Group g } && g != _group) ShowGroup(g);
    }

    private void ShowGroup(Group? group)
    {
        _group = group;
        _parent = group is null ? null : ParentOf(group);
        _loading = true;
        GroupEditor.IsEnabled = _group is not null;
        LinksBox.IsEnabled = _group is not null;
        LinksBox.Header = _parent is null ? "Links" : "Links of subgroup";
        // Subgroups always show as a submenu, so the display options belong to top-level Groups only.
        TopLevelOptions.Visibility = _parent is null ? Visibility.Visible : Visibility.Collapsed;
        GroupNameBox.Text = _group?.Name ?? "";
        ShowInlineBox.IsChecked = _group?.ShowInline ?? false;
        HideNameBox.IsChecked = _group?.HideName ?? false;
        GroupPopupKeyBox.Value = _group?.PopupKey;
        LinkList.ItemsSource = _group?.Links;
        _loading = false;
        LinkList.SelectedIndex = _group?.Links.Count > 0 ? 0 : -1;
        if (_group is null || _group.Links.Count == 0) ShowLink(null);
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        var g = new Group { Name = $"Group {_draft.Groups.Count + 1}" };
        _draft.Groups.Add(g);
        RefreshGroups(g);
        GroupNameBox.Focus();
        GroupNameBox.SelectAll();
    }

    /// <summary>Adds a Subgroup at the end of the selected top-level Group (or of the selected Subgroup's parent).</summary>
    private void AddSubgroup_Click(object sender, RoutedEventArgs e)
    {
        var parent = _parent ?? _group;
        if (parent is null) return;
        var sub = new Group { Name = $"Subgroup {parent.Subgroups.Count() + 1}" };
        parent.Links.Add(Link.ForSubgroup(sub));
        RefreshGroups(sub);
        GroupNameBox.Focus();
        GroupNameBox.SelectAll();
    }

    private void RemoveGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_group is null || !ConfirmRemove(_group)) return;
        if (_parent is { } parent)
        {
            parent.Links.RemoveAll(l => l.Subgroup == _group);
            RefreshGroups(parent);
            return;
        }
        var index = _draft.Groups.IndexOf(_group);
        _draft.Groups.RemoveAt(index);
        RefreshGroups(_draft.Groups.Count == 0 ? null : _draft.Groups[Math.Min(index, _draft.Groups.Count - 1)]);
    }

    private bool ConfirmRemove(Group group)
    {
        var linkCount = group.AllLinks.Count();
        return linkCount == 0 ||
               MessageBox.Show(this, $"Remove \"{group.Name}\" and its {linkCount} link(s)?", "LinkVault",
                   MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private void GroupUp_Click(object sender, RoutedEventArgs e) => MoveGroup(-1);

    private void GroupDown_Click(object sender, RoutedEventArgs e) => MoveGroup(+1);

    /// <summary>Moves a Group among the Groups, or a Subgroup past its neighbouring Subgroup in the parent's entries.</summary>
    private void MoveGroup(int delta)
    {
        if (_group is null) return;
        if (_parent is { } parent)
        {
            var entries = parent.Links;
            var i = entries.FindIndex(l => l.Subgroup == _group);
            var j = i + delta;
            while (j >= 0 && j < entries.Count && !entries[j].IsSubgroup) j += delta;
            if (j < 0 || j >= entries.Count) return;
            (entries[i], entries[j]) = (entries[j], entries[i]);
        }
        else if (!Move(_draft.Groups, _group, delta))
        {
            return;
        }
        RefreshGroups(_group);
    }

    private void GroupName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _group is null) return;
        _group.Name = GroupNameBox.Text;
        if (GroupTree.SelectedItem is TreeViewItem item) item.Header = _group.Name;
        _loading = true;
        LinkGroupBox.ItemsSource = ((IEnumerable<GroupChoice>)LinkGroupBox.ItemsSource)
            .Select(c => c.Group == _group ? c with { Display = (_parent is null ? "" : "      ") + _group.Name } : c).ToList();
        LinkGroupBox.SelectedItem = _link is { IsLink: true } ? ChoiceFor(_group) : null;
        _loading = false;
    }

    private void ShowInline_Click(object sender, RoutedEventArgs e)
    {
        if (_group is not null) _group.ShowInline = ShowInlineBox.IsChecked == true;
    }

    private void HideName_Click(object sender, RoutedEventArgs e)
    {
        if (_group is not null) _group.HideName = HideNameBox.IsChecked == true;
    }

    // ---- links ----

    private void LinkList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) ShowLink(LinkList.SelectedItem as Link);
    }

    private void ShowLink(Link? link)
    {
        _link = link;
        _loading = true;
        LinkEditor.IsEnabled = _link is { IsLink: true };
        LinkNameBox.Text = _link?.Name ?? "";
        LinkUrlBox.Text = _link?.Url ?? "";
        LinkHotkeyBox.Value = _link?.Hotkey;
        LinkPopupKeyBox.Value = _link?.PopupKey;
        LinkGroupBox.SelectedItem = _link is { IsLink: true } && _group is not null ? ChoiceFor(_group) : null;
        _loading = false;
        ShowUrlInfo();
    }

    private GroupChoice? ChoiceFor(Group group) =>
        ((IEnumerable<GroupChoice>?)LinkGroupBox.ItemsSource)?.FirstOrDefault(c => c.Group == group);

    /// <summary>Double-clicking a Subgroup entry selects that Subgroup in the tree.</summary>
    private void LinkList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_link?.Subgroup is { } sub) RefreshGroups(sub);
    }

    private void AddLink_Click(object sender, RoutedEventArgs e)
    {
        if (_group is null) return;
        var link = new Link { Url = "https://" };
        _group.Links.Add(link);
        RefreshLinks(link);
        LinkUrlBox.Focus();
        LinkUrlBox.CaretIndex = LinkUrlBox.Text.Length;
    }

    private void AddDivider_Click(object sender, RoutedEventArgs e)
    {
        if (_group is null) return;
        var divider = Link.Divider();
        var index = _link is null ? _group.Links.Count : _group.Links.IndexOf(_link) + 1;
        _group.Links.Insert(index, divider);
        RefreshLinks(divider);
    }

    private void RemoveLink_Click(object sender, RoutedEventArgs e)
    {
        if (_group is null || _link is null) return;
        if (_link.Subgroup is { } sub && !ConfirmRemove(sub)) return;
        var index = _group.Links.IndexOf(_link);
        var wasSubgroup = _link.IsSubgroup;
        _group.Links.RemoveAt(index);
        if (wasSubgroup) RefreshGroups(_group);
        RefreshLinks(_group.Links.Count == 0 ? null : _group.Links[Math.Min(index, _group.Links.Count - 1)]);
    }

    private void LinkUp_Click(object sender, RoutedEventArgs e) => MoveLink(-1);

    private void LinkDown_Click(object sender, RoutedEventArgs e) => MoveLink(+1);

    private void MoveLink(int delta)
    {
        if (_group is null || _link is null || !Move(_group.Links, _link, delta)) return;
        var link = _link;
        if (link.IsSubgroup) RefreshGroups(_group);   // the tree lists Subgroups in entry order
        RefreshLinks(link);
    }

    private void RefreshLinks(Link? select)
    {
        LinkList.Items.Refresh();
        LinkList.SelectedItem = select;
        if (select is null) ShowLink(null);
    }

    private void LinkName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _link is null) return;
        _link.Name = LinkNameBox.Text;
        LinkList.Items.Refresh();
    }

    private void LinkUrl_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _link is null) return;
        _link.Url = LinkUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(_link.Name)) LinkList.Items.Refresh();   // the label is the URL
        ShowUrlInfo();
    }

    /// <summary>Lists the Parameters found in the URL, or the reason it cannot be parsed.</summary>
    private void ShowUrlInfo()
    {
        if (_link is not { IsLink: true }) { UrlInfo.Text = ""; return; }
        var template = UrlTemplate.TryParse(_link.Url, out var error);
        if (template is null)
        {
            UrlInfo.Text = error;
            UrlInfo.Foreground = Brushes.Firebrick;
            return;
        }
        UrlInfo.Foreground = Brushes.Gray;
        UrlInfo.Text = template.Parameters.Count == 0
            ? "No parameters."
            : "Parameters: " + string.Join(", ", template.Parameters.Select(p => p.Default is null ? p.Name : $"{p.Name} = \"{p.Default}\""));
    }

    /// <summary>Moving a link to another Group or Subgroup appends it there and follows it.</summary>
    private void LinkGroup_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _link is not { IsLink: true } || _group is null || LinkGroupBox.SelectedItem is not GroupChoice { Group: var target } || target == _group) return;
        var link = _link;
        _group.Links.Remove(link);
        target.Links.Add(link);
        RefreshGroups(target);
        RefreshLinks(link);
    }

    private static bool Move<T>(List<T> list, T item, int delta)
    {
        var index = list.IndexOf(item);
        var to = index + delta;
        if (index < 0 || to < 0 || to >= list.Count) return false;
        list.RemoveAt(index);
        list.Insert(to, item);
        return true;
    }

    // ---- app ----

    private void RefreshIcons_Click(object sender, RoutedEventArgs e) => _refreshIcons(_draft.AllLinks.ToList());

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "LinkVault settings.json", Filter = JsonFilter };
        if (dialog.ShowDialog(this) != true) return;
        _draft.PopupHotkey = PopupHotkeyBox.Value ?? new Hotkey();
        _draft.StartWithWindows = StartupBox.IsChecked == true;
        try
        {
            VaultStorage.Export(_draft, dialog.FileName);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not export: {ex.Message}", "LinkVault", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Replaces the draft with the file's settings; OK applies them, Cancel drops them.</summary>
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = JsonFilter };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _draft = VaultStorage.Import(dialog.FileName);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not import: {ex.Message}", "LinkVault", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        PopupHotkeyBox.Value = _draft.PopupHotkey;
        StartupBox.IsChecked = _draft.StartWithWindows;
        RefreshGroups(_draft.Groups.FirstOrDefault());
    }

    private const string JsonFilter = "LinkVault settings (*.json)|*.json|All files (*.*)|*.*";

    // IsCancel only closes windows opened with ShowDialog; this one is opened with Show.
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Apply()) Close();
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => Apply();

    /// <summary>Validates and applies a copy of the draft, so editing can go on without touching the live settings. False when invalid.</summary>
    private bool Apply()
    {
        _draft.PopupHotkey = PopupHotkeyBox.Value ?? new Hotkey();
        _draft.StartWithWindows = StartupBox.IsChecked == true;

        if (_draft.Validate() is { } problem)
        {
            MessageBox.Show(this, problem, "LinkVault", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var applied = _draft.Clone();
        var failures = _apply(applied);
        if (failures.Count > 0)
        {
            // Hotkeys Windows refused were changed in the applied copy (fallback or cleared); show that in the draft too.
            _draft.PopupHotkey = applied.PopupHotkey;
            PopupHotkeyBox.Value = applied.PopupHotkey;
            foreach (var (draft, live) in _draft.AllLinks.Zip(applied.AllLinks)) draft.Hotkey = live.Hotkey;
            LinkHotkeyBox.Value = _link?.Hotkey;
            MessageBox.Show(this, string.Join("\n\n", failures), "LinkVault - hotkeys", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return true;
    }
}
