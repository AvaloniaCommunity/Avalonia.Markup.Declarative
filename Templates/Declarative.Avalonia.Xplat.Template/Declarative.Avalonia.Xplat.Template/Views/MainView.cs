using System;
using Avalonia.Controls;
using Avalonia.Markup.Declarative;
using CommunityToolkit.Mvvm.ComponentModel;

public partial class MainView() : ViewBase<MainView.State>(new State())
{
    protected override object Build(State vm)
    {
        return new StackPanel()
                .Orientation(Avalonia.Layout.Orientation.Vertical)
                .VerticalAlignment(Avalonia.Layout.VerticalAlignment.Center)
                .Children([
                new StackPanel()
                .Classes("bg-primary")
                .Orientation(Avalonia.Layout.Orientation.Horizontal)
                .HorizontalAlignment(Avalonia.Layout.HorizontalAlignment.Center)
                .VerticalAlignment(Avalonia.Layout.VerticalAlignment.Center)
                .Height(50)
                .Spacing(8)
                .Children([
                    new Border()
                        .Classes("underline")
                        .Padding(8)
                        .Child(
                        new TextBlock()
                            .VerticalAlignment(Avalonia.Layout.VerticalAlignment.Center)
                            .Text(vm,x=>x.CountText)),
                    new Button().Content("+").OnClick(_=>vm.Count++),
                    new Button().Content("-").OnClick(_=>vm.Count--),
                    new Border().Padding(0,0,0,8)
            ]),
            ]);

    }

    public sealed partial class State : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CountText))]
        private int count = 0;

        public string CountText => Count.ToString();
    }
}