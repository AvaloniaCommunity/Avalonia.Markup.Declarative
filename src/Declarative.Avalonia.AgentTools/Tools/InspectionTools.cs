using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Declarative.Diagnostics;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Declarative.Avalonia.AgentTools.Tools;

/// <summary>
/// Inspection MCP tools that let an agent see the running Avalonia app. Most are read-only (tree,
/// layout, screenshots, errors); <c>highlight</c> adds a non-destructive diagnostic overlay. Tools
/// compose: get a tree with names, read one control's layout, then screenshot or highlight it.
/// </summary>
[McpServerToolType]
public sealed class InspectionTools
{
    private InspectionTools() { }

    /// <summary>Runs the <c>get_app_info</c> MCP tool.</summary>
    [McpServerTool(Name = "get_app_info", ReadOnly = true), Description(
        "Returns a one-call orientation of the running app: open windows (title, index, client size, " +
        "render scaling), the focused element, the active/requested theme variant, the Avalonia version, " +
        "whether component tracking (hot reload) is active, and whether the interaction tier is enabled. " +
        "Call this first when attaching to an unfamiliar app.")]
    public static Task<string> GetAppInfo() =>
        AgentToolContext.RunOnUiThreadAsync(() =>
        {
            var builder = new StringBuilder();

            var topLevels = AgentToolContext.GetTopLevels();
            builder.Append("Windows/top-levels: ").Append(topLevels.Count).Append('\n');
            for (var i = 0; i < topLevels.Count; i++)
            {
                var top = topLevels[i];
                var title = (top as Window)?.Title ?? top.GetType().Name;
                builder.Append("  [").Append(i).Append("] ").Append(title)
                    .Append(" client=").Append(FormatSize(top.ClientSize))
                    .Append(" scaling=").Append(top.RenderScaling.ToString("0.##", CultureInfo.InvariantCulture));
                if (top is Window w)
                    builder.Append(w.IsActive ? " [active]" : string.Empty);
                builder.Append('\n');
            }

            var popups = AgentToolContext.GetOpenPopups();
            builder.Append("Open popups: ").Append(popups.Count);
            if (popups.Count > 0)
                builder.Append(" (").Append(string.Join(", ", popups.Select(p => p.Owner))).Append(')');
            builder.Append('\n');

            var focused = AgentToolContext.GetFocusedElement();
            builder.Append("Focused: ").Append(focused is null ? "none" : DescribeElement(focused)).Append('\n');

            var app = global::Avalonia.Application.Current;
            builder.Append("Theme: actual=").Append(app?.ActualThemeVariant?.ToString() ?? "?")
                .Append(" requested=").Append(app?.RequestedThemeVariant?.ToString() ?? "Default").Append('\n');

            var version = typeof(global::Avalonia.Application).Assembly.GetName().Version;
            builder.Append("Avalonia: ").Append(version?.ToString() ?? "?").Append('\n');

            var componentCount = ComponentRegistry.GetActiveViews().Count;
            builder.Append("Component tracking (hot reload): ")
                .Append(componentCount > 0 ? $"active ({componentCount} views)" : "inactive").Append('\n');

            builder.Append("Interaction tier (invoke etc.): ")
                .Append(AgentToolContext.Options.EnableInteraction ? "enabled" : "disabled").Append('\n');

            return builder.ToString();
        });

    /// <summary>Runs the <c>get_visual_tree</c> MCP tool.</summary>
    [McpServerTool(Name = "get_visual_tree", ReadOnly = true), Description(
        "Returns a text rendering of the live visual tree (type, #Name, layout bounds, key properties incl. " +
        "non-default alignment/margin). Each node also carries abs=[…] and center=(x,y): ABSOLUTE " +
        "client-DIP coordinates (top-level origin, 96-DPI pixels) — the SAME frame click_at/tap/drag " +
        "consume and hit_test returns. The leading [x y w h] stays parent-relative for layout debugging; " +
        "pass a node's center=(x,y) straight to click_at/tap. Bounds reveal layout problems (zero-size, " +
        "off-screen, overlap) without pixels. Pass rootName to dump a named subtree; omit it to dump the " +
        "active window. Use maxDepth to cap depth and filter to show only subtrees matching a type/name " +
        "substring (keeps large trees readable).")]
    public static Task<string> GetVisualTree(
        [Description("Optional Name of a control to use as the root. Omit for the active window.")]
        string? rootName = null,
        [Description("Optional maximum depth to traverse (0 = root only). Omit for unlimited.")]
        int? maxDepth = null,
        [Description("Optional type/name substring; only matching subtrees are shown, with ancestor context.")]
        string? filter = null) =>
        AgentToolContext.RunOnUiThreadAsync(() =>
        {
            global::Avalonia.Visual? root = string.IsNullOrWhiteSpace(rootName)
                ? AgentToolContext.GetPrimaryTopLevel()
                : AgentToolContext.FindControl(rootName);

            if (root is null)
                return string.IsNullOrWhiteSpace(rootName)
                    ? "No active window/top-level was found."
                    : $"No control named '{rootName}' was found.";

            var options = new VisualTreeDumpOptions
            {
                MaxDepth = maxDepth ?? -1,
                Filter = filter,
            };

            var dump = VisualTreeInspector.DumpVisualTree(root, options);

            // When dumping the whole window, also show any open popups (dropdowns, menus, flyouts) —
            // their content lives outside the window tree and is otherwise invisible.
            if (string.IsNullOrWhiteSpace(rootName))
            {
                var popups = AgentToolContext.GetOpenPopups();
                if (popups.Count > 0)
                {
                    var builder = new StringBuilder(dump);
                    foreach (var popup in popups)
                    {
                        builder.Append("\n[popup] open, owned by ").Append(popup.Owner).Append(":\n");
                        builder.Append(VisualTreeInspector.DumpVisualTree(popup.ContentRoot, options));
                    }

                    return builder.ToString();
                }
            }

            return dump;
        });

    /// <summary>Runs the <c>get_properties</c> MCP tool.</summary>
    [McpServerTool(Name = "get_properties", ReadOnly = true), Description(
        "Returns a control's property values: effective state (IsEnabled/IsVisible/IsFocused/Opacity/...), " +
        "DataContext type, style classes, and every locally-set styled property (the values the author " +
        "assigned — good for debugging appearance and bindings). Pass a comma-separated 'properties' list " +
        "to read only those (matched against styled properties first, then CLR properties). The selector " +
        "is a control Name, visible label, or type name.")]
    public static Task<string> GetProperties(
        [Description("Control Name, visible label, or type name.")]
        string selector,
        [Description("Optional comma-separated property names to read (e.g. 'Background,FontSize').")]
        string? properties = null) =>
        AgentToolContext.RunOnUiThreadAsync(() =>
        {
            var control = AgentToolContext.FindControl(selector)
                          ?? AgentToolContext.FindControls(selector).FirstOrDefault();
            if (control is null)
                return $"No control matching '{selector}' was found. Use get_visual_tree to discover names and types.";

            var names = string.IsNullOrWhiteSpace(properties)
                ? null
                : properties.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return PropertyInspector.DescribeProperties(control, names);
        });

    /// <summary>Runs the <c>get_property_sources</c> MCP tool.</summary>
    [McpServerTool(Name = "get_property_sources", ReadOnly = true), Description(
        "Reports where a property's value comes from — local set, style, template, animation or " +
        "inheritance (the binding priority) — for one property, or for every set styled property when " +
        "'property' is omitted. Use this when a style or template overrides the value you expected. " +
        "Selector is a Name, visible label, or type name.")]
    public static Task<string> GetPropertySources(
        [Description("Control Name, visible label, or type name.")]
        string selector,
        [Description("Optional single property name (e.g. 'Background'). Omit to list all set properties.")]
        string? property = null) =>
        AgentToolContext.RunOnUiThreadAsync(() =>
        {
            var control = AgentToolContext.FindControl(selector)
                          ?? AgentToolContext.FindControls(selector).FirstOrDefault();
            if (control is null)
                return $"No control matching '{selector}' was found. Use get_visual_tree to discover names and types.";

            return PropertySourceInspector.Describe(control, property);
        });

    /// <summary>Runs the <c>get_data_context</c> MCP tool.</summary>
    [McpServerTool(Name = "get_data_context", ReadOnly = true), Description(
        "Renders a control's DataContext (view-model) as text — property names and current values a " +
        "binding could target — for debugging bindings without a debugger. Cycle-safe; collections are " +
        "summarized. Pass depth 1-3 (default 1) to expand nested objects. Selector is a Name, label or type.")]
    public static Task<string> GetDataContext(
        [Description("Control Name, visible label, or type name.")]
        string selector,
        [Description("Nesting depth to expand (1-3, default 1).")]
        int? depth = null) =>
        AgentToolContext.RunOnUiThreadAsync(() =>
        {
            var control = AgentToolContext.FindControl(selector)
                          ?? AgentToolContext.FindControls(selector).FirstOrDefault();
            if (control is null)
                return $"No control matching '{selector}' was found. Use get_visual_tree to discover names and types.";

            return $"{control.GetType().Name}{(control.Name is { Length: > 0 } n ? $" #{n}" : string.Empty)} DataContext:\n" +
                   DataContextInspector.Describe(control.DataContext, depth ?? 1);
        });

    /// <summary>Runs the <c>get_source</c> MCP tool.</summary>
    [McpServerTool(Name = "get_source", ReadOnly = true), Description(
        "Maps a control back to the declarative component (ViewBase subclass) that built it, by full type " +
        "name and assembly, plus any enclosing components. Use this to find which class to edit. Selector " +
        "is a Name, visible label, or type name.")]
    public static Task<string> GetSource(
        [Description("Control Name, visible label, or type name.")]
        string selector) =>
        AgentToolContext.RunOnUiThreadAsync(() =>
        {
            var control = AgentToolContext.FindControl(selector)
                          ?? AgentToolContext.FindControls(selector).FirstOrDefault();
            if (control is null)
                return $"No control matching '{selector}' was found. Use get_visual_tree to discover names and types.";

            return ComponentSourceLocator.Describe(control);
        });

    /// <summary>Runs the <c>find_text</c> MCP tool.</summary>
    [McpServerTool(Name = "find_text", ReadOnly = true), Description(
        "Finds controls whose visible text contains the given text (case-insensitive) — the reverse of " +
        "reading a label off a screenshot. Checks TextBlock/TextBox text and string Content/Header. " +
        "Returns each match's type, #Name, bounds and window.")]
    public static Task<string> FindText(
        [Description("Text to search for (substring, case-insensitive).")]
        string text) =>
        AgentToolContext.RunOnUiThreadAsync(() =>
        {
            if (string.IsNullOrEmpty(text))
                return "Provide some text to search for.";

            var matches = AgentToolContext.FindByText(text);
            if (matches.Count == 0)
                return $"No control with visible text containing '{text}' was found.";

            const int cap = 50;
            var builder = new StringBuilder();
            builder.Append(matches.Count).Append(" match(es)");
            if (matches.Count > cap)
                builder.Append(" (showing first ").Append(cap).Append(')');
            builder.Append(":\n");

            foreach (var control in matches.Take(cap))
            {
                builder.Append("  ").Append(control.GetType().Name);
                if (control.Name is { Length: > 0 } name)
                    builder.Append(" #").Append(name);
                builder.Append(' ').Append(FormatBounds(control.Bounds));
                if (TopLevel.GetTopLevel(control) is { } top)
                    builder.Append("  in ").Append((top as Window)?.Title ?? top.GetType().Name);
                builder.Append('\n');
            }

            return builder.ToString();
        });

    /// <summary>Runs the <c>hit_test</c> MCP tool.</summary>
    [McpServerTool(Name = "hit_test", ReadOnly = true), Description(
        "Returns the control(s) at a pixel coordinate — the reverse of a screenshot. Screenshots render " +
        "at 96 DPI, so screenshot pixel coordinates equal these absolute client-DIP coordinates (the same " +
        "frame get_visual_tree's abs/center and click_at/drag use). Reports the topmost-hit visual up to " +
        "the root, and how the topmost control can be driven (automation-invokable / focusable / " +
        "raw-pointer-only) so you can pick click_at(automation) vs tap/drag(pointer). Pass x/y and an " +
        "optional windowId (title or 0-based index).")]
    public static Task<string> HitTest(
        [Description("X coordinate in window-client pixels/DIPs.")]
        double x,
        [Description("Y coordinate in window-client pixels/DIPs.")]
        double y,
        [Description("Optional window title or 0-based index. Omit for the main window.")]
        string? windowId = null) =>
        AgentToolContext.RunOnUiThreadAsync(() =>
        {
            var top = AgentToolContext.ResolveTopLevel(windowId);
            if (top is null)
                return "No active window/top-level was found.";

            return HitTester.Describe(top, new global::Avalonia.Point(x, y));
        });

    /// <summary>Runs the <c>get_layout</c> MCP tool.</summary>
    [McpServerTool(Name = "get_layout", ReadOnly = true), Description(
        "Returns a detailed layout report for a control: its bounds, its position relative to the window, " +
        "the size it requested vs got (DesiredSize, Width/Height, Min/Max), HorizontalAlignment/" +
        "VerticalAlignment, Margin/Padding, visibility (IsVisible/IsEffectivelyVisible/IsEnabled/Opacity), " +
        "flags (zero-size/off-screen/partially-clipped/hidden) and the ancestor chain that arranged it. " +
        "The selector is a control Name, its visible label, or a type name (e.g. 'Button' matches all buttons).")]
    public static Task<string> GetLayout(
        [Description("Control Name, visible label, or type name (e.g. 'Button').")]
        string selector) =>
        AgentToolContext.RunOnUiThreadAsync(() =>
        {
            var matches = AgentToolContext.FindControls(selector);
            if (matches.Count == 0)
                return $"No control matching '{selector}' was found (by Name, visible label, or type). " +
                       "Use get_visual_tree to discover names and types.";

            var builder = new StringBuilder();
            builder.Append(LayoutInspector.DescribeLayout(matches[0]));

            if (matches.Count > 1)
            {
                builder.Append('\n')
                    .Append(matches.Count)
                    .Append(" controls matched '")
                    .Append(selector)
                    .Append("'; the report above is for the first. Others:\n");

                for (var i = 1; i < matches.Count; i++)
                {
                    var control = matches[i];
                    builder.Append("  ").Append(control.GetType().Name);
                    if (control.Name is { Length: > 0 } name)
                        builder.Append(" #").Append(name);
                    builder.Append(' ').Append(FormatBounds(control.Bounds)).Append('\n');
                }
            }

            return builder.ToString();
        });

    /// <summary>Runs the <c>layout_audit</c> MCP tool.</summary>
    [McpServerTool(Name = "layout_audit", ReadOnly = true), Description(
        "Sweeps a subtree and reports common layout problems: zero-size (a control that wanted space but " +
        "got none), off-screen / partially-clipped, out-of-parent (overflows its container), overlap " +
        "(siblings colliding in a StackPanel/DockPanel/WrapPanel) and text-clipped. Findings are ordered " +
        "errors-first. Pass rootName to audit a named subtree; omit it for the active window. Use this to " +
        "verify a layout change instead of eyeballing a screenshot.")]
    public static Task<string> LayoutAudit(
        [Description("Optional Name of a control to audit as the root. Omit for the active window.")]
        string? rootName = null) =>
        AgentToolContext.RunOnUiThreadAsync(() =>
        {
            global::Avalonia.Visual? root = string.IsNullOrWhiteSpace(rootName)
                ? AgentToolContext.GetPrimaryTopLevel()
                : AgentToolContext.FindControl(rootName);

            if (root is null)
                return string.IsNullOrWhiteSpace(rootName)
                    ? "No active window/top-level was found."
                    : $"No control named '{rootName}' was found.";

            var findings = LayoutAuditor.Audit(root);
            if (findings.Count == 0)
                return "No layout anomalies found.";

            var builder = new StringBuilder();
            builder.Append(findings.Count).Append(" finding(s):\n");
            foreach (var finding in findings)
                builder.Append("  ").Append(finding.Format()).Append('\n');

            return builder.ToString();
        });

    /// <summary>Runs the <c>list_components</c> MCP tool.</summary>
    [McpServerTool(Name = "list_components", ReadOnly = true), Description(
        "Lists the active declarative views (components) currently tracked in the running app: type, " +
        "Name, attachment state and bounds. Use a returned Name as input to other tools.")]
    public static Task<string> ListComponents() =>
        AgentToolContext.RunOnUiThreadAsync(() =>
        {
            var views = ComponentRegistry.GetActiveViews();
            if (views.Count == 0)
                return "No active components are tracked. (Components are tracked while hot reload is enabled, which is the default.)";

            var builder = new StringBuilder();
            foreach (var view in views)
            {
                builder
                    .Append(view.TypeName)
                    .Append(view.Name is null ? string.Empty : $" #{view.Name}")
                    .Append(view.IsAttached ? " [attached]" : " [detached]")
                    .Append(' ')
                    .Append(FormatBounds(view.Bounds))
                    .Append('\n');
            }

            return builder.ToString();
        });

    /// <summary>Runs the <c>screenshot_window</c> MCP tool.</summary>
    [McpServerTool(Name = "screenshot_window", ReadOnly = true), Description(
        "Captures a PNG screenshot of a window/top-level. Rendering re-draws the visual, so it works " +
        "even for a minimized or occluded window. Pass windowId (window title or 0-based index); omit " +
        "for the main window. Set annotate=true to frame and label every named control (optionally " +
        "narrowed by a type/name 'filter') — a fast way to map names to positions. On a big window pass " +
        "maxWidth (e.g. 1280) to shrink the image: a full 2560px capture costs a lot of tokens and rarely " +
        "shows more. To look closely at something small, use screenshot_region with scale instead.")]
    public static Task<CallToolResult> ScreenshotWindow(
        [Description("Optional window title or 0-based index. Omit for the main window.")]
        string? windowId = null,
        [Description("Set true to overlay a labeled frame on every named control before capturing.")]
        bool annotate = false,
        [Description("Optional type/name substring to limit which controls are annotated.")]
        string? filter = null,
        [Description("Optional size multiplier: 2 doubles (nearest-neighbour, crisp pixels), 0.5 halves.")]
        double? scale = null,
        [Description("Optional cap on the delivered width in pixels; shrinks proportionally, never enlarges.")]
        int? maxWidth = null) =>
        AgentToolContext.RunOnUiThreadAsync(() =>
        {
            try
            {
                var top = AgentToolContext.ResolveTopLevel(windowId);
                if (top is null)
                    return Error("No active window/top-level was found.");

                var title = (top as Window)?.Title ?? top.GetType().Name;

                if (!annotate)
                {
                    // Stable capture: drain layout/render jobs and retry once on a degenerate (flat)
                    // frame, so a capture right after a state change isn't an empty/dark image (P6).
                    var captured = Rescale(ControlScreenshotService.CaptureTopLevelStable(top), scale, maxWidth, out var resized);
                    var id = ScreenshotStore.Add($"Window '{title}'{resized}", captured);
                    return Image(captured.Png, $"Window '{title}' {FormatBounds(top.Bounds)}{resized} (id={id})");
                }

                var named = top.GetSelfAndVisualDescendants()
                    .OfType<Control>()
                    .Where(c => c.Name is { Length: > 0 } && MatchesFilter(c, filter))
                    .ToList();

                var adorners = ControlHighlightService.Highlight(named, labelText: c => $"#{c.Name}");
                try
                {
                    Dispatcher.UIThread.RunJobs();
                    var captured = Rescale(ControlScreenshotService.CaptureTopLevelStable(top), scale, maxWidth, out var resized);
                    return Image(captured.Png, $"Window '{title}' {FormatBounds(top.Bounds)}{resized} — annotated {adorners.Count} named control(s).");
                }
                finally
                {
                    // Remove only the frames we added, leaving any user highlights untouched.
                    ControlHighlightService.Remove(adorners);
                }
            }
            catch (Exception ex)
            {
                return Error($"Failed to capture window: {ex.Message}");
            }
        });

    /// <summary>Runs the <c>screenshot_control</c> MCP tool.</summary>
    [McpServerTool(Name = "screenshot_control", ReadOnly = true), Description(
        "Captures a PNG screenshot of a single control identified by its Name. mode='isolated' renders " +
        "just the control's subtree; mode='in_context' renders the window and crops to the control. " +
        "Waits for a rendered frame and retries a degenerate (empty/flat) capture once. If the target is " +
        "a closed popup/flyout (zero size), returns a hint on how to open it (see open_popup). " +
        "scale enlarges a small control (nearest-neighbour, so pixels stay crisp) and maxWidth caps a " +
        "large one.")]
    public static Task<CallToolResult> ScreenshotControl(
        [Description("The Name of the control to capture.")]
        string name,
        [Description("Capture mode: 'isolated' (default) or 'in_context'.")]
        string? mode = null,
        [Description("Optional size multiplier: 4 quadruples (nearest-neighbour, crisp pixels), 0.5 halves.")]
        double? scale = null,
        [Description("Optional cap on the delivered width in pixels; shrinks proportionally, never enlarges.")]
        int? maxWidth = null) =>
        AgentToolContext.RunOnUiThreadAsync(() =>
        {
            var control = AgentToolContext.FindControl(name);
            if (control is null)
                return Error($"No control named '{name}' was found. Use get_visual_tree to discover names.");

            // If the named target is a closed popup/flyout, capturing it is a dead end — guide the agent
            // to open it first instead of returning a bare "has a zero size" (P5).
            if (PopupLocator.DescribeIfClosedPopup(control) is { } closedHint)
                return Error(closedHint);

            try
            {
                var screenshotMode = string.Equals(mode, "in_context", StringComparison.OrdinalIgnoreCase)
                    ? ScreenshotMode.InContext
                    : ScreenshotMode.Isolated;

                var captured = Rescale(ControlScreenshotService.CaptureControlStable(control, screenshotMode), scale, maxWidth, out var resized);
                var id = ScreenshotStore.Add($"Control '{name}' ({control.GetType().Name}){resized}", captured);
                return Image(captured.Png, $"Control '{name}' ({control.GetType().Name}) {FormatBounds(control.Bounds)} mode={screenshotMode}{resized} (id={id})");
            }
            catch (Exception ex)
            {
                // A zero-size failure on a control that turns out to be a closed popup: surface the hint.
                if (PopupLocator.DescribeIfClosedPopup(control) is { } hint)
                    return Error(hint);
                return Error($"Failed to capture control '{name}': {ex.Message}");
            }
        });

    /// <summary>Runs the <c>screenshot_region</c> MCP tool.</summary>
    [McpServerTool(Name = "screenshot_region", ReadOnly = true), Description(
        "Captures a rectangle of a window as PNG. Coordinates are the SAME absolute client-DIP frame as " +
        "hit_test / click_at / get_visual_tree's center=(x,y), so you can screenshot exactly the area you " +
        "just interacted with. This is the tool for looking inside a custom canvas, which is one opaque " +
        "control to get_visual_tree: crop to the interesting rectangle instead of shipping the whole " +
        "window. Pass scale to enlarge (nearest-neighbour, so a 16x16 sprite at scale=8 stays crisp " +
        "rather than blurred) or maxWidth to shrink. The result lands in the screenshot store, so " +
        "compare_screenshots works on regions too — capture the same rectangle before and after an edit " +
        "to see precisely what changed.")]
    public static Task<CallToolResult> ScreenshotRegion(
        [Description("Left edge in absolute client-DIP pixels.")] double x,
        [Description("Top edge in absolute client-DIP pixels.")] double y,
        [Description("Width in DIPs.")] double width,
        [Description("Height in DIPs.")] double height,
        [Description("Optional window title or 0-based index. Omit for the main window.")]
        string? windowId = null,
        [Description("Optional size multiplier: 8 enlarges 8x (nearest-neighbour, crisp pixels), 0.5 halves.")]
        double? scale = null,
        [Description("Optional cap on the delivered width in pixels; shrinks proportionally, never enlarges.")]
        int? maxWidth = null) =>
        AgentToolContext.RunOnUiThreadAsync(() =>
        {
            if (width <= 0 || height <= 0)
                return Error($"The region must have a positive size (got {width}x{height}).");

            var top = AgentToolContext.ResolveTopLevel(windowId);
            if (top is null)
                return Error("No active window/top-level was found.");

            try
            {
                var region = new global::Avalonia.Rect(x, y, width, height);
                var captured = Rescale(ControlScreenshotService.CaptureRegionStable(top, region), scale, maxWidth, out var resized);
                var title = (top as Window)?.Title ?? top.GetType().Name;
                var label = $"Region {FormatBounds(region)} of '{title}'{resized}";
                var id = ScreenshotStore.Add(label, captured);
                return Image(captured.Png, $"{label} (id={id})");
            }
            catch (Exception ex)
            {
                return Error($"Failed to capture region: {ex.Message}");
            }
        });

    /// <summary>
    /// Applies the optional scale/maxWidth to a capture and reports what happened, so the agent can tell
    /// the delivered pixel size from the real on-screen size (and not misread a shrunk shot as a layout
    /// change).
    /// </summary>
    private static CapturedImage Rescale(CapturedImage captured, double? scale, int? maxWidth, out string note)
    {
        note = string.Empty;
        if (ControlScreenshotService.ResolveTargetSize(captured.Size, scale, maxWidth) is not { } target)
            return captured;

        var resampled = ControlScreenshotService.Resample(captured, target);
        var how = target.Width > captured.Size.Width ? "nearest-neighbour" : "box-averaged";
        note = $" — delivered at {target.Width}x{target.Height} ({how} from {captured.Size.Width}x{captured.Size.Height})";
        return resampled;
    }

    /// <summary>Runs the <c>list_screenshots</c> MCP tool.</summary>
    [McpServerTool(Name = "list_screenshots", ReadOnly = true), Description(
        "Lists recently captured screenshots (id, label, size, when) so you can pick ids for compare_screenshots.")]
    public static Task<string> ListScreenshots() =>
        Task.FromResult(RenderScreenshotList());

    /// <summary>Runs the <c>compare_screenshots</c> MCP tool.</summary>
    [McpServerTool(Name = "compare_screenshots", ReadOnly = true), Description(
        "Diffs two captured screenshots and returns a diff image (changed pixels in red) plus stats: " +
        "percent changed and the changed-region bounds. Screenshots are captured by screenshot_window / " +
        "screenshot_control (each returns an id). Omit ids to compare the two most recent (before/after).")]
    public static Task<CallToolResult> CompareScreenshots(
        [Description("Optional 'before' screenshot id. Omit to use the second-most-recent.")]
        string? idA = null,
        [Description("Optional 'after' screenshot id. Omit to use the most recent.")]
        string? idB = null) =>
        AgentToolContext.RunOnUiThreadAsync(() =>
        {
            var list = ScreenshotStore.List();
            if (list.Count < 2 && (idA is null || idB is null))
                return Error("Need at least two screenshots to compare. Capture with screenshot_window / screenshot_control first.");

            var a = idA ?? (list.Count >= 2 ? list[^2].Id : list[^1].Id);
            var b = idB ?? list[^1].Id;

            var comparison = ScreenshotStore.Compare(a, b);
            if (comparison is null)
                return Error($"Unknown screenshot id ('{a}' or '{b}'). Use list_screenshots to see available ids.");

            if (comparison.SizeMismatch)
                return Text($"Compared {a} → {b}: sizes differ ({comparison.SizeA.Width}x{comparison.SizeA.Height} vs " +
                            $"{comparison.SizeB.Width}x{comparison.SizeB.Height}) — the layout resized, so a pixel diff is not meaningful.");

            var percent = (comparison.ChangedFraction * 100).ToString("0.##", CultureInfo.InvariantCulture);
            var bounds = comparison.ChangedBounds is { } r
                ? $"changed region [x={r.X} y={r.Y} w={r.Width} h={r.Height}]"
                : "no changed region";
            var summary = $"Compared {a} → {b}: {percent}% of pixels changed ({comparison.ChangedPixels}/{comparison.TotalPixels}); {bounds}.";

            return comparison.DiffPng is { } diff ? Image(diff, summary) : Text(summary);
        });

    /// <summary>Runs the <c>wait_for</c> MCP tool.</summary>
    [McpServerTool(Name = "wait_for", ReadOnly = true), Description(
        "Waits until a control reaches a condition, polling so you don't busy-loop get_visual_tree after " +
        "an invoke or hot reload. Conditions: 'exists', 'gone', 'visible', 'hidden', 'enabled', " +
        "'bounds-stable' (bounds unchanged for ~300ms — good after animations). Returns when met or on " +
        "timeout (default 5000ms, max 30000ms). Selector is a Name or visible label.")]
    public static async Task<string> WaitFor(
        [Description("Control Name or visible label to watch.")]
        string selector,
        [Description("Condition: exists | gone | visible | hidden | enabled | bounds-stable.")]
        string condition,
        [Description("Optional timeout in milliseconds (default 5000, max 30000).")]
        int? timeoutMs = null)
    {
        var cond = condition?.Trim().ToLowerInvariant() ?? string.Empty;
        if (cond is not ("exists" or "gone" or "visible" or "hidden" or "enabled" or "bounds-stable"))
            return $"Unknown condition '{condition}'. Use exists | gone | visible | hidden | enabled | bounds-stable.";

        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs ?? 5000, 100, 30000));
        var start = DateTime.UtcNow;

        global::Avalonia.Rect? previousBounds = null;
        var lastChange = start;

        while (true)
        {
            var snapshot = await AgentToolContext.RunOnUiThreadAsync(() =>
            {
                var control = AgentToolContext.FindControl(selector);
                return control is null
                    ? (Found: false, Visible: false, Enabled: false, Bounds: default(global::Avalonia.Rect), Desc: $"'{selector}' not found")
                    : (Found: true, Visible: control.IsEffectivelyVisible, Enabled: control.IsEnabled, Bounds: control.Bounds,
                       Desc: $"{control.GetType().Name}{(control.Name is { Length: > 0 } n ? $" #{n}" : string.Empty)} {FormatBounds(control.Bounds)}");
            });

            var now = DateTime.UtcNow;

            if (cond == "bounds-stable")
            {
                if (!snapshot.Found)
                {
                    previousBounds = null;
                }
                else
                {
                    if (previousBounds is not { } prev || !BoundsClose(prev, snapshot.Bounds))
                        lastChange = now;
                    previousBounds = snapshot.Bounds;
                }
            }

            var satisfied = cond switch
            {
                "exists" => snapshot.Found,
                "gone" => !snapshot.Found,
                "visible" => snapshot.Found && snapshot.Visible,
                "hidden" => !snapshot.Found || !snapshot.Visible,
                "enabled" => snapshot.Found && snapshot.Enabled,
                "bounds-stable" => snapshot.Found && (now - lastChange) >= TimeSpan.FromMilliseconds(300),
                _ => false
            };

            if (satisfied)
                return $"Condition '{cond}' met for '{selector}' after {(now - start).TotalMilliseconds:0}ms. {snapshot.Desc}";

            if (now - start > timeout)
                return $"Timed out after {timeout.TotalMilliseconds:0}ms waiting for '{cond}' on '{selector}'. Last: {snapshot.Desc}";

            await Task.Delay(100);
        }
    }

    /// <summary>Runs the <c>wait_idle</c> MCP tool.</summary>
    [McpServerTool(Name = "wait_idle", ReadOnly = true), Description(
        "Waits until the UI thread has drained its queued work (layout, data-binding, rendering). Call " +
        "after a hot reload or an interaction to be sure the UI has settled before you screenshot or " +
        "read the tree.")]
    public static async Task<string> WaitIdle()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        return "UI thread is idle.";
    }

    /// <summary>Runs the <c>highlight</c> MCP tool.</summary>
    [McpServerTool(Name = "highlight"), Description(
        "Draws a frame around a control (or all controls of a type) in the live app so you can see where " +
        "it actually ended up, then returns an in-context screenshot showing the frame. The frame is an " +
        "overlay — it does not change layout, input or app state — and persists until you clear it, so " +
        "later screenshots also show it. Pass action='clear' to remove all frames. The selector is a " +
        "control Name, visible label, or type name (e.g. 'Button'). Optionally pass a color (e.g. '#00A0FF' or 'Lime').")]
    public static Task<CallToolResult> Highlight(
        [Description("Control Name, visible label, or type name. Omit only when action='clear'.")]
        string? selector = null,
        [Description("Action: 'highlight' (default) or 'clear' (remove all frames).")]
        string? action = null,
        [Description("Optional frame color, e.g. '#00A0FF' or 'Lime'. Defaults to pink/red.")]
        string? color = null) =>
        AgentToolContext.RunOnUiThreadAsync(() =>
        {
            try
            {
                if (string.Equals(action, "clear", StringComparison.OrdinalIgnoreCase))
                {
                    var cleared = ControlHighlightService.Clear();
                    return Text($"Cleared {cleared} highlight frame(s).");
                }

                if (string.IsNullOrWhiteSpace(selector))
                    return Error("A selector is required to highlight. (Use action='clear' to remove frames.)");

                var matches = AgentToolContext.FindControls(selector);
                if (matches.Count == 0)
                    return Error($"No control matching '{selector}' was found (by Name, visible label, or type). " +
                                 "Use get_visual_tree to discover names and types.");

                Color? frameColor = null;
                if (!string.IsNullOrWhiteSpace(color))
                {
                    if (!Color.TryParse(color, out var parsed))
                        return Error($"'{color}' is not a valid color. Use e.g. '#00A0FF' or a name like 'Lime'.");
                    frameColor = parsed;
                }

                var added = ControlHighlightService.Highlight(matches, frameColor).Count;
                if (added == 0)
                    return Error($"'{selector}' matched {matches.Count} control(s), but none has an adorner layer " +
                                 "to draw on (it may not be attached to a window yet).");

                // Flush a layout pass so the freshly-added frame is arranged before we render it.
                Dispatcher.UIThread.RunJobs();

                var top = TopLevel.GetTopLevel(matches[0]);
                if (top is null)
                    return Text($"Highlighted {added} control(s) matching '{selector}', but no window was available to screenshot.");

                var png = ControlScreenshotService.CaptureTopLevelPng(top);
                var title = (top as Window)?.Title ?? top.GetType().Name;
                return Image(png, $"Highlighted {added} control(s) matching '{selector}' in window '{title}'. " +
                                  "Frames persist until highlight action='clear'.");
            }
            catch (Exception ex)
            {
                return Error($"Failed to highlight '{selector}': {ex.Message}");
            }
        });

    /// <summary>Runs the <c>get_errors</c> MCP tool.</summary>
    [McpServerTool(Name = "get_errors", ReadOnly = true), Description(
        "Returns recent build, binding, converter and runtime errors from the diagnostics buffer. Runtime " +
        "errors include unhandled exceptions thrown in event handlers/commands (with the throw site when " +
        "symbols are available). Pass an ISO-8601 sinceTimestamp to only get newer entries.")]
    public static string GetErrors(
        [Description("Optional ISO-8601 timestamp; only entries at/after it are returned.")]
        string? sinceTimestamp = null)
    {
        DateTimeOffset? since = null;
        if (!string.IsNullOrWhiteSpace(sinceTimestamp) &&
            DateTimeOffset.TryParse(sinceTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            since = parsed;
        }

        var entries = DiagnosticsErrorLog.GetEntries(since);
        if (entries.Count == 0)
            return "No diagnostics recorded.";

        return string.Join("\n", entries.Select(e => e.ToString()));
    }

    /// <summary>Runs the <c>get_logs</c> MCP tool.</summary>
    [McpServerTool(Name = "get_logs", ReadOnly = true), Description(
        "Returns the app's recent log output from an in-process ring buffer: everything written through " +
        "Avalonia's Logger plus everything printed to stdout/stderr. Use this when the app was started by " +
        "the developer from an IDE and you have no terminal to read — it is the only way to see what the " +
        "app is telling you. Broader than get_errors, which reports only curated build/binding/runtime " +
        "errors. Filter with sinceTimestamp (ISO-8601, e.g. from just before an action), level " +
        "(verbose|debug|information|warning|error|fatal — returns that level and above) and filter (a " +
        "case-insensitive substring of the message or Avalonia log area). limit returns the most recent " +
        "matches (default 200).")]
    public static string GetLogs(
        [Description("Optional ISO-8601 timestamp; only lines at/after it are returned.")]
        string? sinceTimestamp = null,
        [Description("Optional minimum level: verbose | debug | information | warning | error | fatal.")]
        string? level = null,
        [Description("Optional case-insensitive substring the message or log area must contain.")]
        string? filter = null,
        [Description("Maximum lines to return, most recent first-in-time-order (default 200).")]
        int? limit = null)
    {
        DateTimeOffset? since = null;
        if (!string.IsNullOrWhiteSpace(sinceTimestamp))
        {
            if (!DateTimeOffset.TryParse(sinceTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return $"'{sinceTimestamp}' is not a valid ISO-8601 timestamp (e.g. '2026-08-02T14:31:05').";
            since = parsed;
        }

        global::Avalonia.Logging.LogEventLevel? minimumLevel = null;
        if (!string.IsNullOrWhiteSpace(level))
        {
            if (!AppLogBuffer.TryParseLevel(level, out var parsedLevel))
                return $"Unknown level '{level}'. Use verbose | debug | information | warning | error | fatal.";
            minimumLevel = parsedLevel;
        }

        var entries = AppLogBuffer.Query(since, minimumLevel, filter, limit ?? 200, out var matched);
        if (matched == 0)
        {
            if (!AppLogSink.IsInstalled)
                return "Log capture is not installed — start the inspector with UseAgentInspector (it installs the buffer).";

            return "No log lines match. The buffer is capturing; the app may simply not have logged anything yet." +
                   BelowCaptureThresholdNote(minimumLevel);
        }

        var builder = new StringBuilder();
        if (BelowCaptureThresholdNote(minimumLevel) is { Length: > 0 } note)
            builder.Append(note.TrimStart()).Append('\n');
        if (matched > entries.Count)
            builder.Append(matched).Append(" line(s) matched; showing the most recent ").Append(entries.Count).Append(".\n");
        if (AppLogBuffer.DroppedCount > 0)
            builder.Append(AppLogBuffer.DroppedCount).Append(" older line(s) have been dropped from the ring buffer.\n");

        foreach (var entry in entries)
            builder.Append(entry).Append('\n');

        return builder.ToString();
    }

    /// <summary>
    /// Warns when the agent asks for a level the buffer is not capturing, so an empty result is not read
    /// as "the app logged nothing".
    /// </summary>
    private static string BelowCaptureThresholdNote(global::Avalonia.Logging.LogEventLevel? requested) =>
        requested is { } level && level < AppLogBuffer.MinimumLevel
            ? $" Note: Avalonia's own logging is only captured from {AppLogBuffer.MinimumLevel} up " +
              "(capturing lower switches on framework tracing the app would otherwise skip). Console " +
              "output is captured at every level. Set AppLogBuffer.MinimumLevel to lower the threshold."
            : string.Empty;

    /// <summary>Runs the <c>get_render_stats</c> MCP tool.</summary>
    [McpServerTool(Name = "get_render_stats", ReadOnly = true), Description(
        "Returns rendering and layout cost for a window: measured frame rate, the number of visuals in the " +
        "tree, client/frame size, render scaling and — with layoutTiming=true — the last layout run's pass " +
        "count and duration. Use it to catch a performance regression without a profiler: record the " +
        "numbers, make a change, compare. By default nothing is switched on to gather them, so an idle " +
        "window honestly reports 0 fps because nothing asked it to redraw. sampleMs sets how long frames " +
        "are counted (default 500, max 5000). layoutTiming=true briefly enables Avalonia's LayoutTimeGraph " +
        "overlay, which is the only way it records layout durations — it draws a visible graph while " +
        "active, so don't screenshot during that call; the previous overlays are restored afterwards.")]
    public static async Task<string> GetRenderStats(
        [Description("Optional window title or 0-based index. Omit for the main window.")]
        string? windowId = null,
        [Description("Frame-counting window in milliseconds (default 500, max 5000).")]
        int? sampleMs = null,
        [Description("True to enable the LayoutTimeGraph overlay for this call so layout is timed.")]
        bool layoutTiming = false)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Clamp(sampleMs ?? 500, 0, 5000));

        var sampling = await AgentToolContext.RunOnUiThreadAsync<(TopLevel Top, RenderStatsInspector.FrameCounter Counter, IDisposable? Timing)?>(() =>
        {
            var top = AgentToolContext.ResolveTopLevel(windowId);
            if (top is null)
                return null;

            // Arm the overlay before sampling so a layout run inside the window is actually timed.
            var timing = layoutTiming ? RenderStatsInspector.EnableLayoutTiming(top) : null;
            return (top, RenderStatsInspector.FrameCounter.Start(top), timing);
        });

        if (sampling is not { } sampled)
            return "No active window/top-level was found.";

        var started = DateTime.UtcNow;
        if (duration > TimeSpan.Zero)
            await Task.Delay(duration);
        var actual = DateTime.UtcNow - started;

        return await AgentToolContext.RunToolAsync("get_render_stats", () =>
        {
            sampled.Counter.Dispose();
            try
            {
                return RenderStatsInspector.Collect(sampled.Top, sampled.Counter.Frames, actual).ToString();
            }
            finally
            {
                // Restore the overlays after collecting, so the snapshot still sees timing as armed.
                sampled.Timing?.Dispose();
            }
        });
    }

    private static string FormatBounds(global::Avalonia.Rect b) =>
        string.Format(CultureInfo.InvariantCulture, "[x={0:0.#} y={1:0.#} w={2:0.#} h={3:0.#}]", b.X, b.Y, b.Width, b.Height);

    private static string FormatSize(global::Avalonia.Size s) =>
        string.Format(CultureInfo.InvariantCulture, "{0:0.#}x{1:0.#}", s.Width, s.Height);

    private static bool BoundsClose(global::Avalonia.Rect a, global::Avalonia.Rect b)
    {
        const double eps = 0.5;
        return Math.Abs(a.X - b.X) < eps && Math.Abs(a.Y - b.Y) < eps &&
               Math.Abs(a.Width - b.Width) < eps && Math.Abs(a.Height - b.Height) < eps;
    }

    private static string RenderScreenshotList()
    {
        var list = ScreenshotStore.List();
        if (list.Count == 0)
            return "No screenshots captured yet.";

        var builder = new StringBuilder();
        builder.Append(list.Count).Append(" screenshot(s) (oldest first):\n");
        foreach (var entry in list)
        {
            builder.Append("  ").Append(entry.Id).Append(": ").Append(entry.Label)
                .Append(' ').Append(entry.Image.Size.Width).Append('x').Append(entry.Image.Size.Height)
                .Append(" @ ").Append(entry.TimestampUtc.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return builder.ToString();
    }

    private static bool MatchesFilter(Control control, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        var f = filter.Trim();
        return control.GetType().Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
               (control.Name is { } name && name.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    private static string DescribeElement(global::Avalonia.Input.IInputElement element)
    {
        if (element is Control { Name: { Length: > 0 } name } named)
            return $"{named.GetType().Name} #{name}";
        return element.GetType().Name;
    }

    private static CallToolResult Image(byte[] png, string meta)
    {
        var result = new CallToolResult();
        result.Content.Add(new TextContentBlock { Text = meta });
        result.Content.Add(ImageContentBlock.FromBytes(png, "image/png"));
        return result;
    }

    private static CallToolResult Text(string message)
    {
        var result = new CallToolResult();
        result.Content.Add(new TextContentBlock { Text = message });
        return result;
    }

    private static CallToolResult Error(string message)
    {
        var result = new CallToolResult { IsError = true };
        result.Content.Add(new TextContentBlock { Text = message });
        return result;
    }
}
