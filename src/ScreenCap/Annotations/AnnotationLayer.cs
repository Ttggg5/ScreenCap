using System.Windows;
using System.Windows.Media;

namespace ScreenCap.Annotations;

/// <summary>Renders committed annotations plus the one currently being drawn.</summary>
internal sealed class AnnotationLayer(IReadOnlyList<Annotation> annotations) : FrameworkElement
{
    public Annotation? Current { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        foreach (var annotation in annotations)
            annotation.Draw(dc);
        Current?.Draw(dc);
    }
}

/// <summary>Draws the dashed outline and drag handles of the selected annotation.</summary>
internal sealed class AnnotationAdorner : FrameworkElement
{
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x1A, 0x9F, 0xFF));

    public Annotation? Target { get; set; }
    public double Scale { get; set; } = 1;
    public double HandleSize { get; set; } = 7;

    protected override void OnRender(DrawingContext dc)
    {
        if (Target == null)
            return;

        double s = Scale;
        if (Target is not LineAnnotation)
        {
            var box = Target.Bounds;
            if (Target.Handles.Length == 0)
                box.Inflate(3 * s, 3 * s); // free-form shapes: give the outline some breathing room
            dc.DrawRectangle(null, new Pen(Brushes.White, s), box);
            dc.DrawRectangle(null, new Pen(Accent, s) { DashStyle = new DashStyle([4, 3], 0) }, box);
        }

        double hs = HandleSize * s;
        var border = new Pen(Accent, s);
        foreach (var h in Target.Handles)
            dc.DrawRectangle(Brushes.White, border, new Rect(h.X - hs / 2, h.Y - hs / 2, hs, hs));
    }
}
