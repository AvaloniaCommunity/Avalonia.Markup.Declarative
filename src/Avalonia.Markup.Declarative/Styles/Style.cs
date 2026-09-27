using Avalonia.Styling;
using System;
using System.Runtime.CompilerServices;

namespace Avalonia.Markup.Declarative;
/// <summary>
/// Typed style to support method chains with generic arguments
/// </summary>
/// <typeparam name="TControl">Type of the control that style will be applied to</typeparam>
public class Style<TControl> : Style, IRelativeStyle
    where TControl : StyledElement
{
    private Func<Selector, Selector> SelectorFunc { get; }

    /// <summary>
    /// Creates a style with an <c>OfType&lt;TControl&gt;()</c> selector.
    /// </summary>
    public Style()
    {
        SelectorFunc = s => s.OfType<TControl>();

        UpdateSelector(null);
    }

    /// <summary>
    /// Creates Style with applied selector.
    /// </summary>
    public Style(
            Func<Selector, Selector> selectorFunc,
            [CallerArgumentExpression(nameof(selectorFunc))] string? expression = null,
            [CallerFilePath] string? callerFile = null)
    {
        var selectorString = selectorFunc(null!).ToString();

        // 1. Check if the user explicitly specified a root (starts with a letter, e.g. OfType<StackPanel>).
        bool hasExplicitRootType = !string.IsNullOrEmpty(selectorString) && char.IsLetter(selectorString[0]);

        // 2. Check whether the selector is a traversal (a path to descendants).
        // In Avalonia traversals include spaces, '>', or '/' in the selector string.
        // Also inspect the C# expression for traversal helpers for robustness.
        bool isTraversal =
            (!string.IsNullOrEmpty(selectorString) && (selectorString.Contains(' ') || selectorString.Contains('>') || selectorString.Contains('/'))) ||
            (expression != null && (expression.Contains(".Descendant(") || expression.Contains(".Child(") || expression.Contains(".Template(")));

        // If a root type is explicitly specified OR the selector is a traversal, do not modify it.
        if (hasExplicitRootType || isTraversal)
        {
            SelectorFunc = selectorFunc;
        }
        else
        {
            // For simple single selectors (e.g. `.my-class` or `:pointerover`),
            // safely bind the selector to `TControl` so it won't apply to other control types.
            SelectorFunc = s => selectorFunc(s.OfType<TControl>());
        }

        UpdateSelector(null);
    }

    /// <inheritdoc/>
    public void UpdateSelector(Func<Selector, Selector>? baseSelectorFunc)
    {
        if (baseSelectorFunc != null)
            Selector = SelectorFunc(baseSelectorFunc(null!));
        else
            Selector = SelectorFunc(null!);
    }
}

internal interface IRelativeStyle : IStyle
{
    /// <summary>Updates this style's selector using an optional selector from its containing group.</summary>
    void UpdateSelector(Func<Selector, Selector>? baseSelectorFunc);
}
