# M2 play-test checklist

Work through this list in the game (not the debug viewer) before signing off M2. Tick a box when the situation behaves as
described. Note anything odd next to the box. The checks run in roughly the order they come up in a game, so one or two
full games cover most of the list; a few need a saved game or a lucky roll.

Start a game from the menu with the default settings (10 points, Normal bot speed, 20 s to answer offers).

## Menu and settings

- [ ] The menu shows a new random board each time you open it.
- [ ] Settings: change points to win, bot speed, answer time and friendly robber. Quit the game and reopen it: the settings are kept.
- [ ] A new game uses the settings (for example, "Instant" bots move without pauses).
- [ ] Continue appears only when there is an autosave, and resumes it.

## Setup

- [ ] Both setup rounds: only legal corners glow for settlements, then only the edges next to your new settlement glow for roads.
- [ ] Your second settlement gives you its starting cards. They fly from its hexes to your hand, and the log shows them.
- [ ] The bots' placements pop onto the board and appear in the log with their piece icon.

## Rolling and production

- [ ] On your turn the dice glow beside your panel. Click them (or press Space) to roll.
- [ ] The dice tumble briefly, then the rolled number's hexes light up, then cards fly to each player.
- [ ] Your hand and the player rows update when the cards land, not before.
- [ ] After a bot's roll, the dice sit beside that bot's row and show its roll.
- [ ] A hex under the robber produces nothing when its number is rolled.

## Building

- [ ] Click-to-build: with a wood and a brick, hover an edge next to your road. A faint road shadow appears. Click once: the
      shadow stays and pulses, and the status box says "Click Again: Road". Click the same spot again: the road is built.
- [ ] Click a different buildable spot after the first click: the shadow moves there. Esc cancels it.
- [ ] Settlement and city work the same way (a city on one of your settlements).
- [ ] The Road / Settlement / City buttons: disabled with a reason in the tooltip when you can't afford or have nowhere to
      build ("You need 1 more ore"); when enabled, click one and its legal spots glow, then one click builds.
- [ ] Pieces left: the counts on the buttons go down as you build.
- [ ] Longest Road and Largest Army: the stat turns gold on the holder's row, and a toast announces a change.

## Sevens and the robber

- [ ] A 7 when you hold more than 7 cards: the discard panel opens. Click cards in your hand to pick exactly half (rounded
      down). Picked cards leave the hand bar; click one in the panel to put it back. The check lights up only at the right count.
- [ ] Move the robber: only legal hexes glow. The robber slides to its new hex.
- [ ] Robber next to two players: a popup shows both with their hand sizes; pick one. Esc lets you pick a different hex.
- [ ] Robber next to one player: you rob them straight away.
- [ ] A bot robs you: a toast says what it took ("Blue stole a sheep from you").
- [ ] Friendly robber (if on): hexes next to a player with 2 or fewer points don't glow.

## Development cards

- [ ] Buy a dev card: it appears in your hand dimmed with "1 new" and can't be played this turn (the popup says why).
- [ ] Knight before rolling and after rolling: the robber flow starts; knights count toward Largest Army.
- [ ] Road Building: two free roads, edges glow each time; with only one road piece left, only one is placed.
- [ ] Year of Plenty: pick two resources (the same one twice works); they fly from the bank to your hand.
- [ ] Monopoly: pick a resource; everyone's cards of it fly to you.
- [ ] Only one dev card per turn: the second popup says "You've already played a development card this turn".
- [ ] Victory Point card: not playable; it counts in your own VP (the tooltip on your panel says how many are hidden).
- [ ] Winning on a bought Victory Point card ends the game straight away.

## Trading

- [ ] Click the Trade button or a card in your hand: the proposal panel grows out of your hand, and the Trade button turns into a cross.
- [ ] Click resources in the top row to ask for them, and hand cards to give them; click a card in either row to take it back.
- [ ] Bank at 4:1, 3:1 (generic harbor) and 2:1 (resource harbor): the bank button's tooltip shows your rates, and the check appears only for a valid trade.
- [ ] A bank trade of several lots at once (for example 8 brick for 2 ore) goes through in one click.
- [ ] Offer to players: your offer card appears top right. Each bot's answer shows as a tick, a cross or a counter arrow.
- [ ] An offer accepted by two bots: click one accepter's avatar to trade with them; right-click to turn one down.
- [ ] Edit an open offer with the pencil: the proposal reopens with its cards; saving resets the answers.
- [ ] A bot's counter-offer to you: accept or decline it on its card.
- [ ] A bot's offer to you: accept (greyed out with a reason if you can't pay), decline, or counter with the pencil. The
      countdown bar runs out after the answer time and the game moves on.
- [ ] All open offers close at the end of the turn.

## Log, toasts and turn flow

- [ ] The log shows colored names with card, dice and piece icons, a divider between turns, and follows the newest line.
- [ ] Hidden information stays hidden: another player's steal shows a face-down card; bots' dev card purchases show a card back.
- [ ] End Turn shows ">>" when you can end your turn and an hourglass otherwise. Space ends your turn.
- [ ] Bots pause about half a second between moves, and wait for flying cards to land.
- [ ] The acting player's row has a gold border; bots show "…" while thinking.

## Save, load, game over

- [ ] Save from the settings menu (gear), including during a trade. Load it from the menu: the game continues from the same spot.
- [ ] The autosave after each of your turns resumes correctly with Continue.
- [ ] A saved game replays in the Sim to the same hash: `dotnet run -c Release --project src/Catan.Sim -- replay --file <save>` (saves are in `%APPDATA%\Godot\app_userdata\CatanAI\saves`).
- [ ] The game-over screen appears when someone wins: standings with points by source (settlements, cities, Victory Point
      cards, awards), stats, and the dice-roll chart. "View board" closes it; the gear menu's "Game results" reopens it.
- [ ] "New game" starts a fresh game; "Main menu" returns to the menu.
