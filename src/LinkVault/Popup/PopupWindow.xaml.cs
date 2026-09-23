using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using LinkVault.Native;
using static LinkVault.Native.NativeMethods;

namespace LinkVault.Popup;

/// <summary>One line of the popup. Content is a ready-made element; the window only handles selection.</summary>
internal sealed class Row
{
    public required object Content { get; init; }
    public object? Tag { get; init; }
    public object? ToolTip { get; init; }
    public bool Selectable { get; init; } = true;
    public bool IsSeparator { get; init; }
    public FontWeight FontWeight { get; init; } = FontWeights.Normal;
    public double FontSize { get; init; } = SystemFonts.MessageFontSize;
}

/// <summary>
/// A menu-like window that never takes focus (WS_EX_NOACTIVATE), so the application that had focus keeps it.
/// Keyboard input arrives through <see cref="LinkPopup"/>'s low-level hook; this window only knows how to
/// show rows, move the selection and report hover and activation.
/// </summary>
public partial class PopupWindow : Window
{
    private const int MouseArmDistancePx = 3;

    private List<Row> _rows = new();
    private POINT _mouseAtShow;
    private bool _mouseArmed;

    internal PopupWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST));
        };
    }

    /// <summary>A selectable row was activated by mouse click (keyboard activation is routed by the owner).</summary>
    internal event Action<Row>? RowClicked;

    /// <summary>The mouse moved onto a selectable row, which is now selected.</summary>
    internal event Action<Row>? RowHovered;

    internal int SelectedIndex => List.SelectedIndex;

    /// <summary>Index of the row whose Tag is <paramref name="tag"/>, or -1.</summary>
    internal int IndexOfTag(object tag) => _rows.FindIndex(r => ReferenceEquals(r.Tag, tag));

    internal Row? SelectedRow => List.SelectedIndex >= 0 && List.SelectedIndex < _rows.Count ? _rows[List.SelectedIndex] : null;

    /// <summary>Replaces the rows. A negative index leaves nothing selected.</summary>
    internal void SetRows(List<Row> rows, int selectedIndex)
    {
        _rows = rows;
        List.ItemsSource = rows;
        List.SelectedIndex = -1;
        Select(selectedIndex);
    }

    internal void Select(int index)
    {
        if (index < 0 || index >= _rows.Count || !_rows[index].Selectable) return;
        List.SelectedIndex = index;
        List.ScrollIntoView(_rows[index]);
    }

    internal void MoveSelection(int delta)
    {
        if (_rows.Count == 0) return;
        var index = List.SelectedIndex;
        if (index < 0 && delta < 0) index = 0;   // nothing selected yet: Up starts from the bottom, Down from the top
        for (var step = 0; step < _rows.Count; step++)
        {
            index = ((index + delta) % _rows.Count + _rows.Count) % _rows.Count;
            if (_rows[index].Selectable) { Select(index); return; }
        }
    }

    internal void SelectFirst() => Select(_rows.FindIndex(r => r.Selectable));

    internal void SelectLast() => Select(_rows.FindLastIndex(r => r.Selectable));

    /// <summary>Shows the window at a screen point (pixels), kept inside that monitor's work area, without activating it.</summary>
    internal void ShowAt(POINT pt) => Place(pt, (w, h, work) =>
    {
        var x = pt.X;
        var y = pt.Y;
        if (x + w > work.Right) x = Math.Max(work.Left, work.Right - w);
        if (y + h > work.Bottom) y = Math.Max(work.Top, pt.Y - h > work.Top ? pt.Y - h : work.Bottom - h);
        return (x, y);
    });

    /// <summary>Shows the window as a submenu: right of <paramref name="parent"/> (left when there is no room), top at <paramref name="top"/>.</summary>
    internal void ShowBeside(RECT parent, int top) => Place(new POINT { X = parent.Right, Y = top }, (w, h, work) =>
    {
        var x = parent.Right;
        if (x + w > work.Right) x = Math.Max(work.Left, parent.Left - w);
        var y = top;
        if (y + h > work.Bottom) y = Math.Max(work.Top, work.Bottom - h);
        return (x, y);
    });

    private void Place(POINT pt, Func<int, int, RECT, (int X, int Y)> position)
    {
        var monitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        var work = info.rcWork;
        GetDpiForMonitor(monitor, 0, out var dpi, out _);
        var scale = dpi / 96.0;

        // Approximate DIP position first so WPF creates the window on the right monitor with the right DPI,
        // then place it exactly in pixels.
        MaxHeight = Math.Max(100, (work.Bottom - work.Top) / scale - 8);
        Left = pt.X / scale;
        Top = pt.Y / scale;
        GetCursorPos(out _mouseAtShow);
        _mouseArmed = false;
        Show();
        UpdateLayout();

        var hwnd = new WindowInteropHelper(this).Handle;
        GetWindowRect(hwnd, out var rect);
        var (x, y) = position(rect.Right - rect.Left, rect.Bottom - rect.Top, work);
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    internal RECT ScreenRect()
    {
        GetWindowRect(new WindowInteropHelper(this).Handle, out var rect);
        return rect;
    }

    internal bool Contains(POINT pt)
    {
        if (!IsVisible) return false;
        var r = ScreenRect();
        return pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom;
    }

    /// <summary>Screen top (pixels) of the row, or the window top when the row has no container.</summary>
    internal int RowScreenTop(Row row)
    {
        if (List.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem item)
            return (int)item.PointToScreen(new Point(0, 0)).Y;
        return ScreenRect().Top;
    }

    private int IndexUnderMouse(MouseEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return -1;
        if (ItemsControl.ContainerFromElement(List, source) is not ListBoxItem item) return -1;
        return List.ItemContainerGenerator.IndexFromContainer(item);
    }

    /// <summary>Hover selects, but only after the pointer has really moved: the window often opens under it.</summary>
    private void List_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseArmed)
        {
            GetCursorPos(out var now);
            if (Math.Abs(now.X - _mouseAtShow.X) < MouseArmDistancePx && Math.Abs(now.Y - _mouseAtShow.Y) < MouseArmDistancePx) return;
            _mouseArmed = true;
        }
        var index = IndexUnderMouse(e);
        if (index < 0 || !_rows[index].Selectable || index == List.SelectedIndex) return;
        Select(index);
        RowHovered?.Invoke(_rows[index]);
    }

    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;   // keep the ListBox from doing its own selection/focus dance
    }

    private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var index = IndexUnderMouse(e);
        if (index >= 0 && _rows[index].Selectable) RowClicked?.Invoke(_rows[index]);
    }
}
