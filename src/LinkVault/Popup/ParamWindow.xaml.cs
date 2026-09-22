using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using LinkVault.Core;
using LinkVault.Native;

namespace LinkVault.Popup;

/// <summary>The Input Window: asks for the value of every Parameter of a Link before it is opened. Enter opens, Esc cancels.</summary>
public partial class ParamWindow : Window
{
    private readonly List<(UrlParameter Parameter, TextBox Box)> _boxes = new();

    internal ParamWindow(string linkLabel, IReadOnlyList<UrlParameter> parameters)
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
            var box = new TextBox { Text = p.Default ?? "", Margin = new Thickness(0, 0, 0, 6), VerticalContentAlignment = VerticalAlignment.Center };
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
            if (_boxes.Count > 0)
            {
                _boxes[0].Box.Focus();
                _boxes[0].Box.SelectAll();
            }
        };
    }

    public Dictionary<string, string> Values => _boxes.ToDictionary(b => b.Parameter.Name, b => b.Box.Text);

    private void Open_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
