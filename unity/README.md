# Cake Sort: Unity rebuild (2.5D, endless)

An original Unity (C#) rebuild of the core loop described in
`../REVERSE_ENGINEERING_REPORT.md`. All code is original. The art in
`Assets/Resources/Art` was generated for this project, and the 3D cakes, plates,
tray and obstacles are built from procedural meshes at runtime. No assets or
code from the original game are included.

## How the game plays

- **Endless.** There are no levels. You score by sorting slices into whole cakes,
  and the run lasts as long as you keep finding room. It ends only when the
  waiting pieces no longer fit on the board.
- **Batches of three.** The counter shows three pieces. A new set of three
  arrives only after all three are placed. Every batch is dealt so that the
  whole batch can still fit on the current board, so a loss always comes from
  the player's placements, never from an impossible deal.
- **Linked plates.** Some pieces are two plates joined on a pink platter
  (side by side or front to back). They need two neighbouring empty cells.
  They show up more often as the score rises (14% at the start, up to 34%).
- **Sorting.** When a piece lands, its slices exchange with neighbouring plates
  so matching flavors gather together; full plates trade mismatched slices
  back. A plate with six slices of one flavor is served for points, and chain
  reactions build a combo multiplier.
- **New cakes to unlock.** Play starts with 4 flavors. New ones unlock at 600,
  1,500, 3,000 and 5,000 points, with a "NEW CAKE!" reveal each time. After all
  8 are unlocked, a milestone every 4,000 points may add an obstacle (at most 4
  on the board), and every other milestone grants a free booster. Only the
  newest flavors are dealt, and that set rotates as new cakes unlock.
- **Difficulty ramp.** The opening is gentle. From 2,000 points on, the game
  slowly deals more mixed cakes, more linked pieces and fewer cakes that match
  the board (full strength at 10,000). The menu of dealt flavors widens from 4
  to 5 at 7,000 points, 6 at 14,000 and 7 at 22,000, which is what eventually
  fills the board in a long run.
- **Boosters:** Hammer (clears a plate, and also breaks boxes, cloches and
  slime), Cupcake Bomb (clears a 3x3 area), Magic Whisk (turns a plate into its
  main flavor), Firework (clears a random plate), and Shuffle (rerolls the
  waiting pieces).
- **Obstacles:** Gift Box (blocks a cell; opened by two cakes served next to
  it), Glass Cloche (locks a cell until two cakes are served anywhere), and
  Slime (slices on that plate are stuck until a cake is served there).
- **Game over** offers a revive (the first one in a run is free, then up to two
  paid ones at 300 and 600 coins), "use a booster", or a new game. The current
  run, boosters and best score are saved automatically (`PlayerPrefs`), so
  CONTINUE on the title screen resumes the run.
- **First game tutorial.** A new player's first game is a short scripted board
  with three guided moves (a single plate, a second one that completes a cake,
  then a linked pair). A hand pointer shows each drag, a mascot bubble explains
  it, and only the highlighted move is accepted. Existing players skip it.

## Reasons to keep playing

- **Customer orders.** Three animal customers wait at the top of the screen,
  each asking for something: a few cakes of one flavor, a total number of
  cakes, a big enough combo chain, or a number of linked-plate placements.
  Serving what they ask for completes the order, pays coins and a star, and a
  new customer walks in. Orders survive between runs, and an order for a
  flavor that has rotated off the menu is swapped for a fresh one.
- **Coins.** Every cake served pays coins (more for combos), on top of the
  order rewards. Coins buy boosters: tapping a booster you don't have opens
  a small shop (Hammer 150, Bomb 300, Whisk 200, Firework 200, Shuffle 120),
  and they pay for revives after the free one. New players get a starter set
  of boosters once; after that boosters come from unlocks, milestones,
  level-ups, the daily treat, or the shop.
- **Bakery levels.** Each completed order is a star; collecting enough stars
  levels up the bakery (5 stars for level 2, then 3 more per level). A level-up
  pays coins and a free booster, with a chest celebration.
- **Daily treat.** Once a day the title screen offers a gift: coins that grow
  over a 7-day streak (40 up to 250 plus a free Bomb on day 7). Missing a day
  restarts the streak.

## Presentation

- A 2.5D bakery: a perspective camera (58° tilt, FOV 38) looks over a gingham
  tabletop carrying a wooden tray (5x5 plates) and a wooden counter holding the
  three waiting pieces. The camera fits the tray edge to edge between the top
  bar and the booster bar on any aspect ratio; leftover height on tall screens
  first widens the gap to the counter, then is split evenly.
- Cakes are layered 3D wedges (sponge and cream bands on the sides, decorated
  tops) with soft blob shadows. Pieces lift and lean while dragged and snap
  toward the cell under them; they land with a squash. Wedges glide around
  their plate and hop between plates when they merge, and a finished cake
  springs, spins and bursts into stars that fly to the score. The sorting
  rules skip merges that would only move slices back and forth.
- **Haptics.** iOS uses the system impact, selection and notification
  feedback (`Assets/Plugins/iOS/CakeSortHaptics.mm`); Android uses the view's
  haptic feedback. Taps, landings, merges, serves, combos, unlocks and game
  over each have their own feel. VIBRATION in the pause menu turns them off.
- Custom shaders live in `Assets/Resources/Shaders` (`CakeSort/Lit`, a
  transparent variant, and glass for the cloche).
- Input: drag a piece onto the board, or tap a piece and then tap a cell.

## Open and run

1. Install Unity Hub and Unity **6000.6.8f1** (the version in
   `ProjectSettings/ProjectVersion.txt`). Add the **iOS Build Support** module
   for iOS builds.
2. Unity Hub: *Add → Add project from disk*, then pick `CakeSortUnity`.
3. Open `Assets/Scenes/Game.unity` and press **Play**. The game builds itself
   from code at startup, so the scene is intentionally almost empty.

Editor menu **Cake Sort**:

- *Run Engine Self Test*: runs the rules checks (results are in the Console).
- *Run Economy Simulation*: plays casual, average and strong bot players
  through 15 runs each with the real rules and meta, and prints pacing and
  coin numbers (`[CakeSort][Economy]` in the Console).
- *Apply Project Settings*: sets portrait orientation, bundle ID and other
  player settings.
- *Reimport Art*: reapplies the texture import settings in `Resources/Art`.
- *Build iOS Xcode Project*: writes an Xcode project to `Builds/iOS`. Set
  your signing team in Xcode, then run on a device.
- *Build iOS Simulator Xcode Project*: writes a Simulator-SDK project to
  `Builds/iOSSimulator`; build it with `xcodebuild` for an iOS Simulator and
  install the resulting `.app` with `simctl`.

## Command-line checks

The commands below are for when the project is closed in the Editor. If it's
open, copy the project first (for example,
`rsync -a --exclude Temp --exclude Library/PackageCache CakeSortUnity/ /tmp/CakeSortBatch/`)
and point `-projectPath` at the copy.

```sh
UNITY=/Applications/Unity/Hub/Editor/6000.6.8f1/Unity.app/Contents/MacOS/Unity
# Rules, meta and tutorial self test (130 checks)
$UNITY -batchmode -nographics -quit -projectPath CakeSortUnity \
  -executeMethod CakeSort.EditorTools.EngineSelfTest.Run -logFile /tmp/selftest.log
# macOS build, then a scripted smoke run that plays a full game and saves screenshots
$UNITY -batchmode -nographics -quit -projectPath CakeSortUnity \
  -executeMethod CakeSort.EditorTools.BuildTools.BuildMac -logFile /tmp/build.log
"CakeSortUnity/Builds/Mac/CakeSort.app/Contents/MacOS/Cake Sort" -smoketest /tmp/smoke
# Economy simulation (add -econPlayers N to change players per tier, default 5)
$UNITY -batchmode -nographics -projectPath CakeSortUnity \
  -executeMethod CakeSort.EditorTools.EconomySim.Run -logFile /tmp/econ.log
```

The smoke run uses the built-in move advisor (`Core/Advisor.cs`). It claims
the daily treat, places pieces, completes customer orders, levels up, uses
each booster, buys from the shop, and saves screenshots (daily, title, board,
linked drag, unlock, order done, boosters, obstacles, level up, shop, game
over) plus a stats line for the run and the meta state. Add `-smokequick` to
stop after 30 moves (one board and one midgame shot) for fast layout checks,
and `-screen-width/-screen-height` to try other aspect ratios. The smoke run
plays the tutorial first (shots 14 and 15) and logs how many haptics of each
kind fired. `-smokerecord -smokemoves N` saves 30 fps frames of N moves to
`<dir>/frames` for reviewing animations, and `-smokespeed 1` plays at real
speed instead of fast-forwarding.

## Layout

- `Assets/Scripts/Core/Engine.cs`: the rules, written in plain C# with no
  UnityEngine dependency. Games are deterministic per seed and can be saved
  and restored with `Save()`/`Load()`.
- `Assets/Scripts/Core/Advisor.cs`: a one-move-lookahead player, used by the
  smoke test and for balance tuning.
- `Assets/Scripts/Core/Meta.cs`: the meta rules, also plain C#: customer
  orders, coins, bakery levels, the daily streak, and shop/revive prices.
- `Assets/Scripts/Core/Tutorial.cs`: the scripted first-game board and steps.
  `Assets/Scripts/Game/GameTutorial.cs` shows the hand, bubble and gating.
- `Assets/Scripts/Game/Haptics.cs`: haptic kinds, the on/off setting and the
  platform calls.
- `Assets/Scripts/Game/World3D.cs`: procedural meshes, materials, and the
  plate, piece and obstacle views.
- `Assets/Scripts/Game/GameController.cs`: camera, input, HUD, overlays,
  animations, saving, and the smoke test.
- `Assets/Scripts/Game/GameMeta.cs`: the meta presentation (partial class of
  GameController): order cards, coin pill, level badge, and the shop,
  level-up and daily-treat popups.
- `Assets/Scripts/Game/GameKit.cs`: UI helpers, tweens and sprite loading.
- `Assets/Scripts/Game/AudioTones.cs`: sound effects and music generated at runtime.
- `Assets/Editor/`: build tools, project settings, the engine self test and
  the economy simulation.

## Notes and limits

- The original game's exact tuning (unlock scores, obstacle rules, deal odds)
  could not be recovered, and it has no documented order/coin/level system, so
  the whole meta layer and every value here is our own, tuned with the
  economy simulation (bots that make random moves 35%, 15% or 0% of the time,
  3.5 seconds per move). Median results over 15 runs per player:

  | Player | First loss | Whole run (with revives and boosters) | Coins earned per run | Coins after 15 runs | Level after 15 runs |
  |---|---|---|---|---|---|
  | Casual | ~7 min | ~14 min | ~510 | ~230 | 11 |
  | Average | ~10 min | ~25 min | ~900 | ~230 | 15 |
  | Strong | ~15 min | ~42 min | ~1,480 | ~2,000 | 18 |

  Casual and average players spend roughly what they earn on boosters and
  revives. The strong bot plays the advisor's best move every time, so it is
  an upper bound rather than a typical player. Real players haven't tested
  this tuning yet.
- The game has been verified in the Editor/batch mode and in macOS builds, and
  the generated Xcode project compiles for iPhone (unsigned). It hasn't been
  run on an iOS device yet, so the haptics have not been felt on hardware.
