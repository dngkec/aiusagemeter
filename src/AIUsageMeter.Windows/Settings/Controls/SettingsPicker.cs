using System.Collections;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AIUsageMeter.Windows.Design;
using Border = System.Windows.Controls.Border;
using Button = System.Windows.Controls.Button;
using Dock = System.Windows.Controls.Dock;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace AIUsageMeter.Windows.Settings;

/// <summary>A carbon popup list. Not ComboBox chrome.</summary>
internal sealed class SettingsPicker : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(SettingsPicker),
        new PropertyMetadata(null, (d, _) => ((SettingsPicker)d).PaintCaption()));

    public static readonly DependencyProperty SelectedValueProperty = DependencyProperty.Register(
        nameof(SelectedValue), typeof(object), typeof(SettingsPicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((SettingsPicker)d).PaintCaption()));

    private readonly TextBlock _caption = new()
    {
        Name = "Caption",
        FontSize = SettingsTypo.BodySize,
        Foreground = Palette.Primary,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly Popup _popup = new() { Placement = PlacementMode.Bottom, StaysOpen = false };
    private readonly StackPanel _menu = new();
    private readonly Border _shell;
    private readonly Border _focus;

    public SettingsPicker()
    {
        Focusable = true;
        FocusVisualStyle = null;
        MinWidth = 160;
        var chevron = new TextBlock
        {
            Text = "▾",
            FontSize = 11,
            Foreground = Palette.Secondary,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(chevron, Dock.Right);
        row.Children.Add(chevron);
        row.Children.Add(_caption);
        _shell = new Border
        {
            Background = Palette.Inset,
            BorderBrush = Palette.Divider,
            BorderThickness = new Thickness(1),
            CornerRadius = SettingsMetrics.Field,
            Padding = new Thickness(10, 6, 10, 6),
            Child = row,
            Cursor = Cursors.Hand
        };
        // The ring is drawn outside the shell, as the toggle's is. Thickening the shell's own border
        // on focus would shrink its content box and shove the caption sideways by a pixel.
        _focus = new Border
        {
            Margin = new Thickness(-3),
            CornerRadius = SettingsMetrics.FieldRing,
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            IsHitTestVisible = false
        };
        var host = new Grid();
        host.Children.Add(_shell);
        host.Children.Add(_focus);
        MouseEnter += (_, _) => { if (!IsKeyboardFocusWithin) _shell.BorderBrush = Palette.Dormant; };
        MouseLeave += (_, _) => { if (!IsKeyboardFocusWithin) _shell.BorderBrush = Palette.Divider; };
        _popup.PlacementTarget = _shell;
        _popup.Child = new Border
        {
            Background = Palette.Surface,
            BorderBrush = Palette.Edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = new ScrollViewer
            {
                MaxHeight = 280,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _menu
            }
        };
        host.Children.Add(_popup);
        Content = host;
        MouseLeftButtonUp += (_, e) => { Toggle(); e.Handled = true; };
        KeyDown += OnKey;
        SnapsToDevicePixels = true;
    }

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public object? SelectedValue { get => GetValue(SelectedValueProperty); set => SetValue(SelectedValueProperty, value); }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        _focus.BorderBrush = Palette.FocusRing;
        _shell.BorderBrush = Palette.Dormant;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        _focus.BorderBrush = Brushes.Transparent;
        _shell.BorderBrush = IsMouseOver ? Palette.Dormant : Palette.Divider;
    }

    private void OnKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter or Key.Down)
        {
            Toggle();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _popup.IsOpen)
        {
            _popup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void Toggle()
    {
        if (!IsEnabled) return;
        if (_popup.IsOpen) { _popup.IsOpen = false; return; }
        RebuildMenu();
        _popup.MinWidth = Math.Max(ActualWidth, 160);
        _popup.IsOpen = true;
    }

    private void RebuildMenu()
    {
        _menu.Children.Clear();
        if (ItemsSource is null) return;
        foreach (var item in ItemsSource)
        {
            var choice = Choice.Of(item);
            var on = Equals(choice.Value, SelectedValue);
            var button = new Button
            {
                Content = choice.Label,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 1, 0, 1),
                Background = on ? Palette.ActiveFill : Brushes.Transparent,
                Foreground = Palette.Primary,
                FontSize = SettingsTypo.BodySize,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FocusVisualStyle = null
            };
            button.Click += (_, _) =>
            {
                SelectedValue = choice.Value;
                _popup.IsOpen = false;
            };
            button.Template = RowTemplate();
            _menu.Children.Add(button);
        }
    }

    private void PaintCaption()
    {
        _caption.Text = CurrentLabel();
        AutomationProperties.SetName(this, _caption.Text);
    }

    /// <summary>
    /// The label for the stored value. A preferences file can hold a value outside the list — the
    /// refresh interval is clamped to a range, not snapped to these five — so fall back to the
    /// value itself rather than leaving the row blank.
    /// </summary>
    private string CurrentLabel()
    {
        if (ItemsSource is not null)
            foreach (var item in ItemsSource)
            {
                var choice = Choice.Of(item);
                if (Equals(choice.Value, SelectedValue)) return choice.Label;
            }
        return SelectedValue?.ToString() ?? "";
    }

    private static ControlTemplate RowTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Fill");
        border.SetValue(Border.CornerRadiusProperty, SettingsMetrics.Field);
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(content);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hovered = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hovered.Setters.Add(new Setter(Border.BackgroundProperty, Palette.HoverFill, "Fill"));
        template.Triggers.Add(hovered);
        return template;
    }
}

internal readonly record struct Choice(object? Value, string Label)
{
    public static Choice Of(object item) => item is IChoice choice
        ? new(choice.Value, choice.Label)
        : new(item, item.ToString() ?? "");
}
