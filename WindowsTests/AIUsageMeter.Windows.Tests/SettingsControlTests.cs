using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using AIUsageMeter.Core;
using AIUsageMeter.Windows;
using AIUsageMeter.Windows.Design;
using AIUsageMeter.Windows.Settings;
using RadioButton = System.Windows.Controls.RadioButton;

namespace AIUsageMeter.Windows.Tests;

/// <summary>
/// The Settings controls are hand-drawn, so nothing gives them focus visuals or a screen-reader
/// identity for free. These check the parts a person operating by keyboard depends on.
/// </summary>
[TestClass]
public sealed class SettingsControlTests
{
    [TestMethod]
    public void TheToggleReportsOnAndOffToAutomation()
    {
        var state = Rendering.Sta(() =>
        {
            var toggle = new SettingsToggle();
            Lay(toggle, 36, 20);
            var provider = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(toggle).GetPattern(PatternInterface.Toggle);
            var off = provider.ToggleState;
            provider.Toggle();
            return (off, on: provider.ToggleState, toggle.IsChecked);
        });

        Assert.AreEqual(ToggleState.Off, state.off);
        Assert.AreEqual(ToggleState.On, state.on);
        Assert.IsTrue(state.IsChecked);
    }

    [TestMethod]
    public void TheToggleKeepsItsSizeWhenFocused()
    {
        // The focus ring used to grow the track's own border, shrinking its content box and
        // shoving the thumb. It is drawn outside the track instead.
        var sizes = Rendering.Sta(() =>
        {
            var toggle = new SettingsToggle();
            Lay(toggle, 60, 40);
            var resting = new Size(toggle.ActualWidth, toggle.ActualHeight);
            toggle.Focus();
            toggle.UpdateLayout();
            return (resting, focused: new Size(toggle.ActualWidth, toggle.ActualHeight));
        });

        Assert.AreEqual(new Size(36, 20), sizes.resting);
        Assert.AreEqual(sizes.resting, sizes.focused);
    }

    [TestMethod]
    public void TheSegmentedControlNamesEachSegmentAndMarksTheChosenOne()
    {
        var report = Rendering.Sta(() =>
        {
            var segmented = new SettingsSegmented
            {
                ItemsSource = new[] { new Option<int>(0, "Small"), new Option<int>(1, "Medium"), new Option<int>(2, "Large") },
                SelectedValue = 1
            };
            Lay(segmented, 260, 40);
            var segments = Descendants<RadioButton>(segmented).ToList();
            return segments.Select(x => (Name: AutomationProperties.GetName(x), Checked: x.IsChecked == true)).ToList();
        });

        Assert.HasCount(3, report);
        Assert.AreEqual("Small", report[0].Name);
        Assert.AreEqual("Medium", report[1].Name);
        Assert.IsFalse(report[0].Checked);
        Assert.IsTrue(report[1].Checked, "the chosen segment must read as selected, not just look filled");
        Assert.IsFalse(report[2].Checked);
    }

    [TestMethod]
    public void ChoosingASegmentWritesTheValueBackOnce()
    {
        var chosen = Rendering.Sta(() =>
        {
            var segmented = new SettingsSegmented
            {
                ItemsSource = new[] { new Option<VerticalPosition>(VerticalPosition.Top, "Top"), new Option<VerticalPosition>(VerticalPosition.Bottom, "Bottom") },
                SelectedValue = VerticalPosition.Top
            };
            Lay(segmented, 200, 40);
            var segments = Descendants<RadioButton>(segmented).ToList();
            segments[1].IsChecked = true;
            return segmented.SelectedValue;
        });

        Assert.AreEqual(VerticalPosition.Bottom, chosen);
    }

    [TestMethod]
    public void ThePickerShowsAValueTheListDoesNotCarry()
    {
        // The store clamps the refresh interval to a range rather than snapping it to these five,
        // so a hand-edited file can hold 120. A blank row would tell the reader nothing.
        var captions = Rendering.Sta(() =>
        {
            var picker = new SettingsPicker
            {
                ItemsSource = new[] { new Option<double>(30, "30 seconds"), new Option<double>(300, "5 minutes") },
                SelectedValue = 300d
            };
            Lay(picker, 200, 40);
            var known = Caption(picker);
            picker.SelectedValue = 120d;
            picker.UpdateLayout();
            return (known, unknown: Caption(picker));
        });

        Assert.AreEqual("5 minutes", captions.known);
        Assert.AreEqual("120", captions.unknown);
    }

    [TestMethod]
    public void SettingsBrushesComeFromTheOverlayPalette()
    {
        Assert.AreEqual(0.06, Palette.GroupFill.Opacity, 0.0001);
        Assert.AreEqual(Colors.White, Palette.GroupFill.Color);
        Assert.AreEqual(0.10, Palette.Divider.Opacity, 0.0001);
        Assert.AreEqual(0.45, Palette.FocusRing.Opacity, 0.0001);
        Assert.AreEqual(Palette.Usage(0).Color, Palette.ToggleOn.Color, "the on-toggle is the healthy-usage green");
        Assert.AreEqual(Palette.Usage(95).Color, Palette.Failure.Color, "failures reuse the overlay's high-usage red");
        Assert.IsTrue(Palette.GroupFill.IsFrozen);
        Assert.IsTrue(Palette.FocusRing.IsFrozen);
    }

    /// <summary>The picker's visible caption, which it also publishes as its automation name.</summary>
    private static string Caption(SettingsPicker picker)
    {
        var shown = Descendants<TextBlock>(picker).Single(x => x.Name == "Caption").Text;
        Assert.AreEqual(shown, AutomationProperties.GetName(picker), "the caption is what a reader hears");
        return shown;
    }

    private static void Lay(FrameworkElement element, double width, double height)
    {
        var host = new Border { Child = element, Width = width, Height = height };
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
