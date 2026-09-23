using Catan.Core;
using Godot;

/// <summary>The bank row in the right column: a bank icon, then each resource and the dev card deck with a count badge.</summary>
public partial class BankView : Control
{
    private int[] _bank = new int[GameConstants.ResourceCount];
    private int _devDeck;

    public BankView()
    {
        MouseFilter = MouseFilterEnum.Pass;
        TooltipText = "Bank: cards left of each resource, and development cards left";
    }

    public void Show(PlayerView v)
    {
        _bank = v.Bank;
        _devDeck = v.DevDeckSize;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var style = Ui.PanelStyle(Ui.Cream, radius: 6);
        style.ShadowSize = 2;
        DrawStyleBox(style, new Rect2(Vector2.Zero, Size));
        DrawBankIcon(this, new Vector2(34, Size.Y / 2), 34, new Color(0.55f, 0.45f, 0.3f));

        var card = new Vector2(36, 50);
        float x = 70, gap = (Size.X - x - 10 - 6 * card.X) / 5;
        for (int i = 0; i < 6; i++, x += card.X + gap)
        {
            var rect = new Rect2(new Vector2(x, (Size.Y - card.Y) / 2 + 2), card);
            if (i < GameConstants.ResourceCount)
                Icons.Skin.CardFace(this, rect, false, i, dimmed: _bank[i] == 0);
            else
                Icons.Skin.CardBack(this, rect, isDev: true);
            Ui.Badge(this, rect.Position + new Vector2(card.X, 0), i < GameConstants.ResourceCount ? _bank[i] : _devDeck);
        }
    }

    /// <summary>A little bank building: roof, columns, base.</summary>
    public static void DrawBankIcon(CanvasItem c, Vector2 at, float s, Color ink)
    {
        c.DrawColoredPolygon(new[] { at + new Vector2(-s * 0.5f, -s * 0.15f), at + new Vector2(0, -s * 0.45f), at + new Vector2(s * 0.5f, -s * 0.15f) }, ink);
        for (int i = 0; i < 4; i++)
            c.DrawRect(new Rect2(at + new Vector2(-s * 0.4f + i * s * 0.25f, -s * 0.1f), new Vector2(s * 0.1f, s * 0.42f)), ink);
        c.DrawRect(new Rect2(at + new Vector2(-s * 0.5f, s * 0.33f), new Vector2(s, s * 0.1f)), ink);
    }
}
