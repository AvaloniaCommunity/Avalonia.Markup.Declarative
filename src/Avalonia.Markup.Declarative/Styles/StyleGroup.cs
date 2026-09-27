using System;
using System.Collections.Generic;
using Avalonia.Styling;

namespace Avalonia.Markup.Declarative;

/// <summary>A collection of styles that can share a selector prefix.</summary>
/// <param name="groupSelectorFunc">An optional selector function applied to each relative style in the group.</param>
public class StyleGroup(Func<Selector,Selector>? groupSelectorFunc = null) : List<object>
{
    /// <summary>Gets the selector function shared by styles in this group, if one was provided.</summary>
    public Func<Selector, Selector>? GroupSelectorFunc { get; } = groupSelectorFunc;

    /// <inheritdoc/>
    public override string ToString()
    {
        if (GroupSelectorFunc != null)
        {
            return GroupSelectorFunc(null!).ToString();
        }
        return base.ToString() ?? "- No selector --";
    }
}
