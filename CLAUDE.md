# CatanAI

A Catan (base game) AI project: a C# rules engine, bots, a simulation CLI, and a Godot front end. The plans are **[docs/M1-brief.md](docs/M1-brief.md)** (engine: ids, rules, APIs, tests), **[docs/M2-brief.md](docs/M2-brief.md)** (playable Godot UI) and **[docs/M3-brief.md](docs/M3-brief.md)** (smart bots, overnight training). Read the relevant one before working on a milestone.

## Git and commit rules

- **All commits are authored by the repo owner** (Hyrex8301). Use the configured git identity as-is. Never change `user.name` / `user.email` or pass `--author`.
- **No AI attribution anywhere.** No `Co-Authored-By: Claude` (or any AI) trailers, no "Generated with Claude Code" lines, no AI mentions in commit messages, PR titles/descriptions, code comments, or docs.
- **Never commit secrets:** no API keys, tokens, passwords, `.env` files, credentials, or personal data. Check `git status` / `git diff --cached` before committing, and stop and ask if anything looks sensitive.
- Never commit `.claude/` (local tool settings; gitignored). Review `git status --short` *before* committing, not after.
- Commit messages are short and imperative, describing the change (e.g. `M1 step 2: topology ids and adjacency tables`).
- Don't force-push, rewrite history, or delete branches without asking.

## Status

- **M0 (setup): done** 2026-09-22. Godot shows "Catan.Core says: 19 hexes" and CI is green.
- **M1 (rules engine): signed off 2026-09-22** (all 15 steps, checkpoints A–D). M1 can still change later; tests, golden records and CI fuzz guard it.
- **M2 (playable Godot UI): in progress**, plan in `docs/M2-brief.md` (written by Claude, approved by the user; the original brief only covered M0/M1). Steps 1–3 done plus the gap fill (2026-09-22): a full game is playable with plain controls: board clicks, buttons, a discard picker, a trade panel (offers, edits, counters, answers), save / autosave / continue / load (`GameRunner.Resume`; dice after a resume are seeded from the save point).
- **M2 Part 2 (colonist.io-style UI, approved 2026-09-23)** replaces the old steps 4–12 with steps 4–10, checkpoints A2 (after 4), B2 (after 7), C2 (after 9), D (after 10). Decisions: colonist layout, icons drawn in code through `IconSkin` (art pass swaps it later), key animations now, sound later. Trading opens from the Trade button **or by clicking a resource card in your hand** (that card goes into "give"). Checkpoint A2 passed; **steps 5 and 6 done** (the trade window moved up from step 8 at the user's request; brief renumbered: 7 log icons / toasts, 8 sevens and dev card flows, then B2). Step 5: `ActionBar` (Trade, Dev card, Road / Settlement / City with cost chips and pieces left, clickable `DiceView` showing everyone's last roll, End Turn; states from `ActionBarModel`, build modes highlight spots only after you pick one; Space rolls / ends the turn). Step 6: `TradeWindow` with card-face give / get rows, Players and Bank tabs (multi-lot bank trades split by `TradeModel.TryBankTrades` and submitted one by one), offer strips with answer chips (click an accepter to trade), bot offers with a countdown. Dev card plays are still plain buttons in a column on the board's right edge until step 8. Step 4: new layout (board top left, bank in its corner, log and `PlayerCard`s down the right with you at the bottom, `HandBar` of real card faces along the bottom, prompt banner under the board); `HudModel` (Catan.UI) builds hand stacks and seat summaries from the view. Esc no longer leaves the game (closes the trade window / cancels a choice).
- Dev screenshots: `CATAN_SHOT=out.png CATAN_SEED=7 CATAN_AUTOPLAY=200 CATAN_SHOT_AFTER=15 <godot mono console exe> --path game res://scenes/Game.tscn` saves a screenshot and quits (autoplay: a bot plays your seat, no pauses, for the first N actions). Godot .NET build is at `E:\GODOT\Godot_v4.7.2-stable_mono_win64\`; run `dotnet build game/CatanGame.csproj` first.
- **M3 (smart bots + overnight training): steps 1–7 done, checkpoints A and B passed; checkpoint C result in (awaiting review), then D (play the trained bots in Godot).** SmartBot (Play settings) beat 3 RandomBots in 400/400 games; `Catan.Sim match` compares bots (identical bots split 25.4% ± 1.3%, so it's fair).
- First overnight run (`training/night1`, 2026-09-22/23): 7 h, 4,680 generations, 6.07M games, 169 champions. Its `best.json` beats the starting weights **45.8% ± 2.2%** (2,000 fresh games, Play settings, 1v3; fair = 25%) and RandomBots 400/400. It plateaued after ~1.5 h (champion vs start stays ~45%); later champions pass the 28.9% bar without getting stronger vs the start. Bundled as `game/bots/best.json` (`BundledGameWeightsLoadAndNameEveryWeight` fails if it stops naming every weight).
- Step 7: the brief's Random / Smart / Trained picker is dropped per decision 1 (strongest only); the game always uses SmartBot with the bundled weights.
- Trainer: `Catan.Sim train --out training/<run> --hours 8` (resumes from its checkpoint; Ctrl+C stops safely). Output: `state.json`, `best.json`, `progress.csv`. Keep good weights by copying `best.json` to `game/bots/best.json` (the game loads it; defaults otherwise) and committing it. `training/` is gitignored.
- The user knows training tunes weights of existing features; weak ideas need new features, and multi-turn search (ISMCTS) is the next big strength step. Goal for the first night: a SmartBot with turn planning and a self-play trainer running. Decisions: strongest only (no difficulty levels); training uses all but 2 threads; smart-vs-smart self-play with a pool of past champions; ISMCTS multi-turn search is the next milestone.
- M2 decisions: flat drawn style now, sprite / texture art pass after M2 (draw through a `BoardSkin` so it's a drop-in swap); 1600×900 window; human seat and color random each game (color preference later); bot delay 0.5 s, 20 s response window for bot offers.
- M1 done-when, as of 2026-09-22: 100,000 validated RandomBot games with 0 violations (plus 100,000 pure-random, also 0); every saved record replays to its hash; all tests pass.
- **Bench baseline** (2026-09-22, Ryzen 7 5800X, 16 threads, `bench --seconds 10`, no validation): **1,136 games/s, 1.04M actions/s**, avg 271.5 turns, 3.3% turn-cap draws. Compare later milestones against this.
- `RandomBot` (Catan.AI) is a **tester, not the real AI**: it exists to find engine bugs and as the weakest baseline. Smart bots start in M3.
- CI runs 2,000 weighted + 500 pure-random validated fuzz games per push with fresh seeds (`run_number * 100000`) and uploads `failures/` as an artifact on failure. To turn a fixed failure into a permanent test: `dotnet run -c Release --project src/Catan.Sim -- random --games 1 --seed N [--pure] --validate --save tests/Catan.Tests/Records/regressions`.
- Records: `GameRunner` wraps its chance in `RecordingChance`; `runner.ToRecord(seed)` → `GameRecord` (JSON via `ToJson` / `FromJson`); `record.Replay(stopAfter, validate)` reports the first bad action or a hash mismatch. `ReplayChance` reads dice / steals / draws from separate queues.
- **Every `.json` under `tests/Catan.Tests/Records/` is replayed by `RecordTests.SavedRecordStillReplays`.** `golden/` holds 3 full random games (regenerate only on purpose with `CATAN_WRITE_GOLDEN=1`, never to hide a failure: a failing golden replay means old saves broke). Sim failures that get fixed are copied in here as permanent regression tests.
- Agents only ever get a `PlayerView` (a copy; `PlayerViewTests` prove it leaks nothing). View-based helpers for bots: `Rules.RandomDiscard(view, rng)`, `RandomTradeOffer(view, rng)`, `RandomEditOffer(view, rng)`, `RandomCounterOffer(view, rng)`. `DevCardBought.Type` is nullable (null = hidden); `CardStolen.Resource` is -1 when hidden.
- Trade offers, edits and counters, like discards, are never in the legal list; `Rules.RandomTradeOffer` / `RandomEditOffer` / `RandomCounterOffer` build random valid ones.

## Changes from the brief (user decisions)

- **Player trading follows colonist.io** (decided 2026-09-22, replaces the brief's TradeReply/TradeConfirm phases and moves counter-offers from M5 into M1):
  - All trading happens inside Main. The current player may have up to 10 offers open at once (`GameConstants.MaxOpenOffers`); every offer goes to all opponents. New offers **and edits** count toward `MaxOffersPerTurn` (default 10). An edit resets that offer's responses.
  - Responses are **truly simultaneous**: opponents may Accept / Decline / Counter any open offer at any time, in any order, once per offer version. The game never waits on them. `ActingSeat` is still the one seat the game waits on; `Rules.OptionalSeats` lists opponents who may act; `IsLegal` accepts their trade responses during Main; `GetLegalActions(s, seat, buffer)` lists any seat's actions.
  - A counter is a proposal from that opponent to the current player (one open counter per opponent; a new one replaces it). The current player accepts it (trades immediately) or declines it. For its own offers the current player confirms with any accepter, turns an acceptance down, or cancels. All trades close at end of turn.
  - State: `GameState.Offers` (13 slots = 10 offers + 3 counters) replaces `Offer`/`OfferReply`, so a state is a bit over the brief's 1 KB target.
  - **Step 13 `GameRunner`**: after each action, ask the acting seat *and* every optional seat. For bots, build all their views from the same state snapshot and ask concurrently (nobody sees another's answer first), then apply in a fixed order so Sim results stay deterministic. Records store actions in applied order, so replays are exact.
- Win check: `TryWin` runs after every `Apply` (current seat only, total VP incl. hidden) and at the start of each turn in `ApplyEndTurn`, before the turn-cap draw.
- Largest Army (`UpdateLargestArmy` in `Rules.Awards.cs`) landed with Knights in step 10, for the same validator reason as Longest Road.
- Discards are never in the legal list (the brief's rule): agents build them, `Rules.RandomDiscard` builds a random valid one, `TestPlay.RandomAction` handles it in tests.
- The Longest Road award (`Rules.Awards.cs`) landed in step 8, because random games reach 5 roads and the validator checks the holder. Step 11 adds the FAQ cut-case tests, Largest Army and the win check. Until then games only end at `MaxTurns` (counted across all seats).
- A rolled 7 currently just skips production and goes to Main (`SevenSkipsProductionForNow` test); step 9 replaces this with discards and the robber.
- `Rules` is split into partial files by area (`Rules.cs` shared API and helpers, `Rules.Setup.cs`, ...). `Apply` assumes legality (fast path for search); `ApplyChecked` validates first. Events (`GameAction.cs`) are emitted as each rule is written; step 13 adds redaction.
- `GameState` adds `DevPlayed[5]` (played dev cards by type) to the brief's layout so the validator can check all 25 dev cards. `EveryFieldIsHashedAndCopied` fails if a new field is missing from `ComputeHash` or `CopyFrom`; `NewGameHashIsPinned` pins the hash format used by saved games.
- `LongestRoad.Compute` exists already (validator needs it); step 11 adds the award rules. `StateBuilder` lives in Core; `TestBoards.Standard` (tests) is a hand-written fixed board, desert at hex 9.
- Topology ids are pinned by `IdTablesMatchGoldenChecksum`. Harbor spots are 0-based everywhere, including board JSON (the brief's table rows 1–9 are spots 0–8).
- `Terrain` and `HarborType` put their resource first in the same order as `Resource` (Hills 0 = Brick ... ; Desert 5, Generic 5), so `(Resource)terrain` works. `Board` only accepts the exact base-game piece set.
- **Generated boards are always balanced** (no 6 or 8 next to another 6 or 8), **and no two equal numbers touch** (`Balanced(rng)` defaults to strict, 2026-09-22); both are the user's rules. `BoardGenerator.Random` is internal (one shuffle attempt used by `Balanced`, visible to tests only). Games, Sim and bots use `BoardGenerator.Balanced` or a JSON layout.
- Update this section when a step or checkpoint is finished.

## Workflow

- Build M1 in the brief's 15 steps, writing each step's tests with it (the brief's "Tests with it" column and Test plan table).
- **Stop at checkpoints A (after step 2), B (after 8), C (after 11) and D (after 15)** for review. The user is the only person on the project and does the reviews. Summarize what to check (choices the brief left open, edge cases), and don't start the next step until the user approves.
- `game/scripts/DebugView.cs` is the M1 debug viewer: plays random legal moves on a real `GameState` (Space step, A autoplay, +/- speed, N new game), shows every seat's full state and recent events, runs `StateValidator` after each move, and can overlay hex / vertex / edge / harbor ids (H / V / E / P). Keep it working as rules are added (describe new events, handle new phases).
- In Godot scripts, `Resource` is ambiguous with `Godot.Resource`: write `Catan.Core.Resource`.
- Every rules bullet in the brief should end up as at least one test.

## Layout

| Path | What |
|---|---|
| `CatanAI.sln` | Core, AI, Sim, Tests. **Not** the Godot project, so CI never needs Godot |
| `src/Catan.Core/` | Rules engine. **No Godot references** |
| `src/Catan.AI/` | Bots (RandomBot in M1 step 15; smarter bots in M3) |
| `src/Catan.Sim/` | Console tool: `random`, `replay`, `bench` |
| `tests/Catan.Tests/` | xUnit (`Xunit` is a global using) |
| `game/` | Godot 4.7 .NET project (`CatanGame.csproj` references Core and AI) |
| `.github/workflows/ci.yml` | Runs `dotnet test CatanAI.sln -c Release` on push and PR |
| `docs/M1-brief.md` | Design brief for M0 and M1 |
| `failures/` | Sim output for games that broke a rule (gitignored) |

## Commands

```
dotnet build CatanAI.sln -c Release    # dotnet test alone doesn't build Catan.Sim; build first
dotnet test CatanAI.sln -c Release
dotnet run -c Release --project src/Catan.Sim -- random --games 10000 --seed 1 --validate
dotnet run -c Release --project src/Catan.Sim -- replay --file failures/seed-N.json
dotnet run -c Release --project src/Catan.Sim -- bench --seconds 10
```

## Conventions (from the brief)

- Everything targets **net8.0** (Godot's C# template still targets .NET 8).
- Resource order everywhere: **Brick 0, Lumber 1, Wool 2, Grain 3, Ore 4**. Hands, bank and costs are plain `int[5]`; per-seat arrays are `[seat * 5 + index]`.
- Dev card order: Knight 0, VictoryPoint 1, RoadBuilding 2, YearOfPlenty 3, Monopoly 4.
- Topology ids (54 vertices, 72 edges) are derived deterministically and **must never change**, because saved games depend on them.
- `GameState` is plain data (< 1 KB), with rules in the static `Rules` class and all randomness through `IChance`. Rules never call an RNG directly.
- Hot paths (`GetLegalActions`, `Apply` with `events == null`, `CopyFrom`) must not allocate.
- Only two things are hidden information: the resource in `CardStolen` and the type in `DevCardBought`. `PlayerView` must be a copy with no path back to hidden data.

## Environment notes (Windows 10, PowerShell 5.1)

- Git and the GitHub CLI were installed with winget. Shells started before that install don't have them on PATH, so refresh it first:
  `$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')`
- `gh` is logged in as Hyrex8301 and is the git credential helper. Remote: https://github.com/Hyrex8301/CatanAI
- .NET SDK 8.0.425; `global.json` pins 8.0.100 with `rollForward: latestFeature`.
- Godot must be the **.NET (mono) build** 4.7.2; the standard build can't run C#. Commit the `*.uid` files Godot creates next to scripts; `.godot/` is gitignored.
