using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Declarative.Diagnostics;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using ModelContextProtocol.Server;

namespace Declarative.Avalonia.AgentTools.Tools;

/// <summary>
/// Tier-2 remote-control MCP tools. This type is registered only when
/// <see cref="AgentInspectorOptions.EnableInteraction"/> is set, because it can change app state.
/// </summary>
/// <remarks>
/// Interaction is dispatched through Avalonia's UI Automation peers (the same accessibility layer a
/// screen reader drives), which is the most general way to act on any control in a <em>live</em> app:
/// it is platform-agnostic and works regardless of which control type is targeted. Headless input
/// simulation (<c>Avalonia.Headless</c>) is deliberately not used — it requires the headless platform
/// and cannot drive a real desktop window. Each automation action has a typed-property fallback for
/// controls whose peer does not implement the matching pattern.
/// </remarks>
[McpServerToolType]
public sealed class InteractionTools
{
    private InteractionTools() { }

    /// <summary>Runs the <c>invoke</c> MCP tool.</summary>
    [McpServerTool(Name = "invoke", Destructive = true), Description(
        "EXPERIMENTAL remote control: performs a user action on a control, dispatched via UI Automation. " +
        "Target by Name or by visible label (automation name). Actions: " +
        "'invoke' (press a Button/MenuItem/Hyperlink), " +
        "'select' (choose a TabItem/ListBoxItem/RadioButton — e.g. switch tabs), " +
        "'select_item' (choose an item in a ComboBox/ListBox/TabControl by its text — pass it in 'value'), " +
        "'toggle' (CheckBox/ToggleButton), " +
        "'set' (set value: TextBox text, or a Slider/NumericUpDown number — pass it in 'value'), " +
        "'expand'/'collapse' (TreeViewItem/Expander/ComboBox), " +
        "'focus', 'scroll' (bring into view), 'scroll_by' (scroll the nearest ScrollViewer by 'dx,dy' in 'value'), " +
        "'context_menu' (open the context menu), " +
        "'key' (press a key/gesture from 'value', e.g. 'Enter' or 'Ctrl+S'; targets the named control or the focused element), " +
        "'type' (raw text input from 'value' into the named control or the focused element). " +
        "Disabled unless EnableInteraction was set when starting the inspector.")]
    public static Task<string> Invoke(
        [Description("Name or visible label (automation name) of the target control. Optional for 'key'/'type' (defaults to the focused element).")]
        string? name,
        [Description("Action: invoke | select | select_item | toggle | set | expand | collapse | focus | scroll | scroll_by | context_menu | key | type.")]
        string action,
        [Description("Value used by 'set' (text/number), 'key' (gesture) and 'type' (text).")]
        string? value = null) =>
        // RunToolAsync guarantees this never surfaces the MCP SDK's opaque "An error occurred invoking
        // 'invoke'": any exception becomes an actionable message naming the tool and cause.
        AgentToolContext.RunToolAsync("invoke", () => InvokeCore(name, action, value));

    private static string InvokeCore(string? name, string action, string? value)
    {
        switch (action?.Trim().ToLowerInvariant())
        {
            case "key":
                return PressKey(name, value);
            case "type":
                return TypeText(name, value);
        }

        if (string.IsNullOrWhiteSpace(name))
            return "A control name is required for this action.";

        var control = AgentToolContext.FindInteractionTarget(name);
        if (control is null)
            return $"No control named (or labeled) '{name}' was found. Use get_visual_tree or find_text to discover names.";

        var typeName = control.GetType().Name;

        switch (action?.Trim().ToLowerInvariant())
        {
            case "invoke":
                return InvokeControl(control, $"'{name}'");

            case "select":
                return SelectControl(control, $"'{name}'");

            case "toggle":
                return ToggleControl(control, $"'{name}'");

            case "set":
                return SetValue(control, name, value);

            case "expand":
                if (ResolveProvider<IExpandCollapseProvider>(control) is { } expand)
                {
                    expand.Expand();
                    return $"Expanded '{name}'.";
                }

                if (SetExpanded(control, true))
                    return $"Expanded '{name}'.";

                return $"Control '{name}' ({typeName}) does not support 'expand'.";

            case "collapse":
                if (ResolveProvider<IExpandCollapseProvider>(control) is { } collapse)
                {
                    collapse.Collapse();
                    return $"Collapsed '{name}'.";
                }

                if (SetExpanded(control, false))
                    return $"Collapsed '{name}'.";

                return $"Control '{name}' ({typeName}) does not support 'collapse'.";

            case "focus":
                return control.Focus() ? $"Focused '{name}'." : $"Could not focus '{name}'.";

            case "scroll":
                if (TryCreatePeer(control) is not { } scrollPeer)
                    return $"Could not bring '{name}' into view: no automation peer is available (the control may not be realized yet).";
                scrollPeer.BringIntoView();
                return $"Brought '{name}' into view.";

            case "context_menu":
                if (TryCreatePeer(control) is not { } menuPeer)
                    return $"Could not open the context menu for '{name}': no automation peer is available (the control may not be realized yet).";
                menuPeer.ShowContextMenu();
                return $"Opened context menu for '{name}'.";

            case "select_item":
                return SelectItem(control, name, value);

            case "scroll_by":
                return ScrollBy(control, name, value);

            default:
                return $"Unknown action '{action}'. " +
                       "Use invoke | select | select_item | toggle | set | expand | collapse | focus | scroll | scroll_by | context_menu | key | type.";
        }
    }

    /// <summary>
    /// Invokes <paramref name="control"/> — or the nearest actionable ancestor. Prefers the
    /// <see cref="IInvokeProvider"/> peer (walked up the tree), then falls back to raising
    /// <see cref="Button.ClickEvent"/> on the nearest <see cref="Button"/> ancestor. The typed fallback
    /// must climb too: addressing a button by its label or hitting it by pixel lands on the inner
    /// <c>AccessText</c>/<c>TextBlock</c>, which is not itself a <see cref="Button"/>.
    /// </summary>
    private static string InvokeControl(Control control, string label)
    {
        if (ResolveProviderOwner<IInvokeProvider>(control) is { } invoke)
        {
            invoke.Provider.Invoke();
            return $"Invoked {ResolvedName(control, invoke.Owner, label)}.";
        }

        if (FindSelfOrAncestor<Button>(control) is { } button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return $"Clicked {ResolvedName(control, button, label)}.";
        }

        return $"Control {label} ({control.GetType().Name}) does not support 'invoke'.";
    }

    private static string SelectControl(Control control, string label)
    {
        if (ResolveProviderOwner<ISelectionItemProvider>(control) is { } select)
        {
            select.Provider.Select();
            return $"Selected {ResolvedName(control, select.Owner, label)}.";
        }

        if (FindSelfOrAncestor<TabItem>(control) is { } tabItem)
        {
            tabItem.IsSelected = true;
            return $"Selected {ResolvedName(control, tabItem, label)}.";
        }

        return $"Control {label} ({control.GetType().Name}) does not support 'select'.";
    }

    private static string ToggleControl(Control control, string label)
    {
        if (ResolveProviderOwner<IToggleProvider>(control) is { } toggle)
        {
            toggle.Provider.Toggle();
            return $"Toggled {ResolvedName(control, toggle.Owner, label)}.";
        }

        if (FindSelfOrAncestor<ToggleButton>(control) is { } toggleButton)
        {
            toggleButton.IsChecked = !(toggleButton.IsChecked ?? false);
            return $"Toggled {ResolvedName(control, toggleButton, label)} to {toggleButton.IsChecked}.";
        }

        return $"Control {label} ({control.GetType().Name}) does not support 'toggle'.";
    }

    /// <summary>Runs the <c>set_window_size</c> MCP tool.</summary>
    [McpServerTool(Name = "set_window_size", Destructive = true), Description(
        "Resizes a desktop window so you can verify responsive layout at a breakpoint (e.g. 400/800/1200 " +
        "wide). Returns the resulting client size (which may be clamped by Min/Max) and the previous size " +
        "so you can restore it. Disabled unless EnableInteraction was set.")]
    public static Task<string> SetWindowSize(
        [Description("New width in DIPs.")] double width,
        [Description("New height in DIPs.")] double height,
        [Description("Optional window title or 0-based index. Omit for the main window.")]
        string? windowId = null) =>
        AgentToolContext.RunOnUiThreadAsync(() =>
        {
            if (AgentToolContext.ResolveTopLevel(windowId) is not Window window)
                return "set_window_size requires a desktop Window (the app has none, or this is a single-view top-level).";

            var previous = window.ClientSize;
            window.Width = width;
            window.Height = height;
            Dispatcher.UIThread.RunJobs();

            return string.Format(CultureInfo.InvariantCulture,
                "Resized '{0}' from client {1:0.#}x{2:0.#} to {3:0.#}x{4:0.#} (requested {5:0.#}x{6:0.#}).",
                window.Title, previous.Width, previous.Height, window.ClientSize.Width, window.ClientSize.Height, width, height);
        });

    /// <summary>Runs the <c>set_theme</c> MCP tool.</summary>
    [McpServerTool(Name = "set_theme", Destructive = true), Description(
        "Switches the application theme variant so you can verify both light and dark. Pass 'Light', " +
        "'Dark' or 'Default'. Returns the previous and resulting variant. Disabled unless EnableInteraction was set.")]
    public static Task<string> SetTheme(
        [Description("Theme variant: Light | Dark | Default.")]
        string variant) =>
        AgentToolContext.RunOnUiThreadAsync(() =>
        {
            if (Application.Current is not { } app)
                return "No Application is running.";

            ThemeVariant? target = variant?.Trim().ToLowerInvariant() switch
            {
                "light" => ThemeVariant.Light,
                "dark" => ThemeVariant.Dark,
                "default" => ThemeVariant.Default,
                _ => null
            };

            if (target is null)
                return $"Unknown theme variant '{variant}'. Use Light | Dark | Default.";

            var previous = app.RequestedThemeVariant;
            app.RequestedThemeVariant = target;
            Dispatcher.UIThread.RunJobs();

            return $"Theme requested={target}, actual now={app.ActualThemeVariant} (was requested={previous?.ToString() ?? "Default"}).";
        });

    /// <summary>Runs the <c>click_at</c> MCP tool.</summary>
    [McpServerTool(Name = "click_at", Destructive = true), Description(
        "Clicks whatever control is at a coordinate. Coordinates are ABSOLUTE client-DIP (the same frame " +
        "get_visual_tree's center=(x,y)/hit_test/drag use; 96-DPI screenshot pixels match 1:1). By default " +
        "it synthesizes a REAL pointer click through Avalonia's input pipeline (works for custom Border/" +
        "Panel controls with hand-written pointer handlers and no automation peer), and only falls back to " +
        "a UI-Automation invoke when synthetic input is unavailable. Pass method='pointer' to force real " +
        "input or method='automation' to force the invoke/select-by-peer path. For drags/hover use drag / " +
        "pointer_* / tap. Disabled unless EnableInteraction was set.")]
    public static Task<string> ClickAt(
        [Description("X coordinate in absolute client-DIP pixels.")] double x,
        [Description("Y coordinate in absolute client-DIP pixels.")] double y,
        [Description("Optional window title or 0-based index. Omit for the main window.")]
        string? windowId = null,
        [Description("Dispatch method: 'auto' (default: real pointer, automation fallback), 'pointer', or 'automation'.")]
        string? method = null) =>
        AgentToolContext.RunToolAsync("click_at", () =>
        {
            var top = AgentToolContext.ResolveTopLevel(windowId);
            if (top is null)
                return "No active window/top-level was found.";

            var mode = method?.Trim().ToLowerInvariant();
            var point = new Point(x, y);

            if (mode == "automation")
                return ClickAtCore(x, y, windowId);

            // Pointer-first (universal): real synthetic click when the input pipeline is reachable.
            if (mode == "pointer" || InputSynthesizer.CanSynthesize(top))
                return "click_at via real pointer — " + InputSynthesizer.Tap(top, point, MouseButton.Left, RawInputModifiers.None);

            // Automation fallback when synthetic input is unavailable (e.g. window not realized).
            return "click_at via automation (synthetic input unavailable) — " + ClickAtCore(x, y, windowId);
        });

    private static string ClickAtCore(double x, double y, string? windowId)
    {
        var top = AgentToolContext.ResolveTopLevel(windowId);
        if (top is null)
            return "No active window/top-level was found.";

        var point = new Point(x, y);
        if (HitTester.HitTest(top, point) is not { } hit)
            return $"Nothing is at ({x}, {y}).";

        var target = hit as Control ?? hit.FindAncestorOfType<Control>();
        if (target is null)
            return $"A non-control visual is at ({x}, {y}).";

        // The hit almost always lands on an inner glyph/child (an AccessText inside a Button). Climb to
        // the nearest control that can actually be acted on, and report what we resolved to, so a
        // "clicked the label, nothing happened" no-op can't slip through silently.
        var at = $"at ({x}, {y})";

        if (ResolveProviderOwner<IInvokeProvider>(target) is { } invoke)
        {
            invoke.Provider.Invoke();
            return $"Invoked {ResolvedName(target, invoke.Owner, DescribeTarget(target))} {at}.";
        }

        if (FindSelfOrAncestor<Button>(target) is { } button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return $"Clicked {ResolvedName(target, button, DescribeTarget(target))} {at}.";
        }

        if (ResolveProviderOwner<ISelectionItemProvider>(target) is { } selection)
        {
            selection.Provider.Select();
            return $"Selected {ResolvedName(target, selection.Owner, DescribeTarget(target))} {at}.";
        }

        if (FindSelfOrAncestor<TabItem>(target) is { } tabItem)
        {
            tabItem.IsSelected = true;
            return $"Selected {ResolvedName(target, tabItem, DescribeTarget(target))} {at}.";
        }

        if (FindSelfOrAncestor<ToggleButton>(target) is { } toggle)
        {
            toggle.IsChecked = !(toggle.IsChecked ?? false);
            return $"Toggled {ResolvedName(target, toggle, DescribeTarget(target))} {at} to {toggle.IsChecked}.";
        }

        return target.Focus()
            ? $"No invokable control {at}; focused {DescribeTarget(target)} instead."
            : $"{DescribeTarget(target)} {at} is neither invokable nor focusable.";
    }

    // ── Real synthetic input (P1) ─────────────────────────────────────────────────────────────────
    // These feed real RawPointer/RawKey/RawTextInput events through Avalonia's input pipeline, so
    // hit-testing, pointer capture and PointerPressed/Moved/Released fire exactly as from a device.
    // They drive controls that have NO automation peer (custom Border/Panel sliders, drag thumbs,
    // drawing canvases) — where click_at(automation)/invoke cannot. Coordinates are absolute client-DIP
    // (the frame get_visual_tree's center=/hit_test/screenshots share).

    /// <summary>Runs the <c>tap</c> MCP tool.</summary>
    [McpServerTool(Name = "tap", Destructive = true), Description(
        "Synthesizes a REAL pointer click (move → press → release) at an absolute client-DIP coordinate — " +
        "the universal way to click, including custom controls with hand-written pointer handlers and no " +
        "automation peer. Focuses a focusable target and enters text-edit mode on a click-to-edit control, " +
        "just like a real click. Use get_visual_tree center=(x,y) or hit_test to find the point. " +
        "Set count=2 for a genuine DOUBLE click (the presses share one device, so the handler really sees " +
        "e.ClickCount==2 and DoubleTapped fires) — two separate tap calls do NOT add up to one. " +
        "pointerType switches the emulated device: 'pen' (with pressure / inverted for the eraser end) or " +
        "'touch'. Disabled unless EnableInteraction was set.")]
    public static Task<string> Tap(
        [Description("X in absolute client-DIP pixels.")] double x,
        [Description("Y in absolute client-DIP pixels.")] double y,
        [Description("Mouse button: Left (default) | Right | Middle.")] string? button = null,
        [Description("Optional modifiers, e.g. 'Ctrl+Shift'.")] string? modifiers = null,
        [Description("Optional window title or 0-based index. Omit for the main window.")] string? windowId = null,
        [Description("Consecutive clicks: 1 (default), 2 for a double click, 3 for a triple.")] int count = 1,
        [Description("Emulated device: mouse (default) | pen | touch.")] string? pointerType = null,
        [Description("Pen/touch tip pressure 0..1. Omit for the device default (0.5).")] double? pressure = null,
        [Description("Pen only: true for the inverted (eraser) end — sets IsInverted and IsEraser.")] bool inverted = false) =>
        AgentToolContext.RunToolAsync("tap", () =>
            WithPointer(windowId, pointerType, pressure, inverted, (top, spec) =>
                InputSynthesizer.Tap(top, new Point(x, y), ParseButton(button), ParseModifiers(modifiers), count, spec)));

    /// <summary>Runs the <c>pointer_press</c> MCP tool.</summary>
    [McpServerTool(Name = "pointer_press", Destructive = true), Description(
        "Synthesizes a REAL pointer button press at an absolute client-DIP coordinate and BEGINS a gesture " +
        "(the button stays held). Follow with pointer_move / pointer_release — capture is maintained across " +
        "the calls, so PointerMoved reports the button down (what drag/scrub handlers check). For a one-shot " +
        "press+drag+release use drag; for a click use tap. pointerType='pen' with a pressure lets you drive " +
        "a pressure-sensitive brush; for more than one finger use touch_press instead (this tool holds a " +
        "single gesture). Disabled unless EnableInteraction was set.")]
    public static Task<string> PointerPress(
        [Description("X in absolute client-DIP pixels.")] double x,
        [Description("Y in absolute client-DIP pixels.")] double y,
        [Description("Mouse button: Left (default) | Right | Middle.")] string? button = null,
        [Description("Optional modifiers, e.g. 'Ctrl+Shift'.")] string? modifiers = null,
        [Description("Optional window title or 0-based index. Omit for the main window.")] string? windowId = null,
        [Description("Emulated device: mouse (default) | pen | touch.")] string? pointerType = null,
        [Description("Pen/touch tip pressure 0..1. Omit for the device default (0.5).")] double? pressure = null,
        [Description("Pen only: true for the inverted (eraser) end — sets IsInverted and IsEraser.")] bool inverted = false) =>
        AgentToolContext.RunToolAsync("pointer_press", () =>
            WithPointer(windowId, pointerType, pressure, inverted, (top, spec) =>
                InputSynthesizer.PointerPress(top, new Point(x, y), ParseButton(button), ParseModifiers(modifiers), spec)));

    /// <summary>Runs the <c>pointer_move</c> MCP tool.</summary>
    [McpServerTool(Name = "pointer_move", Destructive = true), Description(
        "Synthesizes a REAL pointer move to an absolute client-DIP coordinate. During a gesture started by " +
        "pointer_press the held button and the device it began with are carried (drag); otherwise it is a " +
        "hover move. Pass a pressure to vary a pen stroke mid-drag. Disabled unless EnableInteraction was set.")]
    public static Task<string> PointerMove(
        [Description("X in absolute client-DIP pixels.")] double x,
        [Description("Y in absolute client-DIP pixels.")] double y,
        [Description("Optional modifiers, e.g. 'Ctrl+Shift'.")] string? modifiers = null,
        [Description("Optional window title or 0-based index. Omit for the main window.")] string? windowId = null,
        [Description("Emulated device for a hover move: mouse (default) | pen. Ignored during a gesture.")] string? pointerType = null,
        [Description("Pen/touch tip pressure 0..1. Omit for the device default (0.5).")] double? pressure = null,
        [Description("Pen only: true for the inverted (eraser) end — sets IsInverted and IsEraser.")] bool inverted = false) =>
        AgentToolContext.RunToolAsync("pointer_move", () =>
            WithPointer(windowId, pointerType, pressure, inverted, (top, spec) =>
                InputSynthesizer.PointerMove(top, new Point(x, y), ParseModifiers(modifiers), spec)));

    /// <summary>Runs the <c>pointer_release</c> MCP tool.</summary>
    [McpServerTool(Name = "pointer_release", Destructive = true), Description(
        "Synthesizes a REAL pointer button release at an absolute client-DIP coordinate, ending the active " +
        "gesture. Disabled unless EnableInteraction was set.")]
    public static Task<string> PointerRelease(
        [Description("X in absolute client-DIP pixels.")] double x,
        [Description("Y in absolute client-DIP pixels.")] double y,
        [Description("Mouse button: Left (default) | Right | Middle.")] string? button = null,
        [Description("Optional modifiers, e.g. 'Ctrl+Shift'.")] string? modifiers = null,
        [Description("Optional window title or 0-based index. Omit for the main window.")] string? windowId = null,
        [Description("Emulated device when no gesture is active: mouse (default) | pen | touch.")] string? pointerType = null) =>
        AgentToolContext.RunToolAsync("pointer_release", () =>
            WithPointer(windowId, pointerType, pressure: null, inverted: false, (top, spec) =>
                InputSynthesizer.PointerRelease(top, new Point(x, y), ParseButton(button), ParseModifiers(modifiers), spec)));

    /// <summary>Runs the <c>drag</c> MCP tool.</summary>
    [McpServerTool(Name = "drag", Destructive = true), Description(
        "Synthesizes a REAL press-drag-release from (x1,y1) to (x2,y2) in absolute client-DIP coordinates, " +
        "with the button held through intermediate moves — this is how you scrub a custom slider, move a " +
        "drag thumb, draw a stroke, or reorder a list. 'steps' controls the number of intermediate moves " +
        "(default 10) and 'holdMs' an optional dwell after pressing. pointerType='pen' + pressure draws " +
        "with a pressure-sensitive brush; pointerType='touch' produces a single-finger pan. " +
        "Disabled unless EnableInteraction was set.")]
    public static Task<string> Drag(
        [Description("Start X in absolute client-DIP pixels.")] double x1,
        [Description("Start Y in absolute client-DIP pixels.")] double y1,
        [Description("End X in absolute client-DIP pixels.")] double x2,
        [Description("End Y in absolute client-DIP pixels.")] double y2,
        [Description("Mouse button: Left (default) | Right | Middle.")] string? button = null,
        [Description("Intermediate move count (default 10).")] int steps = 10,
        [Description("Optional dwell in ms after the press.")] int holdMs = 0,
        [Description("Optional modifiers, e.g. 'Ctrl+Shift'.")] string? modifiers = null,
        [Description("Optional window title or 0-based index. Omit for the main window.")] string? windowId = null,
        [Description("Emulated device: mouse (default) | pen | touch.")] string? pointerType = null,
        [Description("Pen/touch tip pressure 0..1. Omit for the device default (0.5).")] double? pressure = null,
        [Description("Pen only: true for the inverted (eraser) end — sets IsInverted and IsEraser.")] bool inverted = false) =>
        AgentToolContext.RunToolAsync("drag", () =>
            WithPointer(windowId, pointerType, pressure, inverted, (top, spec) =>
                InputSynthesizer.Drag(top, new Point(x1, y1), new Point(x2, y2), ParseButton(button), steps <= 0 ? 10 : steps, Math.Max(0, holdMs), ParseModifiers(modifiers), spec)));

    /// <summary>Runs the <c>pointer_wheel</c> MCP tool.</summary>
    [McpServerTool(Name = "pointer_wheel", Destructive = true), Description(
        "Synthesizes a REAL mouse-wheel event (PointerWheelChanged) at an absolute client-DIP coordinate — " +
        "the only way to zoom or scroll a custom canvas, which usually zooms around the cursor rather than " +
        "scrolling a ScrollViewer. Deltas are in NOTCHES, exactly like PointerWheelEventArgs.Delta: dy=1 is " +
        "one detent away from you (scroll up / zoom in on most apps), dy=-1 towards you, dx scrolls " +
        "horizontally. The pointer is moved to (x,y) first, so zoom-at-cursor lands where you asked. " +
        "Set precision=true to emulate a touchpad instead of a notched wheel: the same total delta arrives " +
        "as a burst of small fractional steps, which is how you exercise code that distinguishes the two. " +
        "Disabled unless EnableInteraction was set.")]
    public static Task<string> PointerWheel(
        [Description("X in absolute client-DIP pixels.")] double x,
        [Description("Y in absolute client-DIP pixels.")] double y,
        [Description("Horizontal delta in notches (positive scrolls right).")] double dx = 0,
        [Description("Vertical delta in notches (positive scrolls up / away from you).")] double dy = 0,
        [Description("Optional modifiers, e.g. 'Ctrl' — Ctrl+wheel is often zoom.")] string? modifiers = null,
        [Description("Optional window title or 0-based index. Omit for the main window.")] string? windowId = null,
        [Description("True to emulate a precision touchpad (fractional multi-step deltas).")] bool precision = false) =>
        AgentToolContext.RunToolAsync("pointer_wheel", () =>
        {
            if (dx == 0 && dy == 0)
                return "Both deltas are zero, so nothing would happen. Pass dy=1 for one notch up, dy=-1 for one notch down.";

            return WithTop(windowId, top =>
                InputSynthesizer.Wheel(top, new Point(x, y), new Vector(dx, dy), ParseModifiers(modifiers), precision));
        });

    /// <summary>Runs the <c>touch_press</c> MCP tool.</summary>
    [McpServerTool(Name = "touch_press", Destructive = true), Description(
        "Puts a FINGER down at an absolute client-DIP coordinate. 'touchId' names the finger, so several " +
        "can be down at once — this is how you build a multi-touch gesture (two-finger tap to undo, " +
        "two-finger pan, a custom pinch) that a mouse cannot express. Pair every touch_press with a " +
        "touch_release of the same id. For the common spread/pinch use the 'pinch' tool instead. " +
        "Disabled unless EnableInteraction was set.")]
    public static Task<string> TouchPress(
        [Description("Finger id — any number; use different ids for simultaneous contacts (e.g. 1 and 2).")] long touchId,
        [Description("X in absolute client-DIP pixels.")] double x,
        [Description("Y in absolute client-DIP pixels.")] double y,
        [Description("Contact pressure 0..1. Omit for the device default (0.5).")] double? pressure = null,
        [Description("Optional modifiers, e.g. 'Ctrl+Shift'.")] string? modifiers = null,
        [Description("Optional window title or 0-based index. Omit for the main window.")] string? windowId = null) =>
        AgentToolContext.RunToolAsync("touch_press", () =>
            WithTop(windowId, top => InputSynthesizer.TouchPress(top, touchId, new Point(x, y), ParseModifiers(modifiers), pressure)));

    /// <summary>Runs the <c>touch_move</c> MCP tool.</summary>
    [McpServerTool(Name = "touch_move", Destructive = true), Description(
        "Moves the finger 'touchId' to an absolute client-DIP coordinate. The finger must be down " +
        "(touch_press) first. Move each finger in turn to advance a multi-touch gesture one frame. " +
        "Disabled unless EnableInteraction was set.")]
    public static Task<string> TouchMove(
        [Description("Finger id previously passed to touch_press.")] long touchId,
        [Description("X in absolute client-DIP pixels.")] double x,
        [Description("Y in absolute client-DIP pixels.")] double y,
        [Description("Contact pressure 0..1. Omit for the device default (0.5).")] double? pressure = null,
        [Description("Optional modifiers, e.g. 'Ctrl+Shift'.")] string? modifiers = null,
        [Description("Optional window title or 0-based index. Omit for the main window.")] string? windowId = null) =>
        AgentToolContext.RunToolAsync("touch_move", () =>
            WithTop(windowId, top => InputSynthesizer.TouchMove(top, touchId, new Point(x, y), ParseModifiers(modifiers), pressure)));

    /// <summary>Runs the <c>touch_release</c> MCP tool.</summary>
    [McpServerTool(Name = "touch_release", Destructive = true), Description(
        "Lifts the finger 'touchId' at an absolute client-DIP coordinate, ending that contact. " +
        "Disabled unless EnableInteraction was set.")]
    public static Task<string> TouchRelease(
        [Description("Finger id previously passed to touch_press.")] long touchId,
        [Description("X in absolute client-DIP pixels.")] double x,
        [Description("Y in absolute client-DIP pixels.")] double y,
        [Description("Optional modifiers, e.g. 'Ctrl+Shift'.")] string? modifiers = null,
        [Description("Optional window title or 0-based index. Omit for the main window.")] string? windowId = null) =>
        AgentToolContext.RunToolAsync("touch_release", () =>
            WithTop(windowId, top => InputSynthesizer.TouchRelease(top, touchId, new Point(x, y), ParseModifiers(modifiers))));

    /// <summary>Runs the <c>pinch</c> MCP tool.</summary>
    [McpServerTool(Name = "pinch", Destructive = true), Description(
        "Performs a complete two-finger pinch/spread centred on (cx,cy) in absolute client-DIP coordinates: " +
        "both contacts go down 'fromDistance' apart, travel to 'toDistance', then lift — raising " +
        "PinchEvent for a PinchGestureRecognizer. toDistance > fromDistance spreads (zoom in), " +
        "toDistance < fromDistance pinches (zoom out); the scale factor is toDistance/fromDistance. " +
        "Use this rather than hand-rolling touch_press/move/release pairs: a recognizer needs both fingers " +
        "moving together and enough intermediate steps to lock on. Disabled unless EnableInteraction was set.")]
    public static Task<string> Pinch(
        [Description("Centre X in absolute client-DIP pixels.")] double cx,
        [Description("Centre Y in absolute client-DIP pixels.")] double cy,
        [Description("Starting distance between the two contacts, in DIPs.")] double fromDistance,
        [Description("Ending distance between the two contacts, in DIPs.")] double toDistance,
        [Description("Intermediate move count (default 10, min 2).")] int steps = 10,
        [Description("Optional modifiers, e.g. 'Ctrl+Shift'.")] string? modifiers = null,
        [Description("Optional window title or 0-based index. Omit for the main window.")] string? windowId = null) =>
        AgentToolContext.RunToolAsync("pinch", () =>
            WithTop(windowId, top => InputSynthesizer.Pinch(top, new Point(cx, cy), fromDistance, toDistance, steps <= 0 ? 10 : steps, ParseModifiers(modifiers))));

    /// <summary>Runs the <c>open_popup</c> MCP tool.</summary>
    [McpServerTool(Name = "open_popup", Destructive = true), Description(
        "Opens a closed Popup/Flyout so its content becomes visible and capturable — the guided way out of " +
        "the 'has a zero size and cannot be captured' dead end (see the hint from screenshot_control). Pass " +
        "the Name of the Popup, of the control that owns it, or of the control with an attached flyout; it " +
        "sets the popup's IsOpen=true (or shows the flyout). Disabled unless EnableInteraction was set.")]
    public static Task<string> OpenPopup(
        [Description("Name of the Popup, its owning control, or a control with an attached flyout.")]
        string name) =>
        AgentToolContext.RunToolAsync("open_popup", () =>
        {
            var result = PopupLocator.Open(AgentToolContext.GetToolSearchRoots(), name);
            Dispatcher.UIThread.RunJobs(); // flush layout so a follow-up screenshot sees the opened content
            return result;
        });

    /// <summary>Runs the <c>list_bindable</c> MCP tool.</summary>
    [McpServerTool(Name = "list_bindable", ReadOnly = true), Description(
        "Lists what an escape hatch can drive on a DataContext: settable properties (for set_view_model), " +
        "ICommand properties + invokable methods (for invoke_command), and nested sub-objects to drill into " +
        "with a dotted path (e.g. AppState.UiState.ShowBrushSettings). Resolve the DataContext from 'name' " +
        "(a control Name/label) or, if omitted, the main window's. Use this to discover the correct path " +
        "when set_view_model/invoke_command reports the member is missing.")]
    public static Task<string> ListBindable(
        [Description("Optional control Name/label whose DataContext to enumerate. Omit for the main window's.")]
        string? name = null) =>
        AgentToolContext.RunToolAsync("list_bindable", () =>
        {
            if (!TryResolveDataContext(name, out var dataContext, out var error))
                return error;
            return ViewModelInspector.ListBindable(dataContext);
        });

    private static string WithTop(string? windowId, Func<TopLevel, string> body)
    {
        var top = AgentToolContext.ResolveTopLevel(windowId);
        return top is null ? "No active window/top-level was found." : body(top);
    }

    /// <summary>
    /// Resolves the top-level and the emulated device, then runs <paramref name="body"/>. An unknown
    /// pointerType is reported rather than silently downgraded to a mouse: an agent asking for 'pen'
    /// wants the pen code path, and a quiet fallback would look like the app ignoring pressure.
    /// </summary>
    private static string WithPointer(string? windowId, string? pointerType, double? pressure, bool inverted, Func<TopLevel, PointerSpec, string> body)
    {
        if (!TryParsePointerType(pointerType, out var kind))
            return $"Unknown pointerType '{pointerType}'. Use mouse | pen | touch.";

        if (pressure is { } value && (value < 0 || value > 1))
            return $"Pressure must be between 0 and 1 (got {value.ToString(CultureInfo.InvariantCulture)}).";

        if (inverted && kind != PointerType.Pen)
            return "'inverted' only applies to pointerType='pen' (it is the pen's eraser end).";

        var spec = new PointerSpec(kind, pressure, inverted);
        return WithTop(windowId, top => body(top, spec));
    }

    private static bool TryParsePointerType(string? pointerType, out PointerType kind)
    {
        switch (pointerType?.Trim().ToLowerInvariant())
        {
            case null or "" or "mouse":
                kind = PointerType.Mouse;
                return true;
            case "pen" or "stylus":
                kind = PointerType.Pen;
                return true;
            case "touch" or "finger":
                kind = PointerType.Touch;
                return true;
            default:
                kind = PointerType.Mouse;
                return false;
        }
    }

    private static MouseButton ParseButton(string? button) =>
        button?.Trim().ToLowerInvariant() switch
        {
            null or "" or "left" or "l" => MouseButton.Left,
            "right" or "r" => MouseButton.Right,
            "middle" or "m" => MouseButton.Middle,
            _ => MouseButton.Left
        };

    private static RawInputModifiers ParseModifiers(string? modifiers)
    {
        if (string.IsNullOrWhiteSpace(modifiers))
            return RawInputModifiers.None;

        var result = RawInputModifiers.None;
        foreach (var raw in modifiers.Split(new[] { '+', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result |= raw.ToLowerInvariant() switch
            {
                "ctrl" or "control" => RawInputModifiers.Control,
                "shift" => RawInputModifiers.Shift,
                "alt" => RawInputModifiers.Alt,
                "meta" or "win" or "cmd" or "super" => RawInputModifiers.Meta,
                _ => RawInputModifiers.None
            };
        }

        return result;
    }

    private static RawInputModifiers ToRawModifiers(KeyModifiers modifiers)
    {
        var result = RawInputModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Alt)) result |= RawInputModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Control)) result |= RawInputModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Shift)) result |= RawInputModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Meta)) result |= RawInputModifiers.Meta;
        return result;
    }

    /// <summary>Runs the <c>set_view_model</c> MCP tool.</summary>
    [McpServerTool(Name = "set_view_model", Destructive = true), Description(
        "ESCAPE HATCH for UI tests: sets a property on a control's DataContext (view-model) directly, by " +
        "reflection — the fast way to drive the app into a state that is awkward to reach through the UI " +
        "(e.g. flip a flag to show a recovery banner) without killing/restarting the process. Give a dotted " +
        "'path' ('IsBusy', 'CurrentUser.Name') and a 'value' as text (converted to the property's type: " +
        "bool/number/enum/string/…). Resolves the view-model from 'name' (a control Name or visible label) " +
        "or, if omitted, the main window's DataContext. Setting the real CLR property raises " +
        "INotifyPropertyChanged, so bindings update just as they would from user input. Disabled unless " +
        "EnableInteraction was set.")]
    public static Task<string> SetViewModel(
        [Description("Optional control Name/label whose DataContext to target. Omit for the main window's DataContext.")]
        string? name,
        [Description("Dotted property path on the view-model, e.g. 'IsBusy' or 'CurrentUser.Name'.")]
        string path,
        [Description("New value as text; converted to the property's type.")]
        string? value = null) =>
        AgentToolContext.RunToolAsync("set_view_model", () =>
        {
            if (!TryResolveDataContext(name, out var dataContext, out var error))
                return error;

            var result = ViewModelInspector.SetValue(dataContext, path, value);
            Dispatcher.UIThread.RunJobs(); // flush bindings/layout so a follow-up screenshot sees the change
            return result;
        });

    /// <summary>Runs the <c>invoke_command</c> MCP tool.</summary>
    [McpServerTool(Name = "invoke_command", Destructive = true), Description(
        "ESCAPE HATCH for UI tests: invokes an ICommand (or a public method) on a control's DataContext " +
        "(view-model) directly — trigger the logic behind a button without finding and clicking that " +
        "button, or reach a command that has no button yet. Give the command's 'path' ('SaveCommand', " +
        "'Editor.RefreshCommand', or a method name) and an optional 'parameter'. CanExecute is checked " +
        "first. Resolves the view-model from 'name' (a control Name or visible label) or, if omitted, the " +
        "main window's DataContext. Disabled unless EnableInteraction was set.")]
    public static Task<string> InvokeCommandTool(
        [Description("Optional control Name/label whose DataContext to target. Omit for the main window's DataContext.")]
        string? name,
        [Description("Dotted path to an ICommand property or a public method, e.g. 'SaveCommand'.")]
        string path,
        [Description("Optional command parameter / single method argument, as text.")]
        string? parameter = null) =>
        AgentToolContext.RunToolAsync("invoke_command", () =>
        {
            if (!TryResolveDataContext(name, out var dataContext, out var error))
                return error;

            var result = ViewModelInspector.InvokeCommand(dataContext, path, parameter);
            Dispatcher.UIThread.RunJobs();
            return result;
        });

    /// <summary>
    /// Resolves the view-model to operate on: the DataContext of the control named by <paramref name="name"/>,
    /// or the main window's DataContext when no name is given. Reports a clear reason when nothing usable
    /// is found instead of returning a null the caller has to interpret.
    /// </summary>
    private static bool TryResolveDataContext(string? name, out object? dataContext, out string error)
    {
        dataContext = null;
        error = string.Empty;

        if (!string.IsNullOrWhiteSpace(name))
        {
            var control = AgentToolContext.FindInteractionTarget(name);
            if (control is null)
            {
                error = $"No control named (or labeled) '{name}' was found to read a DataContext from.";
                return false;
            }

            dataContext = control.DataContext;
            if (dataContext is null)
            {
                error = $"Control '{name}' has a null DataContext.";
                return false;
            }

            return true;
        }

        var top = AgentToolContext.GetPrimaryTopLevel();
        if (top is null)
        {
            error = "No active window/top-level was found.";
            return false;
        }

        dataContext = top.DataContext;
        if (dataContext is null)
        {
            error = "The main window's DataContext is null. Pass 'name' to target a control whose DataContext is set.";
            return false;
        }

        return true;
    }

    private static string DescribeTarget(Control control) =>
        control.Name is { Length: > 0 } name ? $"{control.GetType().Name} #{name}" : control.GetType().Name;

    /// <summary>
    /// Describes the control that actually received the action. When it differs from the addressed/hit
    /// control (the common case — a label or pixel resolves to an inner <c>AccessText</c>), it says so, so
    /// the agent can tell "clicked the Button" from "poked the label and nothing happened".
    /// </summary>
    private static string ResolvedName(Control addressed, Control acted, string addressedLabel) =>
        ReferenceEquals(addressed, acted)
            ? addressedLabel
            : $"{DescribeTarget(acted)} (resolved from {addressedLabel})";

    /// <summary>
    /// Returns the requested automation provider from <paramref name="control"/>'s peer, or from the
    /// nearest visual ancestor whose peer exposes it. This mirrors how a real click/keypress resolves
    /// to the handling control: addressing by visible label often lands on an inner element (e.g. the
    /// <c>TextBlock</c> inside a <c>TreeViewItem</c> or <c>Button</c>), and the actionable element is an
    /// ancestor.
    /// </summary>
    private static T? ResolveProvider<T>(Control control) where T : class =>
        ResolveProviderOwner<T>(control)?.Provider;

    /// <summary>
    /// Like <see cref="ResolveProvider{T}"/> but also returns the <b>owner</b> control whose peer exposed
    /// the provider, so callers can report which control the action actually resolved to.
    /// </summary>
    private static (T Provider, Control Owner)? ResolveProviderOwner<T>(Control control) where T : class
    {
        Visual? current = control;
        while (current is Control owner)
        {
            // A peer can throw for controls that aren't fully realized; treat that the same as
            // "no provider here" and keep walking up to the actionable ancestor.
            if (TryCreatePeer(owner)?.GetProvider<T>() is { } provider)
                return (provider, owner);

            current = owner.GetVisualParent();
        }

        return null;
    }

    /// <summary>
    /// Walks from <paramref name="control"/> up the visual tree to the nearest control assignable to
    /// <typeparamref name="T"/> (inclusive). Used for the typed action fallbacks (<see cref="Button"/>,
    /// <see cref="TabItem"/>, <see cref="ToggleButton"/>) so they climb to the actionable control just
    /// like the peer-provider resolution does — a pixel hit or label match lands on an inner child.
    /// </summary>
    private static T? FindSelfOrAncestor<T>(Control control) where T : Control
    {
        Visual? current = control;
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = current.GetVisualParent();
        }

        return null;
    }

    /// <summary>
    /// Creates the automation peer for <paramref name="control"/>, returning null instead of throwing
    /// for controls that aren't fully realized (mirrors <c>ControlLocator.FindByAutomationName</c>).
    /// </summary>
    private static AutomationPeer? TryCreatePeer(Control control)
    {
        try
        {
            return ControlAutomationPeer.CreatePeerForElement(control);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Fallback for expand/collapse when no <see cref="IExpandCollapseProvider"/> is exposed (Avalonia's
    /// <c>TreeViewItem</c> has no such peer): walks up to the nearest expandable control and sets its
    /// <c>IsExpanded</c>. Returns false if no expandable ancestor was found.
    /// </summary>
    private static bool SetExpanded(Control control, bool expanded)
    {
        Visual? current = control;
        while (current is Control owner)
        {
            switch (owner)
            {
                case TreeViewItem treeViewItem:
                    treeViewItem.IsExpanded = expanded;
                    return true;
                case Expander expander:
                    expander.IsExpanded = expanded;
                    return true;
            }

            current = owner.GetVisualParent();
        }

        return false;
    }

    /// <summary>
    /// Selects an item in the nearest <see cref="SelectingItemsControl"/> (self or ancestor) by its
    /// display text — covers ComboBox/ListBox/TabControl without opening the popup and hunting for the
    /// item container.
    /// </summary>
    private static string SelectItem(Control control, string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "Provide the item text in 'value' for 'select_item'.";

        var itemsControl = control as SelectingItemsControl
                           ?? control.GetVisualAncestors().OfType<SelectingItemsControl>().FirstOrDefault();
        if (itemsControl is null)
            return $"Control '{name}' ({control.GetType().Name}) is not (and is not inside) a selectable items control.";

        var index = 0;
        foreach (var item in itemsControl.Items)
        {
            if (ItemText(item) is { } text && text.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                itemsControl.SelectedIndex = index;
                return $"Selected item '{text}' (index {index}) in {itemsControl.GetType().Name}.";
            }

            index++;
        }

        return $"No item whose text contains '{value}' was found in {itemsControl.GetType().Name}.";
    }

    private static string? ItemText(object? item) =>
        item switch
        {
            null => null,
            string s => s,
            ContentControl { Content: string content } => content,
            HeaderedItemsControl { Header: string header } => header,
            _ => item.ToString()
        };

    /// <summary>
    /// Scrolls the nearest <see cref="ScrollViewer"/> (self, ancestor, or descendant) by a delta given
    /// as "dx,dy".
    /// </summary>
    private static string ScrollBy(Control control, string name, string? value)
    {
        var parts = (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var dx) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var dy))
        {
            return "Provide the scroll delta as 'dx,dy' in 'value' (e.g. '0,120').";
        }

        var scroll = FindScrollViewer(control);
        if (scroll is null)
            return $"No ScrollViewer found at or around '{name}' ({control.GetType().Name}).";

        scroll.Offset = new Vector(scroll.Offset.X + dx, scroll.Offset.Y + dy);
        return $"Scrolled by ({dx},{dy}); offset is now ({scroll.Offset.X:0.#},{scroll.Offset.Y:0.#}) " +
               $"of extent {scroll.Extent.Width:0.#}x{scroll.Extent.Height:0.#}.";
    }

    private static ScrollViewer? FindScrollViewer(Control control) =>
        control as ScrollViewer
        ?? control.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault()
        ?? control.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private static string SetValue(Control control, string name, string? value)
    {
        // Text first: a TextBox-style control exposes IValueProvider.
        if (ResolveProvider<IValueProvider>(control) is { IsReadOnly: false } valueProvider)
        {
            valueProvider.SetValue(value ?? string.Empty);
            return $"Set '{name}' value to '{value}'.";
        }

        // Numeric controls (Slider, NumericUpDown, ScrollBar) expose IRangeValueProvider.
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
            ResolveProvider<IRangeValueProvider>(control) is { IsReadOnly: false } rangeProvider)
        {
            rangeProvider.SetValue(number);
            return $"Set '{name}' value to {number}.";
        }

        // Fallbacks for controls whose peer does not implement a value pattern.
        switch (control)
        {
            case TextBox textBox:
                textBox.Text = value ?? string.Empty;
                return $"Set '{name}'.Text to '{value}'.";

            case ToggleButton toggle when bool.TryParse(value, out var isChecked):
                toggle.IsChecked = isChecked;
                return $"Set '{name}'.IsChecked to {isChecked}.";

            default:
                return $"'set' is not supported for control '{name}' ({control.GetType().Name}).";
        }
    }

    private static string PressKey(string? name, string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
            return "A key or gesture (e.g. 'Enter', 'Ctrl+S') is required in 'value' for the 'key' action.";

        KeyGesture parsed;
        try
        {
            parsed = KeyGesture.Parse(gesture);
        }
        catch (Exception)
        {
            return $"'{gesture}' is not a valid key gesture. Examples: 'Enter', 'Escape', 'Down', 'Ctrl+S'.";
        }

        if (!TryResolveInputTarget(name, out var target, out var error))
            return error;

        // Focus the addressed control so a REAL KeyDown/KeyUp routes to it (keyboard input goes to the
        // focused element). Then synthesize real input through the pipeline so the control's own KeyDown
        // handling runs — e.g. Escape/Enter reach a focused TextBox. Falls back to a routed event if
        // synthesis is unavailable.
        var control = target as Control;
        control?.Focus();

        var top = TopLevelOf(control) ?? AgentToolContext.GetPrimaryTopLevel();
        var modifiers = ToRawModifiers(parsed.KeyModifiers);

        if (top is not null && InputSynthesizer.CanSynthesize(top))
        {
            var result = InputSynthesizer.PressKey(top, parsed.Key, modifiers);
            return $"Pressed '{gesture}'{TargetSuffix(name)} (real input): {result}";
        }

        try
        {
            target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = parsed.Key, KeyModifiers = parsed.KeyModifiers, Source = target });
            target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = parsed.Key, KeyModifiers = parsed.KeyModifiers, Source = target });
        }
        catch (Exception ex)
        {
            return $"Could not press '{gesture}'{TargetSuffix(name)}: {ex.Message}";
        }

        return $"Pressed '{gesture}'{TargetSuffix(name)} (routed-event fallback).";
    }

    private static string TypeText(string? name, string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "Text is required in 'value' for the 'type' action.";

        if (!TryResolveInputTarget(name, out var target, out var error))
            return error;

        var control = target as Control;
        control?.Focus();

        // Prefer REAL text input through the pipeline (the way typed characters reach a focused TextBox).
        var top = TopLevelOf(control) ?? AgentToolContext.GetPrimaryTopLevel();
        if (top is not null && InputSynthesizer.CanSynthesize(top))
        {
            var result = InputSynthesizer.TextInput(top, text);
            return $"Typed '{text}'{TargetSuffix(name)} (real input): {result}";
        }

        // Fallback for a TextBox when synthesis is unavailable: insert at the caret directly.
        if (target is TextBox textBox)
        {
            var current = textBox.Text ?? string.Empty;
            var caret = Math.Clamp(textBox.CaretIndex, 0, current.Length);
            textBox.Text = current.Insert(caret, text);
            textBox.CaretIndex = caret + text.Length;
            return $"Typed '{text}'{TargetSuffix(name)} (direct-insert fallback).";
        }

        try
        {
            target.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = target });
        }
        catch (Exception ex)
        {
            return $"Could not type{TargetSuffix(name)}: {ex.Message}";
        }

        return $"Typed '{text}'{TargetSuffix(name)} (routed-event fallback).";
    }

    private static TopLevel? TopLevelOf(Control? control) =>
        control is null ? null : TopLevel.GetTopLevel(control);

    private static bool TryResolveInputTarget(string? name, out Interactive target, out string error)
    {
        target = null!;
        error = string.Empty;

        if (!string.IsNullOrWhiteSpace(name))
        {
            if (AgentToolContext.FindInteractionTarget(name) is { } control)
            {
                target = control;
                return true;
            }

            error = $"No control named (or labeled) '{name}' was found.";
            return false;
        }

        if (AgentToolContext.GetFocusedElement() is Interactive focused)
        {
            target = focused;
            return true;
        }

        error = "No target was given and nothing currently has focus. Pass 'name' (a control Name or " +
                "visible label) to target a specific control, or focus/click it first (e.g. invoke <name> focus).";
        return false;
    }

    private static string TargetSuffix(string? name) =>
        string.IsNullOrWhiteSpace(name) ? " on the focused element" : $" on '{name}'";
}
