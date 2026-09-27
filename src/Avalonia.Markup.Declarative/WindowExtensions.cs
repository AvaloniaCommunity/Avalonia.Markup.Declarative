using Avalonia.Controls;

namespace Avalonia.Markup.Declarative;

/// <summary>Fluent helpers for building native menu contents.</summary>
public static class NativeMenuExtensions
{
    /// <summary>Adds items to a native menu.</summary>
    /// <param name="menu">The menu to update.</param>
    /// <param name="items">The items to append.</param>
    /// <returns>The same menu.</returns>
    public static NativeMenu Items(this NativeMenu menu, params NativeMenuItemBase[] items)
    {
        foreach (var item in items)
            menu.Items.Add(item);

        return menu;
    }

    /// <summary>Adds items to a native menu item's submenu, creating the submenu when needed.</summary>
    /// <param name="menu">The menu item to update.</param>
    /// <param name="items">The items to append to its submenu.</param>
    /// <returns>The same menu item.</returns>
    public static NativeMenuItem Items(this NativeMenuItem menu, params NativeMenuItemBase[] items)
    {
        menu.Menu ??= [];
        foreach (var item in items)
            menu.Menu.Items.Add(item);

        return menu;
    }
}
