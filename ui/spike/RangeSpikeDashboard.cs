using System.Collections.Generic;
using System.Threading.Tasks;
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
    // Persistent wrapper nodes for tile content — created once, reused across
    // every RefreshTileCanvas() call to avoid orphaned Godot node leaks.
    private VBoxContainer _summaryWrapper;
    private VBoxContainer _logWrapper;
    private bool _isPortraitLayout;
    private int _liveShotCounter;
    private SpikeTileVisibilityDialog _visibilityDialog;
    private SpikeStatsDialog _statsDialog;
    private ClosestToPinDialog _ctpDialog;
    private SpikeSetLabelDialog _setLabelDialog;
    private ClosestToPinTile _ctpTile;
    private CtpScatterPanel _ctpScatter;
    private readonly ClosestToPinEvaluator _ctpEvaluator = new();
    private Button _ctpModeButton;
    private Button _ctpNewSetButton;
    private Button _apexToggleButton;
    // Grid dimensions — expanded when CTP tiles are active so they stay on-screen.
    private const int LandscapeColumns = 4, LandscapeRowsBase = 6, LandscapeRowsCtp = 8;
    private const int PortraitColumns  = 8, PortraitRowsBase  = 10, PortraitRowsCtp  = 12;
    private Label _shotNavLabel;
    private readonly HashSet<string> _visibleTileIds = new();
    // Tracks every tile ID ever seen across both orientations so we can
    // distinguish "new tile → default visible" from "user hid it → keep hidden".
    private readonly HashSet<string> _everSeenTileIds = new();
    private List<SpikeTileSpec> _allTileSpecs = new();
    private SpikeLayoutStore.Data _savedLayout = new();
    // Shot navigation
    private RangeSpikeShotSet _focusedSet;
    private int _focusedTraceIndex = -1;
    private bool _displayLastShot;

    public override void _Ready()
    {
        string projectRoot = ProjectSettings.GlobalizePath("res://");
        _libgolfBridge = new LibgolfBridgeClient(projectRoot);

        // Load persisted layout before building UI so controls start with saved values.
        _savedLayout = SpikeLayoutStore.Load();

        // Pre-populate visibility from saved state so ApplyResponsiveLayout uses it.
        if (_savedLayout.VisibleTileIds.Count > 0)
        {
            _visibleTileIds.UnionWith(_savedLayout.VisibleTileIds);
            _everSeenTileIds.UnionWith(_savedLayout.AllKnownTileIds);
        }

        BuildUi();
        BuildTileContent();

        // Apply saved window settings now that controls exist.
        _windowPresetOption.Select(_savedLayout.WindowPresetIndex);
        _tileCanvas.TileLayoutChanged += SaveLayout;

        ApplyResponsiveLayout(force: true);
        // Apply the saved window size / constraints.
        if (_windowPresets.TryGetValue(_savedLayout.WindowPresetIndex, out Vector2I savedSize))
            ApplyWindowPreset(savedSize);

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

        _statsDialog = new SpikeStatsDialog();
        _statsDialog.StatsChanged += OnStatsChanged;
        _statsDialog.Visible = false;
        AddChild(_statsDialog);

        _ctpDialog = new ClosestToPinDialog();
        _ctpDialog.ModeConfigured += OnCtpModeConfigured;
        _ctpDialog.Visible = false;
        AddChild(_ctpDialog);

        _setLabelDialog = new SpikeSetLabelDialog();
        _setLabelDialog.LabelConfirmed += OnSetLabelConfirmed;
        _setLabelDialog.Visible = false;
        AddChild(_setLabelDialog);
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

        var spacerTitle = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddChild(spacerTitle);

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

        Button labelSetButton = new() { Text = "Label Set" };
        labelSetButton.Pressed += OnLabelSetPressed;
        _controlsFlow.AddChild(labelSetButton);

        var engineLabel = new Label { Text = "Engine:" };
        engineLabel.AddThemeColorOverride("font_color", new Color("90a0b2"));
        _controlsFlow.AddChild(engineLabel);

        _engineOption = new OptionButton();
        _engineOption.AddItem("OpenFairway", 0);
        _engineOption.AddItem("libgolf (ref)", 1);
        _engineOption.AddItem("All", 2);
        _engineOption.Select(2);
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

        var displayLastShotCb = new CheckBox { Text = "Last Shot Only" };
        displayLastShotCb.Toggled += pressed => { _displayLastShot = pressed; RefreshPlots(); };
        _controlsFlow.AddChild(displayLastShotCb);

        _shotNavLabel = new Label { Text = "" };
        _shotNavLabel.AddThemeColorOverride("font_color", new Color("90a0b2"));
        _controlsFlow.AddChild(_shotNavLabel);

        Button statsButton = new() { Text = "Stats" };
        statsButton.Pressed += () =>
        {
            _statsDialog.Populate(new HashSet<string>(_statPanel != null
                ? GetCurrentEnabledStats()
                : SpikeStatsDialog.DefaultEnabled));
            _statsDialog.PopupCentered();
        };
        _controlsFlow.AddChild(statsButton);

        _ctpModeButton = new Button { Text = "Mode: CTP" };
        _ctpModeButton.Pressed += OnCtpModeButtonPressed;
        _controlsFlow.AddChild(_ctpModeButton);

        _ctpNewSetButton = new Button { Text = "New CTP Set", Visible = false };
        _ctpNewSetButton.Pressed += OnNewCtpSetPressed;
        _controlsFlow.AddChild(_ctpNewSetButton);

        _apexToggleButton = new Button { Text = "Apex: ft" };
        _apexToggleButton.Pressed += OnApexTogglePressed;
        _controlsFlow.AddChild(_apexToggleButton);

        _windowPresetOption = new OptionButton();
        _windowPresetOption.AddItem("1280 x 720", 0);
        _windowPresetOption.AddItem("1600 x 900", 1);
        _windowPresetOption.AddItem("1728 x 972", 2);
        _windowPresetOption.AddItem("1920 x 1080", 3);
        _windowPresetOption.AddItem("720 x 1280", 4);
        _windowPresetOption.AddItem("900 x 1600", 5);
        _windowPresetOption.AddItem("972 x 1728", 6);
        _windowPresetOption.AddItem("1080 x 1920", 7);
        _windowPresetOption.Select(2); // overwritten in _Ready from saved layout
        _windowPresetOption.ItemSelected += OnWindowPresetSelected;
        _controlsFlow.AddChild(_windowPresetOption);

        return toolbarPanel;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            ApplyResponsiveLayout(force: false);
    }

    public override void _Input(InputEvent ev)
    {
        if (ev is not InputEventKey keyEv || !keyEv.Pressed || keyEv.Echo) return;
        if (keyEv.Keycode == Key.Left)  { NavigateFocusedShot(-1); GetViewport().SetInputAsHandled(); }
        if (keyEv.Keycode == Key.Right) { NavigateFocusedShot(+1); GetViewport().SetInputAsHandled(); }
    }

    // ── Shot navigation ────────────────────────────────────────────────────────

    private void SetFocusedSet(RangeSpikeShotSet set)
    {
        _focusedSet = set;
        _focusedTraceIndex = set.Traces.Count - 1;
    }

    private void NavigateFocusedShot(int delta)
    {
        if (_focusedSet == null || _focusedSet.Traces.Count == 0) return;

        _focusedTraceIndex = Mathf.Clamp(
            _focusedTraceIndex + delta, 0, _focusedSet.Traces.Count - 1);

        ApplyFocusColors(_focusedSet);

        // If there is a paired set (e.g. libgolf sibling), navigate it in sync.
        string siblingLabel = _focusedSet.Label.StartsWith("libgolf — ")
            ? _focusedSet.Label["libgolf — ".Length..]
            : "libgolf — " + _focusedSet.Label;
        foreach (var set in _shotSets)
        {
            if (set.Label == siblingLabel && set.Traces.Count > 0)
            {
                int sibIdx = Mathf.Clamp(_focusedTraceIndex, 0, set.Traces.Count - 1);
                ApplyFocusColors(set, sibIdx);
            }
        }

        RefreshPlots();
    }

    private void ApplyFocusColors(RangeSpikeShotSet set, int? overrideIndex = null)
    {
        int idx = overrideIndex ?? _focusedTraceIndex;
        bool isLibgolf = set.Label.StartsWith("libgolf");
        Color brightColor = isLibgolf ? HitShotColorLibgolf : HitShotColorOF;
        Color dimColor    = isLibgolf ? DimColorLibgolf      : DimColorOF;
        for (int i = 0; i < set.Traces.Count; i++)
        {
            set.Traces[i].DisplayColor = (i == idx) ? brightColor : dimColor;
        }
    }

    // ── Stats dialog ───────────────────────────────────────────────────────────

    private readonly HashSet<string> _currentEnabledStats =
        new(SpikeStatsDialog.DefaultEnabled);

    private IEnumerable<string> GetCurrentEnabledStats() => _currentEnabledStats;

    private void OnStatsChanged(string[] enabledIds)
    {
        _currentEnabledStats.Clear();
        foreach (string id in enabledIds) _currentEnabledStats.Add(id);
        _statPanel?.SetEnabledStats(_currentEnabledStats);
    }

    // ── libgolf async helper ───────────────────────────────────────────────────

    // Serialise all bridge invocations: libgolf is not safe to run in multiple
    // concurrent processes (triggers std::bad_alloc / SIGABRT after ~5 shots).
    private readonly System.Threading.SemaphoreSlim _libgolfSemaphore = new(1, 1);

    // Runs the libgolf bridge on a background thread and calls onComplete on
    // the Godot main thread once the result is ready.
    // onComplete receives null on failure — callers must handle that.
    private void RunLibgolfAsync(
        float speed, float vla, float hla, float backspin, float sidespin,
        System.Action<List<Vector3>> onComplete)
    {
        var sem = _libgolfSemaphore;
        Task.Run(async () =>
        {
            await sem.WaitAsync();
            List<Vector3> pts;
            try   { pts = await _libgolfBridge.RunSimulationAsync(speed, vla, hla, backspin, sidespin); }
            finally { sem.Release(); }
            Callable.From(() => onComplete(pts)).CallDeferred();
        });
    }

    // ── TCP server integration ─────────────────────────────────────────────────

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
        float lmCarry = data.TryGetValue("CarryDistance", out var cd) ? cd.AsSingle() : 0f;
        float smashFactor = data.TryGetValue("SmashFactor", out var sf) ? sf.AsSingle() : 0f;
        int engineId = _engineOption.GetItemId(_engineOption.Selected);
        _liveShotCounter++;

        if (engineId == 0 || engineId == 2)
        {
            var set = GetOrCreateNamedLiveSet("TCP Live", preset: null);
            RecolorLastTrace(set, isLibgolf: false);

            var trace = _simulator.GenerateShotTraceFromParams(
                set.Label, set.Traces.Count + 1, "Launch Monitor", DimColorOF,
                speed, vla, hla, backspin, sidespin);
            trace.LmCarryDistanceYd = lmCarry;
            trace.SmashFactor = smashFactor;
            trace.DisplayColor = HitShotColorOF;
            set.Traces.Add(trace);
            SetFocusedSet(set);
            if (_ctpEvaluator.IsActive) _ctpEvaluator.AddTrace(trace);
        }

        if (engineId == 1 || engineId == 2)
        {
            if (_libgolfBridge.IsAvailable())
            {
                var set = GetOrCreateNamedLiveSet("libgolf — TCP Live", preset: null);
                RecolorLastTrace(set, isLibgolf: true);
                float s = speed, v = vla, h = hla, bs = backspin, ss = sidespin;
                int nextIdx = set.Traces.Count + 1;
                RunLibgolfAsync(s, v, h, bs, ss, pts =>
                {
                    if (pts == null || pts.Count < 2) return;
                    var trace = new RangeSpikeShotTrace(
                        set.Label, $"Launch Monitor #{nextIdx}", "Launch Monitor", DimColorLibgolf, pts);
                    trace.SpeedMph = s; trace.LaunchAngleDeg = v; trace.DirectionDeg = h;
                    trace.BackspinRpm = bs; trace.SidespinRpm = ss;
                    trace.DisplayColor = HitShotColorLibgolf;
                    set.Traces.Add(trace);
                    RefreshPlots();
                });
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

        _ctpTile = new ClosestToPinTile
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _ctpScatter = new CtpScatterPanel
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _ctpEvaluator.ResultsChanged += OnCtpResultsChanged;

        _summaryWrapper = new VBoxContainer();
        _summaryWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _summaryWrapper.SizeFlagsVertical = SizeFlags.ExpandFill;
        _summaryWrapper.AddThemeConstantOverride("separation", 8);
        _summaryWrapper.AddChild(BuildSectionLabel("Shot Sets"));
        _summaryWrapper.AddChild(_setSummary);

        _logWrapper = new VBoxContainer();
        _logWrapper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _logWrapper.SizeFlagsVertical = SizeFlags.ExpandFill;
        _logWrapper.AddThemeConstantOverride("separation", 8);
        _logWrapper.AddChild(BuildSectionLabel("Mode Events"));
        _logWrapper.AddChild(_modeLog);
        var contents = new Dictionary<string, Control>
        {
            { "viewport_3d", _viewportTileContent },
            { "top_down", _topDownPlot },
            { "side_view", _sidePlot },
            { "distribution", _distributionPlot },
            { "stats", _statPanel },
            { "shot_sets", _summaryWrapper },
            { "mode_events", _logWrapper },
            { "ctp_scoreboard", _ctpTile },
            { "ctp_scatter",    _ctpScatter },
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
            // Override preset palette colors so OF shots always render in the engine color,
            // matching the stat panel's ColOF legend.
            foreach (var t in set.Traces)
                t.DisplayColor = HitShotColorOF;
            _shotSets.Add(set);
            AppendModeEvent($"OpenFairway: {shotCount} shots for {preset.DisplayName}.");
        }

        // libgolf single nominal reference trace — runs async so the main thread stays live.
        if (engineId == 1 || engineId == 2)
        {
            if (!_libgolfBridge.IsAvailable())
            {
                _statusLabel.Text = "libgolf bridge not built — run tools/libgolf-bridge/build.sh.";
            }
            else
            {
                AppendModeEvent($"libgolf: simulating {preset.DisplayName}…");
                RunLibgolfAsync(preset.SpeedMph, preset.LaunchAngleDeg, preset.LaunchDirectionDeg,
                    preset.BackspinRpm, preset.SidespinRpm, pts =>
                {
                    if (pts == null || pts.Count < 2)
                    {
                        _statusLabel.Text = "libgolf simulation failed — see Godot output log.";
                        return;
                    }
                    string label = $"libgolf — {preset.DisplayName}";
                    var trace = new RangeSpikeShotTrace(label, "nominal", preset.ClubLabel, HitShotColorLibgolf, pts);
                    trace.SpeedMph = preset.SpeedMph; trace.LaunchAngleDeg = preset.LaunchAngleDeg;
                    trace.DirectionDeg = preset.LaunchDirectionDeg; trace.BackspinRpm = preset.BackspinRpm;
                    trace.SidespinRpm = preset.SidespinRpm;
                    var set = new RangeSpikeShotSet(label, preset, new List<RangeSpikeShotTrace> { trace });
                    _shotSets.Add(set);
                    float carryYd = trace.LandingPoint.X / 0.9144f;
                    float offYd = trace.LandingPoint.Z / 0.9144f;
                    string offStr = Mathf.Abs(offYd) < 0.3f ? "on line" : $"{Mathf.Abs(offYd):F1} yd {(offYd < 0 ? "L" : "R")}";
                    AppendModeEvent($"libgolf: {preset.DisplayName} — {carryYd:F0} yd carry, {offStr}.");
                    RefreshPlots();
                });
            }
        }

        RefreshPlots();
    }

    private void OnClearSetsPressed()
    {
        _shotSets.Clear();
        // Also clear active CTP results so the scoreboard doesn't show stale data.
        // Archived CTP sessions are preserved for comparison.
        if (_ctpEvaluator.IsActive) _ctpEvaluator.Clear();
        AppendModeEvent("Cleared all shot sets.");
        RefreshPlots();
    }

    // Returns either all shots or only the focused/last trace per set,
    // depending on the "Last Shot Only" checkbox.
    private List<RangeSpikeShotSet> GetDisplaySets()
    {
        if (!_displayLastShot) return _shotSets;

        var result = new List<RangeSpikeShotSet>();
        foreach (var set in _shotSets)
        {
            if (set.Traces.Count == 0) continue;
            int idx = (set == _focusedSet)
                ? Mathf.Clamp(_focusedTraceIndex, 0, set.Traces.Count - 1)
                : set.Traces.Count - 1;
            result.Add(new RangeSpikeShotSet(set.Label, set.Preset,
                new List<RangeSpikeShotTrace> { set.Traces[idx] }));
        }
        return result;
    }

    private void RefreshPlots()
    {
        var display = GetDisplaySets();
        _viewport3D.SetShotSets(display);
        _topDownPlot.SetShotSets(display);
        _sidePlot.SetShotSets(display);
        _distributionPlot.SetShotSets(display);
        // Stat panel receives the full sets for accurate per-set averages and
        // std dev. The 3D overlay shows the focused/latest individual shot.
        _statPanel.SetShotSets(_shotSets);
        _setSummary.Text = _simulator.BuildSummary(_shotSets);
        UpdateShotNavLabel();
        _viewport3D.SetShotDataOverlay(BuildShotDataOverlay());
    }

    private ShotDataOverlay BuildShotDataOverlay()
    {
        // Find the primary (OF/LM) trace — last focused or most recent shot.
        var statSets = GetStatSets();
        RangeSpikeShotTrace primary = null;
        foreach (var set in statSets)
        {
            if (!set.Label.StartsWith("libgolf") && set.Traces.Count > 0)
            {
                primary = set.Traces[0];
                break;
            }
        }
        if (primary == null) return default;

        float carryYd   = primary.LandingPoint.X / 0.9144f;
        float peak      = 0f;
        foreach (var pt in primary.Points) peak = Mathf.Max(peak, pt.Y);
        float apexFt    = peak * ShotSetup.FEET_PER_METER;
        float offlineYd = primary.LandingPoint.Z / 0.9144f;

        var overlay = new ShotDataOverlay
        {
            HasData      = true,
            CarryYards   = carryYd,
            ApexFeet     = apexFt,
            OfflineYards = offlineYd,
        };

        if (_ctpEvaluator.IsActive && _ctpEvaluator.Results.Count > 0)
        {
            var last = _ctpEvaluator.Results[_ctpEvaluator.Results.Count - 1];
            overlay.HasCtp            = true;
            overlay.CtpDistanceYards  = last.DistanceToTargetYards;
            overlay.CtpIsHit          = last.IsHit;
        }

        return overlay;
    }

    // Always returns a single-trace projection (focused or last) per set.
    private List<RangeSpikeShotSet> GetStatSets()
    {
        var result = new List<RangeSpikeShotSet>();
        foreach (var set in _shotSets)
        {
            if (set.Traces.Count == 0) continue;
            int idx = (set == _focusedSet)
                ? Mathf.Clamp(_focusedTraceIndex, 0, set.Traces.Count - 1)
                : set.Traces.Count - 1;
            result.Add(new RangeSpikeShotSet(set.Label, set.Preset,
                new List<RangeSpikeShotTrace> { set.Traces[idx] }));
        }
        return result;
    }

    private void UpdateShotNavLabel()
    {
        if (_shotNavLabel == null) return;
        if (_focusedSet == null || _focusedSet.Traces.Count == 0)
        {
            _shotNavLabel.Text = "";
            return;
        }
        int current = _focusedTraceIndex + 1;
        int total   = _focusedSet.Traces.Count;
        _shotNavLabel.Text = $"Shot {current}/{total}";
    }

    // Engine colors — must match SpikeStatPanel.ColOF / ColLG / ColLM.
    // Bright = focused/latest shot. Dim = older non-focused shots.
    private static readonly Color HitShotColorOF        = new(0.95f, 0.28f, 0.28f, 1.0f); // red
    private static readonly Color HitShotColorLibgolf   = new(0.28f, 0.85f, 0.48f, 1.0f); // green
    private static readonly Color HitShotColorLM        = new(1.00f, 0.72f, 0.20f, 1.0f); // orange
    private static readonly Color DimColorOF            = HitShotColorOF      with { A = 0.45f };
    private static readonly Color DimColorLibgolf       = HitShotColorLibgolf with { A = 0.45f };
    private static readonly Color DimColorLM            = HitShotColorLM      with { A = 0.45f };

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
                liveSet.Label, liveSet.Traces.Count + 1, preset.ClubLabel, DimColorOF,
                speed, vla, hla, backspin, sidespin);
            trace.DisplayColor = HitShotColorOF;
            liveSet.Traces.Add(trace);
            SetFocusedSet(liveSet);
            if (_ctpEvaluator.IsActive) _ctpEvaluator.AddTrace(trace);
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
                float s = speed, v = vla, h = hla, bs = backspin, ss = sidespin;
                string lg_label = $"libgolf — {preset.DisplayName} Live";
                string lg_club = preset.ClubLabel;
                int nextIdx = libgolfSet.Traces.Count + 1;
                RunLibgolfAsync(s, v, h, bs, ss, pts =>
                {
                    if (pts == null || pts.Count < 2)
                    {
                        AppendModeEvent("libgolf hit-shot failed — see output log.");
                        return;
                    }
                    var trace = new RangeSpikeShotTrace(lg_label, $"{lg_club} #{nextIdx}",
                        lg_club, DimColorLibgolf, pts);
                    trace.SpeedMph = s; trace.LaunchAngleDeg = v; trace.DirectionDeg = h;
                    trace.BackspinRpm = bs; trace.SidespinRpm = ss;
                    trace.DisplayColor = HitShotColorLibgolf;
                    libgolfSet.Traces.Add(trace);
                    RefreshPlots();
                });
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
            SaveLayout();
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

    private void ApplyWindowPreset(Vector2I size)
    {
        Vector2I fittedSize = FitWindowToUsableScreen(size);
        ApplyContentBase(fittedSize);
        ApplyWindowConstraints(fittedSize);
        DisplayServer.WindowSetSize(fittedSize);
        CenterWindowOnCurrentScreen(fittedSize);
        UpdateWindowInfo();

        string orientation = fittedSize.Y > fittedSize.X ? "portrait" : "landscape";
        string fitNote = fittedSize == size
            ? string.Empty
            : $" Requested {size.X} x {size.Y}, fitted to usable screen as {fittedSize.X} x {fittedSize.Y}.";
        _statusLabel.Text = $"Window set to {fittedSize.X} x {fittedSize.Y} ({orientation}).{fitNote}";
    }

    private void ApplyResponsiveLayout(bool force)
    {
        bool shouldUsePortraitLayout = Size.Y > Size.X;
        if (!force && shouldUsePortraitLayout == _isPortraitLayout)
            return;

        // Save current tile positions before switching orientation.
        if (!force)
            SaveLayout();

        _isPortraitLayout = shouldUsePortraitLayout;
        bool ctpActive = _ctpEvaluator.IsActive;
        int cols = shouldUsePortraitLayout ? PortraitColumns  : LandscapeColumns;
        int rows = shouldUsePortraitLayout
            ? (ctpActive ? PortraitRowsCtp  : PortraitRowsBase)
            : (ctpActive ? LandscapeRowsCtp : LandscapeRowsBase);
        _tileCanvas.ConfigureGrid(cols, rows);

        _allTileSpecs = BuildTileSpecs(shouldUsePortraitLayout);

        // Add only genuinely new tile IDs as visible; preserve the user's explicit
        // hide choices for tiles they have already seen.
        foreach (SpikeTileSpec spec in _allTileSpecs)
        {
            if (!_everSeenTileIds.Contains(spec.Id))
            {
                _visibleTileIds.Add(spec.Id);
                _everSeenTileIds.Add(spec.Id);
            }
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
        SaveLayout();
    }

    private void OnTileDeleteRequested(string tileId)
    {
        _visibleTileIds.Remove(tileId);
        _tileCanvas.RemoveTile(tileId);
        _statusLabel.Text = $"Tile '{tileId}' removed. Use the Tiles button to restore it.";
        SaveLayout();
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

    // Builds the default tile layout for the given orientation, then overlays
    // any saved positions so the user's arrangement is restored on startup.
    private List<SpikeTileSpec> BuildTileSpecs(bool portrait)
    {
        List<SpikeTileSpec> specs = portrait ? DefaultPortraitSpecs() : DefaultLandscapeSpecs();

        var saved = portrait ? _savedLayout.PortraitTiles : _savedLayout.LandscapeTiles;
        foreach (var spec in specs)
        {
            if (saved.TryGetValue(spec.Id, out var pos))
            {
                spec.Column     = pos[0];
                spec.Row        = pos[1];
                spec.ColumnSpan = pos[2];
                spec.RowSpan    = pos[3];
            }
        }
        return specs;
    }

    private static List<SpikeTileSpec> DefaultLandscapeSpecs() => new()
    {
        new("viewport_3d",   "3D Shot View",      0, 0, 2, 2),
        new("top_down",      "Top-Down Flight",   2, 0, 2, 2),
        new("side_view",     "Side Flight",       0, 2, 2, 2),
        new("distribution",  "Shot Distribution", 2, 2, 2, 2),
        new("stats",         "Engine Stats",      0, 4, 4, 2),
        new("shot_sets",     "Shot Sets",         0, 6, 2, 1),
        new("mode_events",   "Mode Events",       2, 6, 2, 1),
    };

    private static List<SpikeTileSpec> DefaultPortraitSpecs() => new()
    {
        new("viewport_3d",   "3D Shot View",      0, 0, 8, 2),
        new("top_down",      "Top-Down Flight",   0, 2, 4, 2),
        new("side_view",     "Side Flight",       4, 2, 4, 2),
        new("distribution",  "Shot Distribution", 0, 4, 4, 2),
        new("stats",         "Engine Stats",      4, 4, 4, 2),
        new("shot_sets",     "Shot Sets",         0, 6, 4, 2),
        new("mode_events",   "Mode Events",       4, 6, 4, 2),
    };

    // Captures all current layout state to disk.
    private void SaveLayout()
    {
        if (_allTileSpecs == null) return;

        // Record positions for the current orientation.
        var tileDict = _isPortraitLayout ? _savedLayout.PortraitTiles : _savedLayout.LandscapeTiles;
        tileDict.Clear();
        foreach (var spec in _allTileSpecs)
            tileDict[spec.Id] = new[] { spec.Column, spec.Row, spec.ColumnSpan, spec.RowSpan };

        _savedLayout.VisibleTileIds   = new HashSet<string>(_visibleTileIds);
        _savedLayout.AllKnownTileIds  = new HashSet<string>(_everSeenTileIds);
        _savedLayout.WindowPresetIndex = _windowPresetOption?.Selected ?? 2;
        SpikeLayoutStore.Save(_savedLayout);
    }

    private Dictionary<string, Control> BuildTileContentMap()
    {
        // Reuse the persistent wrapper nodes created in BuildTileContent().
        // Do NOT create new nodes here — callers invoke this on every layout
        // change and every tile visibility update, so creating new nodes would
        // leak the old ones as orphaned Godot objects.
        return new Dictionary<string, Control>
        {
            { "viewport_3d", _viewportTileContent },
            { "top_down", _topDownPlot },
            { "side_view", _sidePlot },
            { "distribution", _distributionPlot },
            { "stats", _statPanel },
            { "shot_sets", _summaryWrapper },
            { "mode_events", _logWrapper },
            { "ctp_scoreboard", _ctpTile },
            { "ctp_scatter",    _ctpScatter },
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

    // Dims the last trace in a set when a new shot is about to be added.
    private static void RecolorLastTrace(RangeSpikeShotSet set, bool isLibgolf)
    {
        if (set.Traces.Count == 0) return;
        var last = set.Traces[set.Traces.Count - 1];
        last.DisplayColor = isLibgolf ? DimColorLibgolf : DimColorOF;
    }

    private void ApplyWindowConstraints(Vector2I presetSize)
    {
        Vector2I minSize = new(
            Mathf.Max(480, presetSize.X / 2),
            Mathf.Max(320, presetSize.Y / 2));
        DisplayServer.WindowSetMinSize(minSize);
        DisplayServer.WindowSetMaxSize(Vector2I.Zero); // no maximum — window is freely resizable
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
        var label = new RichTextLabel
        {
            Text = text,
            BbcodeEnabled = false,
            FitContent = false,
            ScrollActive = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        label.AddThemeFontSizeOverride("normal_font_size", 12);
        return label;
    }

    private static Label BuildSectionLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", new Color("c4d4e5"));
        label.AddThemeFontSizeOverride("font_size", 14);
        return label;
    }

    // ── Closest to the Pin ─────────────────────────────────────────────────────

    private void OnCtpModeButtonPressed()
    {
        if (_ctpEvaluator.IsActive)
            StopCtpSession();
        else
            _ctpDialog.OpenDialog();
    }

    private void OnCtpModeConfigured(string club, float targetYards, float winYards)
    {
        var config = new ClosestToPinConfig(club, targetYards, winYards);
        _ctpEvaluator.Activate(config);

        // Add CTP tile spec if not already present, then make it visible.
        bool hasSpec = false;
        foreach (var spec in _allTileSpecs)
            if (spec.Id == "ctp_scoreboard") { hasSpec = true; break; }

        if (!hasSpec)
        {
            if (_isPortraitLayout)
            {
                _allTileSpecs.Add(new SpikeTileSpec("ctp_scatter",    "CTP Target View",    0, 8, 4, 2));
                _allTileSpecs.Add(new SpikeTileSpec("ctp_scoreboard", "CTP Scoreboard",     4, 8, 4, 2));
            }
            else
            {
                _allTileSpecs.Add(new SpikeTileSpec("ctp_scatter",    "CTP Target View",    0, 7, 2, 1));
                _allTileSpecs.Add(new SpikeTileSpec("ctp_scoreboard", "CTP Scoreboard",     2, 7, 2, 1));
            }
        }

        _visibleTileIds.Add("ctp_scoreboard");
        _visibleTileIds.Add("ctp_scatter");
        _everSeenTileIds.Add("ctp_scoreboard");
        _everSeenTileIds.Add("ctp_scatter");
        if (_ctpModeButton != null) _ctpModeButton.Text = "Stop CTP";
        if (_ctpNewSetButton != null) _ctpNewSetButton.Visible = true;
        // Expand the grid so CTP tiles at row 7/8 are within the canvas bounds.
        int ctpRows = _isPortraitLayout ? PortraitRowsCtp : LandscapeRowsCtp;
        int ctpCols = _isPortraitLayout ? PortraitColumns : LandscapeColumns;
        _tileCanvas.ConfigureGrid(ctpCols, ctpRows);
        RefreshTileCanvas();
        AppendModeEvent($"CTP session started: {club} @ {targetYards:F0} yd, win ≤ {winYards:F0} yd.");
    }

    private void StopCtpSession()
    {
        _ctpEvaluator.Deactivate();
        if (_ctpModeButton != null) _ctpModeButton.Text = "Mode: CTP";
        if (_ctpNewSetButton != null) _ctpNewSetButton.Visible = false;

        // Remove CTP tile specs and hide them.
        _allTileSpecs.RemoveAll(s => s.Id == "ctp_scoreboard" || s.Id == "ctp_scatter");
        _visibleTileIds.Remove("ctp_scoreboard");
        _visibleTileIds.Remove("ctp_scatter");

        // Shrink the grid back to the base size.
        int baseRows = _isPortraitLayout ? PortraitRowsBase : LandscapeRowsBase;
        int baseCols = _isPortraitLayout ? PortraitColumns  : LandscapeColumns;
        _tileCanvas.ConfigureGrid(baseCols, baseRows);
        RefreshTileCanvas();
        AppendModeEvent("CTP session stopped. Returning to free range mode.");
    }

    private void OnCtpResultsChanged()
    {
        _ctpTile?.Refresh(_ctpEvaluator.Config, _ctpEvaluator.Results, _ctpEvaluator.ArchivedSessions);
        _ctpScatter?.Refresh(_ctpEvaluator.Config, _ctpEvaluator.Results);
    }

    private void OnNewCtpSetPressed()
    {
        if (!_ctpEvaluator.IsActive) return;
        int frozenNum = _ctpEvaluator.ArchivedSessions.Count + 1;
        _ctpEvaluator.FreezeSession($"Session {frozenNum}");
        AppendModeEvent($"CTP session {frozenNum} saved — starting new set.");
    }

    // ── Set label editor ──────────────────────────────────────────────────────

    private void OnLabelSetPressed()
    {
        if (_focusedSet == null)
        {
            _statusLabel.Text = "No set focused — hit a shot or add a set first.";
            return;
        }
        _setLabelDialog.OpenFor(_focusedSet.DisplayName);
    }

    private void OnSetLabelConfirmed(string tag)
    {
        if (_focusedSet == null) return;
        _focusedSet.Tag = tag;
        _setSummary.Text = _simulator.BuildSummary(_shotSets);
        AppendModeEvent($"Set labeled: \"{_focusedSet.DisplayName}\".");
    }

    // ── Apex height unit toggle ────────────────────────────────────────────────

    private bool _apexInFeet = true;

    private void OnApexTogglePressed()
    {
        _apexInFeet = !_apexInFeet;
        _apexToggleButton.Text = _apexInFeet ? "Apex: ft" : "Apex: m";
        _statPanel?.SetApexUnit(_apexInFeet);
    }
}
