using System.Windows;
using MemoryTrainer.ViewModels;

namespace MemoryTrainer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.ShowHistory();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.ShowSettings();
    }
}