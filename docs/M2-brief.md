# M2 Design Brief: Playable Board in Godot

Approved 2026-09-22. Follows the M0 + M1 brief ([M1-brief.md](M1-brief.md)) and builds on the finished M1 engine.

## Goals and done-when

M2 turns the rules engine into a game you can play with the mouse: the real board drawn in Godot, you in one seat, three bots in the others. The bots are still `RandomBot` (smart bots are M3), so the goal is a complete, correct, pleasant interface, not a challenging opponent.

| Milestone | Delivers | Done when |
|---|---|---|
| M2 Board UI (~3–4 weeks) | Board rendering, a human agent, every action reachable by mouse (setup, building, dice, discards, robber, dev cards, bank trades, colonist-style player trades), player panels, event log, new-game settings, save / load / resume | You can play full games start to finish with only the mouse; the UI never offers an illegal action; a game saved from the UI replays in the Sim to the identical hash; all tests pass |

Out of scope for M2: smarter bots and the hand tracker (M3), the sprite / texture art pass (after M2), a seat-color preference, sound, online multiplayer, the review timeline (M9). (Key animations were moved into M2 on 2026-09-23; see Part 2.)

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

## Part 2: colonist.io-style interface (2026-09-23)

Steps 1–3 plus the gap fill already made every action playable with plain controls. Part 2 replaces the old steps 4–12. It rebuilds the screen so it looks and plays like colonist.io. The Principles and Architecture sections above still apply (the UI never decides rules, draws only from your view, keeps Godot code thin, records every game).

User decisions: colonist layout; icons drawn in code through the skin layer (no asset files, the art pass swaps them later); key animations now, sound later.

**Layout (1600×900).**

```
+-------------------------------------------+---------------+
|                              [bank 5 + dev]|  LOG          |
|                                            |  icons inline |
|              BOARD (about 1150×700)        |               |
|                                            +---------------+
|        [trade offer / popup area]          | player cards  |
|                                            | (3 opponents) |
+-------------------------------------------+---------------+
| YOUR HAND: real cards, grouped with counts | you | Trade Dev Road Sett City | dice | End |
+-----------------------------------------------------------+
```

- **Board**: larger, on the sea. Harbors as small docks with a ratio and a resource icon. Number tokens with pips. Terrain gets a small icon.
- **Player cards** (right): color avatar, name, big VP, card back with hand size, dev card back with count, knights, road length. The Longest Road and Largest Army badges light up for their holder. The acting player is highlighted, with a turn bar. Your own card sits beside your hand.
- **Log** (right): colored names and small card icons instead of words ("Blue got [wool][wool]"). Only what your seat may see.
- **Hand** (bottom): real card faces with icons, one stack per resource with a count badge, then dev cards. Cards bought this turn are dimmed. Hovering a stack shows the full name.
- **Action bar** (bottom right): Trade, Buy Dev Card, Road, Settlement, City. Each button shows its cost on hover and the pieces you have left. A button is greyed out when unaffordable or when there's no legal spot, and the engine's reason becomes its tooltip. Then the dice (click to roll) and End Turn. Space rolls or ends the turn, Esc cancels a mode.
- **Bank** (top-right corner of the board): cards left of each resource, dev deck size.

**How moves feel.**

- **Building**: click Road / Settlement / City and only then do the legal spots glow (pulsing). Click one to build, or Esc / click the button again to cancel. Setup highlights spots automatically. The spots come from the legal list, as now.
- **Dice**: click the dice or press Space. The dice animate, the rolled number's hexes flash, and cards fly from those hexes to each receiving player.
- **Sevens**: the discard popup works by clicking cards in your hand ("Discard 4 cards", counter, confirm). For the robber, hexes glow; when more than one player could be robbed, a small popup picks the victim (avatar and card count).
- **Dev cards**: click a card in your hand and it lifts up with a Play button (disabled, with the reason, if not playable now). Year of Plenty and Monopoly open a resource picker. Road Building glows edges twice.
- **Trade window** (colonist's three-row panel): the Trade button opens it above your hand, and so does clicking a resource card in your hand (the window opens with that card already in the "give" row; while it's open, clicking hand cards adds more). This applies whenever you may trade, outside discards and dev card flows, where clicking a card means picking it. Tabs are Players and Bank. The top row is what you give (click your cards), the middle row is what you get (click resources), and the bottom row is your hand as it would be after the trade. Bank tab: your ratios printed on each resource and a legend of your harbors. Your open offers stack above the board's bottom edge, one strip each with every opponent's status icon (waiting / accepted / declined / countered). Click an accepter to trade. Counters show as offer cards.
- **Bot offers to you**: a popup card above your hand with Accept (✓, disabled if you can't pay), Decline (✗) and Counter, plus a countdown bar (the 20 s response window).
- **Feedback**: short toasts for things that happen to you ("Blue stole a Wool from you", "You got Longest Road"). New pieces pop in and the robber slides.
- **Game over**: an overlay with the final standings, each player's VP breakdown (settlements, cities, cards, awards) and a histogram of the dice rolled. Buttons for New game and Menu.

**Code shape.** New plain C# in `Catan.UI` so it can be tested: `BuildMenu` (button states: affordable, placeable, pieces left, reason), `TradeBuilder` (give / get rows, bank ratios, validation through `IsLegal`), `InteractionMode` (none / build road / settlement / city / robber / Road Building) and turning clicks into actions, `LogText` (log lines as text plus icon runs). Godot: `Hud/HandBar`, `Hud/ActionBar`, `Hud/PlayerCards`, `Hud/TradeWindow`, `Hud/OfferPopups`, `Hud/Toasts`, `Hud/GameOver`, plus `IconSkin` (resource, dev card, knight, road, VP icons) next to `BoardSkin`. An `Animator` queues animations from applied events. The game loop waits for animations to finish before the next bot move (the bot delay still applies).

**Build order.**

| # | Build | Tests with it | Checkpoint |
|---|---|---|---|
| 4 | New layout skeleton; `IconSkin`; hand bar with real card faces; player cards; bank | card grouping, counts from the view | **A2**: the new screen looks right |
| 5 | Action bar: Trade and Dev card buttons, build modes with costs, pieces left and disabled reasons; clickable dice that show every roll (who rolled what); End Turn; keyboard | bar states over random positions match the legal list | |
| 6 | Trade window with card faces (players and bank tabs, multi-lot bank trades), open offer strips with answer chips, bot offers with countdown (moved up from 8 at the user's request) | bank ratios and lots are legal; bad picks say why | |
| 7 | Log with icons; toasts | log text for every event kind, nothing hidden | |
| 8 | Discard from the hand, robber and victim popup, dev card play flows | modes produce exactly the legal actions | **B2**: a whole game played through the new UI |
| 9 | Animations: dice, flashing hexes, flying cards, piece pop, robber slide, turn bar | animation queue ordering; game loop waits | **C2**: feels like colonist |
| 10 | Game-over overlay with standings and dice histogram; menu restyle; manual play-test checklist | VP breakdown, histogram | **D**: M2 sign-off |

The old checkpoints B–D above are replaced by A2–D.

## Test plan

- **Automated** (`Catan.Tests`, runs in CI): hit-testing; every interaction-model mode produces exactly the actions the engine lists, and only those (checked against `GetLegalActions` / `IsLegal` over random game positions, like M1's consistency tests); HumanAgent task completion, cancellation and the response-window timeout; save → load → resume ends at the same hash as an uninterrupted game.
- **Manual play-test checklist** (in this folder at step 12): a scripted list of situations to click through, e.g. both setup rounds, a 7 with a discard, robbing with two possible victims, each dev card before and after rolling, 2:1 / 3:1 / 4:1 trades, an offer accepted by two bots, a counter-offer, turning down an acceptance, Road Building with one road left, winning on a bought VP card, save mid-trade and resume.

## Decisions (2026-09-22)

1. **Look:** M2 uses the clean flat style (shapes drawn in code, no art assets). A **sprite / texture art pass comes later**, after M2 sign-off. To make that a drop-in change, `BoardView` and the HUD draw through a small skin layer (`BoardSkin`: terrain, tokens, pieces, robber, harbors, card faces) instead of hard-coding shapes, so the art pass replaces the skin, not the views.
2. **Window:** designed for **1600×900**, scaling to other sizes (Godot `canvas_items` stretch, keep aspect).
3. **Seat and color:** your seat and color are **random each game**. A color-preference setting is planned for later, not M2.
4. **Timing:** bot delay **0.5 s** after each bot action; **20 s** response window for bot offers (with a Skip button). Both adjustable in settings.
