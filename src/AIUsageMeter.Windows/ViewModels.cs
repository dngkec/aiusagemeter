using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using AIUsageMeter.Core;

namespace AIUsageMeter.Windows;

internal abstract class BindableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); return true;
    }
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class UsageWindowViewModel(UsageWindow value)
{
    public string Label => value.Label;
    public string Caption => value.ReadingCaption;
    public double Percent => Math.Clamp(value.Percent, 0, 100);
    public System.Windows.Media.Brush Color => (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(UsageColor.For(value.Percent))!;
    public string Reset => value.ResetsAt is null ? "" : $"Resets {value.ResetsAt.Value.ToLocalTime():g}";
}

internal sealed class ProviderSnapshotViewModel : BindableBase
{
    private const double RingDiameter = 42;
    private const double RingStrokeWidth = 4;
    private bool _expanded;
    public ProviderSnapshotViewModel(ProviderSnapshot value)
    {
        Id = value.Id; Name = value.Name; Monogram = value.Id.Monogram(); Percent = Math.Clamp(value.PrimaryPercent, 0, 100);
        PercentText = value.Status == ProviderStatus.Ready ? $"{value.PrimaryPercent:F0}%" : ShortStatus(value.Status);
        Status = value.Status; Message = value.Message ?? ""; Source = value.Source == DataSourceKind.Demo ? "DEMO DATA" : value.Source.ToString();
        Color = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(value.Status == ProviderStatus.Ready ? UsageColor.For(value.PrimaryPercent) : "#73747C")!;
        var ringUnits = Math.PI * RingDiameter / RingStrokeWidth;
        var filledUnits = ringUnits * Percent / 100;
        ProgressDashArray = new DoubleCollection { filledUnits, ringUnits - filledUnits };
        Windows = new ObservableCollection<UsageWindowViewModel>(value.FeaturedWindows().Select(x => new UsageWindowViewModel(x)));
        DashboardUrl = value.DashboardUrl;
    }
    public ProviderId Id { get; }
    public string Name { get; }
    public string Monogram { get; }
    public double Percent { get; }
    public string PercentText { get; }
    public ProviderStatus Status { get; }
    public string Message { get; }
    public string Source { get; }
    public System.Windows.Media.Brush Color { get; }
    public DoubleCollection ProgressDashArray { get; }
    public ObservableCollection<UsageWindowViewModel> Windows { get; }
    public Uri? DashboardUrl { get; }
    public bool IsExpanded { get => _expanded; set => Set(ref _expanded, value); }

    private static string ShortStatus(ProviderStatus status) => status switch
    {
        ProviderStatus.SetupNeeded => "SETUP", ProviderStatus.Unauthorized => "SIGN IN", ProviderStatus.RateLimited => "LIMITED",
        ProviderStatus.Offline => "OFFLINE", ProviderStatus.Expired => "EXPIRED", ProviderStatus.Loading => "…", _ => "ERROR"
    };
}

internal sealed class OverlayViewModel : BindableBase
{
    public ObservableCollection<ProviderSnapshotViewModel> Providers { get; } = [];
    public void Replace(IEnumerable<ProviderSnapshot> values)
    {
        var expanded = Providers.FirstOrDefault(x => x.IsExpanded)?.Id;
        Providers.Clear();
        foreach (var value in values)
        {
            var item = new ProviderSnapshotViewModel(value) { IsExpanded = value.Id == expanded };
            Providers.Add(item);
        }
        Raise(nameof(HasProviders));
    }
    public bool HasProviders => Providers.Count > 0;
}
