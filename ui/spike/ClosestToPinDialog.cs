using System;
using Godot;

/// <summary>
/// Modal dialog for configuring a Closest to the Pin session.
///
/// Emits ModeConfigured when the user confirms. Restores last-used values
/// on open and persists confirmed values to ClosestToPinPersistenceService.
/// </summary>
public partial class ClosestToPinDialog : Window
{
    [Signal]
    public delegate void ModeConfiguredEventHandler(string club, float targetYards, float winDistanceYards);

    private ClosestToPinSettings _settings;
    private OptionButton _clubOption;
    private SpinBox _targetSpinBox;
    private SpinBox _winDistSpinBox;
    private bool _targetOverridden;

    public override void _Ready()
    {
        _settings = ClosestToPinPersistenceService.Load();

        Title = "Closest to the Pin";
        Unresizable = false;
        Size = new Vector2I(420, 260);
        Exclusive = true;
        Transient = true;
        CloseRequested += Hide;

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 14);
        margin.AddChild(root);

        // Club picker
        var clubRow = new HBoxContainer();
        clubRow.AddThemeConstantOverride("h_separation", 10);
        var clubLabel = new Label { Text = "Club:", CustomMinimumSize = new Vector2(120, 0) };
        clubLabel.AddThemeColorOverride("font_color", new Color("90a0b2"));
        clubRow.AddChild(clubLabel);

        _clubOption = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (string club in RangeClubCatalog.Labels)
            _clubOption.AddItem(club);
        _clubOption.ItemSelected += OnClubSelected;
        clubRow.AddChild(_clubOption);
        root.AddChild(clubRow);

        // Target distance
        var targetRow = new HBoxContainer();
        targetRow.AddThemeConstantOverride("h_separation", 10);
        var targetLabel = new Label { Text = "Target (yd):", CustomMinimumSize = new Vector2(120, 0) };
        targetLabel.AddThemeColorOverride("font_color", new Color("90a0b2"));
        targetRow.AddChild(targetLabel);

        _targetSpinBox = new SpinBox
        {
            MinValue = 10,
            MaxValue = 400,
            Step = 1,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _targetSpinBox.ValueChanged += _ => _targetOverridden = true;
        targetRow.AddChild(_targetSpinBox);

        var resetBtn = new Button { Text = "Reset" };
        resetBtn.Pressed += OnResetTargetPressed;
        targetRow.AddChild(resetBtn);
        root.AddChild(targetRow);

        // Win distance
        var winRow = new HBoxContainer();
        winRow.AddThemeConstantOverride("h_separation", 10);
        var winLabel = new Label { Text = "Win Radius (yd):", CustomMinimumSize = new Vector2(120, 0) };
        winLabel.AddThemeColorOverride("font_color", new Color("90a0b2"));
        winRow.AddChild(winLabel);

        _winDistSpinBox = new SpinBox
        {
            MinValue = 1,
            MaxValue = 100,
            Step = 1,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        winRow.AddChild(_winDistSpinBox);
        root.AddChild(winRow);

        // Hint
        var hint = new Label
        {
            Text = "A shot is a hit when its 2D distance from the target is ≤ the win radius.\nSlices and hooks count against distance — straight shots score best.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        hint.AddThemeColorOverride("font_color", new Color("60748a"));
        hint.AddThemeFontSizeOverride("font_size", 11);
        root.AddChild(hint);

        // Buttons
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("h_separation", 12);
        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        buttons.AddChild(spacer);

        var cancelBtn = new Button { Text = "Cancel" };
        cancelBtn.Pressed += Hide;
        buttons.AddChild(cancelBtn);

        var okBtn = new Button { Text = "Start Session" };
        okBtn.Pressed += OnOkPressed;
        buttons.AddChild(okBtn);
        root.AddChild(buttons);
    }

    /// <summary>Opens the dialog, restoring last-used values.</summary>
    public void OpenDialog()
    {
        _settings = ClosestToPinPersistenceService.Load();
        _targetOverridden = _settings.LastTargetWasOverridden;

        // Select last club
        int clubIdx = 0;
        for (int i = 0; i < RangeClubCatalog.Labels.Count; i++)
        {
            if (RangeClubCatalog.Labels[i] == _settings.LastClub)
            {
                clubIdx = i;
                break;
            }
        }
        _clubOption.Select(clubIdx);

        _targetSpinBox.Value = _settings.LastTargetYards;
        _winDistSpinBox.Value = _settings.LastWinDistanceYards;

        PopupCentered();
    }

    private void OnClubSelected(long index)
    {
        if (_targetOverridden) return;
        string club = RangeClubCatalog.Labels[(int)index];
        _targetSpinBox.Value = _settings.GetAverage(club);
    }

    private void OnResetTargetPressed()
    {
        string club = RangeClubCatalog.Labels[_clubOption.Selected];
        _targetSpinBox.Value = _settings.GetAverage(club);
        _targetOverridden = false;
    }

    private void OnOkPressed()
    {
        string club = RangeClubCatalog.Labels[_clubOption.Selected];
        float targetYards = (float)_targetSpinBox.Value;
        float winYards = (float)_winDistSpinBox.Value;

        // Update per-club average if the user typed a different value
        if (_targetOverridden)
            _settings.ClubAverages[club] = targetYards;

        _settings.LastClub = club;
        _settings.LastTargetYards = targetYards;
        _settings.LastTargetWasOverridden = _targetOverridden;
        _settings.LastWinDistanceYards = winYards;
        ClosestToPinPersistenceService.Save(_settings);

        EmitSignal(SignalName.ModeConfigured, club, targetYards, winYards);
        Hide();
    }
}
