using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Tangerine.App.Input;
using Tangerine.App.Jobs;
using Tangerine.App.Wheel;
using Tangerine.Core.Wheel;

namespace Tangerine.App;

/// <summary>
/// "--render-preview &lt;folder&gt;" renders the main surfaces to PNG and exits.
/// CI uses it as a smoke test that the UI loads on a real Windows desktop.
/// </summary>
internal static class Preview
{
    public static int Render(string folder, WheelWindow wheel, JobsWindow hud, Func<Window> settingsFactory)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var fixtures = Path.Combine(Path.GetTempPath(), "tangerine-preview");
            Directory.CreateDirectory(fixtures);
            string Fixture(string name)
            {
                var path = Path.Combine(fixtures, name);
                File.WriteAllText(path, "");
                return path;
            }

            var photo = Fixture("holiday.heic");
            Save(wheel.RenderPreview(WheelMode.Convert, [photo], new WheelHit(0, 1)), folder, "wheel-image.png");
            Save(wheel.RenderPreview(WheelMode.Convert, [Fixture("screen-recording.mov")], new WheelHit(1, 0)), folder, "wheel-video.png");
            Save(wheel.RenderPreview(WheelMode.Convert, [Fixture("report.pdf"), Fixture("appendix.pdf")], new WheelHit(0, 0)), folder, "wheel-pdfs.png");
            Save(wheel.RenderPreview(WheelMode.Convert, [Fixture("budget.xlsx")], new WheelHit(0, 2)), folder, "wheel-sheet.png");
            Save(wheel.RenderPreview(WheelMode.Actions, [photo], new WheelHit(0, 1)), folder, "wheel-actions.png");

            var settings = settingsFactory();
            settings.Show();
            settings.UpdateLayout();
            Save(RenderWindow(settings), folder, "settings.png");
            settings.Close();

            var running = new JobViewModel("Convert to WebP", "holiday.heic") { Cancel = () => { } };
            running.Progress = 0.42;
            hud.Add(running);
            var done = new JobViewModel("Compress (smaller file)", "screen-recording.mov");
            done.Complete([@"C:\Users\me\Videos\screen-recording (compressed).mp4"]);
            hud.Add(done);
            var failed = new JobViewModel("Convert to MP3", "interview.wav");
            failed.Failed("FFmpeg is required for this conversion.", ("Install FFmpeg", () => { }));
            hud.Add(failed);
            hud.UpdateLayout();
            Save(RenderWindow(hud), folder, "progress.png");
            hud.Hide();

            File.WriteAllText(Path.Combine(folder, "ok.txt"), "rendered");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(folder, "error.txt"), ex.ToString());
            return 1;
        }
    }

    private static BitmapSource RenderWindow(Window window)
    {
        var content = (FrameworkElement)window.Content;
        var width = Math.Max(1, content.ActualWidth + content.Margin.Left + content.Margin.Right);
        var height = Math.Max(1, content.ActualHeight + content.Margin.Top + content.Margin.Bottom);
        var bitmap = new RenderTargetBitmap((int)(width * 2), (int)(height * 2), 192, 192, PixelFormats.Pbgra32);
        if (!window.AllowsTransparency)
        {
            // Mica is composited by DWM and is not part of the visual tree, so paint a stand-in.
            var background = new DrawingVisual();
            using (var dc = background.RenderOpen())
                dc.DrawRectangle(new SolidColorBrush(PrefersLight() ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x20, 0x20, 0x20)), null, new Rect(0, 0, width, height));
            bitmap.Render(background);
        }
        bitmap.Render(window);
        bitmap.Freeze();
        return bitmap;
    }

    private static bool PrefersLight() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is not 0;

    private static void Save(BitmapSource bitmap, string folder, string name)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name));
        encoder.Save(stream);
    }
}
