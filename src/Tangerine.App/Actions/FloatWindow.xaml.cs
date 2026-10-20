using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tangerine.App.Native;
using Tangerine.Core;

namespace Tangerine.App.Actions;

/// <summary>A small always-on-top card holding files so they can be dragged somewhere later.</summary>
public partial class FloatWindow : Window
{
    private readonly string[] _files;
    private Point? _pressPoint;

    public FloatWindow(IReadOnlyList<string> files)
    {
        InitializeComponent();
        _files = files.ToArray();
        NameText.Text = files.Count == 1 ? Path.GetFileName(files[0].TrimEnd('\\')) : $"{files.Count} items";
        if (files.Count > 1)
        {
            CountBadge.Visibility = Visibility.Visible;
            CountText.Text = files.Count.ToString();
        }
        Thumb.Source = LoadThumbnail(files[0]);

        SourceInitialized += (_, _) =>
        {
            NativeMethods.MakeToolWindow(new WindowInteropHelper(this).Handle, noActivate: false);
            NativeMethods.GetCursorPos(out var cursor);
            var source = PresentationSource.FromVisual(this);
            var scale = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var dip = scale.Transform(new Point(cursor.X, cursor.Y));
            Left = dip.X - Width / 2;
            Top = dip.Y - 80;
        };
    }

    private static ImageSource? LoadThumbnail(string path)
    {
        if (FileKinds.Of(path) == FileKind.Image && PathNames.Extension(path) is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".ico")
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelHeight = 192;
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException)
            {
                // Fall back to the shell icon.
            }
        }
        return ShellIcons.Large(path);
    }

    private void OnHeaderDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnSourceDown(object sender, MouseButtonEventArgs e) => _pressPoint = e.GetPosition(this);

    private void OnSourceMove(object sender, MouseEventArgs e)
    {
        if (_pressPoint is null || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(this) - _pressPoint.Value;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _pressPoint = null;

        var existing = _files.Where(f => File.Exists(f) || Directory.Exists(f)).ToArray();
        if (existing.Length == 0)
        {
            Close();
            return;
        }
        var data = new DataObject(DataFormats.FileDrop, existing);
        var result = DragDrop.DoDragDrop(DragSource, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        if (result == DragDropEffects.Move || existing.All(f => !File.Exists(f) && !Directory.Exists(f))) Close();
    }
}
