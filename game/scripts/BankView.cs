using Catan.Core;
using Godot;

/// <summary>The bank in the board's top-right corner: cards left of each resource, and the dev card deck.</summary>
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
        DrawStyleBox(Ui.PanelStyle(radius: 10), new Rect2(Vector2.Zero, Size));
        var card = new Vector2(30, 42);
        float gap = (Size.X - 20 - 6 * card.X) / 5;
        for (int i = 0; i < 6; i++)
        {
            var rect = new Rect2(new Vector2(10 + i * (card.X + gap), 8), card);
            if (i < GameConstants.ResourceCount)
                Icons.Skin.CardFace(this, rect, false, i, dimmed: _bank[i] == 0);
            else
                Icons.Skin.CardBack(this, rect, isDev: true);
            int count = i < GameConstants.ResourceCount ? _bank[i] : _devDeck;
            Ui.DrawCentered(this, rect.GetCenter() + new Vector2(0, card.Y / 2 + 12), count.ToString(), 14, count == 0 ? new Color(0.8f, 0.1f, 0.1f) : Ui.Text, 40);
        }
    }
}
