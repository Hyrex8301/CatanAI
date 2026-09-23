using Catan.Core;

namespace Catan.UI;

/// <summary>How an opponent has answered one of your offers, for the status chips.</summary>
public enum OfferAnswer { Waiting, Declined, Accepted, Countered }

/// <summary>Trade window helpers that work from your <see cref="PlayerView"/> only: bank ratios and bank trades.</summary>
public static class TradeModel
{
    /// <summary>Your bank ratio per resource (4, or 3 / 2 with harbors), the same rule as <see cref="Rules.TradeRatios"/>.</summary>
    public static int[] Ratios(PlayerView v)
    {
        var ratios = new[] { 4, 4, 4, 4, 4 };
        for (int vertex = 0; vertex < Topology.VertexCount; vertex++)
        {
            int spot = Topology.VertexHarbor[vertex];
            if (spot < 0 || v.VertexOwner[vertex] != v.Seat)
                continue;
            var type = v.Board.HarborTypeAt(spot);
            if (type == HarborType.Generic)
                for (int r = 0; r < ratios.Length; r++)
                    ratios[r] = Math.Min(ratios[r], 3);
            else
                ratios[(int)type] = 2;
        }
        return ratios;
    }

    /// <summary>
    /// A bank trade as picked in the window (any number of lots, e.g. 8 brick + 3 wool for 2 ore + 1 grain at 4:1 and 3:1),
    /// split into the engine's one-lot BankTrade actions in order. False with the reason when the pick doesn't work yet.
    /// </summary>
    public static bool TryBankTrades(PlayerView v, ResourceSet give, ResourceSet get, out List<GameAction> trades, out string reason)
    {
        trades = new List<GameAction>();
        var ratios = Ratios(v);
        if (give.Total == 0 || get.Total == 0)
            return Fail("Pick what you give and what you get", out reason);
        if (!give.FitsIn(v.Hand))
            return Fail("You don't have those cards", out reason);

        var lots = new List<int>();
        for (int r = 0; r < GameConstants.ResourceCount; r++)
        {
            if (give[r] == 0)
                continue;
            if (get[r] > 0)
                return Fail($"You can't give and get {GameText.Resource(r)} at once", out reason);
            if (give[r] % ratios[r] != 0)
                return Fail($"Give {GameText.Resource(r)} in groups of {ratios[r]} (your rate is {ratios[r]}:1)", out reason);
            for (int k = 0; k < give[r] / ratios[r]; k++)
                lots.Add(r);
        }
        if (lots.Count != get.Total)
            return Fail(lots.Count > get.Total
                ? $"That pays for {lots.Count} cards: pick {lots.Count - get.Total} more to get"
                : $"That pays for {lots.Count} card{(lots.Count == 1 ? "" : "s")}: get fewer, or give more", out reason);
        for (int r = 0; r < GameConstants.ResourceCount; r++)
            if (get[r] > v.Bank[r])
                return Fail($"The bank has only {v.Bank[r]} {GameText.Resource(r)}", out reason);

        int lot = 0;
        for (int r = 0; r < GameConstants.ResourceCount; r++)
            for (int k = 0; k < get[r]; k++, lot++)
                trades.Add(new GameAction(ActionType.BankTrade, v.Seat,
                    Give: ResourceSet.Of((Resource)lots[lot], ratios[lots[lot]]), Get: ResourceSet.Of((Resource)r)));
        reason = "";
        return true;
    }

    public static OfferAnswer Answer(TradeOffer offer, int seat) => (OfferAnswer)offer.ResponseOf(seat);

    private static bool Fail(string why, out string reason)
    {
        reason = why;
        return false;
    }
}
