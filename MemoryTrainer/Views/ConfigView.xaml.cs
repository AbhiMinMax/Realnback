using MemoryTrainer.ViewModels;
using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace MemoryTrainer.Views;

public partial class ConfigView : UserControl
{
    public ConfigView()
    {
        InitializeComponent();
    }

    private void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        var mainVm = Window.GetWindow(this)?.DataContext as MainViewModel;
        mainVm?.ShowHistory();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var mainVm = Window.GetWindow(this)?.DataContext as MainViewModel;
        mainVm?.ShowSettings();
    }

    private void OnToggleDecoyExpand(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.DataContext is SessionCycleConfigViewModel vm)
            vm.DecoyOffsetExpanded = !vm.DecoyOffsetExpanded;
    }
}
