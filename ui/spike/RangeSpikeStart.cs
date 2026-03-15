using Godot;

public partial class RangeSpikeStart : Control
{
    private const string MainMenuScenePath = "res://ui/main_menu.tscn";
    private const string DashboardScenePath = "res://ui/spike/range_spike_dashboard.tscn";

    public override void _Ready()
    {
        BuildUi();
    }

    private void BuildUi()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        var background = new ColorRect
        {
            Color = new Color("000000")
        };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var root = new MarginContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("margin_left", 56);
        root.AddThemeConstantOverride("margin_top", 48);
        root.AddThemeConstantOverride("margin_right", 56);
        root.AddThemeConstantOverride("margin_bottom", 48);
        AddChild(root);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 16);
        root.AddChild(column);

        Label kicker = new()
        {
            Text = "EXPERIMENTAL RANGE SPIKE"
        };
        kicker.AddThemeColorOverride("font_color", new Color("7dd3fc"));
        kicker.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(kicker);

        Label title = new()
        {
            Text = "Fresh UI sandbox with the current physics layer"
        };
        title.AddThemeColorOverride("font_color", Colors.White);
        title.AddThemeFontSizeOverride("font_size", 38);
        column.AddChild(title);

        Label body = new()
        {
            Text = "This path is isolated from the current CourseHud so range ideas can move quickly. The dashboard starts with a high-contrast layout, resizable tiles, window-size presets, club-colored shot sets, and projection panels for top-down and side-flight views."
        };
        body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddThemeColorOverride("font_color", new Color("a6b3c2"));
        body.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(body);

        PanelContainer featurePanel = new();
        featurePanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("090d12"),
            BorderColor = new Color("1f2b38"),
            BorderWidthBottom = 2,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10
        });
        column.AddChild(featurePanel);

        MarginContainer featureMargin = new();
        featureMargin.AddThemeConstantOverride("margin_left", 18);
        featureMargin.AddThemeConstantOverride("margin_top", 18);
        featureMargin.AddThemeConstantOverride("margin_right", 18);
        featureMargin.AddThemeConstantOverride("margin_bottom", 18);
        featurePanel.AddChild(featureMargin);

        VBoxContainer features = new();
        features.AddThemeConstantOverride("separation", 10);
        featureMargin.AddChild(features);

        foreach (string line in new[]
        {
            "Resizable tile grid for analysis panels",
            "Window size presets without FOV changes",
            "Top-down, side-flight, and landing distribution plots",
            "Shot-set overlays by club and equipment variant",
            "Stub mode lifecycle events for challenge prototypes"
        })
        {
            Label label = new() { Text = $"• {line}" };
            label.AddThemeColorOverride("font_color", new Color("dbe6f3"));
            label.AddThemeFontSizeOverride("font_size", 18);
            features.AddChild(label);
        }

        HBoxContainer actions = new();
        actions.AddThemeConstantOverride("separation", 12);
        column.AddChild(actions);

        Button openButton = new() { Text = "Open Sandbox", CustomMinimumSize = new Vector2(220.0f, 48.0f) };
        openButton.Pressed += () => GetTree().ChangeSceneToFile(DashboardScenePath);
        actions.AddChild(openButton);

        Button backButton = new() { Text = "Back to Menu", CustomMinimumSize = new Vector2(180.0f, 48.0f) };
        backButton.Pressed += () => GetTree().ChangeSceneToFile(MainMenuScenePath);
        actions.AddChild(backButton);
    }
}
