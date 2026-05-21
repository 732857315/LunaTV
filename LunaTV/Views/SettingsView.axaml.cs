using Avalonia.Controls;
using LunaTV.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LunaTV.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<SettingsViewModel>();
    }
}