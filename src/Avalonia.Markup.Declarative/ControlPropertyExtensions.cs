using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Styling;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Avalonia.Markup.Declarative;

/// <summary>Fluent helpers for setting Avalonia control properties, bindings, and content.</summary>
public static class ControlPropertyExtensions
{
    /// <summary>Runs a property setter while preserving the fluent chain and adding build-error context.</summary>
    /// <typeparam name="TControl">The control being configured.</typeparam>
    /// <param name="control">The control being configured.</param>
    /// <param name="setAction">The setter action to run.</param>
    /// <param name="file">The source file where the setter was called.</param>
    /// <param name="line">The source line where the setter was called.</param>
    /// <returns>The configured control.</returns>
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TControl _set<TControl>(
        this TControl control,
        Action setAction,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
    {
        try
        {
            setAction();
            return control;
        }
        catch (ViewBuildingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateControlBuildException(control, ex, file, line);
        }
    }

    /// <summary>
    /// Creates a compiled binding from an expression and applies it to an Avalonia property.
    /// </summary>
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TControl _setCompiledBinding<TControl, TViewModel, TValue>(
        this TControl control,
        AvaloniaProperty avaloniaProperty,
        TViewModel source,
        Expression<Func<TViewModel, TValue>> getter,
        BindingMode? mode = null,
        IValueConverter? converter = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
        where TControl : AvaloniaObject
    {
        try
        {
            var effectiveConverter = DeclarativeBindingAutoConverter.GetEffectiveConverter<TValue>(avaloniaProperty, converter);
            var binding = CompiledBinding.Create(getter,
                source: source,
                mode: mode ?? BindingMode.Default,
                converter: effectiveConverter);

            control.Bind(avaloniaProperty, binding);
            return control;
        }
        catch (ViewBuildingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateControlBuildException(control, ex, file, line, avaloniaProperty?.Name);
        }
    }

    /// <summary>
    /// Creates a compiled binding from an expression and applies it to an Avalonia property. The source of the binding will be determined automatically at runtime based on the DataContext of the control.
    /// </summary>
    /// <typeparam name="TControl"></typeparam>
    /// <typeparam name="TViewModel"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    /// <param name="control"></param>
    /// <param name="avaloniaProperty"></param>
    /// <param name="getter"></param>
    /// <param name="mode"></param>
    /// <param name="converter"></param>
    /// <param name="file">The source file where the binding was declared, supplied by the compiler.</param>
    /// <param name="line">The source line where the binding was declared, supplied by the compiler.</param>
    /// <returns></returns>
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TControl _setCompiledBinding<TControl, TViewModel, TValue>(
    this TControl control,
    AvaloniaProperty avaloniaProperty,
    Expression<Func<TViewModel, TValue>> getter,
    BindingMode? mode = null,
    IValueConverter? converter = null,
    [CallerFilePath] string? file = null,
    [CallerLineNumber] int line = 0)
    where TControl : AvaloniaObject
    {
        try
        {
            var effectiveConverter = DeclarativeBindingAutoConverter.GetEffectiveConverter<TValue>(avaloniaProperty, converter);
            // don't specify source for the binding, so it will be determined automatically at runtime based on the DataContext of the control
            var binding = CompiledBinding.Create(getter,
                mode: mode ?? BindingMode.Default,
                converter: effectiveConverter);

            control.Bind(avaloniaProperty, binding);
            return control;
        }
        catch (ViewBuildingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateControlBuildException(control, ex, file, line, avaloniaProperty?.Name);
        }
    }

    /// <summary>
    /// Applies any binding to the specified <see cref="AvaloniaProperty"/> and returns the control for fluent chaining.
    /// </summary>
    /// <remarks>
    /// Unlike the strongly-typed expression overloads (e.g. <c>Text(source, x =&gt; x.Value)</c>), this method accepts a
    /// ready-made <see cref="BindingBase"/>, so it works with the full feature set of reflection bindings (<see cref="Binding"/>),
    /// compiled bindings (<c>CompiledBinding</c>), template bindings and multi-bindings.
    /// The generated per-property setters expose this as a named overload, e.g. <c>new TextBlock().Text(new Binding("Value"))</c>.
    /// </remarks>
    /// <typeparam name="TControl">The control type, which must derive from <see cref="AvaloniaObject"/>.</typeparam>
    /// <param name="control">The control to bind. Cannot be null.</param>
    /// <param name="avaloniaProperty">The property to bind. Cannot be null.</param>
    /// <param name="binding">The binding to apply to the property. Cannot be null.</param>
    /// <param name="file">The source file where the binding was declared, supplied by the compiler.</param>
    /// <param name="line">The source line where the binding was declared, supplied by the compiler.</param>
    /// <returns>The same control instance, enabling method chaining.</returns>
    [StackTraceHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TControl BindValue<TControl>(
        this TControl control,
        AvaloniaProperty avaloniaProperty,
        BindingBase binding,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
        where TControl : AvaloniaObject
    {
        try
        {
            control.Bind(avaloniaProperty, binding);
            return control;
        }
        catch (ViewBuildingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateControlBuildException(control, ex, file, line, avaloniaProperty?.Name);
        }
    }

    [StackTraceHidden]
    private static ViewBuildingException CreateControlBuildException(
        object? target,
        Exception exception,
        string? file,
        int line,
        string? operation = null)
    {
        var identity = CreateTargetIdentity(target);
        var header = string.IsNullOrWhiteSpace(operation)
            ? $"UI Build Error on {identity}."
            : $"UI Build Error on {identity} while applying '{operation}'.";

        var message =
            $"{header}{Environment.NewLine}" +
            $"File: {file}{Environment.NewLine}" +
            $"Line: {line}{Environment.NewLine}" +
            $"Error: {exception.Message}";

        // Record into the diagnostics buffer at the single creation site so agent tooling can read
        // the rich control/file/line message. This is the only place build setter failures originate.
        Diagnostics.DiagnosticsErrorLog.Record(
            Diagnostics.DiagnosticSeverity.Error, Diagnostics.DiagnosticCategory.Build, identity, message);

        return new ViewBuildingException(message, exception);
    }

    private static string CreateTargetIdentity(object? target)
    {
        if (target is null)
        {
            return "Unknown";
        }

        var name = (target as Control)?.Name;
        return string.IsNullOrEmpty(name)
            ? target.GetType().Name
            : $"{target.GetType().Name} (Name: '{name}')";
    }

    /// <summary>
    /// Sets the data context of the specified control and returns the control instance for method chaining.
    /// </summary>
    /// <remarks>This method enables fluent configuration of a control's data context, allowing for concise
    /// initialization patterns.</remarks>
    /// <typeparam name="TElement">The type of the control, which must derive from StyledElement.</typeparam>
    /// <typeparam name="TDataContext">The type of the data context to assign. Must be a reference type.</typeparam>
    /// <param name="control">The control whose data context will be set.</param>
    /// <param name="value">The data context value to assign to the control. Can be null.</param>
    /// <param name="dataContext">When this method returns, contains the data context value that was assigned to the control.</param>
    /// <returns>The control instance with its data context set to the specified value.</returns>
    [StackTraceHidden]
    public static TElement DataContext<TElement, TDataContext>(
        this TElement control,
        TDataContext value,
        out TDataContext dataContext)
        where TElement : StyledElement where TDataContext : class
    {
        dataContext = value;
        control.DataContext = value;
        return control;
    }

    /// <summary>
    /// It's a shortcut for Grid_Column (in xaml: Grid.Column) extension
    /// </summary>
    /// <typeparam name="TElement"></typeparam>
    /// <param name="control">Control for positioning</param>
    /// <param name="value">Grid.Column value</param>
    /// <returns></returns>
    [StackTraceHidden]
    public static TElement Col<TElement>(this TElement control, int value)
        where TElement : Control
    {
        Grid.SetColumn(control, value);
        return control;
    }

    /// <summary>
    /// It's a shortcut for Grid_Row (in xaml: Grid.Row) extension
    /// </summary>
    /// <typeparam name="TElement">Control type</typeparam>
    /// <param name="control">Control for positioning</param>
    /// <param name="value">Grid.Row value</param>
    /// <returns></returns>
    [StackTraceHidden]
    public static TElement Row<TElement>(this TElement control, int value)
        where TElement : Control
    {
        Grid.SetRow(control, value);
        return control;
    }

    /// <summary>
    /// It's a shortcut for Grid_ColumnDefinitions (in xaml: Grid.ColumnDefinitions) extension
    /// </summary>
    /// <typeparam name="TElement">Grid</typeparam>
    /// <param name="control">Grid control</param>
    /// <param name="value">Grid.ColumnDefinitions value</param>
    /// <returns></returns>
    [StackTraceHidden]
    public static TElement Cols<TElement>(this TElement control, ColumnDefinitions value)
        where TElement : Grid
    {
        control.ColumnDefinitions = value;
        return control;
    }

    /// <summary>
    /// It's a shortcut for Grid_RowDefinitions (in xaml: Grid.RowDefinitions) extension
    /// </summary>
    /// <typeparam name="TElement">Grid</typeparam>
    /// <param name="control">Grid control</param>
    /// <param name="value">Grid.RowDefinitions value</param>
    /// <returns></returns>
    [StackTraceHidden]
    public static TElement Rows<TElement>(this TElement control, RowDefinitions value)
        where TElement : Grid
    {
        control.RowDefinitions = value;
        return control;
    }

    /// <summary>
    /// It's a shortcut for Grid_ColumnDefinitions (in xaml: Grid.ColumnDefinitions) extension
    /// </summary>
    /// <typeparam name="TElement">Grid</typeparam>
    /// <param name="control">Grid control</param>
    /// <param name="value">String representing ColumnDefinitions i.e. "0,*,30,Auto" </param>
    /// <returns></returns>
    [StackTraceHidden]
    public static TElement Cols<TElement>(this TElement control, string value)
        where TElement : Grid
    {
        control.ColumnDefinitions = ColumnDefinitions.Parse(value);
        return control;
    }

    /// <summary>
    /// It's a shortcut for Grid_RowDefinitions (in xaml: Grid.RowDefinitions) extension
    /// </summary>
    /// <typeparam name="TElement">Grid</typeparam>
    /// <param name="control">Grid control</param>
    /// <param name="value">String representing RowDefinitions i.e. "0,*,30,Auto" </param>
    /// <returns></returns>
    [StackTraceHidden]
    public static TElement Rows<TElement>(this TElement control, string value)
        where TElement : Grid
    {
        control.RowDefinitions = RowDefinitions.Parse(value);
        return control;
    }

    /// <summary>
    /// It's a shortcut for Grid_ColumnSpan (in xaml: Grid.ColumnSpan) extension
    /// </summary>
    /// <typeparam name="TElement">Control Type</typeparam>
    /// <param name="control">Control for positioning</param>
    /// <param name="value">Grid.ColumnSpan value</param>
    /// <returns></returns>
    [StackTraceHidden]
    public static TElement ColSpan<TElement>(this TElement control, int value)
        where TElement : Control
    {
        Grid.SetColumnSpan(control, value);
        return control;
    }

    /// <summary>
    /// It's a shortcut for Grid_RowSpan (in xaml: Grid.RowSpan) extension
    /// </summary>
    /// <typeparam name="TElement">Control type</typeparam>
    /// <param name="control">Control for positioning</param>
    /// <param name="value">Grid.RowSpan value</param>
    /// <returns></returns>
    [StackTraceHidden]
    public static TElement RowSpan<TElement>(this TElement control, int value)
        where TElement : Control
    {
        Grid.SetRowSpan(control, value);
        return control;
    }

    /// <summary>
    /// Adds the specified child controls to the container and returns the container instance.
    /// </summary>
    /// <remarks>This method enables fluent configuration of panel controls by allowing multiple child
    /// controls to be added in a single call. The method returns the container to support method chaining.</remarks>
    /// <typeparam name="TPanel">The type of the container, which must derive from Panel.</typeparam>
    /// <param name="container">The panel to which the child controls will be added. Must not be null.</param>
    /// <param name="children">An array of controls to add as children to the container. Can be empty.</param>
    /// <returns>The container instance with the specified child controls added.</returns>
    [StackTraceHidden]
    public static TPanel Children<TPanel>(this TPanel container, params Control[] children)
        where TPanel : Panel
    {
        foreach (var child in children)
            container.Children.Add(child);
        return container;
    }

    /// <summary>
    /// Adds the specified items to the item collection of the given items control and returns the control for further
    /// configuration.
    /// </summary>
    /// <remarks>This method is typically used in fluent UI construction scenarios to add multiple items to an
    /// ItemsControl-derived control. If the control's Items property does not implement IList, the items will not be
    /// added.</remarks>
    /// <typeparam name="TItemsControl">The type of the items control to which items are added. Must inherit from ItemsControl.</typeparam>
    /// <param name="container">The items control instance to which the items will be added. Must not be null.</param>
    /// <param name="items">The items to add to the control's item collection. Each item must be a valid AvaloniaObject.</param>
    /// <returns>The same items control instance provided in the container parameter, enabling method chaining.</returns>
    [StackTraceHidden]
    public static TItemsControl Items<TItemsControl>(this TItemsControl container, params AvaloniaObject[] items)
        where TItemsControl : ItemsControl
    {
        if (container.Items is IList itemsCollection)
            foreach (var item in items)
                itemsCollection.Add(item);
        return container;
    }

    /// <summary>
    /// Configures the item template for the specified TabControl using a delegate to generate controls for each item.
    /// </summary>
    /// <remarks>This method enables custom rendering of each tab item by providing a function that generates
    /// a Control for each data item. The method supports fluent configuration by returning the TabControl
    /// instance.</remarks>
    /// <typeparam name="TItem">The type of the data items to be displayed in the TabControl.</typeparam>
    /// <param name="control">The TabControl to configure with the item template.</param>
    /// <param name="build">A delegate that creates a Control for each data item of type TItem. Cannot be null.</param>
    /// <returns>The TabControl instance with the item template applied.</returns>
    [StackTraceHidden]
    public static TabControl ItemTemplate<TItem>(this TabControl control, Func<TItem, Control> build) =>
        ItemTemplate<TItem, TabControl>(control, build);

    /// <summary>
    /// Configures the item template for the specified SelectingItemsControl using a delegate that builds a control for
    /// each item.
    /// </summary>
    /// <remarks>This method enables fluent configuration of item templates for SelectingItemsControl
    /// instances. The provided delegate is invoked for each item to generate its corresponding control.</remarks>
    /// <typeparam name="TItem">The type of the items displayed by the control.</typeparam>
    /// <param name="control">The SelectingItemsControl to configure the item template for.</param>
    /// <param name="build">A delegate that creates a Control for a given item of type TItem. Cannot be null.</param>
    /// <returns>The SelectingItemsControl instance with the item template configured.</returns>
    [StackTraceHidden]
    public static SelectingItemsControl ItemTemplate<TItem>(this SelectingItemsControl control,
        Func<TItem, Control> build) =>
        ItemTemplate<TItem, SelectingItemsControl>(control, build);

    /// <summary>
    /// Sets the item template for the specified items control using a delegate to build each item.
    /// </summary>
    /// <remarks>This method enables fluent configuration of item templates for custom or standard items
    /// controls. The provided delegate is used to generate a control for each item in the collection.</remarks>
    /// <typeparam name="TItem">The type of the data items displayed by the items control.</typeparam>
    /// <typeparam name="TItemsControl">The type of the items control to configure. Must inherit from ItemsControl.</typeparam>
    /// <param name="control">The items control whose item template is to be set.</param>
    /// <param name="build">A delegate that creates a control for each data item. The delegate receives the data item as a parameter and
    /// returns a Control to represent it.</param>
    /// <returns>The items control instance with the item template configured.</returns>
    [StackTraceHidden]
    public static TItemsControl ItemTemplate<TItem, TItemsControl>(this TItemsControl control,
        Func<TItem, Control> build)
        where TItemsControl : ItemsControl
    {
        control.ItemTemplate = control.ItemTemplate = new FuncDataTemplate<TItem>((val, _) => build(val));
        return control;
    }

    /// <summary>
    /// Sets a custom item template for the specified MenuFlyout using the provided item builder function.
    /// </summary>
    /// <remarks>Use this method to define how each item in the MenuFlyout should be displayed by providing a
    /// function that returns a Control for each data item. This enables flexible and dynamic item rendering based on
    /// the data.</remarks>
    /// <typeparam name="TItem">The type of the data items displayed in the MenuFlyout.</typeparam>
    /// <param name="control">The MenuFlyout control to which the item template will be applied.</param>
    /// <param name="build">A function that creates a Control for a given data item of type TItem. This function is used to generate the
    /// visual representation of each item.</param>
    /// <returns>The MenuFlyout control with the custom item template applied.</returns>
    [StackTraceHidden]
    public static MenuFlyout ItemTemplate<TItem>(this MenuFlyout control, Func<TItem, Control> build)
    {
        control.ItemTemplate = new FuncDataTemplate<TItem>((val, _) => build(val));
        return control;
    }

    /// <summary>
    /// Sets a data template for the menu items using a delegate that builds a control for each item of the specified
    /// type.
    /// </summary>
    /// <remarks>Use this method to define how each item in the menu should be rendered by providing a
    /// function that returns a control for each data item. This enables custom item layouts and behaviors.</remarks>
    /// <typeparam name="TItem">The type of the data items displayed in the menu.</typeparam>
    /// <param name="control">The menu item control to which the data template will be applied.</param>
    /// <param name="build">A delegate that creates a control for a given data item of type TItem.</param>
    /// <returns>The menu item control with the specified item template applied.</returns>
    [StackTraceHidden]
    public static MenuItem ItemTemplate<TItem>(this MenuItem control, Func<TItem, Control> build)
    {
        control.ItemTemplate = new FuncDataTemplate<TItem>((val, _) => build(val));
        return control;
    }

    /// <summary>
    /// Sets a data template for items in the menu using the specified item builder function.
    /// </summary>
    /// <remarks>Use this method to define how each item in the menu should be rendered based on its data.
    /// This is useful for customizing the appearance of menu items when binding to a collection.</remarks>
    /// <typeparam name="TItem">The type of the data items to be displayed in the menu.</typeparam>
    /// <param name="control">The menu control to which the item template will be applied.</param>
    /// <param name="build">A function that creates a control for each item of type TItem. The function is called for each data item to
    /// generate its visual representation.</param>
    /// <returns>The menu control with the item template applied, enabling fluent configuration.</returns>
    [StackTraceHidden]
    public static Menu ItemTemplate<TItem>(this Menu control, Func<TItem, Control> build)
    {
        control.ItemTemplate = new FuncDataTemplate<TItem>((val, _) => build(val));
        return control;
    }

    /// <summary>
    /// Sets the ItemsPanel property of an ItemsControl to a new PanelTemplate that produces the specified Panel instance. This allows you to define a custom panel for arranging the items in the control. The method returns the control instance to enable fluent configuration.
    /// </summary>
    /// <typeparam name="TItemsControl"></typeparam>
    /// <param name="control"></param>
    /// <param name="panel"></param>
    /// <returns></returns>
    [StackTraceHidden]
    public static TItemsControl ItemsPanel<TItemsControl>(this TItemsControl control, Panel panel)
        where TItemsControl : ItemsControl
    {
        control.ItemsPanel = new PanelTemplate(panel);
        return control;
    }

    /// <summary>
    /// Represents a template that produces a predefined Panel instance.
    /// </summary>
    /// <param name="panel">The Panel instance to be returned by the template. Cannot be null.</param>
    [StackTraceHidden]
    record PanelTemplate(Panel panel) : ITemplate<Panel?>
    {
        public Panel Build() => panel;
        object ITemplate.Build() => throw new NotImplementedException();
    }

    /// <summary>
    /// Performs additional processing on the control and returns the control itself. This method is useful for chaining multiple operations on a control in a fluent manner.
    /// </summary>
    /// <typeparam name="TElement"></typeparam>
    /// <param name="control"></param>
    /// <param name="process"></param>
    /// <returns></returns>
    [StackTraceHidden]
    public static TElement With<TElement>(this TElement control, Action<TElement> process)
    {
        process?.Invoke(control);
        return control;
    }

    /// <summary>
    /// Sets the Name property of the control and registers it with the provided INameScope. This allows the control to be referenced by name within the scope, enabling features like data binding and event handling that rely on named controls.
    /// </summary>
    /// <typeparam name="TElement"></typeparam>
    /// <param name="control">The control whose Name property will be set. Cannot be null.</param>
    /// <param name="name">The name to assign to the control. Cannot be null or empty.</param>
    /// <param name="ns">The name scope in which to register the control. Cannot be null.</param>
    /// <param name="_callerFile">The source file where the control was declared, supplied by the compiler.</param>
    /// <param name="_callerLine">The source line where the control was declared, supplied by the compiler.</param>
    /// <returns>The control instance with the specified name set and registered in the provided name scope. Enables method chaining.</returns>
    [StackTraceHidden]
    public static TElement Name<TElement>(this TElement control, string name, INameScope ns,
        [CallerFilePath] string? _callerFile = null, [CallerLineNumber] int _callerLine = 0)
        where TElement : Control
    {
        ns?.Register(name, control);
        control.Name = name;

        // Opt-in (dev-only): record where this control was declared so agent tooling can jump to the line.
        if (Diagnostics.AgentSourceTagging.Enabled && _callerFile is not null)
            Diagnostics.AgentSourceTagging.SetSource(control, $"{_callerFile}:{_callerLine}");

        return control;
    }

    /// <summary>
    /// Adds one or more styles to the specified control and returns the control for further configuration.
    /// </summary>
    /// <remarks>This method is intended for use with fluent configuration patterns, allowing multiple style
    /// additions in a single statement.</remarks>
    /// <typeparam name="TElement">The type of the control to which styles are applied. Must derive from Control.</typeparam>
    /// <param name="control">The control to which the styles will be added. Cannot be null.</param>
    /// <param name="styles">An array of styles to apply to the control. Cannot contain null elements.</param>
    /// <returns>The control instance with the specified styles applied. Enables method chaining.</returns>
    [StackTraceHidden]
    public static TElement Styles<TElement>(this TElement control, params Style[] styles)
        where TElement : Control
    {
        foreach (var style in styles)
            control.Styles.Add(style);

        return control;
    }

    /// <summary>
    /// Adds the specified styles to the control's style collection.
    /// </summary>
    /// <remarks>This method enables fluent configuration of a control's styles by allowing multiple styles to
    /// be added in a single call. The method returns the original control instance to support method
    /// chaining.</remarks>
    /// <typeparam name="TElement">The type of control to which the styles will be applied. Must inherit from Control.</typeparam>
    /// <param name="control">The control to which the styles will be added.</param>
    /// <param name="styles">A collection of styles to add to the control. Cannot be null.</param>
    /// <returns>The control instance with the specified styles added.</returns>
    [StackTraceHidden]
    public static TElement Styles<TElement>(this TElement control, IEnumerable<Style> styles)
        where TElement : Control
    {
        foreach (var style in styles)
            control.Styles.Add(style);

        return control;
    }

    /// <summary>
    /// Adds the specified CSS class name to the control's class collection and returns the control instance for method
    /// chaining.
    /// </summary>
    /// <remarks>This method is intended for use in fluent APIs to simplify the addition of CSS classes to
    /// controls. The method modifies the control's class collection in place.
    /// If you need to BIND a class to a boolean property, use the <see cref="BindClass{TElement, TViewModel}"/> method instead, which allows you to conditionally apply a class based on the value of a property in your view model.
    /// </remarks>
    /// <typeparam name="TElement">The type of control to which the class name is added. Must inherit from Control.</typeparam>
    /// <param name="control">The control to which the CSS class name will be added. Cannot be null.</param>
    /// <param name="className">The CSS class name to add to the control's class collection. Cannot be null.</param>
    /// <param name="line">The line number in the source file at which the method is called. This parameter is populated automatically by
    /// the compiler and is not intended to be set explicitly.</param>
    /// <param name="caller">The name of the calling member. This parameter is populated automatically by the compiler and is not intended to
    /// be set explicitly.</param>
    /// <returns>The control instance with the specified class name added, enabling method chaining.</returns>
    [StackTraceHidden]
    public static TElement Classes<TElement>(this TElement control, string className, [CallerLineNumber] int line = 0,
        [CallerMemberName] string? caller = null)
        where TElement : Control
    {
        control.Classes.Add(className);
        return control;
    }

    /// <summary>
    /// Binds a CSS class to a control based on a boolean property of the specified view model.
    /// </summary>
    /// <remarks>Use this method to dynamically apply or remove a CSS class from a control based on the state
    /// of a boolean property in the view model. This is useful for reflecting view model state changes in the
    /// UI.</remarks>
    /// <typeparam name="TElement">The type of the control to which the class will be bound. Must inherit from Control.</typeparam>
    /// <typeparam name="TViewModel">The type of the view model containing the property to bind.</typeparam>
    /// <param name="control">The control to which the CSS class will be conditionally applied.</param>
    /// <param name="source">The view model instance that provides the property used for binding.</param>
    /// <param name="getter">An expression that selects the boolean property from the view model. The class is applied when this property
    /// evaluates to <see langword="true"/>.</param>
    /// <param name="className">The name of the CSS class to apply to the control when the condition is met.</param>
    /// <returns>The control instance with the class binding applied, enabling fluent configuration.</returns>
    [StackTraceHidden]
    public static TElement BindClass<TElement, TViewModel>(this TElement control,
        TViewModel source,
        Expression<Func<TViewModel, bool>> getter,
        string className)
        where TElement : Control
    {
        var binding = CompiledBinding.Create(getter, source: source);
        control.BindClass(className, binding, null!);
        return control;
    }

    /// <summary>Returns a stack trace with the requested number of leading frames skipped.</summary>
    /// <param name="depth">The number of leading frames to skip.</param>
    /// <returns>The resulting stack trace.</returns>
    public static StackTrace GetDeeperStackTrace(int depth) =>
        depth > 0 ? GetDeeperStackTrace(depth - 1) : new StackTrace(0, true);


    /// <summary>
    /// Adds one or more data templates to the control's DataTemplates collection.
    /// </summary>
    /// <remarks>This method is intended for use in fluent configuration scenarios, allowing multiple data
    /// templates to be added in a single call.</remarks>
    /// <typeparam name="TElement">The type of the control to which the data templates are added. Must derive from Control.</typeparam>
    /// <param name="control">The control instance to which the data templates will be added. Cannot be null.</param>
    /// <param name="dataTemplate">An array of data templates to add to the control. Cannot contain null elements.</param>
    /// <returns>The control instance with the specified data templates added, enabling method chaining.</returns>
    [StackTraceHidden]
    public static TElement DataTemplates<TElement>(this TElement control, params IDataTemplate[] dataTemplate)
        where TElement : Control
    {
        foreach (var template in dataTemplate)
            control.DataTemplates.Add(template);
        return control;
    }

    /// <summary>
    /// Adds flyout to button and activates it on button click
    /// </summary>
    /// <typeparam name="TElement"></typeparam>
    /// <param name="control">target button</param>
    /// <param name="flyout">flyout to activate</param>
    /// <returns></returns>
    public static TElement AddFlyoutOnClick<TElement>(this TElement control, FlyoutBase flyout)
        where TElement : Button
    {
        control.Click += (o, e) => flyout.ShowAt(control);
        return control;
    }

    /// <summary>
    /// Adds a menu item to the specified menu flyout.
    /// </summary>
    /// <typeparam name="TElement">The type of the menu flyout element.</typeparam>
    /// <param name="menuFlyout">The menu flyout to which the item will be added.</param>
    /// <param name="menuItem">The menu item to be added to the flyout.</param>
    /// <returns>The menu flyout with the added item.</returns>
    public static TElement AddItem<TElement>(this TElement menuFlyout, MenuItem menuItem)
        where TElement : MenuFlyout
    {
        menuFlyout.Items.Add(menuItem);
        return menuFlyout;
    }

    /// <summary>
    /// Adds item to MenuFlyout
    /// </summary>
    /// <typeparam name="TElement">MenuFlyout type</typeparam>
    /// <param name="menuFlyout">Target MenuFlyout control</param>
    /// <param name="text">Item text</param>
    /// <param name="command">Item command</param>
    /// <param name="commandParameter">Command parameter</param>
    /// <returns></returns>
    public static TElement AddItem<TElement>(this TElement menuFlyout, string text, ICommand command,
        object? commandParameter = null)
        where TElement : MenuFlyout
    {
        var item = new MenuItem() { Header = text, Command = command };
        if (commandParameter != null)
            item.CommandParameter = commandParameter;

        menuFlyout.Items.Add(item);
        return menuFlyout;
    }

    /// <summary>
    /// Sets control instance reference to field so it can be accessed later in Markup
    /// </summary>
    /// <typeparam name="TElement">Control Type</typeparam>
    /// <param name="control">Control instance</param>
    /// <param name="field">field that will accept reference to control</param>
    /// <returns></returns>
    public static TElement Ref<TElement>(this TElement control, out TElement field)
    {
        field = control;
        return control;
    }
}
