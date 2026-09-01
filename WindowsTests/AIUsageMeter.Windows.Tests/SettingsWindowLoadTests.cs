using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AIUsageMeter.Core;
using AIUsageMeter.Windows;

namespace AIUsageMeter.Windows.Tests;

/// <summary>
/// The window is nine tenths XAML, and a bad resource key, a mistyped <c>x:Static</c>, or a binding
/// onto a property that no longer exists compiles fine and only shows itself when someone opens
/// Settings. This builds the real window and listens for what WPF would otherwise print and swallow.
/// </summary>
[TestClass]
public sealed class SettingsWindowLoadTests
{
    [TestMethod]
    public void EveryPaneBindsToAPropertyThatExists()
    {
        var broken = Rendering.Sta(() =>
        {
            var window = new SettingsWindow(Model());
            var model = (SettingsViewModel)window.DataContext;
            var found = new List<string>();
            var inspected = 0;

            void Sweep()
            {
                model.Notice = SettingsNotice.Failure("Could not save.");
                window.UpdateLayout();
                found.AddRange(PathErrors(window));
                inspected += Bindings(window);
            }

            // A window that is never shown builds no visual tree, so there would be nothing to
            // inspect. Off-screen and unactivated keeps it out of the way of whoever runs the suite.
            OffScreen(window);
            try
            {
                Sweep();
                // Every pane and every mode-only section: each is collapsed until something shows
                // it, and a collapsed subtree is exactly where a stale binding hides.
                model.SelectGeneral();
                Sweep();
                model.SelectAbout();
                Sweep();
                model.SelectedProvider = model.Providers.First(x => x.Id == ProviderId.OpenRouter);
                Sweep();
                foreach (var mode in new[] { ProviderMode.CustomJson, ProviderMode.Manual, ProviderMode.Live })
                {
                    model.SelectedProvider!.Mode = mode;
                    Sweep();
                }
                model.SelectedProvider!.Mode = ProviderMode.CustomJson;
                model.SelectedProvider.SecretPlacement = SecretPlacement.ApiKeyHeader;
                Sweep();
                model.Query = "open";
                Sweep();
            }
            finally { window.Close(); }
            return (Errors: found.Distinct().ToList(), inspected);
        });

        // Without this the walk could pass by finding nothing at all.
        Assert.IsGreaterThan(500, broken.inspected, "the window's visual tree did not build");
        Assert.IsEmpty(broken.Errors, "bindings onto a property that is not there: " + string.Join(" | ", broken.Errors));
    }

    [TestMethod]
    public void OnlyTheChosenPaneIsOnScreen()
    {
        // The provider pane used to carry its visibility and its DataContext on one element, so the
        // visibility binding asked a ProviderSettingsItem for IsProviderPane, failed, fell back to
        // the default, and left the pane stacked under General and About for good.
        var seen = Rendering.Sta(() =>
        {
            var window = new SettingsWindow(Model());
            var model = (SettingsViewModel)window.DataContext;
            var log = new List<string>();
            OffScreen(window);
            try
            {
                void Record(string pane)
                {
                    window.UpdateLayout();
                    log.Add($"{pane}:{string.Join(",", Panes(window).Select(x => $"{x.Path}={x.Element.Visibility}"))}");
                }

                Record("provider");
                model.SelectGeneral();
                Record("general");
                model.SelectAbout();
                Record("about");
            }
            finally { window.Close(); }
            return log;
        });

        Assert.AreEqual("provider:IsGeneralPane=Collapsed,IsAboutPane=Collapsed,IsProviderPane=Visible", seen[0]);
        Assert.AreEqual("general:IsGeneralPane=Visible,IsAboutPane=Collapsed,IsProviderPane=Collapsed", seen[1]);
        Assert.AreEqual("about:IsGeneralPane=Collapsed,IsAboutPane=Visible,IsProviderPane=Collapsed", seen[2]);
    }

    /// <summary>The three detail panes, found by the property each one's visibility hangs off.</summary>
    private static IEnumerable<(string Path, FrameworkElement Element)> Panes(DependencyObject root)
    {
        var wanted = new[] { "IsGeneralPane", "IsAboutPane", "IsProviderPane" };
        return Descendants(root)
            .OfType<FrameworkElement>()
            .Select(element => (Path: BindingOperations.GetBinding(element, UIElement.VisibilityProperty)?.Path?.Path ?? "", Element: element))
            .Where(found => wanted.Contains(found.Path))
            .OrderBy(found => Array.IndexOf(wanted, found.Path));
    }

    private static void OffScreen(Window window)
    {
        window.Width = 940;
        window.Height = 680;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.ShowActivated = false;
        // A window that is never shown builds no visual tree, so there would be nothing to walk.
        // ApplyTemplate alone is not enough. Off-screen and unactivated keeps it out of the way.
        window.Show();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static int Bindings(DependencyObject root)
    {
        var count = 0;
        var values = root.GetLocalValueEnumerator();
        while (values.MoveNext())
            if (values.Current.Value is BindingExpression) count++;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            count += Bindings(VisualTreeHelper.GetChild(root, index));
        return count;
    }

    private static bool HasSource(DependencyObject target, BindingExpression expression)
        => expression.ParentBinding.Source is not null
           || expression.ParentBinding.RelativeSource is not null
           || expression.ParentBinding.ElementName is not null
           || (target as FrameworkElement)?.DataContext is not null;

    /// <summary>
    /// Every binding in the tree whose path did not resolve. WPF prints these only to an attached
    /// debugger, so a renamed view-model property leaves a silently blank row in a shipped build.
    /// </summary>
    private static IEnumerable<string> PathErrors(DependencyObject root)
    {
        var values = root.GetLocalValueEnumerator();
        while (values.MoveNext())
        {
            if (values.Current.Value is not BindingExpression expression) continue;
            // A binding whose source is null is simply waiting for one — the provider pane while
            // General is open. Only a live source that does not carry the property is a defect.
            if (expression.Status != BindingStatus.PathError || !HasSource(root, expression)) continue;
            yield return $"{root.GetType().Name}.{values.Current.Property.Name} -> {expression.ParentBinding.Path?.Path}";
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var error in PathErrors(VisualTreeHelper.GetChild(root, index)))
                yield return error;
    }

    [TestMethod]
    public void SidebarRowsAreLegibleAgainstTheSurface()
    {
        // ListBoxItem is a Control, and Control re-registers Foreground with its own default of
        // black. A row label carrying no brush of its own inherits that black rather than the
        // window's white, and every provider name in the sidebar vanishes into the black surface.
        // It binds, it lays out, it renders: only the colour gives it away.
        var rows = Rendering.Sta(() =>
        {
            var window = new SettingsWindow(Model());
            OffScreen(window);
            try
            {
                window.UpdateLayout();
                var style = window.FindResource("SidebarRow");
                return Descendants(window)
                    .OfType<TextBlock>()
                    .Where(label => ReferenceEquals(label.Style, style))
                    .Select(label => (label.Text, Ink: ((SolidColorBrush)label.Foreground).Color))
                    .ToList();
            }
            finally { window.Close(); }
        });

        Assert.IsNotEmpty(rows, "the sidebar built no provider rows to inspect");
        var surface = ((SolidColorBrush)Design.Palette.Surface).Color;
        foreach (var row in rows)
            Assert.IsGreaterThan(128, Rendering.Probe.Distance(row.Ink, surface),
                $"'{row.Text}' is drawn too close to the surface colour to read");
    }

    [TestMethod]
    public void ScrollingToTheEndOfTheSidebarStillShowsProviders()
    {
        // CanContentScroll is inherited and ListBox's own theme style turns it on, so a bare
        // ScrollViewer around the sidebar hands scrolling to the StackPanel inside it, which moves
        // one whole child per notch: General, About, the section label, then a single notch that
        // steps over all 27 providers at once and leaves the sidebar empty.
        var seen = Rendering.Sta(() =>
        {
            var window = new SettingsWindow(Model());
            OffScreen(window);
            try
            {
                window.UpdateLayout();
                var list = Descendants(window).OfType<ListBox>().First();
                var scroll = Descendants(list).OfType<ScrollViewer>().First();
                scroll.ScrollToEnd();
                window.UpdateLayout();

                var viewport = new Rect(0, 0, scroll.ViewportWidth, scroll.ViewportHeight);
                var onScreen = Enumerable.Range(0, list.Items.Count)
                    .Select(index => list.ItemContainerGenerator.ContainerFromIndex(index))
                    .OfType<ListBoxItem>()
                    .Count(row => viewport.IntersectsWith(
                        row.TransformToAncestor(scroll).TransformBounds(new Rect(row.RenderSize))));
                return (OnScreen: onScreen, scroll.ExtentHeight);
            }
            finally { window.Close(); }
        });

        Assert.IsGreaterThan(0, seen.OnScreen, "scrolled to the end and not one provider is on screen");
        // Scrolling by item counts the sidebar's five children; scrolling by pixel counts its height.
        Assert.IsGreaterThan(100, seen.ExtentHeight, "the sidebar is scrolling by item, not by pixel");
    }

    [TestMethod]
    public void ClosingTheWindowFlushesAndUnsubscribes()
    {
        var host = Rendering.Sta(() =>
        {
            var probe = new RecordingHost();
            var model = new SettingsViewModel(Preferences(), new NoSecrets(), probe);
            var window = new SettingsWindow(model);
            window.Measure(new Size(940, 680));
            probe.Applies.Clear();
            window.Close();
            probe.RaiseChanged();
            return probe;
        });

        Assert.HasCount(1, host.Applies, "closing writes the last change through the host");
        Assert.AreEqual(0, host.ChangesSeenAfterClose, "the closed window must stop listening");
    }

    private static SettingsViewModel Model() => new(Preferences(), new NoSecrets(), new RecordingHost());

    private static AppPreferences Preferences() => AppPreferences.Defaults with
    {
        Providers =
        [
            new ProviderConfiguration(ProviderId.Claude, Enabled: true),
            new ProviderConfiguration(ProviderId.OpenRouter, Enabled: true, Mode: ProviderMode.Live),
            new ProviderConfiguration(ProviderId.Warp),
            new ProviderConfiguration(ProviderId.Custom, Mode: ProviderMode.CustomJson)
        ]
    };

    private sealed class RecordingHost : ISettingsHost
    {
        private bool _closed;
        public List<(AppPreferences Preferences, bool? Refetch)> Applies { get; } = [];
        public int ChangesSeenAfterClose { get; private set; }
        public IReadOnlyList<ProviderSnapshot> Snapshots => [];
        public DateTimeOffset? LastRefresh => DateTimeOffset.Now.AddMinutes(-3);
        public bool IsRefreshing => false;
        public string? PersistError => null;
        public event EventHandler? HostChanged;
        public void Apply(AppPreferences preferences, bool? refetch = null) { Applies.Add((preferences, refetch)); _closed = true; }
        public Task RefreshNowAsync() => Task.CompletedTask;
        public void RaiseChanged()
        {
            var before = HostChanged;
            HostChanged?.Invoke(this, EventArgs.Empty);
            if (_closed && before is not null) ChangesSeenAfterClose++;
        }
    }

    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string account) => null;
        public void Write(string account, string? value) { }
    }

}
