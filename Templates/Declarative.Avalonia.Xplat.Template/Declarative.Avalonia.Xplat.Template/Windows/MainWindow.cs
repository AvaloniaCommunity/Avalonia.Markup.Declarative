using Avalonia.Controls;
namespace Declarative.Avalonia.Xplat.Template.Windows
{
    public class MainWindow : Window
    {
        public MainWindow()
        {
            this.Content = new HostView();
        }
    }
}