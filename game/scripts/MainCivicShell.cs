using Godot;
using Guild.Application;

namespace Guild.Game;

public partial class Main
{
    private static readonly Font CivicDisplayFont = new SystemFont
    {
        FontNames = ["Yu Mincho", "MS Mincho", "Noto Serif CJK JP", "serif"],
        FontWeight = 600, AllowSystemFallback = true
    };
    private Label? _civicTitle;
    private Button? _displayModeButton;
    private Vector2I _windowedSize;
    private Vector2I _windowedPosition;
    private Window.ModeEnum _windowedMode = Window.ModeEnum.Windowed;
    private bool _stampSubmitting;

    private static StyleBoxFlat CivicSurface(string background = "fffdf6", string border = "cfbf9b", int margin = 14)
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(background), BorderColor = new Color(border),
            ContentMarginLeft = margin, ContentMarginRight = margin,
            ContentMarginTop = margin, ContentMarginBottom = margin,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        };
        style.SetBorderWidthAll(1);
        return style;
    }

    private static Theme CreateCivicTheme()
    {
        var theme = new Theme
        {
            DefaultFont = new SystemFont { FontNames = ["Yu Gothic UI", "Meiryo", "Noto Sans CJK JP", "sans-serif"], AllowSystemFallback = true },
            DefaultFontSize = 24
        };
        theme.SetConstant("separation", "VBoxContainer", 10);
        theme.SetConstant("separation", "HBoxContainer", 12);
        foreach (var type in new[] { "Label", "Button", "CheckBox", "CheckButton", "OptionButton", "LineEdit", "PopupMenu" })
        {
            theme.SetColor("font_color", type, TextColor);
            theme.SetColor("font_hover_color", type, TextColor);
            theme.SetColor("font_pressed_color", type, TextColor);
            theme.SetColor("font_focus_color", type, TextColor);
            theme.SetColor("font_disabled_color", type, new Color("7b8078"));
        }
        var normal = CivicSurface("fffdf6", "c7bdab", 12);
        var hover = (StyleBoxFlat)normal.Duplicate(); hover.BgColor = new Color("e6efea"); hover.BorderColor = new Color("537a71");
        var pressed = (StyleBoxFlat)normal.Duplicate(); pressed.BgColor = new Color("d7e8e2"); pressed.BorderColor = new Color("2f6f73");
        var disabled = (StyleBoxFlat)normal.Duplicate(); disabled.BgColor = new Color("e9e8e2"); disabled.BorderColor = new Color("d2d2c9");
        var focus = CivicSurface("ffffff00", "2f6f73", 0); focus.SetBorderWidthAll(3);
        foreach (var type in new[] { "Button", "OptionButton", "CheckBox", "CheckButton" })
        {
            theme.SetStylebox("normal", type, normal); theme.SetStylebox("hover", type, hover);
            theme.SetStylebox("pressed", type, pressed); theme.SetStylebox("disabled", type, disabled);
            theme.SetStylebox("focus", type, focus);
        }
        theme.SetStylebox("normal", "LineEdit", normal); theme.SetStylebox("focus", "LineEdit", focus);
        theme.SetColor("font_placeholder_color", "LineEdit", MutedColor);
        theme.SetColor("caret_color", "LineEdit", TextColor);
        theme.SetStylebox("panel", "PanelContainer", CivicSurface());
        theme.SetStylebox("panel", "PopupMenu", CivicSurface());
        theme.SetStylebox("hover", "PopupMenu", hover);
        theme.SetColor("font_color_hover", "PopupMenu", TextColor);
        theme.SetStylebox("background", "ProgressBar", CivicSurface("e3e6d9", "d5d9cc", 0));
        theme.SetStylebox("fill", "ProgressBar", CivicSurface("65a27d", "65a27d", 0));
        foreach (var type in new[] { "VScrollBar", "HScrollBar" })
        {
            theme.SetStylebox("scroll", type, CivicSurface("ebece4", "ebece4", 0));
            theme.SetStylebox("grabber", type, CivicSurface("8fa598", "8fa598", 0));
            theme.SetStylebox("grabber_highlight", type, CivicSurface("597d70", "597d70", 0));
            theme.SetStylebox("grabber_pressed", type, CivicSurface("2f6f73", "2f6f73", 0));
        }
        return theme;
    }

    private void BuildCivicShell()
    {
        var background = new TextureRect
        {
            Texture = CivicAssets.Texture("paper-desk"), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore
        };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);
        var margin = new MarginContainer(); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 24);
        AddChild(margin);
        var shell = new VBoxContainer { Name = "CivicShell" }; shell.AddThemeConstantOverride("separation", 12); margin.AddChild(shell);
        var header = Row(shell); header.CustomMinimumSize = new Vector2(0, 86);
        var crest = new TextureRect
        {
            Texture = CivicAssets.Texture("guild-crest"), CustomMinimumSize = new Vector2(92, 86),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        header.AddChild(crest);
        var titles = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; titles.AddThemeConstantOverride("separation", 28); header.AddChild(titles);
        var guild = AddText(titles, DisplayText.Get("ui.shell.001")); guild.AddThemeFontSizeOverride("font_size", 36); guild.AddThemeFontOverride("font", CivicDisplayFont);
        _civicTitle = AddText(titles, DisplayText.Get("ui.shell.002")); _civicTitle.AddThemeFontSizeOverride("font_size", 40); _civicTitle.AddThemeFontOverride("font", CivicDisplayFont);
        var version = AddText(header, DisplayText.Get("ui.shell.003") + ProjectSettings.GetSetting("application/config/version").AsString());
        version.AddThemeFontSizeOverride("font_size", 18); version.Modulate = MutedColor;
        _displayModeButton = AddButton(header, DisplayText.Get("ui.shell.004"), ToggleDisplayMode); _displayModeButton.Name = "DisplayModeToggle";
        _displayModeButton.CustomMinimumSize = new Vector2(194, 44); _displayModeButton.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _summary = AddText(shell, DisplayText.Get("ui.shell.005"), true); _summary.AddThemeFontSizeOverride("font_size", 23);
        _status = AddText(shell, "", true); _status.Modulate = MutedColor; _status.AddThemeFontSizeOverride("font_size", 21);
        _body = new VBoxContainer { Name = "CivicBody", SizeFlagsVertical = SizeFlags.ExpandFill }; shell.AddChild(_body);
        _message = AddText(shell, DisplayText.Get("ui.shell.006"), true); _message.Modulate = new Color("286d60");
        _message.AddThemeFontSizeOverride("font_size", 21); _message.CustomMinimumSize = new Vector2(0, 28);
    }

    private void RefreshCivicShell()
    {
        if (_view is null) return;
        var preparation = _view.Screen == "Preparation";
        _summary.Visible = !preparation;
        _status.Visible = !preparation;
        if (_civicTitle is not null) _civicTitle.Text = _view.Screen switch
        {
            "Preparation" => DisplayText.Format("ui.screen.preparation", FeedbackMode(_view.Mode)),
            "Battle" => DisplayText.Format("ui.screen.battle", FeedbackMode(_view.Mode)),
            "Guild" => _choosingExpedition ? DisplayText.Get("ui.shell.007") : _activeTab == "dispatch" ? DisplayText.Get("economy.ui.dispatch_tab")
                : _activeTab == "deliveries" ? DisplayText.Get("economy.ui.delivery_tab") : DisplayText.Get("ui.shell.002"),
            "RankEvent" => DisplayText.Get("ui.shell.008"), "LevelChoice" => DisplayText.Get("ui.shell.009"),
            "Victory" or "Defeat" => DisplayText.Format("ui.screen.result", FeedbackMode(_view.Mode)), _ => DisplayText.Get("ui.shell.001")
        };
        if (_displayModeButton is not null) _displayModeButton.Text = IsFullscreen() ? DisplayText.Get("ui.shell.010") : DisplayText.Get("ui.shell.004");
    }

    private bool IsFullscreen() => GetWindow().Mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen;

    private void ToggleDisplayMode()
    {
        var window = GetWindow();
        if (IsFullscreen()) { ExitFullscreen(); return; }
        _windowedSize = window.Size; _windowedPosition = window.Position; _windowedMode = window.Mode;
        window.Mode = Window.ModeEnum.ExclusiveFullscreen;
        RefreshCivicShell();
    }

    private void ExitFullscreen()
    {
        if (!IsFullscreen()) return;
        var window = GetWindow();
        window.Mode = _windowedMode == Window.ModeEnum.Maximized ? Window.ModeEnum.Maximized : Window.ModeEnum.Windowed;
        if (_windowedMode != Window.ModeEnum.Maximized && _windowedSize.X > 0 && _windowedSize.Y > 0)
        {
            window.Size = _windowedSize; window.Position = _windowedPosition;
        }
        RefreshCivicShell();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.F11) { ToggleDisplayMode(); GetViewport().SetInputAsHandled(); }
        else if (key.Keycode == Key.Escape && IsFullscreen()) { ExitFullscreen(); GetViewport().SetInputAsHandled(); }
    }

    private void StampStartEncounter()
    {
        if (_stampSubmitting || _view?.Screen != "Preparation") return;
        _stampSubmitting = true;
        if (_startWaveButton is not null) _startWaveButton.Disabled = true;
        try
        {
            Dispatch(new("StartWave"));
            if (_view?.Screen != "Battle") return;
            var seal = new Label { Text = DisplayText.Get("ui.preparation.029"), Modulate = new Color("b43b2f"), MouseFilter = MouseFilterEnum.Ignore, Rotation = -.06f };
            seal.AddThemeFontSizeOverride("font_size", 42);
            seal.Position = new Vector2(Math.Max(0, Size.X - 320), 120); AddChild(seal);
            var tween = CreateTween(); tween.TweenProperty(seal, "modulate:a", 0f, .45).SetDelay(.12);
            tween.TweenCallback(Callable.From(seal.QueueFree));
        }
        finally { _stampSubmitting = false; }
    }
}
