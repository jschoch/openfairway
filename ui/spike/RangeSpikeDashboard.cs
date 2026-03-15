using System.Collections.Generic;
using Godot;

public partial class RangeSpikeDashboard : Control
{
    private const string MainMenuScenePath = "res://ui/main_menu.tscn";
    private static readonly Vector2I LandscapeContentBase = new(1728, 972);
    private static readonly Vector2I PortraitContentBase = new(1080, 1728);

    private readonly RangeSpikeTrajectorySimulator _simulator = new();
    private readonly List<RangeSpikeShotSet> _shotSets = new();
    private LibgolfBridgeClient _libgolfBridge;
    private OptionButton _engineOption;
    private readonly Dictionary<int, Vector2I> _windowPresets = new()
    {
        { 0, new Vector2I(1280, 720) },
        { 1, new Vector2I(1600, 900) },
        { 2, new Vector2I(1728, 972) },
        { 3, new Vector2I(1920, 1080) },
        { 4, new Vector2I(720, 1280) },
        { 5, new Vector2I(900, 1600) },
        { 6, new Vector2I(972, 1728) },
        { 7, new Vector2I(1080, 1920) },
    };

    private SpikeTileCanvas _tileCanvas;
    private OptionButton _presetOption;
    private SpinBox _shotCountSpinBox;
    private OptionButton _windowPresetOption;
    private CheckBox _capWindowSizeCheckBox;
    private OptionButton _modeOption;
    private HFlowContainer _controlsFlow;
    private Label _statusLabel;
    private Label _windowInfoLabel;
    private RichTextLabel _modeLog;
    private RichTextLabel _setSummary;
    private VBoxContainer _viewportTileContent;
    private Spike3DViewportTile _viewport3D;
    private SpikePlotPanel _topDownPlot;
    private SpikePlotPanel _sidePlot;
    private SpikePlotPanel _distributionPlot;
    private SpikeStatPanel _statPanel;
    private bool _isPortraitLayout;
    private int _liveShotCounter;
    private SpikeTileVisibilityDialog _visibilityDialog;
    private readonly HashSet<string> _visibleTileIds = new();
    private List<SpikeTileSpec> _allTileSpecs = new();

    public override void _Ready()
    {
        string projectRoot = ProjectSettings.GlobalizePath("res://");
        _libgolfBridge = new LibgolfBridgeClient(projectRoot);

        BuildUi();
        BuildTileContent();
        ApplyResponsiveLayout(force: true);
        RefreshPlots();
        AppendModeEvent("Spike dashboard ready.");
        if (_libgolfBridge.IsAvailable())
            AppendModeEvent("libgolf bridge ready. Set Engine → libgolf or Both, then Add Shot Set to compare.");
        else
            AppendModeEvent("libgolf bridge not built. Run tools/libgolf-bridge/build.sh to enable it.");
        ConnectTcpServer();
        UpdateWindowInfo();
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

        var rootMargin = new MarginContainer();
        rootMargin.SetAnchorsPreset(LayoutPreset.FullRect);
        rootMargin.AddThemeConstantOverride("margin_left", 18);
        rootMargin.AddThemeConstantOverride("margin_top", 18);
        rootMargin.AddThemeConstantOverride("margin_right", 18);
        rootMargin.AddThemeConstantOverride("margin_bottom", 18);
        AddChild(rootMargin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 14);
        rootMargin.AddChild(root);

        root.AddChild(BuildToolbar());

        _tileCanvas = new SpikeTileCanvas
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0.0f, 620.0f)
        };
        _tileCanvas.StatusChanged += OnTileStatusChanged;
        _tileCanvas.TileDeleteRequested += OnTileDeleteRequested;
        root.AddChild(_tileCanvas);

        _statusLabel = new Label
        {
            Text = "Experimental spike. Physics is reusable; the UI is intentionally separate from the current HUD."
        };
        _statusLabel.AddThemeColorOverride("font_color", new Color("bcc7d3"));
        root.AddChild(_statusLabel);

        _visibilityDialog = new SpikeTileVisibilityDialog();
        _visibilityDialog.VisibilityChanged += OnTileVisibilityChanged;
        _visibilityDialog.Visible = false;
        AddChild(_visibilityDialog);
    }

    private Control BuildToolbar()
    {
        var toolbarPanel = new PanelContainer();
        toolbarPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("06080b"),
            BorderColor = new Color("1d2935"),
            BorderWidthBottom = 2,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10
        });

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        toolbarPanel.AddChild(margin);

        var toolbar = new VBoxContainer();
        toolbar.AddThemeConstantOverride("separation", 8);
        margin.AddChild(toolbar);

        var header = new HBoxContainer();
        toolbar.AddChild(header);

        var title = new Label
        {
            Text = "Range Spike Sandbox",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        title.AddThemeColorOverride("font_color", new Color("f7fbff"));
        title.AddThemeFontSizeOverride("font_size", 28);
        header.AddChild(title);

        _windowInfoLabel = new Label();
        _windowInfoLabel.AddThemeColorOverride("font_color", new Color("90a0b2"));
        header.AddChild(_windowInfoLabel);

        var backButton = new Button { Text = "Back to Menu" };
        backButton.Pressed += () => GetTree().ChangeSceneToFile(MainMenuScenePath);
        header.AddChild(backButton);

        _controlsFlow = new HFlowContainer();
        _controlsFlow.AddThemeConstantOverride("h_separation", 10);
        _controlsFlow.AddThemeConstantOverride("v_separation", 10);
        toolbar.AddChild(_controlsFlow);

        _presetOption = new OptionButton();
        foreach (RangeSpikeShotPreset preset in RangeSpikeShotCatalog.Presets)
            _presetOption.AddItem(preset.DisplayName);
        _controlsFlow.AddChild(_presetOption);

        _shotCountSpinBox = new SpinBox
        {
            MinValue = 3,
            MaxValue = 20,
            Step = 1,
            Value = 7,
            CustomMinimumSize = new Vector2(72.0f, 0.0f)
        };
        _controlsFlow.AddChild(_shotCountSpinBox);

        Button addSetButton = new() { Text = "Add Shot Set" };
        addSetButton.Pressed += OnAddShotSetPressed;
        _controlsFlow.AddChild(addSetButton);

        Button clearSetsButton = new() { Text = "Clear Sets" };
        clearSetsButton.Pressed += OnClearSetsPressed;
        _controlsFlow.AddChild(clearSetsButton);

        var engineLabel = new Label { Text = "Engine:" };
        engineLabel.AddThemeColorOverride("font_color", new Color("90a0b2"));
        _controlsFlow.AddChild(engineLabel);

        _engineOption = new OptionButton();
        _engineOption.AddItem("OpenFairway", 0);
        _engineOption.AddItem("libgolf (ref)", 1);
        _engineOption.AddItem("Both", 2);
        _engineOption.Select(0);
        _controlsFlow.AddChild(_engineOption);

        Button tilesButton = new() { Text = "Tiles" };
        tilesButton.Pressed += () =>
        {
            var pairs = new List<(string id, string title)>();
            foreach (SpikeTileSpec spec in _allTileSpecs)
                pairs.Add((spec.Id, spec.Title));
            _visibilityDialog.Populate(pairs, _visibleTileIds);
            _visibilityDialog.PopupCentered();
        };
        _controlsFlow.AddChild(tilesButton);

        _windowPresetOption = new OptionButton();
        _windowPresetOption.AddItem("1280 x 720", 0);
        _windowPresetOption.AddItem("1600 x 900", 1);
        _windowPresetOption.AddItem("1728 x 972", 2);
        _windowPresetOption.AddItem("1920 x 1080", 3);
        _windowPresetOption.AddItem("720 x 1280", 4);
        _windowPresetOption.AddItem("900 x 1600", 5);
        _windowPresetOption.AddItem("972 x 1728", 6);
        _windowPresetOption.AddItem("1080 x 1920", 7);
        _windowPresetOption.Select(2);
        _windowPresetOption.ItemSelected += OnWindowPresetSelected;
        _controlsFlow.AddChild(_windowPresetOption);

        _capWindowSizeCheckBox = new CheckBox
        {
            Text = "Cap Max To Preset",
            ButtonPressed = true
        };
        _capWindowSizeCheckBox.Toggled += OnCapWindowSizeToggled;
        _controlsFlow.AddChild(_capWindowSizeCheckBox);

        _modeOption = new OptionButton();
        _modeOption.AddItem("Proximity: 5 shots within N feet");
        _modeOption.AddItem("Shape: cut 5-15 yards");
        _modeOption.AddItem("Shape: slice 30+ yards");
        _controlsFlow.AddChild(_modeOption);

        Button startModeButton = new() { Text = "Start Mode" };
        startModeButton.Pressed += () => AppendModeEvent($"Mode started: {_modeOption.GetItemText(_modeOption.Selected)}");
        _controlsFlow.AddChild(startModeButton);

        Button simulateModeButton = new() { Text = "Sim Event" };
        simulateModeButton.Pressed += () => AppendModeEvent("Simulation event raised for current mode.");
        _controlsFlow.AddChild(simulateModeButton);

        Button endModeButton = new() { Text = "End Mode" };
        endModeButton.Pressed += () => AppendModeEvent("Mode ended.");
        _controlsFlow.AddChild(endModeButton);

        return toolbarPanel;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            ApplyResponsiveLayout(force: false);
    }

    // ── TCP server integration ─────────────────────────────────────────────────

    // Palette colour for TCP live traces (replaced by red/green on most-recent shot).
    private static readonly Color TcpTraceColor = new(0.65f, 0.85f, 1.0f, 0.9f); // light blue

    private void ConnectTcpServer()
    {
        var tcp = GetNodeOrNull<TcpServer>("/root/TcpServerService");
        if (tcp == null)
        {
            AppendModeEvent("TcpServer autoload not found — TCP shots disabled.");
            return;
        }
        tcp.HitBall += OnTcpHitBall;
        tcp.ConnectionStatusChanged += OnTcpConnectionStatusChanged;
        AppendModeEvent("TCP server connected. Shots from launch monitor will accumulate in TCP Live set.");
    }

    private void OnTcpHitBall(Godot.Collections.Dictionary data)
    {
        var (speed, vla, hla, backspin, sidespin) = _simulator.ExtractTcpParams(data);
        int engineId = _engineOption.GetItemId(_engineOption.Selected);
        _liveShotCounter++;

        if (engineId == 0 || engineId == 2)
        {
            var set = GetOrCreateNamedLiveSet("TCP Live", preset: null);
            RecolorLastTrace(set, isLibgolf: false);

            var trace = _simulator.GenerateShotTraceFromParams(
                set.Label, set.Traces.Count + 1, "LM", TcpTraceColor,
                speed, vla, hla, backspin, sidespin);
            trace.DisplayColor = HitShotColorOF;
            set.Traces.Add(trace);
        }

        if (engineId == 1 || engineId == 2)
        {
            if (_libgolfBridge.IsAvailable())
            {
                var set = GetOrCreateNamedLiveSet("libgolf — TCP Live", preset: null);
                RecolorLastTrace(set, isLibgolf: true);

                var pts = _libgolfBridge.RunSimulation(speed, vla, hla, backspin, sidespin);
                if (pts != null && pts.Count >= 2)
                {
                    var refColor = new Color(1.0f, 0.88f, 0.25f, 0.95f);
                    var trace = new RangeSpikeShotTrace(
                        set.Label, $"LM #{set.Traces.Count + 1}", "LM", refColor, pts);
                    trace.DisplayColor = HitShotColorLibgolf;
                    set.Traces.Add(trace);
                }
            }
        }

        AppendModeEvent($"TCP shot #{_liveShotCounter}: {speed:F0} mph, VLA {vla:F1}°, HLA {hla:F1}°, BS {backspin:F0}, SS {sidespin:F0} rpm.");
        RefreshPlots();
    }

    private void OnTcpConnectionStatusChanged(bool connected, string deviceId)
    {
        string msg = connected
            ? $"Launch monitor connected: {deviceId}"
            : "Launch monitor disconnected.";
        AppendModeEvent(msg);
        _statusLabel.Text = msg;
    }

    private void BuildTileContent()
    {
        _viewportTileContent = new VBoxContainer();
        _viewportTileContent.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _viewportTileContent.SizeFlagsVertical = SizeFlags.ExpandFill;
        _viewportTileContent.AddThemeConstantOverride("separation", 8);
        _viewport3D = new Spike3DViewportTile();
        _viewport3D.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _viewport3D.SizeFlagsVertical = SizeFlags.ExpandFill;
        _viewportTileContent.AddChild(_viewport3D);

        Button hitShotButton = new() { Text = "Hit Shot", CustomMinimumSize = new Vector2(0.0f, 36.0f) };
        hitShotButton.Pressed += OnViewportHitShotPressed;
        _viewportTileContent.AddChild(hitShotButton);

        _topDownPlot = new SpikePlotPanel { Mode = SpikePlotPanel.PlotMode.TopDown };
        _sidePlot = new SpikePlotPanel { Mode = SpikePlotPanel.PlotMode.Side };
        _distributionPlot = new SpikePlotPanel { Mode = SpikePlotPanel.PlotMode.Distribution };
        _statPanel = new SpikeStatPanel
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _modeLog = BuildRichText("Mode lifecycle events will show up here.");
        _setSummary = BuildRichText("No shot sets yet.");

        var summaryWrapper = new VBoxContainer();
        summaryWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        summaryWrapper.SizeFlagsVertical = SizeFlags.ExpandFill;
        summaryWrapper.AddThemeConstantOverride("separation", 8);
        summaryWrapper.AddChild(BuildSectionLabel("Shot Sets"));
        summaryWrapper.AddChild(_setSummary);

        var logWrapper = new VBoxContainer();
        logWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        logWrapper.SizeFlagsVertical = SizeFlags.ExpandFill;
        logWrapper.AddThemeConstantOverride("separation", 8);
        logWrapper.AddChild(BuildSectionLabel("Mode Events"));
        logWrapper.AddChild(_modeLog);
        var contents = new Dictionary<string, Control>
        {
            { "viewport_3d", _viewportTileContent },
            { "top_down", _topDownPlot },
            { "side_view", _sidePlot },
            { "distribution", _distributionPlot },
            { "stats", _statPanel },
            { "shot_sets", summaryWrapper },
            { "mode_events", logWrapper },
        };
        _tileCanvas.ConfigureTiles(new List<SpikeTileSpec>(), contents);
    }

    private void OnAddShotSetPressed()
    {
        RangeSpikeShotPreset preset = RangeSpikeShotCatalog.Presets[_presetOption.Selected];
        int shotCount = Mathf.RoundToInt((float)_shotCountSpinBox.Value);
        int engineId = _engineOption.GetItemId(_engineOption.Selected);

        // OpenFairway randomised set
        if (engineId == 0 || engineId == 2)
        {
            RangeSpikeShotSet set = _simulator.GenerateShotSet(
                preset, shotCount, seed: _shotSets.Count * 101 + preset.Id.GetHashCode());
            _shotSets.Add(set);
            AppendModeEvent($"OpenFairway: {shotCount} shots for {preset.DisplayName}.");
        }

        // libgolf single nominal reference trace
        if (engineId == 1 || engineId == 2)
        {
            RangeSpikeShotSet refSet = GenerateLibgolfSet(preset);
            if (refSet != null)
                _shotSets.Add(refSet);
        }

        RefreshPlots();
    }

    private void OnClearSetsPressed()
    {
        _shotSets.Clear();
        AppendModeEvent("Cleared all shot sets.");
        RefreshPlots();
    }

    // Runs the libgolf bridge for the preset's nominal launch params and returns
    // a single-trace shot set, or null on failure.
    private RangeSpikeShotSet GenerateLibgolfSet(RangeSpikeShotPreset preset)
    {
        if (!_libgolfBridge.IsAvailable())
        {
            _statusLabel.Text = "libgolf bridge not built — run tools/libgolf-bridge/build.sh.";
            return null;
        }

        List<Vector3> points = _libgolfBridge.RunSimulation(
            preset.SpeedMph,
            preset.LaunchAngleDeg,
            preset.LaunchDirectionDeg,
            preset.BackspinRpm,
            preset.SidespinRpm);

        if (points == null || points.Count < 2)
        {
            _statusLabel.Text = "libgolf simulation failed — see Godot output log.";
            return null;
        }

        // Bright gold — distinct from all OpenFairway set colours.
        var refColor = new Color(1.0f, 0.88f, 0.25f, 0.95f);
        string label = $"libgolf — {preset.DisplayName}";
        var trace = new RangeSpikeShotTrace(label, "nominal", preset.ClubLabel, refColor, points);
        var set = new RangeSpikeShotSet(label, preset, new List<RangeSpikeShotTrace> { trace });

        float carryYd = trace.LandingPoint.X / 0.9144f;
        float offYd = trace.LandingPoint.Z / 0.9144f;
        string offStr = Mathf.Abs(offYd) < 0.3f ? "on line" : $"{Mathf.Abs(offYd):F1} yd {(offYd < 0 ? "L" : "R")}";
        AppendModeEvent($"libgolf: {preset.DisplayName} — {carryYd:F0} yd carry, {offStr}.");

        return set;
    }

    private void RefreshPlots()
    {
        _viewport3D.SetShotSets(_shotSets);
        _topDownPlot.SetShotSets(_shotSets);
        _sidePlot.SetShotSets(_shotSets);
        _distributionPlot.SetShotSets(_shotSets);
        _statPanel.SetShotSets(_shotSets);
        _setSummary.Text = _simulator.BuildSummary(_shotSets);
    }

    private static readonly Color HitShotColorOF      = new(0.95f, 0.28f, 0.28f, 1.0f); // red
    private static readonly Color HitShotColorLibgolf = new(0.28f, 0.85f, 0.48f, 1.0f); // green

    private void OnViewportHitShotPressed()
    {
        RangeSpikeShotPreset preset = RangeSpikeShotCatalog.Presets[_presetOption.Selected];
        int engineId = _engineOption.GetItemId(_engineOption.Selected);
        _liveShotCounter++;
        int seed = preset.Id.GetHashCode() + _liveShotCounter * 37;

        // Randomise once; share params across both engines so the comparison is apples-to-apples.
        var (speed, vla, hla, backspin, sidespin) = _simulator.RandomizeParams(preset, seed);

        if (engineId == 0 || engineId == 2)
        {
            RangeSpikeShotSet liveSet = GetOrCreateLiveSet(preset, libgolf: false);
            RecolorLastTrace(liveSet, isLibgolf: false);

            RangeSpikeShotTrace trace = _simulator.GenerateShotTraceFromParams(
                liveSet.Label, liveSet.Traces.Count + 1, preset.ClubLabel, preset.Color,
                speed, vla, hla, backspin, sidespin);
            trace.DisplayColor = HitShotColorOF;
            liveSet.Traces.Add(trace);
        }

        if (engineId == 1 || engineId == 2)
        {
            if (!_libgolfBridge.IsAvailable())
            {
                AppendModeEvent("libgolf bridge not built — skipping reference trace.");
            }
            else
            {
                RangeSpikeShotSet libgolfSet = GetOrCreateLiveSet(preset, libgolf: true);
                RecolorLastTrace(libgolfSet, isLibgolf: true);

                List<Vector3> pts = _libgolfBridge.RunSimulation(speed, vla, hla, backspin, sidespin);
                if (pts != null && pts.Count >= 2)
                {
                    var refColor = new Color(1.0f, 0.88f, 0.25f, 0.95f);
                    string label = $"libgolf — {preset.DisplayName} Live";
                    var trace = new RangeSpikeShotTrace(label, $"{preset.ClubLabel} #{libgolfSet.Traces.Count + 1}",
                        preset.ClubLabel, refColor, pts);
                    trace.DisplayColor = HitShotColorLibgolf;
                    libgolfSet.Traces.Add(trace);
                }
                else
                {
                    AppendModeEvent("libgolf hit-shot failed — see output log.");
                }
            }
        }

        AppendModeEvent($"Hit shot #{_liveShotCounter}: {preset.DisplayName} ({_engineOption.GetItemText(_engineOption.Selected)}).");
        RefreshPlots();
    }

    private void OnWindowPresetSelected(long index)
    {
        int presetId = _windowPresetOption.GetItemId((int)index);
        if (_windowPresets.TryGetValue(presetId, out Vector2I size))
        {
            ApplyWindowPreset(size);
        }
    }

    private void UpdateWindowInfo()
    {
        Vector2I size = DisplayServer.WindowGetSize();
        Vector2I maxSize = DisplayServer.WindowGetMaxSize();
        string maxText = maxSize.X > 0 && maxSize.Y > 0 ? $"{maxSize.X} x {maxSize.Y}" : "unbounded";
        _windowInfoLabel.Text = $"Window {size.X} x {size.Y}  Max {maxText}";
    }

    private void OnTileStatusChanged(string message)
    {
        _statusLabel.Text = message;
    }

    private void AppendModeEvent(string text)
    {
        if (_modeLog == null)
            return;

        _modeLog.Text = $"{Time.GetDatetimeStringFromSystem()}  {text}\n{_modeLog.Text}".Trim();
    }

    private void OnCapWindowSizeToggled(bool pressed)
    {
        int selectedIndex = _windowPresetOption.Selected;
        int presetId = selectedIndex >= 0 ? _windowPresetOption.GetItemId(selectedIndex) : -1;
        if (_windowPresets.TryGetValue(presetId, out Vector2I size))
            ApplyWindowConstraints(size, pressed);
    }

    private void ApplyWindowPreset(Vector2I size)
    {
        Vector2I fittedSize = FitWindowToUsableScreen(size);
        ApplyContentBase(fittedSize);
        ApplyWindowConstraints(fittedSize, _capWindowSizeCheckBox?.ButtonPressed ?? false);
        DisplayServer.WindowSetSize(fittedSize);
        CenterWindowOnCurrentScreen(fittedSize);
        UpdateWindowInfo();

        string orientation = fittedSize.Y > fittedSize.X ? "portrait" : "landscape";
        string capMode = _capWindowSizeCheckBox?.ButtonPressed ?? false
            ? "You can shrink the window smaller, but not grow it beyond this preset."
            : "The window can still be resized larger or smaller unless the window manager blocks it.";
        string fitNote = fittedSize == size
            ? string.Empty
            : $" Requested {size.X} x {size.Y}, fitted to usable screen as {fittedSize.X} x {fittedSize.Y}.";
        _statusLabel.Text = $"Window set to {fittedSize.X} x {fittedSize.Y} ({orientation}). FOV remains unchanged.{fitNote} {capMode}";
    }

    private void ApplyResponsiveLayout(bool force)
    {
        bool shouldUsePortraitLayout = Size.Y > Size.X;
        if (!force && shouldUsePortraitLayout == _isPortraitLayout)
            return;

        _isPortraitLayout = shouldUsePortraitLayout;
        _tileCanvas.ConfigureGrid(shouldUsePortraitLayout ? 8 : 4, shouldUsePortraitLayout ? 10 : 6);

        _allTileSpecs = BuildTileSpecs(shouldUsePortraitLayout);

        // Initialize visible set only on first call; preserve visibility on subsequent layout changes.
        if (_visibleTileIds.Count == 0)
        {
            foreach (SpikeTileSpec spec in _allTileSpecs)
                _visibleTileIds.Add(spec.Id);
        }
        else
        {
            // Ensure any newly added tiles (from a different orientation) are visible by default.
            foreach (SpikeTileSpec spec in _allTileSpecs)
                _visibleTileIds.Add(spec.Id);
        }

        RefreshTileCanvas();

        _statusLabel.Text = shouldUsePortraitLayout
            ? "Portrait layout active. Tiles are stacked for a tall analysis workflow."
            : "Landscape layout active. Tiles are arranged side-by-side for comparison.";
    }

    private void RefreshTileCanvas()
    {
        var filteredSpecs = new List<SpikeTileSpec>();
        foreach (SpikeTileSpec spec in _allTileSpecs)
        {
            if (_visibleTileIds.Contains(spec.Id))
                filteredSpecs.Add(spec);
        }

        _tileCanvas.ConfigureTiles(filteredSpecs, BuildTileContentMap());
    }

    private void OnTileVisibilityChanged(string[] visibleIds)
    {
        _visibleTileIds.Clear();
        foreach (string id in visibleIds)
            _visibleTileIds.Add(id);
        RefreshTileCanvas();
    }

    private void OnTileDeleteRequested(string tileId)
    {
        _visibleTileIds.Remove(tileId);
        _tileCanvas.RemoveTile(tileId);
        _statusLabel.Text = $"Tile '{tileId}' removed. Use the Tiles button to restore it.";
    }

    private void ApplyContentBase(Vector2I fittedWindowSize)
    {
        Window rootWindow = GetWindow();
        if (rootWindow == null)
            return;

        bool portrait = fittedWindowSize.Y > fittedWindowSize.X;
        rootWindow.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        rootWindow.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
        rootWindow.ContentScaleSize = portrait ? PortraitContentBase : LandscapeContentBase;
    }

    private List<SpikeTileSpec> BuildTileSpecs(bool portrait)
    {
        if (!portrait)
        {
            // Landscape: 4 cols × 6 rows
            return new List<SpikeTileSpec>
            {
                new("viewport_3d",   "3D Shot View",      0, 0, 2, 2),
                new("top_down",      "Top-Down Flight",   2, 0, 2, 2),
                new("side_view",     "Side Flight",       0, 2, 2, 2),
                new("distribution",  "Shot Distribution", 2, 2, 2, 2),
                new("stats",         "Engine Stats",      0, 4, 4, 2),
                new("shot_sets",     "Shot Sets",         0, 6, 2, 1), // hidden by default — row 6 is beyond visible grid without resize
                new("mode_events",   "Mode Events",       2, 6, 2, 1),
            };
        }

        // Portrait: 8 cols × 10 rows
        return new List<SpikeTileSpec>
        {
            new("viewport_3d",   "3D Shot View",      0, 0, 8, 2),
            new("top_down",      "Top-Down Flight",   0, 2, 4, 2),
            new("side_view",     "Side Flight",       4, 2, 4, 2),
            new("distribution",  "Shot Distribution", 0, 4, 4, 2),
            new("stats",         "Engine Stats",      4, 4, 4, 2),
            new("shot_sets",     "Shot Sets",         0, 6, 4, 2),
            new("mode_events",   "Mode Events",       4, 6, 4, 2),
        };
    }

    private Dictionary<string, Control> BuildTileContentMap()
    {
        var summaryWrapper = new VBoxContainer();
        summaryWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        summaryWrapper.SizeFlagsVertical = SizeFlags.ExpandFill;
        summaryWrapper.AddThemeConstantOverride("separation", 8);
        summaryWrapper.AddChild(BuildSectionLabel("Shot Sets"));
        summaryWrapper.AddChild(_setSummary);

        var logWrapper = new VBoxContainer();
        logWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        logWrapper.SizeFlagsVertical = SizeFlags.ExpandFill;
        logWrapper.AddThemeConstantOverride("separation", 8);
        logWrapper.AddChild(BuildSectionLabel("Mode Events"));
        logWrapper.AddChild(_modeLog);

        return new Dictionary<string, Control>
        {
            { "viewport_3d", _viewportTileContent },
            { "top_down", _topDownPlot },
            { "side_view", _sidePlot },
            { "distribution", _distributionPlot },
            { "stats", _statPanel },
            { "shot_sets", summaryWrapper },
            { "mode_events", logWrapper },
        };
    }

    private RangeSpikeShotSet GetOrCreateLiveSet(RangeSpikeShotPreset preset, bool libgolf)
    {
        string label = libgolf ? $"libgolf — {preset.DisplayName} Live" : $"{preset.DisplayName} Live";
        return GetOrCreateNamedLiveSet(label, preset);
    }

    private RangeSpikeShotSet GetOrCreateNamedLiveSet(string label, RangeSpikeShotPreset preset)
    {
        foreach (RangeSpikeShotSet shotSet in _shotSets)
        {
            if (shotSet.Label == label)
                return shotSet;
        }

        var newSet = new RangeSpikeShotSet(label, preset, new List<RangeSpikeShotTrace>());
        _shotSets.Add(newSet);
        return newSet;
    }

    // Resets the last trace in a set back to its palette colour before a new shot is added.
    private static void RecolorLastTrace(RangeSpikeShotSet set, bool isLibgolf)
    {
        if (set.Traces.Count == 0) return;
        var last = set.Traces[set.Traces.Count - 1];
        last.DisplayColor = last.Color; // restore to palette / gold colour
        _ = isLibgolf; // reserved for future per-engine logic
    }

    private void ApplyWindowConstraints(Vector2I presetSize, bool capToPreset)
    {
        // Clear any prior max constraint first so portrait presets are not
        // clamped by the previous landscape cap before the new size is applied.
        DisplayServer.WindowSetMaxSize(Vector2I.Zero);

        Vector2I minSize = new(
            Mathf.Max(480, presetSize.X / 2),
            Mathf.Max(320, presetSize.Y / 2));
        DisplayServer.WindowSetMinSize(minSize);
        DisplayServer.WindowSetMaxSize(capToPreset ? presetSize : Vector2I.Zero);
    }

    private static Vector2I FitWindowToUsableScreen(Vector2I requestedSize)
    {
        int screen = DisplayServer.WindowGetCurrentScreen();
        Rect2 usableRect = DisplayServer.ScreenGetUsableRect(screen);
        if (usableRect.Size.X <= 0 || usableRect.Size.Y <= 0)
            return requestedSize;

        float scale = Mathf.Min(
            usableRect.Size.X / requestedSize.X,
            usableRect.Size.Y / requestedSize.Y);

        if (scale >= 1.0f)
            return requestedSize;

        return new Vector2I(
            Mathf.Max(320, Mathf.FloorToInt(requestedSize.X * scale)),
            Mathf.Max(320, Mathf.FloorToInt(requestedSize.Y * scale)));
    }

    private static void CenterWindowOnCurrentScreen(Vector2I windowSize)
    {
        int screen = DisplayServer.WindowGetCurrentScreen();
        Rect2 usableRect = DisplayServer.ScreenGetUsableRect(screen);
        if (usableRect.Size.X <= 0 || usableRect.Size.Y <= 0)
            return;

        Vector2I position = new(
            Mathf.RoundToInt(usableRect.Position.X + (usableRect.Size.X - windowSize.X) / 2.0f),
            Mathf.RoundToInt(usableRect.Position.Y + (usableRect.Size.Y - windowSize.Y) / 2.0f));
        DisplayServer.WindowSetPosition(position);
    }

    private static RichTextLabel BuildRichText(string text)
    {
        return new RichTextLabel
        {
            Text = text,
            BbcodeEnabled = false,
            FitContent = false,
            ScrollActive = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
    }

    private static Label BuildSectionLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", new Color("c4d4e5"));
        label.AddThemeFontSizeOverride("font_size", 14);
        return label;
    }
}
