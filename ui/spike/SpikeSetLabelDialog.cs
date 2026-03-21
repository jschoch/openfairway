using System;
using Godot;

/// <summary>
/// Minimal popup for renaming / tagging a shot set.
/// Emits LabelConfirmed(tag) when the user accepts.
/// </summary>
public partial class SpikeSetLabelDialog : Window
{
    [Signal]
    public delegate void LabelConfirmedEventHandler(string tag);

    private LineEdit _lineEdit;

    public override void _Ready()
    {
        Title = "Label Shot Set";
        Unresizable = true;
        Size = new Vector2I(340, 110);
        Exclusive = true;
        Transient = true;
        CloseRequested += Hide;

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);

        _lineEdit = new LineEdit
        {
            PlaceholderText = "e.g. 7I stock, draw shot …",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _lineEdit.TextSubmitted += _ => Confirm();
        root.AddChild(_lineEdit);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("h_separation", 10);
        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        buttons.AddChild(spacer);

        var clearBtn = new Button { Text = "Clear" };
        clearBtn.Pressed += () => { _lineEdit.Text = string.Empty; Confirm(); };
        buttons.AddChild(clearBtn);

        var okBtn = new Button { Text = "Apply" };
        okBtn.Pressed += Confirm;
        buttons.AddChild(okBtn);
        root.AddChild(buttons);
    }

    public void OpenFor(string currentLabel)
    {
        _lineEdit.Text = currentLabel;
        _lineEdit.SelectAll();
        PopupCentered();
        _lineEdit.GrabFocus();
    }

    private void Confirm()
    {
        EmitSignal(SignalName.LabelConfirmed, _lineEdit.Text.Trim());
        Hide();
    }
}
