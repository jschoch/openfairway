using System;
using System.Collections.Generic;
using Godot;

public partial class SpikeTileVisibilityDialog : Window
{
    [Signal] public delegate void VisibilityChangedEventHandler(string[] visibleIds);

    private readonly List<(string id, string title, CheckBox cb)> _rows = new();

    public override void _Ready() => CloseRequested += Hide;

    public void Populate(IEnumerable<(string id, string title)> tiles, ISet<string> visibleIds)
    {
        // Clear existing children except required Window children
        foreach (Node child in GetChildren()) child.QueueFree();
        _rows.Clear();

        Title = "Tile Visibility";
        Unresizable = false;
        Size = new Vector2I(320, 400);

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

        foreach (var (id, title) in tiles)
        {
            var cb = new CheckBox { Text = title, ButtonPressed = visibleIds.Contains(id) };
            string capturedId = id;
            cb.Toggled += (_) => EmitVisibility();
            _rows.Add((id, title, cb));
            vbox.AddChild(cb);
        }

        var apply = new Button { Text = "Apply" };
        apply.Pressed += () => { EmitVisibility(); Hide(); };
        vbox.AddChild(apply);
    }

    private void EmitVisibility()
    {
        var ids = new List<string>();
        foreach (var (id, _, cb) in _rows)
            if (cb.ButtonPressed) ids.Add(id);
        EmitSignal(SignalName.VisibilityChanged, ids.ToArray());
    }
}
