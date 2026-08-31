using System.Windows;
using System.Windows.Controls;
using AIUsageMeter.Core;

namespace AIUsageMeter.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _model;
    public event EventHandler<AppPreferences>? Saved;
    internal SettingsWindow(SettingsViewModel model) { InitializeComponent(); _model = model; DataContext = model; }

    private void Secret_Changed(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedProvider is not null && sender is PasswordBox box) _model.SelectedProvider.NewSecret = box.Password;
    }
    private void ProviderSelection_Changed(object sender, SelectionChangedEventArgs e) => SecretBox?.Clear();
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var preferences = _model.BuildPreferences();
            await _model.CommitSecretsAsync();
            _model.CommitStartupSetting();
            Saved?.Invoke(this, preferences); Close();
        }
        catch (Exception error) when (error is UsageMeterException or ArgumentException or InvalidOperationException
            or UnauthorizedAccessException or System.IO.IOException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        { _model.ValidationMessage = error.Message; }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void MoveUp_Click(object sender, RoutedEventArgs e) => _model.MoveSelected(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => _model.MoveSelected(1);
    private void Repository_Click(object sender, RoutedEventArgs e) => _model.Open(SupportLinks.Repository);
    private void Issues_Click(object sender, RoutedEventArgs e) => _model.Open(SupportLinks.Issues);
    private void Sponsor_Click(object sender, RoutedEventArgs e) => _model.Open(SupportLinks.Sponsor);
}
