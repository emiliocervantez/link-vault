using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using LinkVault.Core;
using LinkVault.Native;

namespace LinkVault.Popup;

/// <summary>The Input Window: asks for the value of every Parameter of a Link before it is opened. Enter opens, Esc cancels.</summary>
public partial class ParamWindow : Window
{
    private readonly List<(UrlParameter Parameter, ComboBox Box)> _boxes = new();

    /// <param name="remembered">The values entered before for a Parameter, most recent first.</param>
    internal ParamWindow(string linkLabel, IReadOnlyList<UrlParameter> parameters, Func<UrlParameter, IReadOnlyList<string>> remembered)
    {
        InitializeComponent();
        LinkLabel.Text = linkLabel;
        LinkLabel.ToolTip = linkLabel;

        foreach (var p in parameters)
        {
            var row = Fields.RowDefinitions.Count;
            Fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            // "__": a single '_' in a Label is an access key marker.
            var label = new Label { Content = p.Name.Replace("_", "__") + ":", Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 6), VerticalAlignment = VerticalAlignment.Center };
            // Editable, so it is a text field whose list holds the remembered values. No text search: typing must not autocomplete.
            var box = new ComboBox { IsEditable = true, IsTextSearchEnabled = false, ItemsSource = remembered(p), Text = p.Default ?? "", Margin = new Thickness(0, 0, 0, 6), VerticalContentAlignment = VerticalAlignment.Center };
            // By default Down steps through the values without showing them; show the list instead.
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Down || box.IsDropDownOpen || !box.HasItems) return;
                box.IsDropDownOpen = true;
                e.Handled = true;
            };
            label.Target = box;
            Grid.SetRow(label, row);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            Fields.Children.Add(label);
            Fields.Children.Add(box);
            _boxes.Add((p, box));
        }

        Loaded += (_, _) =>
        {
            // Opened from a global hotkey the app may not be in the foreground yet.
            NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
            Activate();
            // An editable ComboBox selects its whole text when it gets the focus.
            if (_boxes.Count > 0) _boxes[0].Box.Focus();
        };
    }

    public Dictionary<string, string> Values => _boxes.ToDictionary(b => b.Parameter.Name, b => b.Box.Text);

    private void Open_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
