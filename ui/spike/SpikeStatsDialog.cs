using System.Collections.Generic;
using Godot;

// Stat picker dialog — matches the look/feel of SpikeTileVisibilityDialog.
// Lets the user choose which metrics appear in the Engine Stats tile.
public partial class SpikeStatsDialog : Window
{
    [Signal] public delegate void StatsChangedEventHandler(string[] enabledStatIds);

    // All available stats in display order.
    public static readonly (string id, string label)[] AllStats =
    {
        ("carry",    "Carry Distance"),
        ("height",   "Peak Height"),
        ("offline",  "Offline"),
        ("speed",    "Ball Speed"),
        ("smash",    "Smash Factor"),
        ("vla",      "VLA (Launch Angle)"),
        ("hla",      "HLA (Direction)"),
        ("backspin", "Backspin"),
        ("sidespin", "Sidespin"),
    };

    public static readonly string[] DefaultEnabled = { "carry", "height", "offline", "speed", "smash", "vla", "backspin" };

    private readonly List<(string id, CheckBox cb)> _rows = new();

    public override void _Ready() => CloseRequested += Hide;

    public void Populate(ISet<string> enabledIds)
    {
        foreach (Node child in GetChildren()) child.QueueFree();
        _rows.Clear();

        Title = "Stats";
        Unresizable = false;
        Size = new Vector2I(300, 380);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        margin.AddChild(vbox);

        foreach (var (id, label) in AllStats)
        {
            var cb = new CheckBox { Text = label, ButtonPressed = enabledIds.Contains(id) };
            cb.Toggled += (_) => EmitStats();
            _rows.Add((id, cb));
            vbox.AddChild(cb);
        }

        var apply = new Button { Text = "Apply" };
        apply.Pressed += () => { EmitStats(); Hide(); };
        vbox.AddChild(apply);
    }

    private void EmitStats()
    {
        var ids = new List<string>();
        foreach (var (id, cb) in _rows)
            if (cb.ButtonPressed) ids.Add(id);
        EmitSignal(SignalName.StatsChanged, ids.ToArray());
    }
}
