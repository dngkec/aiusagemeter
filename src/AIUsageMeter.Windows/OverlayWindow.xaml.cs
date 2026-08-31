using System.Windows;
using System.Windows.Input;

namespace AIUsageMeter.Windows;

public partial class OverlayWindow : Window
{
    public event EventHandler? PresentationChanged;
    internal OverlayWindow(OverlayViewModel model) { InitializeComponent(); DataContext = model; }

    private void Provider_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ProviderSnapshotViewModel selected) return;
        if (DataContext is OverlayViewModel model)
            foreach (var item in model.Providers) item.IsExpanded = ReferenceEquals(item, selected) ? !item.IsExpanded : false;
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (DataContext is OverlayViewModel model)
            foreach (var item in model.Providers) item.IsExpanded = false;
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }
}
