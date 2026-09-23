using Catan.UI;
using Godot;

/// <summary>
/// One player on the HUD, colonist style: a color avatar, name, big VP, then hand size, dev cards, knights and road length
/// with icons. The Largest Army / Longest Road stat glows gold for its holder; the acting player gets a colored border.
/// Everything comes from a <see cref="SeatSummary"/> (the viewer's view only).
/// </summary>
public partial class PlayerCard : Control
{
    private SeatSummary? _p;

    public PlayerCard() => MouseFilter = MouseFilterEnum.Pass;

    public void Show(SeatSummary summary)
    {
        _p = summary;
        TooltipText = summary.IsYou
            ? $"You: {summary.Vp} VP" + (summary.HiddenVp > 0 ? $" ({summary.HiddenVp} from Victory Point cards only you can see)" : "")
            : $"{summary.Name}: {summary.Vp} VP showing";
        TooltipText += $"\nPieces left: {summary.RoadsLeft} roads, {summary.SettlementsLeft} settlements, {summary.CitiesLeft} cities";
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_p is not { } p)
            return;
        var seatColor = Ui.SeatColor(p.Color);
        var style = Ui.PanelStyle(p.IsActing ? Ui.Highlight : Ui.PanelFill, radius: 12);
        if (p.IsActing)
        {
            style.BorderColor = seatColor == Colors.White || p.Color == SeatColor.White ? Ui.MutedText : seatColor;
            style.SetBorderWidthAll(3);
        }
        DrawStyleBox(style, new Rect2(Vector2.Zero, Size));

        // Avatar.
        var avatar = new Vector2(44, Size.Y / 2);
        DrawCircle(avatar, 30, seatColor);
        DrawArc(avatar, 30, 0, Mathf.Tau, 40, p.Color == SeatColor.White ? Ui.MutedText : seatColor.Darkened(0.25f), 2, true);
        Ui.DrawCentered(this, avatar, p.IsYou ? "You" : p.Name[..1], p.IsYou ? 17 : 26, Ui.OnSeatColor(p.Color), 60);

        // Name and status.
        Ui.DrawLeft(this, new Vector2(88, 24), p.Name, 19, Ui.Text);
        string status = p.IsActing ? (p.IsYou ? "your move" : "thinking…") : p.IsCurrent ? "their turn" : p.IsYou ? "" : "bot";
        if (status.Length > 0)
            Ui.DrawLeft(this, new Vector2(88 + NameWidth(p.Name) + 10, 25), status, 13, p.IsActing ? seatColor.Darkened(0.2f) : Ui.MutedText);

        // Stats row.
        float x = 92, y = Size.Y - 26;
        x = StatCell(x, y, StatIcon.Cards, p.Cards.ToString(), false);
        x = StatCell(x, y, StatIcon.DevCards, p.DevCards.ToString(), false);
        x = StatCell(x, y, StatIcon.Knight, p.Knights.ToString(), p.LargestArmy);
        StatCell(x, y, StatIcon.Road, p.RoadLength.ToString(), p.LongestRoad);

        // Victory points.
        var vpAt = new Vector2(Size.X - 42, Size.Y / 2 - 6);
        DrawCircle(vpAt, 26, Ui.Text);
        Ui.DrawCentered(this, vpAt, p.Vp.ToString(), 26, Colors.White, 60);
        Ui.DrawCentered(this, vpAt + new Vector2(0, 36), p.HiddenVp > 0 ? $"VP ({p.HiddenVp} hidden)" : "VP", 12, Ui.MutedText, 100);
    }

    private float StatCell(float x, float y, StatIcon icon, string value, bool award)
    {
        const float width = 62;
        if (award)
            FlatIcons.Rounded(this, new Rect2(x - 6, y - 15, width - 4, 30), Ui.Gold, 15);
        Icons.Skin.Stat(this, new Vector2(x + 10, y), 28, icon, award ? Ui.Text : new Color(0.55f, 0.58f, 0.64f));
        Ui.DrawLeft(this, new Vector2(x + 26, y), value, 17, Ui.Text);
        return x + width;
    }

    private static float NameWidth(string name) => ThemeDB.FallbackFont.GetStringSize(name, HorizontalAlignment.Left, -1, 19).X;
}
