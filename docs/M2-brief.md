# M2 Design Brief: Playable Board in Godot

Approved 2026-09-22. Follows the M0 + M1 brief ([M1-brief.md](M1-brief.md)) and builds on the finished M1 engine.

## Goals and done-when

M2 turns the rules engine into a game you can play with the mouse: the real board drawn in Godot, you in one seat, three bots in the others. The bots are still `RandomBot` (smart bots are M3), so the goal is a complete, correct, pleasant interface, not a challenging opponent.

| Milestone | Delivers | Done when |
|---|---|---|
| M2 Board UI (~3–4 weeks) | Board rendering, a human agent, every action reachable by mouse (setup, building, dice, discards, robber, dev cards, bank trades, colonist-style player trades), player panels, event log, new-game settings, save / load / resume | You can play full games start to finish with only the mouse; the UI never offers an illegal action; a game saved from the UI replays in the Sim to the identical hash; all tests pass |

Out of scope for M2: smarter bots and the hand tracker (M3), the sprite / texture art pass (after M2), a seat-color preference, animation and sound polish, online multiplayer, the review timeline (M9).

## Principles

- **The UI never decides rules.** Everything clickable comes from `Rules.GetLegalActions`, and anything the player builds (discards, trade offers, counters) goes through `Rules.IsLegal`, whose reason text becomes the tooltip. If the engine and UI ever disagree, the engine wins.
- **You see what your seat may see.** The UI draws from your `PlayerView`, never from `GameState`, so the interface can't leak hidden cards any more than a bot can. A developer toggle can show every hand for debugging.
- **Godot code stays thin.** Anything that can be tested without Godot (click routing, interaction modes, action descriptions, hit-testing) goes in a new plain C# library, `src/Catan.UI`, and is covered by `Catan.Tests`. Godot scripts only draw and forward input.
- **Games are always recorded.** Every game played in the UI runs through `GameRunner`, so it can be saved, replayed and resumed exactly.

## Architecture

```
src/Catan.UI/                 plain C# (no Godot): interaction model, hit-testing, text, settings
game/
  scenes/Game.tscn            the playable game
  scenes/Debug.tscn           the M1 DebugView, kept for development
  scenes/Menu.tscn            new game / load game / settings
  scripts/GameController.cs   owns the GameRunner, drives the loop, bot pacing
  scripts/HumanAgent.cs       IPlayerAgent completed by UI input
  scripts/BoardView.cs        board, pieces, robber, highlights, click hit-testing
  scripts/Hud/*.cs            player panels, hand, dev cards, dice, buttons, dialogs, trade panel, log
```

**Game loop.** `GameController` runs `GameRunner.StepAsync` in a loop on Godot's main thread (Godot's C# synchronization context brings `await` continuations back to the main thread). Bots answer instantly; the controller waits a configurable delay after each bot action (default 0.5 s) so you can follow along. When it's your decision, `HumanAgent.DecideAsync` returns a `Task` that completes when you click.

**Your answers to bot trade offers.** The runner asks every optional seat at once and waits for all of them. For your seat, `HumanAgent.RespondAsync` waits until you accept, decline, counter or dismiss the offer, or until a response window times out (default 20 s, "Skip" button). That keeps the engine's determinism and recording unchanged while giving you a fair chance to answer, like colonist.io.

**Interaction model** (`Catan.UI.InteractionModel`). Given your view and legal list, it holds the current mode and turns clicks into actions:

| Situation | What you click | Action produced |
|---|---|---|
| Setup | highlighted vertex, then highlighted edge | BuildSettlement, BuildRoad |
| Main, build mode Road / Settlement / City | highlighted edge / vertex / own settlement | the build |
| PreRoll / Main | Roll, End Turn, Buy Dev Card buttons | the matching action |
| MoveRobber | highlighted hex; then a victim if more than one | MoveRobber with victim |
| Discard | pick cards in a dialog until the owed count is reached | Discard |
| Dev card | a playable card, then its choices (resources for Year of Plenty / Monopoly, roads for Road Building) | the Play* action, then free roads |
| Bank trade | give / get pickers showing your ratios | BankTrade |
| Player trade | trade panel (below) | OfferTrade / EditOffer / Confirm / Decline / Cancel / Accept / Counter |

Highlights come straight from the legal list: only legal targets glow, and hovering an illegal one shows the `IsLegal` reason.

**Board rendering** (`BoardView`). Draws through `BoardSkin` (flat shapes now, sprites in the later art pass). Uses `Topology` pixel positions (already in Core). Terrain colors, number tokens with pips (6 and 8 red), harbors with ratio labels, roads, settlements and cities in seat colors, the robber, legal-target highlights and hover. Hit-testing (pixel → nearest vertex / edge / hex within a radius) lives in `Catan.UI` and is unit-tested.

**Player trade panel** (colonist.io style, matching the M1 rules):

- Your turn: build an offer (give / get pickers), send it to everyone; up to 10 offers and edits per turn, several open at once. Each open offer shows every opponent's answer (waiting / accepted / declined / countered). Click an accepter to trade, or turn them down, edit, or cancel. Counters appear as cards you accept or decline.
- Someone else's turn: their open offers appear with Accept (if you can pay), Decline and Counter.

**HUD.** Four player panels (name, color, public VP, card count, dev card count, road length, knights, Longest Road / Largest Army badges, "to act" indicator); your hand with resource cards; your dev cards (bought-this-turn shown as not yet playable); dice with the last roll; bank counts and dev deck size; the event log (from your redacted log, so you see "Blue stole a card from Red" but not which one); a game-over screen with final scores.

**Menu and settings.** New game: board seed (or random), VP target, friendly robber, bot speed, response window, show-all-hands (developer). Your seat and color are random each game. Load game.

**Save / load / resume.** Saves are `GameRecord` JSON in Godot's user folder (`user://saves/`), plus an autosave at the end of each of your turns. Loading replays the record and continues the game. This needs one engine addition: `GameRunner` must be able to start from a record (replay it, then keep playing and recording, with the chance source continuing deterministically from a seed stored in the record). The saved file replays in `Catan.Sim replay` to the identical hash.

## Build order and review checkpoints

Same rhythm as M1: small steps, tests with each step, stop at checkpoints for your review.

| # | Build | Tests with it | Checkpoint |
|---|---|---|---|
| 1 | `Catan.UI` project; Game / Debug / Menu scenes; screen layout skeleton | project wiring | |
| 2 | `BoardView` from a `PlayerView`; hit-testing | hit-testing picks the right vertex / edge / hex | **A**: board looks right, clicks land |
| 3 | `HumanAgent`, `GameController`, bot pacing, turn indicator, event log | HumanAgent completes / cancels correctly | |
| 4 | Setup interaction with legal highlights | interaction model: setup | |
| 5 | Dice, build modes with costs, hand display, End Turn | interaction model: main phase | |
| 6 | Discard dialog, robber placement, victim chooser | interaction model: sevens | **B**: full game playable (without your own trades and dev cards) |
| 7 | Dev cards: buy, playable states, Knight / Road Building / Year of Plenty / Monopoly flows | interaction model: dev cards | |
| 8 | Bank trade dialog with your ratios | ratios and dialog output | |
| 9 | Player trade panel (offers, edits, answers, counters, response window) | interaction model: trades; response window timeout | **C**: every action reachable |
| 10 | Player panels, awards, game-over screen, menu and settings | text / formatting | |
| 11 | Save / load / resume (engine: `GameRunner` from a record), autosave | resumed game equals the uninterrupted game's hash | |
| 12 | Polish pass and the manual play-test checklist | full test suite | **D**: M2 sign-off |

What gets checked at each checkpoint: it looks and feels right, nothing illegal is offered, nothing hidden is shown, and nothing makes M3 (smart bots, hand tracker) harder.

## Test plan

- **Automated** (`Catan.Tests`, runs in CI): hit-testing; every interaction-model mode produces exactly the actions the engine lists, and only those (checked against `GetLegalActions` / `IsLegal` over random game positions, like M1's consistency tests); HumanAgent task completion, cancellation and the response-window timeout; save → load → resume ends at the same hash as an uninterrupted game.
- **Manual play-test checklist** (in this folder at step 12): a scripted list of situations to click through, e.g. both setup rounds, a 7 with a discard, robbing with two possible victims, each dev card before and after rolling, 2:1 / 3:1 / 4:1 trades, an offer accepted by two bots, a counter-offer, turning down an acceptance, Road Building with one road left, winning on a bought VP card, save mid-trade and resume.

## Decisions (2026-09-22)

1. **Look:** M2 uses the clean flat style (shapes drawn in code, no art assets). A **sprite / texture art pass comes later**, after M2 sign-off. To make that a drop-in change, `BoardView` and the HUD draw through a small skin layer (`BoardSkin`: terrain, tokens, pieces, robber, harbors, card faces) instead of hard-coding shapes, so the art pass replaces the skin, not the views.
2. **Window:** designed for **1600×900**, scaling to other sizes (Godot `canvas_items` stretch, keep aspect).
3. **Seat and color:** your seat and color are **random each game**. A color-preference setting is planned for later, not M2.
4. **Timing:** bot delay **0.5 s** after each bot action; **20 s** response window for bot offers (with a Skip button). Both adjustable in settings.
