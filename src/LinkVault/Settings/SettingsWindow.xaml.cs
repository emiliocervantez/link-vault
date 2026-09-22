using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LinkVault.Core;
using CoreSettings = LinkVault.Core.Settings;

namespace LinkVault.Config;

/// <summary>Edits a copy of the settings; OK validates and applies it, Cancel drops it.</summary>
public partial class SettingsWindow : Window
{
    private readonly CoreSettings _draft;
    private readonly Func<CoreSettings, IReadOnlyList<string>> _apply;
    private readonly Action<IEnumerable<Link>> _refreshIcons;
    private Group? _group;
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
        GroupList.ItemsSource = _draft.Groups;
        LinkGroupBox.ItemsSource = _draft.Groups;
        LinkHotkeyBox.ValueChanged += () => { if (_link is not null) _link.Hotkey = LinkHotkeyBox.Value; };
        LinkPopupKeyBox.ValueChanged += () => { if (_link is not null) _link.PopupKey = LinkPopupKeyBox.Value; };
        if (_draft.Groups.Count > 0) GroupList.SelectedIndex = 0;
    }

    // ---- groups ----

    private void GroupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _group = GroupList.SelectedItem as Group;
        _loading = true;
        GroupEditor.IsEnabled = _group is not null;
        LinksBox.IsEnabled = _group is not null;
        GroupNameBox.Text = _group?.Name ?? "";
        ShowInlineBox.IsChecked = _group?.ShowInline ?? false;
        LinkList.ItemsSource = _group?.Links;
        _loading = false;
        if (_group?.Links.Count > 0) LinkList.SelectedIndex = 0;
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        var g = new Group { Name = $"Group {_draft.Groups.Count + 1}" };
        _draft.Groups.Add(g);
        RefreshGroups(g);
        GroupNameBox.Focus();
        GroupNameBox.SelectAll();
    }

    private void RemoveGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_group is null) return;
        if (_group.Links.Count > 0 &&
            MessageBox.Show(this, $"Remove group \"{_group.Name}\" and its {_group.Links.Count} link(s)?", "LinkVault",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        var index = _draft.Groups.IndexOf(_group);
        _draft.Groups.RemoveAt(index);
        RefreshGroups(_draft.Groups.Count == 0 ? null : _draft.Groups[Math.Min(index, _draft.Groups.Count - 1)]);
    }

    private void GroupUp_Click(object sender, RoutedEventArgs e) => MoveGroup(-1);

    private void GroupDown_Click(object sender, RoutedEventArgs e) => MoveGroup(+1);

    private void MoveGroup(int delta)
    {
        if (_group is null || !Move(_draft.Groups, _group, delta)) return;
        RefreshGroups(_group);
    }

    private void RefreshGroups(Group? select)
    {
        GroupList.Items.Refresh();
        LinkGroupBox.Items.Refresh();
        GroupList.SelectedItem = select;
    }

    private void GroupName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _group is null) return;
        _group.Name = GroupNameBox.Text;
        GroupList.Items.Refresh();
        LinkGroupBox.Items.Refresh();
    }

    private void ShowInline_Click(object sender, RoutedEventArgs e)
    {
        if (_group is not null) _group.ShowInline = ShowInlineBox.IsChecked == true;
    }

    // ---- links ----

    private void LinkList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _link = LinkList.SelectedItem as Link;
        _loading = true;
        LinkEditor.IsEnabled = _link is not null;
        LinkNameBox.Text = _link?.Name ?? "";
        LinkUrlBox.Text = _link?.Url ?? "";
        LinkHotkeyBox.Value = _link?.Hotkey;
        LinkPopupKeyBox.Value = _link?.PopupKey;
        LinkGroupBox.SelectedItem = _link is null ? null : _group;
        _loading = false;
        ShowUrlInfo();
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

    private void RemoveLink_Click(object sender, RoutedEventArgs e)
    {
        if (_group is null || _link is null) return;
        var index = _group.Links.IndexOf(_link);
        _group.Links.RemoveAt(index);
        RefreshLinks(_group.Links.Count == 0 ? null : _group.Links[Math.Min(index, _group.Links.Count - 1)]);
    }

    private void LinkUp_Click(object sender, RoutedEventArgs e) => MoveLink(-1);

    private void LinkDown_Click(object sender, RoutedEventArgs e) => MoveLink(+1);

    private void MoveLink(int delta)
    {
        if (_group is null || _link is null || !Move(_group.Links, _link, delta)) return;
        RefreshLinks(_link);
    }

    private void RefreshLinks(Link? select)
    {
        LinkList.Items.Refresh();
        LinkList.SelectedItem = select;
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
        if (_link is null) { UrlInfo.Text = ""; return; }
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

    /// <summary>Moving a link to another group appends it there and follows it.</summary>
    private void LinkGroup_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _link is null || _group is null || LinkGroupBox.SelectedItem is not Group target || target == _group) return;
        var link = _link;
        _group.Links.Remove(link);
        target.Links.Add(link);
        GroupList.SelectedItem = target;
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

    // IsCancel only closes windows opened with ShowDialog; this one is opened with Show.
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _draft.PopupHotkey = PopupHotkeyBox.Value ?? new Hotkey();
        _draft.StartWithWindows = StartupBox.IsChecked == true;

        if (_draft.Validate() is { } problem)
        {
            MessageBox.Show(this, problem, "LinkVault", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var failures = _apply(_draft);
        if (failures.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n\n", failures), "LinkVault - hotkeys", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Close();
    }
}
