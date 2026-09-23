# M3 Design Brief: Smart Bots and Overnight Training

Approved 2026-09-22 (decisions at the end). Builds on the M1 engine and the M2 playable UI.

## Goals and done-when

M3 replaces RandomBot as your opponent with a bot that plays real Catan, and gives you a trainer you can leave running overnight that makes it stronger.

| Milestone | Delivers | Done when |
|---|---|---|
| M3 Smart bots (~3–4 weeks) | Hand tracker, board evaluation with tunable weights, a SmartBot that plays every part of the game (setup, building, robber, dev cards, bank and player trades), a match harness, an overnight self-play trainer, and bot choice in Godot | One SmartBot beats three RandomBots in over 90% of games; an overnight training run finishes on its own and its weights beat the starting weights with a statistically clear margin; the hand tracker never contradicts the true state over 10,000 games; you can play against trained bots in Godot |

Out of scope for M3: tree search (ISMCTS) and neural networks. Both build on M3's pieces and are the natural next milestone (see the end).

## How the bots get smart

**What "training" means here.** The bot scores positions with an evaluation function: a weighted sum of features like "expected ore per roll", "distance to Longest Road", "cards at risk from a 7". Training tunes those weights by self-play: the trainer tries variations of the weights, plays thousands of games between them on all 16 threads, keeps what wins, and repeats. It needs no GPU, no extra libraries, and no hand-labeled data, and it improves steadily over hours. It won't reach a superhuman level alone, but it produces a solid, human-like opponent. Tree search and a learned neural evaluation are the later steps to go beyond that.

**Rough numbers** (from the M1 bench, 1.1M actions/s): a SmartBot thinks about 50–100 candidate moves per decision, so games are slower than random ones, around 50–150 games/s across 16 threads. That is 200,000+ games per night, plenty for weight tuning.

## Architecture (all in `Catan.AI` unless noted)

**1. Hand tracker** (`HandTracker`). Each bot sees only its PlayerView. The tracker follows the redacted event log and keeps, for every opponent, what is known exactly (production, discards, trades, Monopoly counts, build costs) plus the cards it can't identify (stolen cards, shown as −1). It can:
- give certain lower and upper bounds per resource,
- sample a full hidden hand consistent with everything seen (for "what if" thinking),
- guess dev cards from what's been bought and played.

It never uses hidden information; the M1 leak test guarantees views can't provide any.

**2. Features and evaluation** (`Evaluator`, `BotWeights`). About 25–40 features per seat, for example: public and total VP; production pips per resource weighted by scarcity; resource diversity; reachable settlement spots; road length and gap to Longest Road; knights and gap to Largest Army; dev cards held; harbor access for resources you produce; hand size and 7-risk; robber on your hexes; buildings left. Score = your value minus the strongest opponent's (plus a smaller share of the others'). Weights live in a JSON file, so trained bots are just different files.

**3. SmartBot** (`SmartBot : IPlayerAgent`). For each decision it samples the opponents' hidden hands from the tracker, tries every legal move on a copy of the state (the engine's fast `CopyFrom`), and picks the move with the best evaluation. On its own turn it plans ahead: a beam search over sequences of its own moves (see Decisions, item 4).
- Dice rolls and dev card draws use the exact odds: all 11 roll totals weighted by probability; the remaining deck composition as the tracker sees it.
- Setup placement, the robber (hex and victim), discards (lose the least value) and dev card timing all come from the same evaluation.
- Trades: it accepts or counters an offer only if it's better off afterwards and it doesn't help a leader too much; on its own turn it proposes trades that unlock a build (bank trade vs player trade compared by value).
- A `Temperature` setting adds controlled randomness (for training variety and "easier" bots).

**4. Match harness** (Sim `match`). Plays thousands of games between weight files (or RandomBot) with rotated seats and fixed seed sets, and reports win rates with confidence intervals, so "is B better than A?" gets a real answer.

**5. Trainer** (Sim `train`). Evolution strategy over the weights:
- each generation, make variations of the current best weights, play them against the current best (and a pool of past bests, so it can't just overfit one rival) on the same seed sets;
- move the weights toward the variations that won; accept a new champion only when it wins by a statistically clear margin;
- checkpoint every generation to `training/<run>/` (weights, a progress log, win rate vs RandomBot and vs the starting weights), so a run can be stopped and resumed, and a crash loses at most one generation;
- run for a set time (`--hours 8`) and finish with a summary.

```
dotnet run -c Release --project src/Catan.Sim -- train --hours 8 --out training/night1
dotnet run -c Release --project src/Catan.Sim -- match --a training/night1/best.json --b start --games 4000
```

The best trained weights you want to keep get copied into the repo (`bots/`), so the game can ship with them; `training/` stays out of git.

**6. Godot.** The new-game screen gets an opponent choice: Random, Smart (starting weights), or Trained (pick a weights file). A "bot thinking" delay keeps play readable.

## Build order and review checkpoints

| # | Build | Tests with it | Checkpoint |
|---|---|---|---|
| 1 | HandTracker from redacted events; hand sampling | never contradicts the true hands over thousands of random games; bounds tighten correctly after each kind of event | **A**: tracker |
| 2 | Features, Evaluator, BotWeights (JSON) | features match hand-built positions; weights round-trip | |
| 3 | SmartBot without player trades (setup, build, robber, discard, dev cards, bank trades) | always legal; beats RandomBot | |
| 4 | SmartBot player trades (offer, answer, counter) | trades only when better off; always legal | |
| 5 | Sim `match` with confidence intervals | known-strength sanity checks | **B**: SmartBot vs 3 RandomBots > 90% |
| 6 | Sim `train`: evolution strategy, checkpoints, resume, progress log | a short run improves on deliberately bad weights; resume continues exactly | **C**: first overnight run |
| 7 | Godot opponent choice and weights loading | loads the bundled weights | **D**: M3 sign-off, play the trained bots |

## Test plan

Same layers as M1 and M2: unit tests per piece, fuzz (SmartBot games under full validation in CI, a few hundred per push, since they're slower), and determinism (same seed and weights give the same game, so any strange bot move can be replayed and inspected).

## After M3

- **Search**: ISMCTS (information-set Monte Carlo tree search) that samples hidden hands from the tracker and uses the trained evaluation to cut rollouts short. This is how strong Catan bots plan several moves ahead.
- **Learned evaluation**: replace the weighted features with a small neural network trained from the millions of self-play positions the trainer already generates.

## Decisions (2026-09-22)

1. **As strong as possible.** No difficulty levels; `Temperature` stays for training variety only.
2. **Threads:** training uses all but 2 hardware threads by default (14 on the Ryzen 7 5800X); `--threads N` overrides.
3. **Self-play:** SmartBots train against SmartBots: each candidate plays the current champion and a pool of past champions (so it can't overfit one rival). RandomBot is only a sanity check in the progress log.
4. **Lookahead, in two stages:**
   - **Now (M3): turn planning.** On its own turn SmartBot runs a beam search over sequences of its own moves (bank trades, builds, dev card plays), treating dice and dev card draws by their exact odds, and plays the first move of the best plan. Depth and beam width are settings: training uses a cheap setting for throughput, real games a deeper one.
   - **Next milestone: multi-turn search (ISMCTS)** over everyone's turns, using the trained evaluation. Slower per move, so it's for playing against you rather than bulk training.
5. **In the game:** opponents default to the best trained bots.
