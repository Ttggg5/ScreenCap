using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenCap.Services;

namespace ScreenCap.Annotations;

internal static class Compositor
{
    /// <summary>Crops the selection out of the snapshot and burns the annotations into it.</summary>
    public static BitmapSource Compose(ScreenSnapshot snapshot, Rect selection, IReadOnlyList<Annotation> annotations)
    {
        var cropped = snapshot.Crop(selection);
        if (annotations.Count == 0)
            return cropped;

        int w = cropped.PixelWidth, h = cropped.PixelHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(cropped, new Rect(0, 0, w, h));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
            dc.PushTransform(new TranslateTransform(-selection.X, -selection.Y));
            foreach (var annotation in annotations)
                annotation.Draw(dc);
            dc.Pop();
            dc.Pop();
        }

        var result = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }
}
