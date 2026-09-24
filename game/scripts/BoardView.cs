using System;
using System.Collections.Generic;
using System.Linq;
using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>
/// Draws the board from a <see cref="PlayerView"/> (never the game state) through a <see cref="BoardSkin"/>, highlights
/// legal targets, and turns mouse input into <see cref="BoardHit"/>s via <see cref="BoardGeometry"/>.
/// </summary>
public partial class BoardView : Node2D
{
    private BoardGeometry _geometry = null!;
    private PlayerView? _view;
    private IReadOnlyList<SeatColor> _colors = Array.Empty<SeatColor>();
    private readonly HashSet<BoardHit> _targets = new();
    private BoardHit _hover = BoardHit.None;

    public BoardSkin Skin { get; set; } = new PaintedSkin();

    /// <summary>When set, hover only shows over highlighted targets (normal play). Off shows hover anywhere (checkpoint A).</summary>
    public bool HoverTargetsOnly { get; set; }

    /// <summary>A left click on a vertex, edge or hex.</summary>
    public event Action<BoardHit>? Clicked;

    // Animations: a flashing number, pieces popping in, the robber sliding.
    private const double FlashSeconds = 1.2, PopSeconds = 0.35, SlideSeconds = 0.5;
    private int _flashNumber;
    private double _flashStart = -10, _slideStart = -10;
    private readonly Dictionary<BoardHit, double> _pops = new();
    private Vector2 _robberDrawn, _slideFrom;

    /// <summary>The hexes with this number light up.</summary>
    public void Flash(int number)
    {
        _flashNumber = number;
        _flashStart = Time.GetTicksMsec() / 1000.0;
    }

    /// <summary>The piece at this spot pops in (grows from large to its size).</summary>
    public void Pop(PieceType piece, int target) =>
        _pops[piece == PieceType.Road ? BoardHit.Edge(target) : BoardHit.Vertex(target)] = Time.GetTicksMsec() / 1000.0;

    /// <summary>The robber slides from where it is drawn now to its hex.</summary>
    public void SlideRobber()
    {
        _slideFrom = _robberDrawn;
        _slideStart = Time.GetTicksMsec() / 1000.0;
    }

    // Click-to-build: spots you can build on right now (with the piece each would get), a faint shadow of the piece under
    // the mouse, and a stronger one on the spot you clicked once (click it again to build).
    private readonly Dictionary<BoardHit, PieceType> _quick = new();
    private BoardHit _pending = BoardHit.None;
    private Color _ghostColor = Colors.White;
    private GhostLayer _hoverGhost = null!, _pendingGhost = null!;

    // Drawing layers, back to front: the cached terrain, the live pieces, then the click-to-build shadows.
    private readonly GhostLayer _terrain, _pieces;

    public BoardView()
    {
        _terrain = new GhostLayer { DrawFn = DrawTerrain };
        _pieces = new GhostLayer { DrawFn = DrawPieces };
        _hoverGhost = new GhostLayer { Modulate = new Color(1, 1, 1, 0.4f), DrawFn = c => Ghost(c, _hover) };
        _pendingGhost = new GhostLayer { DrawFn = c => Ghost(c, _pending) };
        foreach (var layer in new[] { _terrain, _pieces, _hoverGhost, _pendingGhost })
            AddChild(layer);
    }

    public void SetQuickTargets(IEnumerable<(BoardHit Hit, PieceType Piece)> targets, Color color)
    {
        _quick.Clear();
        foreach (var (hit, piece) in targets)
            _quick[hit] = piece;
        _ghostColor = color;
        _hoverGhost.QueueRedraw();
    }

    /// <summary>The spot clicked once, showing a strong shadow of its piece (BoardHit.None clears it).</summary>
    public void SetPending(BoardHit hit)
    {
        _pending = hit;
        _pendingGhost.QueueRedraw();
    }

    private void Ghost(CanvasItem c, BoardHit hit)
    {
        if (!_quick.TryGetValue(hit, out var piece) || (c == _hoverGhost && hit == _pending))
            return;
        float size = (float)_geometry.Size;
        if (piece == PieceType.Road)
            Skin.Road(c, Vertex(Topology.EdgeVertices[hit.Id, 0]), Vertex(Topology.EdgeVertices[hit.Id, 1]), size, _ghostColor);
        else if (piece == PieceType.Settlement)
            Skin.Settlement(c, Vertex(hit.Id), size, _ghostColor);
        else
            Skin.City(c, Vertex(hit.Id), size, _ghostColor);
    }

    /// <summary>Extra drawing over the pieces (the practice screen's rating markers). Call <see cref="Redraw"/> after changing it.</summary>
    public Action<CanvasItem>? Overlay { get; set; }

    public void Redraw() => _pieces.QueueRedraw();

    /// <summary>A corner's point on screen.</summary>
    public Vector2 VertexPoint(int vertex) => Vertex(vertex) + Position;

    public float HexSize => (float)_geometry.Size;

    /// <summary>A hex's center on screen (for cards flying from it).</summary>
    public Vector2 HexCenter(int hex) => Hex(hex) + Position;

    public override void _Process(double delta)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (_pending.Kind != HitKind.None)
            _pendingGhost.Modulate = new Color(1, 1, 1, 0.6f + 0.25f * Mathf.Sin((float)now * 6)); // a gentle pulse
        if (now - _flashStart < FlashSeconds || now - _slideStart < SlideSeconds || _pops.Count > 0)
            _pieces.QueueRedraw();
        foreach (var (hit, start) in _pops.ToArray()) // a copy: finished pops are removed
            if (now - start > PopSeconds)
                _pops.Remove(hit);
    }

    private float PopScale(BoardHit hit, double now)
    {
        if (!_pops.TryGetValue(hit, out double start))
            return 1;
        float t = Mathf.Clamp((float)((now - start) / PopSeconds), 0, 1);
        return 1 + 0.8f * (1 - t) * (1 - t); // 1.8× shrinking to 1×
    }

    public void Setup(Rect2 area) => _geometry = new BoardGeometry(area.Position.X, area.Position.Y, area.Size.X, area.Size.Y);

    public void Show(PlayerView view, IReadOnlyList<SeatColor> colors)
    {
        var board = _view?.Board;
        _view = view;
        _colors = colors;
        if (!ReferenceEquals(board, view.Board))
            _terrain.QueueRedraw(); // the cached terrain only changes with the board
        _pieces.QueueRedraw();
    }

    public void SetTargets(IEnumerable<BoardHit> targets)
    {
        _targets.Clear();
        _targets.UnionWith(targets);
        _pieces.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_view is null)
            return;
        if (@event is InputEventMouseMotion)
        {
            var hit = HitAtMouse();
            if (HoverTargetsOnly && !_targets.Contains(hit) && !_quick.ContainsKey(hit))
                hit = BoardHit.None;
            if (hit != _hover)
            {
                _hover = hit;
                _hoverGhost.QueueRedraw();
                _pieces.QueueRedraw();
            }
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            var hit = HitAtMouse();
            if (hit.Kind != HitKind.None)
            {
                Clicked?.Invoke(hit);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private BoardHit HitAtMouse()
    {
        var p = GetLocalMousePosition();
        return _geometry.HitTest(p.X, p.Y);
    }

    /// <summary>The part that only changes with the board: sea, coast, tiles and harbors (drawn once, then cached).</summary>
    private void DrawTerrain(CanvasItem c)
    {
        if (_view is null)
            return;
        var board = _view.Board;
        float size = (float)_geometry.Size;
        for (int pass = 0; pass < 2; pass++)
            for (int h = 0; h < Topology.HexCount; h++)
                Skin.Coast(c, Hex(h), Corners(h), pass);
        for (int h = 0; h < Topology.HexCount; h++)
            Skin.Hex(c, Hex(h), Corners(h), board.TerrainAt(h));
        for (int spot = 0; spot < Topology.HarborCount; spot++)
        {
            Vector2 a = Vertex(Topology.HarborVertices[spot, 0]), b = Vertex(Topology.HarborVertices[spot, 1]);
            var mid = (a + b) / 2;
            var outward = (mid - Hex(Topology.HarborHex[spot])).Normalized();
            Skin.Harbor(c, a, b, mid + outward * size * 0.72f, size, board.HarborTypeAt(spot));
        }
    }

    /// <summary>Everything that moves: rolled-number glow, number tiles, pieces, the robber and highlights.</summary>
    private void DrawPieces(CanvasItem c)
    {
        if (_view is null)
            return;
        var v = _view;
        var board = v.Board;
        float size = (float)_geometry.Size;

        // A roll lights up the hexes with its number (two soft pulses), under their number tiles.
        double now = Time.GetTicksMsec() / 1000.0;
        if (_flashNumber > 0 && now - _flashStart < FlashSeconds)
        {
            float t = (float)((now - _flashStart) / FlashSeconds);
            float glow = 0.55f * Mathf.Abs(Mathf.Sin(t * Mathf.Pi * 2)) * (1 - t * 0.5f);
            for (int h = 0; h < Topology.HexCount; h++)
                if (board.NumberAt(h) == _flashNumber)
                    c.DrawColoredPolygon(Corners(h), new Color(1, 1, 0.8f, h == v.RobberHex ? glow * 0.3f : glow));
        }

        for (int h = 0; h < Topology.HexCount; h++)
            if (board.NumberAt(h) != 0)
                Skin.Token(c, Hex(h), size, board.NumberAt(h), board.PipsAt(h));

        for (int e = 0; e < Topology.EdgeCount; e++)
        {
            if (v.EdgeOwner[e] < 0)
                continue;
            Vector2 a = Vertex(Topology.EdgeVertices[e, 0]), b = Vertex(Topology.EdgeVertices[e, 1]), mid = (a + b) / 2;
            float scale = PopScale(BoardHit.Edge(e), now);
            c.DrawSetTransform(mid, 0, new Vector2(scale, scale));
            Skin.Road(c, a - mid, b - mid, size, SeatColor(v.EdgeOwner[e]));
        }

        for (int vertex = 0; vertex < Topology.VertexCount; vertex++)
        {
            int owner = v.VertexOwner[vertex];
            if (owner < 0)
                continue;
            float scale = PopScale(BoardHit.Vertex(vertex), now);
            c.DrawSetTransform(Vertex(vertex), 0, new Vector2(scale, scale));
            if (v.VertexLevel[vertex] == 2)
                Skin.City(c, Vector2.Zero, size, SeatColor(owner));
            else
                Skin.Settlement(c, Vector2.Zero, size, SeatColor(owner));
        }
        c.DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // The robber slides from where it was drawn last.
        var robberAt = Hex(v.RobberHex) + new Vector2(-size * 0.5f, 0);
        if (now - _slideStart < SlideSeconds)
        {
            float t = (float)((now - _slideStart) / SlideSeconds);
            t = 1 - (1 - t) * (1 - t); // ease out
            robberAt = _slideFrom.Lerp(robberAt, t) - new Vector2(0, Mathf.Sin(t * Mathf.Pi) * size * 0.4f);
        }
        _robberDrawn = robberAt;
        Skin.Robber(c, robberAt, size);

        foreach (var target in _targets)
            Highlight(c, target, hover: false, size);
        Overlay?.Invoke(c);
        if (_hover.Kind != HitKind.None && !_quick.ContainsKey(_hover))
            Highlight(c, _hover, hover: true, size);
    }

    private void Highlight(CanvasItem c, BoardHit hit, bool hover, float size)
    {
        switch (hit.Kind)
        {
            case HitKind.Vertex: Skin.HighlightVertex(c, Vertex(hit.Id), size, hover); break;
            case HitKind.Edge: Skin.HighlightEdge(c, Vertex(Topology.EdgeVertices[hit.Id, 0]), Vertex(Topology.EdgeVertices[hit.Id, 1]), size, hover); break;
            case HitKind.Hex: Skin.HighlightHex(c, Corners(hit.Id), hover); break;
        }
    }

    private Color SeatColor(int seat) => seat < _colors.Count ? Ui.SeatColor(_colors[seat]) : Colors.Magenta;

    private Vector2 Vertex(int v)
    {
        var (x, y) = _geometry.Vertex(v);
        return new Vector2((float)x, (float)y);
    }

    private Vector2 Hex(int h)
    {
        var (x, y) = _geometry.Hex(h);
        return new Vector2((float)x, (float)y);
    }

    private Vector2[] Corners(int h)
    {
        var corners = new Vector2[6];
        for (int c = 0; c < 6; c++)
            corners[c] = Vertex(Topology.HexVertices[h, c]);
        return corners;
    }
}

/// <summary>A layer drawn above the board with its own transparency (for the shadow pieces of click-to-build).</summary>
public partial class GhostLayer : Node2D
{
    public Action<CanvasItem>? DrawFn { get; set; }

    public override void _Draw() => DrawFn?.Invoke(this);
}
