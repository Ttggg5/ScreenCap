using System.Windows;
using System.Windows.Media;

namespace ScreenCap.Annotations;

/// <summary>
/// Renders the committed annotations plus the one currently being drawn, clipped to the selection.
/// The two are kept in separate visuals so that drawing a stroke only re-renders that stroke, not every
/// annotation already on the capture; with software rendering that is what keeps drawing smooth.
/// </summary>
internal sealed class AnnotationLayer : FrameworkElement
{
    private readonly IReadOnlyList<Annotation> _annotations;
    private readonly DrawingVisual _committed = new();
    private readonly DrawingVisual _current = new();
    /// <summary>Holds both and clips them, so moving the selection doesn't re-render any annotation.</summary>
    private readonly ContainerVisual _clipped = new();
    private readonly RectangleGeometry _clipGeometry = new(Rect.Empty);
    private Annotation? _drawing;

    public AnnotationLayer(IReadOnlyList<Annotation> annotations)
    {
        _annotations = annotations;
        _clipped.Children.Add(_committed);
        _clipped.Children.Add(_current);
        _clipped.Clip = _clipGeometry;
        AddVisualChild(_clipped);
    }

    protected override int VisualChildrenCount => 1;

    /// <summary>Draws the annotations the capture opens with; the spotlight mask covers the whole size.</summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Redraw();
    }

    protected override Visual GetVisualChild(int index) => _clipped;

    /// <summary>The annotation being drawn, shown above the committed ones.</summary>
    public Annotation? Current
    {
        get => _drawing;
        set
        {
            if (_drawing == value)
                return;
            bool spotlight = _drawing is SpotlightAnnotation || value is SpotlightAnnotation;
            _drawing = value;
            if (spotlight)
                Redraw();
            else
                DrawCurrent();
        }
    }

    /// <summary>Only this area (the selection) shows annotations.</summary>
    /// <remarks>
    /// Clips the annotations' own visual rather than this full-screen element (<see cref="UIElement.Clip"/>),
    /// so a change only redraws where annotations are, not the whole screen.
    /// </remarks>
    public Rect ClipRect
    {
        get => _clipGeometry.Rect;
        set => _clipGeometry.Rect = value;
    }

    /// <summary>Re-renders everything, after the committed annotations changed.</summary>
    public void Redraw()
    {
        DrawCommitted();
        DrawCurrent();
    }

    /// <summary>Re-renders just the annotation being drawn, after it changed.</summary>
    public void RedrawCurrent()
    {
        if (_drawing is SpotlightAnnotation)
            DrawCommitted(); // its hole is cut into the shared dimmed layer
        else
            DrawCurrent();
    }

    private void DrawCommitted()
    {
        using var dc = _committed.RenderOpen();
        // The spotlight mask goes under the other annotations so they stay vivid.
        SpotlightAnnotation.DrawMask(dc, _annotations.Append(_drawing), new Rect(RenderSize));
        foreach (var annotation in _annotations)
            annotation.Draw(dc);
    }

    private void DrawCurrent()
    {
        using var dc = _current.RenderOpen();
        _drawing?.Draw(dc);
    }
}

/// <summary>Draws the dashed outline and drag handles of the selected annotation.</summary>
internal sealed class AnnotationAdorner : FrameworkElement
{
    private static readonly Brush Accent = Frozen(new SolidColorBrush(Color.FromRgb(0x1A, 0x9F, 0xFF)));

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze(); // shared by every overlay
        return brush;
    }

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
