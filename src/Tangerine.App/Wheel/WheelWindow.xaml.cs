using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Tangerine.App.Input;
using Tangerine.App.Native;
using Tangerine.Core.Wheel;
using Path = System.Windows.Shapes.Path;

namespace Tangerine.App.Wheel;

public partial class WheelWindow : Window
{
    private const double Center = 300;
    private const double SegmentGapDegrees = 1.2;

    private static readonly Brush PlateBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xF2, 0x1C, 0x1C, 0x1E)));
    private static readonly Brush SegmentBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2F)));
    private static readonly Brush ToolSegmentBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x29)));
    private static readonly Brush HoleBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x18)));
    private static readonly Brush HitBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)));
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)));
    private static readonly Brush DimTextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA6)));
    private static readonly Brush AccentBrush = Frozen(new LinearGradientBrush(Color.FromRgb(0xFF, 0xA7, 0x3D), Color.FromRgb(0xF2, 0x6B, 0x0F), 90));
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly IntPtr _hwnd;
    private readonly DispatcherTimer _noDragTimer;
    private readonly List<Segment> _segments = [];
    private RadialLayout _layout = new([]);
    private WheelMode _mode;
    private string[]? _files;
    private Segment? _hover;
    private TextBlock _centerTitle = new();
    private TextBlock _centerSubtitle = new();
    private bool _dragSeen;

    private sealed record Segment(WheelItem Item, Path Shape, Brush BaseBrush, UIElement Label, WheelHit Hit);

    public WheelWindow()
    {
        InitializeComponent();
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.MakeToolWindow(_hwnd, noActivate: true);

        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => SetHover(null);
        Drop += OnDrop;

        // If no OLE drag ever reaches the wheel, the gesture was a window move or text selection.
        _noDragTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(1500), DispatcherPriority.Normal, (_, _) =>
        {
            _noDragTimer!.Stop();
            if (!_dragSeen) Dismiss();
        }, Dispatcher);
        _noDragTimer.Stop();
    }

    /// <summary>Supplies the wheel entries for the dragged files.</summary>
    public Func<WheelMode, IReadOnlyList<string>, IReadOnlyList<WheelItem>>? ItemsProvider { get; set; }

    public event Action<WheelItem, IReadOnlyList<string>>? ItemDropped;

    public event Action? Dismissed;

    public bool IsOpen { get; private set; }

    public WheelMode Mode => _mode;

    internal void ShowAt(WheelMode mode, NativeMethods.POINT cursor)
    {
        _mode = mode;
        _files = null;
        _dragSeen = false;
        _hover = null;
        Build(mode == WheelMode.Actions ? ItemsProvider?.Invoke(mode, []) ?? [] : []);
        SetCenter(mode == WheelMode.Convert ? "Convert" : "File actions", mode == WheelMode.Convert ? "Reading file…" : "Drop on an action");

        Opacity = 0;
        if (!IsVisible) Show();
        IsOpen = true;

        // First move onto the target monitor so WPF applies that monitor's DPI, then centre on the cursor.
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, cursor.X, cursor.Y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        NativeMethods.GetWindowRect(_hwnd, out var rect);
        var scale = rect.Width / Width;
        var visibleRadius = (int)Math.Ceiling((_layout.Rings.Count == 0 ? 230 : _layout.OuterRadius + 10) * scale);
        var work = NativeMethods.WorkAreaAt(cursor);
        var cx = Math.Clamp(cursor.X, work.Left + visibleRadius, Math.Max(work.Left + visibleRadius, work.Right - visibleRadius));
        var cy = Math.Clamp(cursor.Y, work.Top + visibleRadius, Math.Max(work.Top + visibleRadius, work.Bottom - visibleRadius));
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, cx - rect.Width / 2, cy - rect.Height / 2, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease });
        SurfaceScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        SurfaceScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });

        _noDragTimer.Stop();
        _noDragTimer.Start();
        // Make the active drag notice the wheel without waiting for the user to move the mouse.
        Dispatcher.BeginInvoke(NativeMethods.NudgeMouse, DispatcherPriority.Input);
    }

    public void Dismiss()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _noDragTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Hide();
        Surface.Children.Clear();
        _segments.Clear();
        Dismissed?.Invoke();
    }

    /// <summary>Renders the wheel for <paramref name="files"/> off-screen, as if hovering <paramref name="hover"/>.</summary>
    internal BitmapSource RenderPreview(WheelMode mode, IReadOnlyList<string> files, WheelHit hover)
    {
        _mode = mode;
        _files = files.ToArray();
        _hover = null;
        Build(ItemsProvider?.Invoke(mode, files) ?? []);
        SetCenter(mode == WheelMode.Convert ? "Convert" : "File actions", DescribeFiles(files));
        SetHover(_segments.FirstOrDefault(s => s.Hit == hover));

        Surface.Measure(new Size(Width, Height));
        Surface.Arrange(new Rect(0, 0, Width, Height));
        Surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Width * 2, (int)Height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(Surface);
        bitmap.Freeze();
        return bitmap;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        _dragSeen = true;

        if (_files is null)
        {
            _files = e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] ?? [] : [];
            if (_mode == WheelMode.Convert)
            {
                Build(_files.Length == 0 ? [] : ItemsProvider?.Invoke(_mode, _files) ?? []);
                if (_files.Length == 0) SetCenter("Not a file", "Drag files from Explorer or the desktop");
                else if (_segments.Count == 0) SetCenter("No conversions", DescribeFiles(_files));
                else SetCenter("Convert", DescribeFiles(_files));
            }
            else
            {
                SetCenter("File actions", _files.Length == 0 ? "Not a file" : DescribeFiles(_files));
            }
        }

        var position = e.GetPosition(Surface);
        var hit = _layout.HitTest(position.X - Center, position.Y - Center);
        var segment = hit.IsNone || _files.Length == 0 ? null : _segments.FirstOrDefault(s => s.Hit == hit);
        SetHover(segment);
        e.Effects = segment is null ? DragDropEffects.None : PreferredEffect(e.AllowedEffects);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var segment = _hover;
        var files = _files ?? [];
        // Never report Move: the drag source would delete the original file.
        e.Effects = segment is null || files.Length == 0 ? DragDropEffects.None : PreferredEffect(e.AllowedEffects);
        Dismiss();
        if (segment is not null && files.Length > 0)
            Dispatcher.BeginInvoke(() => ItemDropped?.Invoke(segment.Item, files));
    }

    private static DragDropEffects PreferredEffect(DragDropEffects allowed) =>
        allowed.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy
        : allowed.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link
        : DragDropEffects.None;

    private void SetHover(Segment? segment)
    {
        if (ReferenceEquals(segment, _hover)) return;
        if (_hover is not null) _hover.Shape.Fill = _hover.BaseBrush;
        _hover = segment;
        if (segment is not null)
        {
            segment.Shape.Fill = AccentBrush;
            SetCenter(segment.Item.Title, _files is { Length: > 0 } ? DescribeFiles(_files) : "");
        }
        else if (_files is { Length: > 0 })
        {
            SetCenter(_mode == WheelMode.Convert ? (_segments.Count == 0 ? "No conversions" : "Convert") : "File actions", DescribeFiles(_files));
        }
    }

    private static string DescribeFiles(IReadOnlyList<string> files) =>
        files.Count == 1 ? System.IO.Path.GetFileName(files[0].TrimEnd('\\')) : $"{files.Count} items";

    private void SetCenter(string title, string subtitle)
    {
        _centerTitle.Text = title;
        _centerSubtitle.Text = subtitle;
    }

    private void Build(IReadOnlyList<WheelItem> items)
    {
        Surface.Children.Clear();
        _segments.Clear();

        var rings = items.GroupBy(i => i.Ring).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
        _layout = new RadialLayout(rings.Select(r => r.Count).ToList());
        var outer = _layout.Rings.Count == 0 ? RadialLayout.DefaultHoleRadius + 30 : _layout.OuterRadius;

        // Near-invisible fill keeps the overshoot zone hit-testable for drag and drop.
        AddCircle(outer + RadialLayout.OvershootTolerance, HitBrush, null);
        var plate = AddCircle(outer + 8, PlateBrush, null);
        plate.Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Opacity = 0.45, Color = Colors.Black };
        plate.Stroke = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        plate.StrokeThickness = 1;

        for (var r = 0; r < _layout.Rings.Count; r++)
        {
            var ring = _layout.Rings[r];
            var ringItems = rings[r];
            for (var i = 0; i < ringItems.Count; i++)
            {
                var item = ringItems[i];
                var start = ring.StartAngle(i) + (ring.Count > 1 ? SegmentGapDegrees / 2 : 0);
                var end = ring.StartAngle(i) + ring.Sweep - (ring.Count > 1 ? SegmentGapDegrees / 2 : 0);
                var brush = r == 0 ? SegmentBrush : ToolSegmentBrush;
                var shape = new Path { Data = AnnularSector(ring.InnerRadius + 1, ring.OuterRadius - 1, start, end), Fill = brush };
                Surface.Children.Add(shape);

                var label = BuildLabel(item, ring, r);
                var (x, y) = RadialLayout.PointAt(ring.MidRadius, ring.MidAngle(i));
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(label, Center + x - label.DesiredSize.Width / 2);
                Canvas.SetTop(label, Center + y - label.DesiredSize.Height / 2);
                Surface.Children.Add(label);

                _segments.Add(new Segment(item, shape, brush, label, new WheelHit(r, i)));
            }
        }

        AddCircle(_layout.HoleRadius - 3, HoleBrush, null);
        var center = new StackPanel { Width = _layout.HoleRadius * 2 - 14, IsHitTestVisible = false };
        _centerTitle = new TextBlock
        {
            Foreground = TextBrush, FontSize = 12, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, MaxHeight = 50, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _centerSubtitle = new TextBlock
        {
            Foreground = DimTextBrush, FontSize = 10, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        };
        center.Children.Add(_centerTitle);
        center.Children.Add(_centerSubtitle);
        center.Loaded += (_, _) => PlaceCenter(center);
        center.SizeChanged += (_, _) => PlaceCenter(center);
        Surface.Children.Add(center);
        PlaceCenter(center);
    }

    private static void PlaceCenter(FrameworkElement center)
    {
        center.Measure(new Size(center.Width, double.PositiveInfinity));
        Canvas.SetLeft(center, Center - center.Width / 2);
        Canvas.SetTop(center, Center - center.DesiredSize.Height / 2);
    }

    private static FrameworkElement BuildLabel(WheelItem item, Ring ring, int ringIndex)
    {
        var arc = 2 * Math.PI * ring.MidRadius * ring.Sweep / 360;
        var maxWidth = Math.Max(40, Math.Min(arc - 8, ring.OuterRadius - ring.InnerRadius + 24));
        var panel = new StackPanel { IsHitTestVisible = false, MaxWidth = maxWidth };
        if (item.Glyph is not null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = item.Glyph, FontFamily = IconFont, FontSize = 18, Foreground = TextBrush,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 3),
            });
        }
        panel.Children.Add(new TextBlock
        {
            Text = item.Label,
            Foreground = TextBrush,
            FontSize = ringIndex == 0 && item.Glyph is null ? 12.5 : 11,
            FontWeight = ringIndex == 0 ? FontWeights.SemiBold : FontWeights.Normal,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        return panel;
    }

    private Ellipse AddCircle(double radius, Brush fill, Brush? stroke)
    {
        var ellipse = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = fill, Stroke = stroke };
        Canvas.SetLeft(ellipse, Center - radius);
        Canvas.SetTop(ellipse, Center - radius);
        Surface.Children.Add(ellipse);
        return ellipse;
    }

    private static Geometry AnnularSector(double inner, double outer, double startDeg, double endDeg)
    {
        if (endDeg - startDeg >= 359.9)
        {
            var ring = new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(new Point(Center, Center), outer, outer),
                new EllipseGeometry(new Point(Center, Center), inner, inner));
            ring.Freeze();
            return ring;
        }

        Point P(double r, double a)
        {
            var (x, y) = RadialLayout.PointAt(r, a);
            return new Point(Center + x, Center + y);
        }

        var large = endDeg - startDeg > 180;
        var figure = new PathFigure { StartPoint = P(outer, startDeg), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(P(outer, endDeg), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(P(inner, endDeg), true));
        figure.Segments.Add(new ArcSegment(P(inner, startDeg), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
