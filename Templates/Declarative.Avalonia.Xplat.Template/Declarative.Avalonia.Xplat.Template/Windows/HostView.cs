using Avalonia.Controls;
using Avalonia.Markup.Declarative;
public partial class HostView() : ViewBase
{
    protected override object Build()
    {
        return new DrawerPage()
                    .DrawerHeader(
                        new Panel()
                        .Classes("banner"))
                        .Drawer(
                        new StackPanel()
                        .HorizontalAlignment(Avalonia.Layout.HorizontalAlignment.Stretch)
                        .Children([
                            new Button().HorizontalAlignment(Avalonia.Layout.HorizontalAlignment.Stretch).Content("Home")
                        ]))
                        .Content(new CounterView());
    }
}