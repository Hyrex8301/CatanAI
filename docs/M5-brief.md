# M5 Design Brief: Table Talk (Chat and Deals)

Draft 2026-09-24, for the user's approval. Adds a chat where players make spoken deals ("I won't rob you if you trade me a
wheat"), which the bots read, offer, accept and usually keep.

## Goals and done-when

| Milestone | Delivers | Done when |
|---|---|---|
| M5 Table talk | A chat panel you can type in; deals made of a normal trade plus a spoken promise; bots that read your messages, offer and accept deals when it pays, usually keep their word and remember who didn't | You can make and receive deals in a real game; bots' promises hold unless breaking them is worth a lot; broken promises are announced and remembered; deals stop once someone has 5 points; all tests pass; saved games still replay |

## Rules of a deal (user decisions, 2026-09-24)

- **No promise button.** A deal is said in the chat. The bots read what you type; they write their own offers and answers
  there too.
- **No gifts.** The cards always move as an ordinary trade (at least one card each way), so the engine and saved games don't
  change. A deal is lopsided when one side also gets a promise: "2 wheat for 1 sheep and I'll keep the robber off you".
- **Promises aren't enforced by the rules.** Bots should almost always keep them, but may break one when it's worth a lot.
  Everyone sees it in the chat ("Red broke their promise to you") and remembers: the breaker can still make deals, but
  others are less willing (they ask more for the same promise, or refuse when it's close). The same applies to you.
- **Early game only.** No new deals once anyone has 5 points (promises already made still run). Bots offer a deal only when
  their evaluation says it pays, at most one per bot per turn, not at every chance.
- **You can take part in everything**: accept or refuse a bot's deal, and propose your own (bots accept when it pays them).

## What a promise can say

| Promise | Example words | Bots read it as |
|---|---|---|
| No robber (non-block) | "nb", "I won't block you", "I'll keep the robber off your tiles" | the robber never goes on a tile next to their buildings |
| No stealing (non-steal) | "ns", "I won't steal from you", "won't rob you" | never picks them as the steal victim |
| No building at a spot | "I won't take 6 5 9", "don't take my 11 3 4" (a spot is the numbers around it, in any order; clicking a spot inserts them) | never settles on that spot or next to it |

**The community shorthand comes with a card:** "wheat nb?" or "wheat nb" = "trade me a wheat and I won't block you".
Since there are no gifts, the wheat goes in a 1-for-1 trade (the promiser picks what they give back, or it rides on the
trade already open between the two); "wheat ns", "nb ns" and "2 ore nb" work the same way.

**Length (user decisions):** nb and ns cover only the promiser's next robber move (a 7 or a knight), kept or broken,
unless a number is said: "for 2 turns" means the promiser's next 2 robber moves (players count knights, not turns;
such deals are rare). A spot promise lasts for the rest of the game unless a number of turns is said. Spots are named by
their numbers; when several open corners share them the reader says so and asks for more.
A promise usually comes with a trade: said while a trade offer between the two is open, it becomes part of that trade and
starts when the trade is done. Said on its own ("leave me alone and I'll leave you alone") it is a straight swap of promises.

## How it works

1. **Chat log and deal book** (Catan.AI, not game state): messages with who said them, and the promises made, who to, when
   they end, and whether they were kept. Saved alongside the game record, so a loaded game remembers its deals.
2. **Reading messages.** A phrase reader (no AI model): it finds the promise type from key words (rob, robber, block, steal,
   build, settle, spot), who it's for ("you" = the player you're trading with, or a colour name), the length, and whether it's
   an offer ("I won't…") or a request ("don't rob me…"). It shows what it understood under your line ("Deal: you won't rob
   Orange for 3 turns"), and says so when it didn't understand.
3. **Keeping promises.** When a bot chooses a robber tile, a victim or a building spot, moves that break a promise are
   dropped, unless the best breaking move beats the best kept one by a large margin (a setting, trainable later).
4. **Offering and accepting deals.** A promise's value comes from the evaluation: for the one protected, the expected damage
   the robber would do to them (chance of a 7 or knight × how likely they're the target × what it costs them); for the
   promiser, how much worse their best robber move gets without that player. A bot offers a deal when the other side should
   value the promise more than the extra card costs them, and accepts one when the promise plus the cards beat the trade
   without it.
5. **Trust.** Each seat remembers broken promises. A bot still deals with a breaker, but values their promises less
   (recent breaks count more), so it asks more or says no when the deal is close.
6. **Chat panel.** A "Log | Chat" switch on the right column, a text box, colour names, bots' lines ("Orange: I'll leave
   your wheat alone if you trade me a sheep"), and the reader's note under your lines.
7. **Measured in the Sim.** `match --deals` lets bots make deals in Sim games, so we can check deals don't make bots weaker.

## Build order and review checkpoints

| # | Build | Tests with it | Checkpoint |
|---|---|---|---|
| 1 | Chat log, deal book, phrase reader | many example sentences read correctly, unclear ones rejected; save and load keep deals | **A**: you try sentences and check what it understood |
| 2 | Bots keep promises (robber, victim, building) with a break margin; trust memory | kept unless the gain is over the margin; breakers are announced and trusted less | |
| 3 | Bots value, offer and accept deals; only before 5 points, at most one per turn | offers only when it pays; Sim with deals isn't weaker than without | |
| 4 | Chat panel in Godot, typing, bots' lines, your promises tracked and breaks detected | a scripted game makes, keeps and breaks deals | **B**: play games with deals |

## More decisions (2026-09-24)

- Bots can say whatever they like: besides deals, short reactions to steals, big builds, sevens and wins.
- Promise length: nb / ns the promiser's next robber move (a number = that many robber moves), spots the rest of the
  game (a number = turns). Spots are named by their numbers ("6 5 9"), not ids.
