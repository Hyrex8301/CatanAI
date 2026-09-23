using Catan.UI;
using Godot;

/// <summary>
/// One player's row in the right column, colonist style: name over an avatar with a VP ribbon, the card back with hand
/// size, the dev card back with its count, knights and road length (gold behind them for Largest Army / Longest Road),
/// and "…" while they think. Your own panel (<see cref="Big"/>) is taller and lighter. Everything comes from a
/// <see cref="SeatSummary"/> (the viewer's view only).
/// </summary>
public partial class PlayerCard : Control
{
    private SeatSummary? _p;
    private double _time;

    public bool Big { get; set; }

    public PlayerCard() => MouseFilter = MouseFilterEnum.Pass;

    public void Show(SeatSummary summary)
    {
        _p = summary;
        TooltipText = (summary.IsYou
                          ? $"You: {summary.Vp} VP" + (summary.HiddenVp > 0 ? $" ({summary.HiddenVp} from Victory Point cards only you can see)" : "")
                          : $"{summary.Name}: {summary.Vp} VP showing")
                      + $"\n{summary.Cards} resource cards, {summary.DevCards} development cards"
                      + $"\n{summary.Knights} knights played{(summary.LargestArmy ? " (Largest Army)" : "")}, longest road {summary.RoadLength}{(summary.LongestRoad ? " (Longest Road)" : "")}"
                      + $"\nPieces left: {summary.RoadsLeft} roads, {summary.SettlementsLeft} settlements, {summary.CitiesLeft} cities";
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_p is { IsActing: true, IsYou: false })
        {
            _time += delta;
            QueueRedraw(); // the thinking dots
        }
    }

    public override void _Draw()
    {
        if (_p is not { } p)
            return;
        var style = Ui.PanelStyle(Big ? Ui.Cream : Ui.RowGray, radius: 6);
        style.ShadowSize = 2;
        if (p.IsActing)
        {
            style.BorderColor = Ui.Gold;
            style.SetBorderWidthAll(3);
        }
        DrawStyleBox(style, new Rect2(Vector2.Zero, Size));

        float avatarX = Big ? 64 : 60, r = Big ? 32 : 22;
        var avatar = new Vector2(avatarX, Big ? Size.Y / 2 + 12 : Size.Y / 2 + 6);
        if (Big)
            Ui.DrawCentered(this, new Vector2(Size.X / 2 + 30, 24), p.IsYou ? "You" : p.Name, 24, Ui.Text, Size.X);
        else
            Ui.DrawCentered(this, new Vector2(avatarX, 14), p.Name, 16, Ui.Text, 110);
        Ui.Avatar(this, avatar, r, p.Color, p.IsYou);
        // The VP ribbon under the avatar.
        var ribbon = new Rect2(avatar + new Vector2(-r * 0.75f, r * 0.62f), new Vector2(r * 1.5f, Big ? 22 : 17));
        FlatIcons.Rounded(this, ribbon, Colors.White, 3);
        DrawRect(ribbon, Ui.PanelBorder, false, 1);
        Ui.DrawCentered(this, ribbon.GetCenter(), p.Vp.ToString(), Big ? 16 : 13, Ui.Text, 40);

        if (p.IsActing && !p.IsYou)
            for (int i = 0; i < 3; i++)
            {
                float bounce = Mathf.Sin((float)_time * 6 - i * 0.8f) * 2;
                DrawCircle(new Vector2(14 + i * 7, avatar.Y + bounce), 2.6f, Ui.Text);
            }

        // Cards and stats.
        float y = Big ? Size.Y / 2 + 16 : Size.Y / 2 + 2;
        var card = Big ? new Vector2(40, 54) : new Vector2(34, 46);
        float x = Big ? 124 : 124;
        var back = new Rect2(new Vector2(x, y - card.Y / 2), card);
        Icons.Skin.CardBack(this, back, false);
        Ui.Badge(this, back.Position + new Vector2(card.X, 0), p.Cards);
        x += card.X + (Big ? 12 : 16);
        var dev = new Rect2(new Vector2(x, y - card.Y / 2), card);
        Icons.Skin.CardBack(this, dev, true);
        Ui.Badge(this, dev.Position + new Vector2(card.X, 0), p.DevCards);
        x += card.X + (Big ? 26 : 30);
        Stat(new Vector2(x, y), StatIcon.Knight, p.Knights, p.LargestArmy);
        Stat(new Vector2(x + (Big ? 52 : 58), y), StatIcon.Road, p.RoadLength, p.LongestRoad);
    }

    private void Stat(Vector2 at, StatIcon icon, int value, bool award)
    {
        if (award)
            DrawCircle(at - new Vector2(0, 8), 17, Ui.Gold);
        Icons.Skin.Stat(this, at - new Vector2(0, 9), 26, icon, award ? Ui.Text : new Color(0.52f, 0.52f, 0.55f));
        Ui.DrawCentered(this, at + new Vector2(0, 17), value.ToString(), 16, Ui.Text, 40);
    }
}
