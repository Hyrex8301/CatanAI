using System;
using System.Collections.Generic;
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

    public BoardSkin Skin { get; set; } = new FlatSkin();

    /// <summary>When set, hover only shows over highlighted targets (normal play). Off shows hover anywhere (checkpoint A).</summary>
    public bool HoverTargetsOnly { get; set; }

    /// <summary>A left click on a vertex, edge or hex.</summary>
    public event Action<BoardHit>? Clicked;

    public void Setup(Rect2 area) => _geometry = new BoardGeometry(area.Position.X, area.Position.Y, area.Size.X, area.Size.Y);

    public void Show(PlayerView view, IReadOnlyList<SeatColor> colors)
    {
        _view = view;
        _colors = colors;
        QueueRedraw();
    }

    public void SetTargets(IEnumerable<BoardHit> targets)
    {
        _targets.Clear();
        _targets.UnionWith(targets);
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_view is null)
            return;
        if (@event is InputEventMouseMotion)
        {
            var hit = HitAtMouse();
            if (HoverTargetsOnly && !_targets.Contains(hit))
                hit = BoardHit.None;
            if (hit != _hover)
            {
                _hover = hit;
                QueueRedraw();
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

    public override void _Draw()
    {
        if (_view is null)
            return;
        var v = _view;
        var board = v.Board;
        float size = (float)_geometry.Size;

        for (int pass = 0; pass < 2; pass++)
            for (int h = 0; h < Topology.HexCount; h++)
                Skin.Coast(this, Hex(h), Corners(h), pass);
        for (int h = 0; h < Topology.HexCount; h++)
            Skin.Hex(this, Hex(h), Corners(h), board.TerrainAt(h));

        for (int spot = 0; spot < Topology.HarborCount; spot++)
        {
            Vector2 a = Vertex(Topology.HarborVertices[spot, 0]), b = Vertex(Topology.HarborVertices[spot, 1]);
            var mid = (a + b) / 2;
            var outward = (mid - Hex(Topology.HarborHex[spot])).Normalized();
            Skin.Harbor(this, a, b, mid + outward * size * 0.72f, size, board.HarborTypeAt(spot));
        }

        for (int h = 0; h < Topology.HexCount; h++)
            if (board.NumberAt(h) != 0)
                Skin.Token(this, Hex(h), size, board.NumberAt(h), board.PipsAt(h));

        for (int e = 0; e < Topology.EdgeCount; e++)
            if (v.EdgeOwner[e] >= 0)
                Skin.Road(this, Vertex(Topology.EdgeVertices[e, 0]), Vertex(Topology.EdgeVertices[e, 1]), size, SeatColor(v.EdgeOwner[e]));

        for (int vertex = 0; vertex < Topology.VertexCount; vertex++)
        {
            int owner = v.VertexOwner[vertex];
            if (owner < 0)
                continue;
            if (v.VertexLevel[vertex] == 2)
                Skin.City(this, Vertex(vertex), size, SeatColor(owner));
            else
                Skin.Settlement(this, Vertex(vertex), size, SeatColor(owner));
        }

        Skin.Robber(this, Hex(v.RobberHex) + new Vector2(-size * 0.5f, 0), size);

        foreach (var target in _targets)
            Highlight(target, hover: false, size);
        if (_hover.Kind != HitKind.None)
            Highlight(_hover, hover: true, size);
    }

    private void Highlight(BoardHit hit, bool hover, float size)
    {
        switch (hit.Kind)
        {
            case HitKind.Vertex: Skin.HighlightVertex(this, Vertex(hit.Id), size, hover); break;
            case HitKind.Edge: Skin.HighlightEdge(this, Vertex(Topology.EdgeVertices[hit.Id, 0]), Vertex(Topology.EdgeVertices[hit.Id, 1]), size, hover); break;
            case HitKind.Hex: Skin.HighlightHex(this, Corners(hit.Id), hover); break;
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
