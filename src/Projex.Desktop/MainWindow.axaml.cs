using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Projex.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<Button>("PlanningButton")!.Click += (_, _) => new GanttConnectionWindow().Show();
    }
}
