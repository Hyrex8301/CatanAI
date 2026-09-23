# CatanAI

A Catan (base game) AI project: a C# rules engine, bots, a simulation CLI, and a Godot front end. The roadmap is **[docs/M1-brief.md](docs/M1-brief.md)**. Read it before starting M1 work; it is the spec for ids, rules, APIs and tests.

## Git and commit rules

- **All commits are authored by the repo owner** (Hyrex8301). Use the configured git identity as-is. Never change `user.name` / `user.email` or pass `--author`.
- **No AI attribution anywhere.** No `Co-Authored-By: Claude` (or any AI) trailers, no "Generated with Claude Code" lines, no AI mentions in commit messages, PR titles/descriptions, code comments, or docs.
- **Never commit secrets:** no API keys, tokens, passwords, `.env` files, credentials, or personal data. Check `git status` / `git diff --cached` before committing, and stop and ask if anything looks sensitive.
- Commit messages are short and imperative, describing the change (e.g. `M1 step 2: topology ids and adjacency tables`).
- Don't force-push, rewrite history, or delete branches without asking.

## Status

- **M0 (setup): done** 2026-09-22. Godot shows "Catan.Core says: 19 hexes" and CI is green.
- **M1 (rules engine): steps 1–3 done**, checkpoint A passed. Next is step 4: `BoardGenerator` (Random, Balanced, JSON) and the board-generation tests.
- Topology ids are pinned by `IdTablesMatchGoldenChecksum`. Harbor spots are 0-based in code (the brief's rows 1–9 are spots 0–8).
- Update this section when a step or checkpoint is finished.

## Workflow

- Build M1 in the brief's 15 steps, writing each step's tests with it (the brief's "Tests with it" column and Test plan table).
- **Stop at checkpoints A (after step 2), B (after 8), C (after 11) and D (after 15)** for review. The user is the only person on the project and does the reviews. Summarize what to check (choices the brief left open, edge cases), and don't start the next step until the user approves.
- `game/scripts/TopologyView.cs` draws hex, vertex, edge and harbor ids in Godot (keys H / V / E / P); handy for picking ids in tests.
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
