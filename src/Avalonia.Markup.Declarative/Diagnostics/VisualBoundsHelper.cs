using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Avalonia.Markup.Declarative.Diagnostics;

/// <summary>
/// Computes a single, consistent coordinate frame for every node: the <b>absolute, axis-aligned
/// bounds in the client-DIP space of a top-level</b> (its top-left is the client area origin;
/// pixels in a 96-DPI screenshot map 1:1 to these units).
/// </summary>
/// <remarks>
/// <para>
/// A visual's own <see cref="Visual.Bounds"/> is expressed relative to its parent, so nested nodes
/// (a child in a <c>*</c> grid column, content inside a popup) report parent-relative X/Y that cannot
/// be fed to <c>click_at</c>/<c>drag</c>. This helper resolves the same absolute frame that the hit
/// tester and pointer synthesis consume, so a reported <c>centerAbs</c> can be handed straight back to
/// <c>click_at</c> and round-trips through <c>hit_test</c>.
/// </para>
/// <para>
/// <c>TransformToVisual</c> accounts for every transform on the path — render transforms,
/// a <c>LayoutTransformControl</c>'s UI-scale, scroll offsets — so scaled/transformed subtrees are
/// reported correctly. It returns <see langword="null"/> when the two visuals are not in the same
/// visual tree (e.g. popup content realized in a separate <c>PopupRoot</c>); resolve such content
/// against its own top-level instead.
/// </para>
/// </remarks>
public static class VisualBoundsHelper
{
    /// <summary>
    /// Returns <paramref name="visual"/>'s axis-aligned bounds in the client-DIP coordinate space of
    /// <paramref name="root"/>, or <see langword="null"/> when they share no visual tree.
    /// </summary>
    public static Rect? GetClientBounds(Visual visual, Visual root)
    {
        if (visual is null || root is null)
            return null;

        if (ReferenceEquals(visual, root))
            return new Rect(visual.Bounds.Size);

        if (visual.TransformToVisual(root) is not { } matrix)
            return null;

        return new Rect(visual.Bounds.Size).TransformToAABB(matrix);
    }

    /// <summary>
    /// Returns the center point of <see cref="GetClientBounds"/>, the coordinate to hand to
    /// <c>click_at</c>/<c>tap</c>, or <see langword="null"/> when it cannot be resolved.
    /// </summary>
    public static Point? GetClientCenter(Visual visual, Visual root) =>
        GetClientBounds(visual, root) is { } bounds ? bounds.Center : null;

    /// <summary>
    /// Resolves the coordinate root for <paramref name="visual"/> — the top-level whose client-DIP
    /// space its absolute bounds are expressed in. This is the visual's own top-level, which correctly
    /// handles popup content hosted in a separate window/overlay root.
    /// </summary>
    public static Visual? GetCoordinateRoot(Visual visual) =>
        TopLevel.GetTopLevel(visual);
}
