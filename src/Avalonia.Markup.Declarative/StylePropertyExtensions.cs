using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Styling;
using System;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Avalonia.Markup.Declarative;

/// <summary>Fluent helpers for configuring styles and their selectors.</summary>
public static class StylePropertyExtensions
{
    /// <summary>
    /// Creates a selector and applies <c>OfType&lt;TElement&gt;()</c> to it.
    /// </summary>
    /// <typeparam name="TElement">Type of the control that style will be applied</typeparam>
    /// <param name="style">Style</param>
    /// <param name="selector">Selector modifier function</param>
    /// <returns>style with applied selector</returns>
    public static Style<TElement> Selector<TElement>(this Style<TElement> style, Func<Selector, Selector> selector)
        where TElement : StyledElement
    {
        Selector TypeSelector(Selector? s) => s.OfType<TElement>();
        style.Selector = selector(TypeSelector(null));
        return style;
    }

    /// <summary>Sets the selector for an untyped style.</summary>
    /// <param name="style">The style to update.</param>
    /// <param name="selector">A function that creates the selector.</param>
    /// <returns>The updated style.</returns>
    public static Style Selector(this Style style, Func<Selector?, Selector> selector)
    {
        style.Selector = selector(null);
        return style;
    }

    /// <summary>Adds a setter to a typed style and returns it for chaining.</summary>
    /// <typeparam name="TElement">The styled element type.</typeparam>
    /// <param name="style">The style to update.</param>
    /// <param name="avaloniaProperty">The property to set.</param>
    /// <param name="value">The setter value.</param>
    /// <param name="file">The source file where the setter was declared.</param>
    /// <param name="line">The source line where the setter was declared.</param>
    /// <returns>The updated style.</returns>
    [StackTraceHidden]
    public static Style<TElement> Setter<TElement>(
        this Style<TElement> style,
        AvaloniaProperty avaloniaProperty,
        object value,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
        where TElement : StyledElement
        => style._addSetter(avaloniaProperty, value, file, line);

    /// <summary>Adds a setter to an untyped style and returns it for chaining.</summary>
    /// <param name="style">The style to update.</param>
    /// <param name="avaloniaProperty">The property to set.</param>
    /// <param name="value">The setter value.</param>
    /// <param name="file">The source file where the setter was declared.</param>
    /// <param name="line">The source line where the setter was declared.</param>
    /// <returns>The updated style.</returns>
    [StackTraceHidden]
    public static Style Setter(
        this Style style,
        AvaloniaProperty avaloniaProperty,
        object value,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
        => ExecuteStyleAction(
            style,
            avaloniaProperty,
            () => style.Setters.Add(new Setter(avaloniaProperty, value)),
            file,
            line);

    /// <summary>Adds a setter to a typed style and wraps failures with source context.</summary>
    /// <typeparam name="TElement">The styled element type.</typeparam>
    /// <param name="style">The style to update.</param>
    /// <param name="avaloniaProperty">The property to set.</param>
    /// <param name="value">The setter value.</param>
    /// <param name="file">The source file where the setter was declared.</param>
    /// <param name="line">The source line where the setter was declared.</param>
    /// <returns>The updated style.</returns>
    [StackTraceHidden]
    public static Style<TElement> _addSetter<TElement>(
        this Style<TElement> style,
        AvaloniaProperty avaloniaProperty,
        object value,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
        where TElement : StyledElement
        => ExecuteStyleAction(
            style,
            avaloniaProperty,
            () => style.Setters.Add(new Setter(avaloniaProperty, value)),
            file,
            line);

    /// <summary>
    /// Adds a style setter using a compiled binding with a specified source.
    /// </summary>
    public static Style<TElement> _addSetterCompiledBinding<TElement, TViewModel, TValue>(
        this Style<TElement> style,
        AvaloniaProperty avaloniaProperty,
        TViewModel source,
        Expression<Func<TViewModel, TValue>> getter,
        BindingMode? bindingMode = null,
        IValueConverter? converter = null,
        [CallerFilePath] string? file = null,
        [CallerLineNumber] int line = 0)
        where TElement : StyledElement
    {
        var effectiveConverter = DeclarativeBindingAutoConverter.GetEffectiveConverter<TValue>(avaloniaProperty, converter);

        return ExecuteStyleAction(
            style,
            avaloniaProperty,
            () =>
            {
                var binding = CompiledBinding.Create(getter, source: source, mode: bindingMode ?? BindingMode.Default, converter: effectiveConverter);
                style.Setters.Add(new Setter(avaloniaProperty, binding));
            },
            file,
            line);
    }

    /// <summary>
    /// Adds a style setter using a compiled binding relative to DataContext.
    /// </summary>
    public static Style<TElement> _addSetterCompiledBinding<TElement, TViewModel, TValue>(
            this Style<TElement> style,
            AvaloniaProperty avaloniaProperty,
            Expression<Func<TViewModel, TValue>> getter,
            BindingMode? bindingMode = null,
            IValueConverter? converter = null,
            [CallerFilePath] string? file = null,
            [CallerLineNumber] int line = 0)
            where TElement : StyledElement
    {
        var effectiveConverter = DeclarativeBindingAutoConverter.GetEffectiveConverter<TValue>(avaloniaProperty, converter);

        return ExecuteStyleAction(
            style,
            avaloniaProperty,
            () =>
            {
                var binding = CompiledBinding.Create(getter, mode: bindingMode ?? BindingMode.Default, converter: effectiveConverter);
                style.Setters.Add(new Setter(avaloniaProperty, binding));
            },
            file,
            line);
    }

    [StackTraceHidden]
    private static Style<TElement> ExecuteStyleAction<TElement>(
        Style<TElement> style,
        AvaloniaProperty avaloniaProperty,
        Action action,
        string? file,
        int line)
        where TElement : StyledElement
    {
        try
        {
            action();
            return style;
        }
        catch (ViewBuildingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateStyleBuildException($"Style<{typeof(TElement).Name}>", avaloniaProperty, ex, file, line);
        }
    }

    [StackTraceHidden]
    private static Style ExecuteStyleAction(
        Style style,
        AvaloniaProperty avaloniaProperty,
        Action action,
        string? file,
        int line)
    {
        try
        {
            action();
            return style;
        }
        catch (ViewBuildingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateStyleBuildException("Style", avaloniaProperty, ex, file, line);
        }
    }

    [StackTraceHidden]
    private static ViewBuildingException CreateStyleBuildException(
        string identity,
        AvaloniaProperty avaloniaProperty,
        Exception exception,
        string? file,
        int line)
    {
        return new ViewBuildingException(
            $"UI Build Error on {identity} while applying '{avaloniaProperty.Name}'.{Environment.NewLine}" +
            $"File: {file}{Environment.NewLine}" +
            $"Line: {line}{Environment.NewLine}" +
            $"Error: {exception.Message}",
            exception);
    }

    /// <summary>Sets the grid column for a style target.</summary>
    /// <typeparam name="TElement">The styled control type.</typeparam>
    /// <param name="style">The style to update.</param>
    /// <param name="value">The zero-based grid column.</param>
    /// <returns>The updated style.</returns>
    public static Style<TElement> Col<TElement>(this Style<TElement> style, int value)
        where TElement : Control
    {
        style.Add(new Setter(Grid.ColumnProperty, value));
        return style;
    }

    /// <summary>Sets the grid row for a style target.</summary>
    /// <typeparam name="TElement">The styled control type.</typeparam>
    /// <param name="style">The style to update.</param>
    /// <param name="value">The zero-based grid row.</param>
    /// <returns>The updated style.</returns>
    public static Style<TElement> Row<TElement>(this Style<TElement> style, int value)
        where TElement : Control
    {
        style.Add(new Setter(Grid.RowProperty, value));
        return style;
    }

    /// <summary>Sets the number of grid columns spanned by a style target.</summary>
    /// <typeparam name="TElement">The styled control type.</typeparam>
    /// <param name="style">The style to update.</param>
    /// <param name="value">The number of columns to span.</param>
    /// <returns>The updated style.</returns>
    public static Style<TElement> ColSpan<TElement>(this Style<TElement> style, int value)
        where TElement : Control
    {
        style.Add(new Setter(Grid.ColumnSpanProperty, value));
        return style;
    }

    /// <summary>Sets the number of grid rows spanned by a style target.</summary>
    /// <typeparam name="TElement">The styled control type.</typeparam>
    /// <param name="style">The style to update.</param>
    /// <param name="value">The number of rows to span.</param>
    /// <returns>The updated style.</returns>
    public static Style<TElement> RowSpan<TElement>(this Style<TElement> style, int value)
        where TElement : Control
    {
        style.Add(new Setter(Grid.RowSpanProperty, value));
        return style;
    }

}
