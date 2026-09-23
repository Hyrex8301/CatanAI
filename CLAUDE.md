# CatanAI

A Catan (base game) AI project: a C# rules engine, bots, a simulation CLI, and a Godot front end. The roadmap is **[docs/M1-brief.md](docs/M1-brief.md)**. Read it before starting M1 work; it is the spec for ids, rules, APIs and tests.

## Git and commit rules

- **All commits are authored by the repo owner** (Hyrex8301). Use the configured git identity as-is. Never change `user.name` / `user.email` or pass `--author`.
- **No AI attribution anywhere.** No `Co-Authored-By: Claude` (or any AI) trailers, no "Generated with Claude Code" lines, no AI mentions in commit messages, PR titles/descriptions, code comments, or docs.
- **Never commit secrets:** no API keys, tokens, passwords, `.env` files, credentials, or personal data. Check `git status` / `git diff --cached` before committing, and stop and ask if anything looks sensitive.
- Never commit `.claude/` (local tool settings; gitignored). Review `git status --short` *before* committing, not after.
- Commit messages are short and imperative, describing the change (e.g. `M1 step 2: topology ids and adjacency tables`).
- Don't force-push, rewrite history, or delete branches without asking.

## Status

- **M0 (setup): done** 2026-09-22. Godot shows "Catan.Core says: 19 hexes" and CI is green.
- **M1 (rules engine): steps 1–14 done**, checkpoints A, B and C passed. Next is step 15: `RandomBot` (Catan.AI), the Sim CLI (`random` / `replay` / `bench`), a 100k-game fuzz run, then **checkpoint D** (M1 sign-off).
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
- **Generated boards are always balanced** (no 6 or 8 next to another 6 or 8); the user's rule. `BoardGenerator.Random` is internal (one shuffle attempt used by `Balanced`, visible to tests only). Games, Sim and bots use `BoardGenerator.Balanced` or a JSON layout.
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
