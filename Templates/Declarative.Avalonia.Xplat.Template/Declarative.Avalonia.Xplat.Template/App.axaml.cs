using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Declarative;
using Declarative.Avalonia.Xplat.Template.Windows;
using Avalonia.Markup.Xaml;

namespace Declarative.Avalonia.Xplat.Template;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () => ViewFactory.Create<HostView>(); ;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = ViewFactory.Create<HostView>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}