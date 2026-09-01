using System.Collections;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AIUsageMeter.Windows.Design;
using Border = System.Windows.Controls.Border;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using RadioButton = System.Windows.Controls.RadioButton;
using UserControl = System.Windows.Controls.UserControl;

namespace AIUsageMeter.Windows.Settings;

/// <summary>
/// A row of mutually exclusive segments. The segments are templated radio buttons rather than
/// plain buttons: a screen reader then reads which segment is chosen, and Left/Right walk the row.
/// The template carries none of the native radio chrome.
/// </summary>
internal sealed class SettingsSegmented : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(SettingsSegmented),
        new PropertyMetadata(null, (d, _) => ((SettingsSegmented)d).Rebuild()));

    public static readonly DependencyProperty SelectedValueProperty = DependencyProperty.Register(
        nameof(SelectedValue), typeof(object), typeof(SettingsSegmented),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((SettingsSegmented)d).Paint()));

    private readonly StackPanel _row = new() { Orientation = System.Windows.Controls.Orientation.Horizontal };
    private readonly string _group = "segment." + Guid.NewGuid().ToString("N");
    private bool _painting;

    public SettingsSegmented()
    {
        Focusable = false;
        // The track is the same inset shell the text fields wear, so a row of controls reads as one
        // family. The chosen segment is then the lighter of the two: a dark chip on a light track
        // reads as a hole cut in the card.
        Content = new Border
        {
            Background = Palette.Inset,
            BorderBrush = Palette.Divider,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(SettingsMetrics.FieldCorner + 1),
            Padding = new Thickness(2),
            Child = _row
        };
        SnapsToDevicePixels = true;
    }

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public object? SelectedValue { get => GetValue(SelectedValueProperty); set => SetValue(SelectedValueProperty, value); }

    private void Rebuild()
    {
        _row.Children.Clear();
        if (ItemsSource is null) return;
        foreach (var item in ItemsSource)
        {
            var choice = Choice.Of(item);
            var segment = new RadioButton
            {
                Content = choice.Label,
                Tag = choice.Value,
                GroupName = _group,
                Cursor = Cursors.Hand,
                FontSize = SettingsTypo.RowLabelSize,
                FontWeight = SettingsTypo.RowLabelWeight,
                Foreground = Palette.Primary,
                Padding = new Thickness(12, 4, 12, 4),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FocusVisualStyle = null,
                Template = SegmentTemplate()
            };
            AutomationProperties.SetName(segment, choice.Label);
            segment.Checked += (_, _) => { if (!_painting) SelectedValue = choice.Value; };
            _row.Children.Add(segment);
        }
        Paint();
    }

    private void Paint()
    {
        // Setting IsChecked re-enters through Checked; the guard keeps it from writing the value back.
        _painting = true;
        try
        {
            foreach (var child in _row.Children)
            {
                if (child is not RadioButton segment) continue;
                var on = Equals(segment.Tag, SelectedValue);
                segment.IsChecked = on;
                segment.Background = on ? Palette.ChipFill : Brushes.Transparent;
            }
        }
        finally { _painting = false; }
    }

    /// <summary>
    /// Carbon segment: a rounded fill, a white focus ring for the keyboard, and nothing else. The
    /// checked fill is set in <see cref="Paint"/> so it survives a template regeneration. Hovering
    /// an unchosen segment lifts it halfway, which is the only affordance the control offers.
    /// </summary>
    private static ControlTemplate SegmentTemplate()
    {
        var ring = new FrameworkElementFactory(typeof(Border), "Ring");
        ring.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        ring.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        ring.SetValue(Border.BorderBrushProperty, Brushes.Transparent);

        var fill = new FrameworkElementFactory(typeof(Border), "Fill");
        fill.SetValue(Border.CornerRadiusProperty, new CornerRadius(SettingsMetrics.FieldCorner - 1));
        fill.SetValue(Border.PaddingProperty, new TemplateBindingExtension(RadioButton.PaddingProperty));
        fill.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(RadioButton.BackgroundProperty));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        fill.AppendChild(content);
        ring.AppendChild(fill);

        var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = ring };
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, Palette.FocusRing, "Ring"));
        template.Triggers.Add(focused);

        // Only while unchosen: the chosen segment already carries the chip and must not flicker
        // to a different fill as the pointer crosses it.
        var hovered = new MultiTrigger();
        hovered.Conditions.Add(new System.Windows.Condition(UIElement.IsMouseOverProperty, true));
        hovered.Conditions.Add(new System.Windows.Condition(ToggleButton.IsCheckedProperty, false));
        hovered.Setters.Add(new Setter(Border.BackgroundProperty, Palette.ActiveFill, "Fill"));
        template.Triggers.Add(hovered);
        return template;
    }
}
