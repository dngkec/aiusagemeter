using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AIUsageMeter.Core;

namespace AIUsageMeter.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _model;
    private readonly DispatcherTimer _caption;
    private Point _dragStart;
    private ProviderSettingsItem? _dragItem;

    internal SettingsWindow(SettingsViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        // "Last read 3 min ago" counts up from a fixed moment. Nothing else would ever redraw it,
        // so an open window would keep claiming the reading was taken just now.
        _caption = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background,
            (_, _) => _model.TickCaption(), Dispatcher);
        _caption.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var dark = 1;
        _ = DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
    }

    protected override void OnClosed(EventArgs e)
    {
        _caption.Stop();
        _model.Flush();
        _model.Detach();
        base.OnClosed(e);
    }

    private void Search_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _model.Query = ""; e.Handled = true; }
        else if (e.Key == Key.Down) { _model.SelectNext(); SyncSidebar(); e.Handled = true; }
    }

    private void Sidebar_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Down) { _model.SelectNext(); SyncSidebar(); e.Handled = true; }
        else if (e.Key == Key.Up) { _model.SelectPrevious(); SyncSidebar(); e.Handled = true; }
    }

    private void SyncSidebar()
    {
        ProviderList.SelectedItem = _model.SelectedProvider;
        SecretBox?.Clear();
    }

    private void FocusSearch(object sender, ExecutedRoutedEventArgs e) => SearchBox.Focus();

    private void General_Click(object sender, RoutedEventArgs e)
    {
        ProviderList.SelectedItem = null;
        _model.SelectGeneral();
        SecretBox?.Clear();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        ProviderList.SelectedItem = null;
        _model.SelectAbout();
        SecretBox?.Clear();
    }

    private void ProviderSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        SecretBox?.Clear();
        if (_model.SelectedProvider is { } item && SecretBox is not null)
            SecretBox.Password = item.NewSecret;
    }

    private void Secret_Changed(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedProvider is not null && sender is PasswordBox box)
            _model.SelectedProvider.NewSecret = box.Password;
    }

    private void SaveSecret_Click(object sender, RoutedEventArgs e)
    {
        _model.SaveSecret();
        SecretBox?.Clear();
    }

    private void RemoveSecret_Click(object sender, RoutedEventArgs e) => _model.RemoveSecret();
    private void MoveUp_Click(object sender, RoutedEventArgs e) => _model.MoveSelected(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => _model.MoveSelected(1);
    private void ResetOffset_Click(object sender, RoutedEventArgs e) => _model.ResetOffset();
    private async void RefreshNow_Click(object sender, RoutedEventArgs e) => await _model.RefreshNowAsync();
    private void DismissNotice_Click(object sender, RoutedEventArgs e) => _model.DismissNotice();
    private void Repository_Click(object sender, RoutedEventArgs e) => _model.Open(SupportLinks.Repository);
    private void Issues_Click(object sender, RoutedEventArgs e) => _model.Open(SupportLinks.Issues);
    private void Sponsor_Click(object sender, RoutedEventArgs e) => _model.Open(SupportLinks.Sponsor);
    private void Designer_Click(object sender, RoutedEventArgs e) => _model.Open(SupportLinks.Designer);

    private void ProviderList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = FindItem(e.OriginalSource as DependencyObject);
    }

    private void ProviderList_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !_model.CanReorder) { _dragItem = null; return; }
        if (_dragItem is not { } item) return;
        var delta = e.GetPosition(null) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragItem = null;
        DragDrop.DoDragDrop(ProviderList, item, System.Windows.DragDropEffects.Move);
    }

    private void ProviderList_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!_model.CanReorder) return;
        if (e.Data.GetData(typeof(ProviderSettingsItem)) is not ProviderSettingsItem moved) return;
        var target = FindItem(e.OriginalSource as DependencyObject);
        if (target is null || ReferenceEquals(target, moved)) return;
        var from = _model.Providers.IndexOf(moved);
        var to = _model.Providers.IndexOf(target);
        if (from < 0 || to < 0) return;
        _model.Move(moved, to - from);
        ProviderList.SelectedItem = moved;
    }

    private static ProviderSettingsItem? FindItem(DependencyObject? origin)
    {
        while (origin is not null)
        {
            if (origin is ListBoxItem { DataContext: ProviderSettingsItem item }) return item;
            origin = System.Windows.Media.VisualTreeHelper.GetParent(origin);
        }
        return null;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
