using Catan.Core;
using Godot;

/// <summary>The menu's emblem: three little painted hexes (wheat, forest, hills) in a row above the title.</summary>
public partial class TitleEmblem : Control
{
    private static readonly Terrain[] Tiles = { Terrain.Fields, Terrain.Forest, Terrain.Hills };
    private const float Radius = 26;

    public TitleEmblem()
    {
        CustomMinimumSize = new Vector2(0, Radius * 2 + 6);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var skin = new PaintedSkin();
        float step = Radius * Mathf.Sqrt(3);
        var first = new Vector2(Size.X / 2 - step, Size.Y / 2);
        for (int i = 0; i < Tiles.Length; i++)
        {
            var center = first + new Vector2(i * step, 0);
            var corners = new Vector2[6];
            for (int k = 0; k < 6; k++)
            {
                float angle = Mathf.DegToRad(60 * k - 90); // pointy top, like the board
                corners[k] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Radius;
            }
            skin.Hex(this, center, corners, Tiles[i]);
        }
    }
}
