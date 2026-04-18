using MemoryTrainer.ViewModels;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MemoryTrainer.Views;

public partial class RecallPromptWindow : Window
{
    private const int GWL_STYLE = -16;
    private const int WS_SYSMENU = 0x80000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public RecallPromptWindow(RecallPromptViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        vm.DismissRequested += () => Dispatcher.BeginInvoke(Close);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Disable the X (close) button
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_SYSMENU);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Block close unless evaluation is complete
        if (DataContext is RecallPromptViewModel vm && !vm.CanDismiss)
            e.Cancel = true;
        base.OnClosing(e);
    }
}
