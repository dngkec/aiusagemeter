using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AIUsageMeter.Windows.Design;
using Border = System.Windows.Controls.Border;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace AIUsageMeter.Windows.Settings;

/// <summary>36×20 switch. Not a CheckBox — the spec forbids native checkbox chrome.</summary>
internal sealed class SettingsToggle : UserControl
{
    public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(
        nameof(IsChecked), typeof(bool), typeof(SettingsToggle),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnChecked));

    private readonly Border _track;
    private readonly Border _focus;
    private readonly Ellipse _thumb;

    public SettingsToggle()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        Width = 36;
        Height = 20;
        SnapsToDevicePixels = true;
        FocusVisualStyle = null;
        _thumb = new Ellipse { Width = 16, Height = 16, Fill = Brushes.White, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(2, 0, 2, 0) };
        _track = new Border
        {
            Width = 36,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Child = _thumb
        };
        // The ring sits outside the track on a negative margin. Growing the track's own border
        // instead would shrink its content box and make the thumb jump on focus.
        _focus = new Border
        {
            Margin = new Thickness(-3),
            CornerRadius = new CornerRadius(13),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            IsHitTestVisible = false
        };
        Content = new Grid { Children = { _track, _focus } };
        Paint();
        MouseLeftButtonUp += (_, e) => { Toggle(); e.Handled = true; };
    }

    public bool IsChecked { get => (bool)GetValue(IsCheckedProperty); set => SetValue(IsCheckedProperty, value); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter)
        {
            Toggle();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        _focus.BorderBrush = Palette.FocusRing;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        _focus.BorderBrush = Brushes.Transparent;
    }

    /// <summary>Reports on/off to a screen reader, which a bare UserControl does not.</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new TogglePeer(this);

    internal void Toggle()
    {
        if (!IsEnabled) return;
        IsChecked = !IsChecked;
    }

    private void Paint()
    {
        _track.Background = IsChecked ? Palette.ToggleOn : Palette.Dormant;
        _thumb.HorizontalAlignment = IsChecked ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left;
        Opacity = IsEnabled ? 1 : 0.4;
    }

    private static void OnChecked(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var toggle = (SettingsToggle)d;
        toggle.Paint();
        if (UIElementAutomationPeer.FromElement(toggle) is TogglePeer peer)
            peer.RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty, State((bool)e.OldValue), State((bool)e.NewValue));
    }

    internal static ToggleState State(bool on) => on ? ToggleState.On : ToggleState.Off;

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty) Paint();
    }

    private sealed class TogglePeer(SettingsToggle owner) : FrameworkElementAutomationPeer(owner), IToggleProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override string GetClassNameCore() => nameof(SettingsToggle);
        protected override bool IsControlElementCore() => true;
        public override object GetPattern(PatternInterface pattern)
            => pattern == PatternInterface.Toggle ? this : base.GetPattern(pattern);

        public ToggleState ToggleState => State(owner.IsChecked);
        public void Toggle() => owner.Toggle();
    }
}
