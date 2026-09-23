# M4 Design Brief: Stronger Bots (Multi-Turn Search)

Draft 2026-09-23, for the user's approval. Follows M3 ([M3-brief.md](M3-brief.md)): SmartBot scores positions with trained
weights and plans within its own turn. M4 makes it look ahead over everyone's turns.

## Goals and done-when

| Milestone | Delivers | Done when |
|---|---|---|
| M4 Search bot | A trainer fix so overnight runs keep improving; a calibrated evaluation (score → chance of winning); **SearchBot**: information-set Monte Carlo tree search (ISMCTS) over several turns, guided by SmartBot, running on all cores within a time budget; the game's opponents switched to it | SearchBot at the play time budget beats three SmartBots (trained weights, Play settings) clearly: over 35% of games in 1v3 (fair is 25%), with the 95% interval above 30%, over 2,000 games; all tests pass; bots still only see their own view |

Out of scope: neural-network evaluation (a later milestone that learns from the positions M4's search produces), search
inside the trainer (search is too slow for millions of games; the trainer keeps tuning SmartBot's weights).

## Why search, and why this kind

SmartBot picks the move whose resulting position scores best, looking only within its own turn. It can't see that
building here lets the next player take the spot it needs, or that holding 8 cards invites a 7. The first overnight run
showed the weights have mostly converged (45.8% against the starting weights, flat after about 1.5 hours), so more
training alone won't add much. Looking ahead over the other players' turns is the next step in strength.

Catan has hidden cards and dice, so the search is **ISMCTS**: each round of the search samples a full game consistent
with what the bot knows (hidden hands from the M3 hand tracker, the unknown dev deck), then plays forward through a
shared tree of the bot's choices. Dice are sampled. The other players move the way SmartBot would (its cheap settings),
so the search spends its time on moves that matter. Positions are cut off a few turns ahead and scored by the trained
evaluation, converted into a chance of winning.

## What gets built

1. **Trainer fix and a strength ladder.** New champions must beat the old one by a bigger margin over more games, and
   must also beat the starting weights and the champion pool, so noise stops crowning false champions. A `ladder` Sim
   command plays fixed reference bots (RandomBot, SmartBot with starting weights, trained SmartBot, SearchBot at a given
   time) and reports a rating per bot. This is how every later change is judged.
2. **Calibrated evaluation.** Fit a curve from the evaluation's score (my value minus the best opponent's) to how often
   that seat went on to win, from self-play games. Search averages win chances, so scores need to mean something.
3. **Search moves.** A bot's legal moves can number in the hundreds, far too many for a tree. At the bot's own decisions
   the tree considers the top few moves by SmartBot's planner (a setting, around 6), plus ending the turn. Player trades
   stay with SmartBot's trade logic (offers and answers) and aren't searched.
4. **SearchBot.** ISMCTS with a time budget per decision: sample hidden cards, walk the tree choosing moves by UCB,
   expand one new move, play forward with SmartBot's cheap policy for everyone until the cut-off (about two rounds), score
   with the calibrated evaluation, and back the result up. All threads search separate trees; their visit counts are
   added and the most-visited move is played. Decisions with one legal move, and trivial ones, skip the search.
5. **Game integration.** Opponents in Godot use SearchBot with a "thinking" indicator on their row while they search.
   The bot delay is on top of thinking time only when thinking was quick.
6. **Tuning.** Exploration constant, top-K, cut-off depth and time budget, each chosen by ladder matches.

## Build order and review checkpoints

| # | Build | Tests with it | Checkpoint |
|---|---|---|---|
| 1 | Trainer gating fix; `ladder` Sim command with fixed reference bots | ladder is fair (identical bots split evenly); gating rejects a no-better candidate | |
| 2 | Evaluation calibration from self-play (score → win chance), saved with the weights | calibration matches observed win rates in held-out games | **A**: ladder numbers and calibration curve reviewed |
| 3 | Search moves (top-K from the planner, end turn); SmartBot's cheap policy for playouts | top-K always contains the planner's best move; playouts stay legal under validation | |
| 4 | SearchBot: single-thread ISMCTS with determinization, UCB, cut-off and calibrated scoring | always legal; determinism with a fixed seed and iteration count; never reads hidden cards | |
| 5 | Parallel search within a time budget; skip trivial decisions | respects the budget; more time never plays worse on the ladder (within noise) | **B**: SearchBot beats trained SmartBot in the Sim |
| 6 | Tuning by ladder; SearchBot in Godot with a thinking indicator | tuned settings saved; game loads them | |
| 7 | Final 2,000-game match; play-test against it | done-when numbers | **C**: M4 sign-off, play the search bots |

## Decisions (2026-09-23)

1. **Thinking time is a setting, chosen with data.** At checkpoint B the ladder measures SearchBot at 0.5, 1, 3 and 5
   seconds per real decision against trained SmartBot, and the user picks the default from those numbers (trivial
   decisions are always instant). Expectation, to be confirmed by the ladder: the biggest gain comes from having search at
   all; each doubling of time adds less; the evaluation's quality caps how far more time can go.
2. **Training keeps tuning SmartBot's weights** overnight (search can't run millions of games); SearchBot uses the best
   weights. (User: yes.)

## Test plan

Same layers as before: unit tests per piece (calibration, move pruning, tree statistics), fuzz (SearchBot games under full
validation, a few per CI push at a tiny iteration count), determinism (fixed seed and iteration count give the same game),
and the view-only guarantee (SearchBot gets only a PlayerView; its samples come from the hand tracker).
