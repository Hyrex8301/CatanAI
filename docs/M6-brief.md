# M6 Design Brief: Game Modes

Approved 2026-09-25 with changes (see "Decisions"). Adds a mode select screen, a fuller placement practice, saving and
loading positions, and position practice with starter scenarios. Built in four small milestones, stopping after each for
testing. **No difficulty tiers:** the user wants only the strongest bot, playing as well as it can without cheating.

## How things work today

**Setup.** The menu (`game/scripts/Menu.cs`) is a list of buttons: Continue, New game, Load game, Placement practice,
Settings, How to play, Quit. Settings live in `GameOptions` (Catan.UI, saved to `user://settings.json`): points to win,
bot speed, answer time, friendly robber, colour, full screen, volume. A new game calls `GameSetup.Create(seed)`: one master
seed decides your seat, the colours, and separate seeds for the board, the dice and the bots, so any game can be recreated
from its seed. `GameSession` (static) carries the options and a game to resume from the menu to the game scene.
`GameScreen.CreateRunner` builds the four agents: you (`HumanAgent`, driven by the UI) and three bots, each a `SearchBot`
wrapped in `BackgroundAgent` (it thinks on a worker thread), all sharing one `TableTalk` for chat and deals.

**Game state.** `GameState` (Catan.Core) is plain data: the board, pieces per vertex and edge, every hand, dev hands,
bank, dev deck counts, robber, phase, current player, points, awards, open trades. The rules are static functions in
`Rules`; all randomness comes through `IChance`. **The engine is built for exactly 4 players** (`GameConstants.PlayerCount`,
used in about 25 places in Core plus the AI's hand tracker, which stores 4 hands). Saves are **replay records**
(`GameRecord`): the board layout, settings, every action and every dice / steal / draw outcome. Loading replays them, so a
save reproduces the exact position, hidden cards included, and a hash check proves it. `GameRunner.Resume` continues a
record with new agents. There's no format for "a position with no history" yet.

**The AI** (Catan.AI), bottom to top:
- `Evaluator`: scores a position for one seat as a weighted sum of about 50 features (production, combos, ports, hand,
  roads, army, threats and so on), with trained weights in `game/bots/best.json`.
- `Planner`: a beam search over the bot's own moves this turn (depth, beam width, samples of hidden cards), with dice,
  draws and steals weighted by their exact odds.
- `HandTracker` + `Determinizer`: track every possible combination of hidden hands from the public log (the only unknowns
  are steals between two other players), then sample full states from them. Bots only ever see their own `PlayerView`.
- `SmartBot`: planner + trading (`Trading`) + table talk (`DealMaker`, `PromiseKeeping`) + dev card timing. Settings:
  `Depth`, `Beam`, `Samples`, `Temperature` (randomness in move choice), `Trades`.
- `SearchBot`: SmartBot plus a multi-turn look-ahead (paired playouts of its top moves, 16 turns deep, scored as win
  chances). **This is what the game uses today** (0.5 s per move). In Sim games it wins about 40% against three SmartBots
  (25% would be equal).
- `PlacementCoach`: already rates every opening spot with the bots' evaluation, with pips, resources, port, starting cards
  and reasons in plain words, and deals practice boards. It is UI-free, so it's the seed of the coach service below.
- Tools: `Catan.Sim match / ladder` measure strength between bots; the trainer tunes weights overnight.

## What gets built

### 1. Mode select (milestone M6.1)

**Mode select screen.** The main menu becomes: Continue, **Play** (opens the mode list), Load game, Settings, How to play,
Quit. The mode list shows one card per mode: title, one line of description, and its setup panel. Modes are entries in one
table (`GameModes`: id, title, description, a setup panel, a start function), so a new mode (coach, review, multiplayer)
is one more entry. Modes not built yet are simply absent (no greyed-out "coming soon" clutter).

**Normal game setup:** your colour, board (Random, or a seed you type; the game shows its board seed so you can replay a
board), points to win, and the existing options. Choices are remembered.

**Players:** you and 3 bots, the standard 4-player game (decided).

**The bot:** one bot, the strongest: today's SearchBot (the smart AI plus the multi-turn look-ahead), with honest hand
tracking and no cheating. Making it stronger continues as its own work (training new features, as before), not as part of
this milestone.

**Difficulty tiers:** dropped (decision 2).

### 2. Placement practice, full opening (milestone M6.2)

Builds on the existing practice screen and `PlacementCoach`.
- **Board:** random or a seed (shown so you can come back to it).
- **The full opening in snake order:** you place settlement + road at your turns, bots place at theirs.
- **Feedback toggle:** after each of your placements, or all at the end.
- **For each of your placements:** your spot's score and rank, and the best 3–5 spots, with score and reasons. Reasons
  come from the evaluation's feature changes, plus explicit facts:
  - pip count and resource mix
  - port
  - which resources are scarce on this board (total pips per resource)
  - expansion (open spots your roads can reach)
  - how exposed you are to being blocked (other players' roads and settlements near your next spots)
  The spots are highlighted on the board (gold / silver / bronze, as today).
- **Buttons:** "Retry this board" (same board and bot placements), "New board", "Play the game out from here" (starts a
  normal game from the finished opening, with the same bots).
- **The coach logic stays out of the UI:** `PlacementCoach` grows into a `Coach` service in Catan.AI (rate moves, explain
  them), which position practice and a future coach mode reuse.

### 3. Saving and loading positions (milestone M6.3)

- **Games:** the existing saves (replay records) already restore everything exactly, hands and dev cards included, and
  stay as they are.
- **Positions:** a new position file (JSON) stores the full state directly, with no history: the board layout, every
  building and road, all hands, dev cards in hand (by type) and played, bank, dev deck counts, robber, awards, knights,
  points, phase, whose turn it is, and the settings. It's written and read by Catan.Core (`Position.ToJson` / `FromJson`),
  checked by the state validator on load, and hashed so a round trip is provably exact.
- **"Save position"** goes in the in-game gear menu, available at any moment of any game.
- **A game can start from a position:** the runner starts from the loaded state, and records started that way store the
  starting position, so they save, load and replay like any other game. Old saves keep working (tested).
- **What bots know after a load:** a position has no history, so bots start by knowing only hand sizes. Their hand
  tracking fills in from there, exactly as it would for a player who just sat down. A scenario can also mark hands as
  known to everyone (for "you know they hold 2 ore" puzzles).
- **Tests:** round trips (save, load, compare hashes) for fresh, mid-game and end-game positions and every phase; starting
  and replaying from a position; old records still loading.

### 4. Position practice (milestone M6.4)

- **A scenario list** in the mode screen: title, a short description, and its type:
  - **Decision:** one move to make. The Coach grades your move against all legal moves (your rank, the best moves with
    reasons, like placement practice), then offers "play it out".
  - **Play out:** play to the end against the bots.
- **Load any saved position** as a custom scenario.
- **Starter scenarios**, shipped as position files:
  1. Longest road or a city: you hold cards for either; which is better here?
  2. Robber placement: a 7 with several reasonable targets.
  3. A trade offer when someone is 1 point from winning.
  4. When to play a knight: blocked on your best hex, knights in hand, army race close.
- **Grading uses the strongest evaluation** (search look-ahead), so a scenario's "best" answer is as good as the bots can
  judge. Where the top moves are close, it says so rather than pretending one is clearly right.

## Build order and checkpoints

| # | Build | Tests with it | Checkpoint |
|---|---|---|---|
| M6.1 | Mode registry and mode screen; normal game setup (colour, board: random or a seed) | settings round trip; a board seed gives the same board; the game still starts and plays | **A**: try the mode screen |
| M6.2 | Placement practice: full snake opening, bots at their turns, feedback toggle, top 3–5 with reasons and highlights, retry / new board / play out; `Coach` service | coach ranks and reasons on fixed boards; the opening sequence follows the snake order; "play out" starts a legal game | **B**: try the practice |
| M6.3 | Position format, save position, start games from positions, records that start from a position | round trips in every phase; replay from a position; old saves load | **C**: save and load positions |
| M6.4 | Position practice: scenario list, decision grading, play out, 4 starter scenarios | each scenario loads and validates; grading ranks the intended answer near the top | **D**: try the scenarios |

Nothing existing breaks: the current normal game stays reachable at every step, and the bot is unchanged, so the trained
weights, the Sim tools and the tests keep working.

## Decisions (user, 2026-09-25)

1. **Players:** you and 3 bots, the standard 4-player base game. No 2- or 3-player games.
2. **No difficulty tiers.** Only the strongest bot (today's SearchBot), as good as it can be.
3. **No cheating:** the bot sees only what a player could see.
4. **Modes:** all three: Normal game, Placement practice, Position practice.
5. **Menu:** just Play (the mode list), Settings, How to play and Quit. No Continue or Load game on the main menu; saved
   games and positions load through Position practice (M6.3, M6.4).
