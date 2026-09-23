using Catan.Core;
using Catan.UI;
using Godot;

/// <summary>One row per seat: color, name, public VP, card and dev card counts, road length, knights, awards, and who's acting.</summary>
public sealed class PlayersPanel
{
    private readonly GameSetup _setup;
    private readonly Label[] _names = new Label[GameConstants.PlayerCount];
    private readonly Label[] _stats = new Label[GameConstants.PlayerCount];
    private readonly PanelContainer[] _rows = new PanelContainer[GameConstants.PlayerCount];

    public PlayersPanel(VBoxContainer body, GameSetup setup)
    {
        _setup = setup;
        for (int seat = 0; seat < GameConstants.PlayerCount; seat++)
        {
            var row = new PanelContainer();
            var column = new VBoxContainer();
            row.AddChild(column);

            var header = new HBoxContainer();
            header.AddThemeConstantOverride("separation", 8);
            header.AddChild(new ColorRect { Color = Ui.SeatColor(setup.Colors[seat]), CustomMinimumSize = new Vector2(20, 20) });
            _names[seat] = Ui.Label("", 17);
            header.AddChild(_names[seat]);
            column.AddChild(header);

            _stats[seat] = Ui.Label("", 14, Ui.MutedText);
            column.AddChild(_stats[seat]);
            _rows[seat] = row;
            body.AddChild(row);
        }
    }

    public void Update(PlayerView v)
    {
        for (int seat = 0; seat < GameConstants.PlayerCount; seat++)
        {
            bool you = seat == _setup.HumanSeat;
            bool acting = seat == v.ActingSeat;
            _names[seat].Text = (you ? $"You ({_setup.Colors[seat]})" : $"{_setup.Colors[seat]} (bot)") + (acting ? "   ◀" : "");

            string vp = you ? $"{v.TotalVP} VP" + (v.TotalVP > v.PublicVP[seat] ? $" ({v.PublicVP[seat]} shown)" : "") : $"{v.PublicVP[seat]} VP";
            string awards = (v.LongestRoadOwner == seat ? "\nLongest Road" : "") + (v.LargestArmyOwner == seat ? "\nLargest Army" : "");
            _stats[seat].Text = $"{vp}   {v.HandSizes[seat]} cards   {v.DevCardCounts[seat]} dev\n" +
                                $"road {v.RoadLength[seat]}   knights {v.KnightsPlayed[seat]}{awards}";

            var style = new StyleBoxFlat
            {
                BgColor = acting ? new Color(1f, 0.97f, 0.8f) : new Color(1, 1, 1, 0.35f),
                CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
                ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6,
                BorderColor = Ui.SeatColor(_setup.Colors[seat]),
            };
            style.SetBorderWidthAll(acting ? 3 : 1);
            _rows[seat].AddThemeStyleboxOverride("panel", style);
        }
    }
}
