using Avalonia;
using Avalonia.Controls;
using LunaTV.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LunaTV.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DataContext = ServiceLocator.GetRequiredService<MainViewModel>();
    }
}