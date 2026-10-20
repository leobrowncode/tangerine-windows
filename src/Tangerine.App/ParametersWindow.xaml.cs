using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tangerine.App.Native;
using Tangerine.Core.Operations;

namespace Tangerine.App;

/// <summary>Asks for the values an operation needs (e.g. trim range) before it runs.</summary>
public partial class ParametersWindow : Window
{
    private readonly Dictionary<string, TextBox> _boxes = [];

    private ParametersWindow(Operation operation, string fileDescription)
    {
        InitializeComponent();
        Heading.Text = operation.Title;
        Subheading.Text = fileDescription;
        RunButton.Content = operation.Label;

        foreach (var spec in operation.Parameters)
        {
            Fields.Children.Add(new TextBlock { Text = spec.Label, Margin = new Thickness(0, 8, 0, 4) });
            var box = new TextBox { Text = spec.DefaultValue };
            if (spec.Hint is not null) box.ToolTip = spec.Hint;
            Fields.Children.Add(box);
            if (spec.Hint is not null)
                Fields.Children.Add(new TextBlock { Text = spec.Hint, FontSize = 11, Opacity = 0.6, Margin = new Thickness(0, 3, 0, 0) });
            _boxes[spec.Key] = box;
        }

        Loaded += (_, _) =>
        {
            NativeMethods.GetCursorPos(out var cursor);
            var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var dip = transform.Transform(new Point(cursor.X, cursor.Y));
            var work = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            Left = Math.Clamp(dip.X - ActualWidth / 2, work.Left, work.Right - ActualWidth);
            Top = Math.Clamp(dip.Y - ActualHeight / 2, work.Top, work.Bottom - ActualHeight);
            NativeMethods.ForceForeground(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            Activate();
            if (_boxes.Values.FirstOrDefault() is { } first)
            {
                first.Focus();
                first.SelectAll();
            }
        };
    }

    public static IReadOnlyDictionary<string, string>? Ask(Operation operation, string fileDescription)
    {
        var window = new ParametersWindow(operation, fileDescription);
        return window.ShowDialog() == true ? window._boxes.ToDictionary(kv => kv.Key, kv => kv.Value.Text) : null;
    }

    private void OnRun(object sender, RoutedEventArgs e) => DialogResult = true;
}
