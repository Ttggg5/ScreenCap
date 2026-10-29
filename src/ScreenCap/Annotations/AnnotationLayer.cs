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
            annotation.Render(dc);
        Current?.Render(dc);
    }
}
