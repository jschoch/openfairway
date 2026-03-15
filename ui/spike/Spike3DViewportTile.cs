using System.Collections.Generic;
using Godot;

// Coordinate convention from physics engine:
//   p.X = carry distance (forward)
//   p.Y = height (up)
//   p.Z = lateral / offline deviation

public partial class Spike3DViewportTile : Control
{
    private readonly List<RangeSpikeShotSet> _shotSets = new();

    private SubViewport _subViewport;
    private TextureRect _viewportTextureRect;
    private Camera3D _camera;
    private MeshInstance3D _gridMeshInstance;
    private MeshInstance3D _traceMeshInstance;
    private Node3D _markerRoot;

    private float _orbitYaw;
    private float _orbitPitch;
    private float _orbitDist;
    private Vector3 _orbitTarget;
    private bool _rightDragging;
    private Vector2 _lastDragPos;
    private const float OrbitSensitivity = 0.005f;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        BuildViewport();
        RefreshScene();
    }

    public override void _Notification(int what)
    {
        if (what != NotificationResized || _subViewport == null || _viewportTextureRect == null)
            return;

        Vector2I viewportSize = new(Mathf.Max(64, Mathf.RoundToInt(Size.X)), Mathf.Max(64, Mathf.RoundToInt(Size.Y)));
        _subViewport.Size = viewportSize;
        _viewportTextureRect.Size = Size;
    }

    public void SetShotSets(IEnumerable<RangeSpikeShotSet> shotSets)
    {
        _shotSets.Clear();
        if (shotSets != null)
        {
            foreach (RangeSpikeShotSet shotSet in shotSets)
                _shotSets.Add(shotSet);
        }

        RefreshScene();
    }

    private void BuildViewport()
    {
        var debugBackground = new ColorRect
        {
            Name = "DebugBackground",
            Color = new Color("182634"),
            MouseFilter = MouseFilterEnum.Ignore
        };
        debugBackground.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(debugBackground);

        _viewportTextureRect = new TextureRect
        {
            Name = "ViewportTextureRect",
            MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale
        };
        _viewportTextureRect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_viewportTextureRect);

        _subViewport = new SubViewport
        {
            Name = "SubViewport",
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Msaa2X,
            Size = new Vector2I(640, 360)
        };
        AddChild(_subViewport);
        _viewportTextureRect.Texture = _subViewport.GetTexture();

        var world = new Node3D { Name = "World" };
        _subViewport.AddChild(world);

        _camera = new Camera3D
        {
            Name = "Camera3D",
            Current = true,
            Fov = 38.0f,
            Far = 2000.0f
        };
        world.AddChild(_camera);

        var light = new DirectionalLight3D
        {
            LightEnergy = 1.35f,
            RotationDegrees = new Vector3(-42.0f, 25.0f, 0.0f)
        };
        world.AddChild(light);

        var environment = new WorldEnvironment
        {
            Environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Color,
                BackgroundColor = new Color("05070a"),
                AmbientLightColor = new Color("d0d7e0"),
                AmbientLightEnergy = 0.45f
            }
        };
        world.AddChild(environment);

        _gridMeshInstance = new MeshInstance3D { Name = "Grid" };
        world.AddChild(_gridMeshInstance);

        _traceMeshInstance = new MeshInstance3D { Name = "Trajectories" };
        world.AddChild(_traceMeshInstance);

        _markerRoot = new Node3D { Name = "Markers" };
        world.AddChild(_markerRoot);
    }

    private void RefreshScene()
    {
        if (_gridMeshInstance == null || _traceMeshInstance == null || _markerRoot == null)
            return;

        ComputeBounds(out float maxCarryM, out float maxHeightM, out float maxOfflineM);
        UpdateCamera(maxCarryM, maxHeightM, maxOfflineM);
        _gridMeshInstance.Mesh = BuildGridMesh(maxCarryM, maxOfflineM);
        _traceMeshInstance.Mesh = BuildTraceMesh();
        RebuildMarkers();
    }

    // p.X = carry, p.Y = height, p.Z = offline.
    private void ComputeBounds(out float maxCarryM, out float maxHeightM, out float maxOfflineM)
    {
        float carry = 0f, height = 0f, offline = 0f;
        foreach (RangeSpikeShotSet set in _shotSets)
            foreach (RangeSpikeShotTrace trace in set.Traces)
                foreach (Vector3 p in trace.Points)
                {
                    if (p.X > carry) carry = p.X;
                    if (p.Y > height) height = p.Y;
                    float abs = Mathf.Abs(p.Z);
                    if (abs > offline) offline = abs;
                }

        // Generous minimums so the empty scene still shows a useful default view.
        maxCarryM = Mathf.Max(carry * 1.1f, 120.0f);
        maxHeightM = Mathf.Max(height * 1.2f, 25.0f);
        maxOfflineM = Mathf.Max(offline * 1.3f, 15.0f);
    }

    // Ball travels in +X (carry), deviates in Z (offline), rises in Y (height).
    // Camera uses an orbit rig seeded from the scene bounds; right-drag to orbit.
    private void UpdateCamera(float maxCarryM, float maxHeightM, float maxOfflineM)
    {
        if (_camera == null) return;
        _orbitTarget = new Vector3(maxCarryM * 0.5f, maxHeightM * 0.35f, 0.0f);
        float camX = -maxCarryM * 0.35f;
        float camY = maxHeightM * 1.1f + 5.0f;
        float camZ = maxOfflineM * 0.5f + 8.0f;
        Vector3 camPos = new(camX, camY, camZ);
        Vector3 offset = camPos - _orbitTarget;
        _orbitDist = offset.Length();
        _orbitPitch = Mathf.Asin(offset.Y / _orbitDist);
        _orbitYaw = Mathf.Atan2(offset.Z, offset.X);
        PositionCamera();
    }

    private void PositionCamera()
    {
        if (_camera == null) return;
        float cx = _orbitTarget.X + _orbitDist * Mathf.Cos(_orbitPitch) * Mathf.Cos(_orbitYaw);
        float cy = _orbitTarget.Y + _orbitDist * Mathf.Sin(_orbitPitch);
        float cz = _orbitTarget.Z + _orbitDist * Mathf.Cos(_orbitPitch) * Mathf.Sin(_orbitYaw);
        _camera.Position = new Vector3(cx, cy, cz);
        _camera.LookAt(_orbitTarget, Vector3.Up);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                _rightDragging = mb.Pressed;
                _lastDragPos = mb.GlobalPosition;
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion motion && _rightDragging)
        {
            Vector2 delta = motion.GlobalPosition - _lastDragPos;
            _lastDragPos = motion.GlobalPosition;
            _orbitYaw -= delta.X * OrbitSensitivity;
            _orbitPitch = Mathf.Clamp(_orbitPitch + delta.Y * OrbitSensitivity, -1.4f, 1.4f);
            PositionCamera();
            AcceptEvent();
        }
    }

    // Grid: X = carry (forward), Z = offline (lateral).
    private static Mesh BuildGridMesh(float maxCarryM, float maxOfflineM)
    {
        float spacing = maxCarryM > 150f ? 10.0f : 5.0f;
        int xFwd = Mathf.CeilToInt(maxCarryM * 1.1f / spacing);
        int zHalf = Mathf.Max(3, Mathf.CeilToInt(maxOfflineM * 1.5f / spacing));

        var mesh = new ImmediateMesh();
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color("22303c")
        };

        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);

        float xExt = xFwd * spacing;
        float zExt = zHalf * spacing;

        // Lines running along the carry (X) axis at each Z offset.
        for (int i = -zHalf; i <= zHalf; i++)
        {
            float z = i * spacing;
            mesh.SurfaceAddVertex(new Vector3(0.0f, 0.0f, z));
            mesh.SurfaceAddVertex(new Vector3(xExt, 0.0f, z));
        }

        // Lines running along the offline (Z) axis at each carry distance.
        for (int j = 0; j <= xFwd; j++)
        {
            float x = j * spacing;
            mesh.SurfaceAddVertex(new Vector3(x, 0.0f, -zExt));
            mesh.SurfaceAddVertex(new Vector3(x, 0.0f, zExt));
        }

        mesh.SurfaceEnd();
        return mesh;
    }

    private Mesh BuildTraceMesh()
    {
        var mesh = new ImmediateMesh();
        foreach (RangeSpikeShotSet shotSet in _shotSets)
        {
            foreach (RangeSpikeShotTrace trace in shotSet.Traces)
            {
                if (trace.Points.Count < 2)
                    continue;

                var material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    AlbedoColor = trace.DisplayColor
                };

                mesh.SurfaceBegin(Mesh.PrimitiveType.LineStrip, material);
                foreach (Vector3 point in trace.Points)
                    mesh.SurfaceAddVertex(point);
                mesh.SurfaceEnd();
            }
        }

        return mesh;
    }

    private void RebuildMarkers()
    {
        foreach (Node child in _markerRoot.GetChildren())
            child.QueueFree();

        AddOriginMarker();

        foreach (RangeSpikeShotSet shotSet in _shotSets)
        {
            foreach (RangeSpikeShotTrace trace in shotSet.Traces)
                AddTraceMarkers(trace);
        }
    }

    private void AddOriginMarker()
    {
        var origin = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.45f, Height = 0.9f },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color("ffffff")
            },
            Position = new Vector3(0.0f, 0.45f, 0.0f)
        };
        _markerRoot.AddChild(origin);
    }

    private void AddTraceMarkers(RangeSpikeShotTrace trace)
    {
        if (trace.Points.Count == 0)
            return;

        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = trace.DisplayColor
        };

        for (int i = 0; i < trace.Points.Count; i += 6)
        {
            var marker = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.18f, Height = 0.36f },
                MaterialOverride = material,
                Position = trace.Points[i]
            };
            _markerRoot.AddChild(marker);
        }

        Vector3 landing = trace.LandingPoint;
        var landingMarker = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.2f, 0.8f) },
            MaterialOverride = material,
            Position = new Vector3(landing.X, 0.1f, landing.Z)
        };
        _markerRoot.AddChild(landingMarker);
    }
}
