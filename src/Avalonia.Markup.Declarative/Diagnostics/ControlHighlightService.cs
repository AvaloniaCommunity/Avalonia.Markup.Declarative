using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Avalonia.Markup.Declarative.Diagnostics;

/// <summary>
/// Draws a diagnostic frame around live controls using Avalonia's <see cref="AdornerLayer"/>, so an
/// agent (or a human watching the app) can see <em>where a control actually ended up</em>. The frame is
/// an overlay: it does not affect layout, input, or app state, and it is removed by <see cref="Clear"/>.
/// </summary>
/// <remarks>
/// The adorner is added to the control's adorner layer and positioned automatically over the adorned
/// element, so it tracks the control's real, transformed bounds. All methods must run on the UI thread;
/// the caller is responsible for marshaling. Highlights persist until cleared, so a subsequent
/// screenshot captures them.
/// </remarks>
public static class ControlHighlightService
{
    // Every frame we add is tracked with its layer so Clear removes exactly ours and nothing else.
    private static readonly List<(AdornerLayer Layer, Control Adorner)> Active = new();

    /// <summary>The default frame color (a saturated pink/red that reads on most backgrounds).</summary>
    public static readonly Color DefaultColor = Color.FromRgb(0xFF, 0x2D, 0x55);

    /// <summary>
    /// Draws a frame around each of <paramref name="controls"/>. Returns the adorner controls that were
    /// added (a control with no adorner layer — e.g. one not attached to a top-level — is skipped, so
    /// the result may be shorter than the input). Pass the result to <see cref="Remove"/> to clear just
    /// this batch without disturbing other highlights.
    /// </summary>
    /// <param name="labelText">
    /// Optional label factory; when null a default "<c>Type #Name  W×H</c>" label is used. Ignored when
    /// <paramref name="showLabel"/> is false.
    /// </param>
    /// <param name="controls">The controls to highlight.</param>
    /// <param name="color">The frame color, or <see cref="DefaultColor"/> when omitted.</param>
    /// <param name="thickness">The frame thickness in device-independent pixels.</param>
    /// <param name="showLabel">Whether to show a label beside each frame.</param>
    public static IReadOnlyList<Control> Highlight(
        IEnumerable<Control> controls,
        Color? color = null,
        double thickness = 2,
        bool showLabel = true,
        Func<Control, string>? labelText = null)
    {
        ArgumentNullException.ThrowIfNull(controls);

        var frameColor = color ?? DefaultColor;
        var added = new List<Control>();

        foreach (var control in controls)
        {
            var layer = AdornerLayer.GetAdornerLayer(control);
            if (layer is null)
                continue;

            var adorner = CreateAdorner(control, frameColor, thickness, showLabel, labelText);
            AdornerLayer.SetAdornedElement(adorner, control);
            AdornerLayer.SetIsClipEnabled(adorner, false);
            layer.Children.Add(adorner);
            Active.Add((layer, adorner));
            added.Add(adorner);
        }

        return added;
    }

    /// <summary>
    /// Removes every frame previously added by <see cref="Highlight"/>. Returns the number removed.
    /// </summary>
    public static int Clear()
    {
        var removed = Active.Count;
        foreach (var (layer, adorner) in Active)
            layer.Children.Remove(adorner);
        Active.Clear();
        return removed;
    }

    /// <summary>
    /// Removes a specific batch of adorners (as returned by <see cref="Highlight"/>), leaving any other
    /// highlights in place. Returns the number removed.
    /// </summary>
    public static int Remove(IEnumerable<Control> adorners)
    {
        ArgumentNullException.ThrowIfNull(adorners);

        var set = new HashSet<Control>(adorners);
        var removed = 0;

        for (var i = Active.Count - 1; i >= 0; i--)
        {
            if (!set.Contains(Active[i].Adorner))
                continue;

            Active[i].Layer.Children.Remove(Active[i].Adorner);
            Active.RemoveAt(i);
            removed++;
        }

        return removed;
    }

    /// <summary>Whether any highlight frame is currently active.</summary>
    public static bool HasActiveHighlights => Active.Count > 0;

    private static Control CreateAdorner(Control target, Color color, double thickness, bool showLabel, Func<Control, string>? labelText)
    {
        var frame = new Border
        {
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(thickness),
            Background = new SolidColorBrush(color, 0.1),
            IsHitTestVisible = false,
        };

        if (showLabel)
        {
            frame.Child = new Border
            {
                Background = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Padding = new Thickness(3, 1),
                Child = new TextBlock
                {
                    Text = labelText?.Invoke(target) ?? DescribeTarget(target),
                    Foreground = Brushes.White,
                    FontSize = 10,
                },
            };
        }

        return frame;
    }

    private static string DescribeTarget(Control control)
    {
        var identity = string.IsNullOrEmpty(control.Name)
            ? control.GetType().Name
            : $"{control.GetType().Name} #{control.Name}";

        var bounds = control.Bounds;
        return $"{identity}  {bounds.Width:0}×{bounds.Height:0}";
    }
}
