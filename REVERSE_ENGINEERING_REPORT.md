# Cake Sort — Reverse Engineering Report

## 1. Artifact identity (evidence: `ev_bd9f13d06703692ccd13f343e195677402c859b8f255c9d9db7ca86528427470`, bundle `evidence/cake-sort-evidence.reabundle`)

| Field | Value |
|---|---|
| File | `cake-sort.xapk` (234,526,327 bytes) |
| SHA-256 | `7e8de96edf06e9518a1a7ed99348455da02630c87693a9e3aa38f4ce18c7f952` |
| App name | Cake Sort |
| Package | `com.falcon.dm.water.cake.sort.puzzle` |
| Version | 6.5.2 (versionCode 6521) |
| minSdk / targetSdk | 24 / 36 |
| Splits | base APK (196 MB) + `config.armeabi_v7a` (27 MB) |
| Engine | **Unity (IL2CPP)** — `libunity.so` 14.4 MB, `libil2cpp.so` 52.7 MB, `global-metadata.dat` 11.3 MB, `data.unity3d` 85.4 MB |
| Developer / publisher | D2M Studio (`Assets/_D2MGame/...`, info@d2mstudio.com) / Falcon |
| Notable SDKs | AppLovin MAX (ads), Firebase, AppsFlyer, Nakama/Satori (teams, LiveOps), Spine (animation), Addressables |

113,094 metadata strings and 1,655 C# source paths were recovered from `global-metadata.dat` and analyzed to map the game systems below. Analysis is static: the artifact was never executed.

## 2. Core gameplay (observed class/method names; static evidence)

Cake Sort is a **slice-sorting puzzle**. Evidence-backed entities and rules:

- **Board**: a grid of **plates** (`CakeMatrix`, `Plate`, `DataPlate`, `PlatePanelCell`, `PlatePanelManager`, `gridRow`/`gridCol`, `InitCellCakePos`).
- **Cakes**: each cake is a disk of **slices** (`Cake`, `CakePiece`, `CakePieces`, `HasSlices`, `AllDepthSlices`, `DataTextureSliced`, `GenerateSlicedSprite`). Slices have flavors/colors (`CakeEnum`, `cake1..cakeN`, `ApplyTextureToCake`). Spawning uses 1–2 flavors per cake (`Cake_1_2_random`, `CakeSpawn`, `CheckAndSpawnCake`, `OnAppearNextCake`).
- **Queue**: upcoming cakes wait in slots (`SlotManager`, `FirstSlotDropCakeTutorial`, `IEJumpCakeToCell`); the player places a cake onto an empty plate (`MoveCake`, `PushCake`, `OnPickCakes`).
- **Auto-sorting**: after placement, matching adjacent slices consolidate (`IESortCake`, `IESortCakMulti`, `IESortCakeByComplete`, `CheckCakeAround`, `FindMergeTarget`, `CanMerge`, `IsMergeable`, `IELoopCheckCake`, `IEDelayCheckCakebyConveyor`).
- **Completion**: a plate holding a full single-flavor cake is served/cleared (`IsFullCake`, `OnFullCake`, `IECompleteCake`, `CompleteCake`, `EliminatedCake`, `CheckEliminatedCakes`, `CakeServer` small/normal/large).
- **Scoring**: points per completed cake with **combo** chains (`AddScore`, `AddScoreBonus`, `Combo`, `ComboController`, `ContinueCombo`, `AchieveCombo`, `ComboText`, `CoinPerCake`, `CakeUpScoreConfig`).
- **Levels**: each level has a **target score** (`GetScoreTarget`, `IsTargetScore`, `TargetManager`, `OnBackScoreTarget`, `LevelConfig`, `GetLevelConfig`, `LevelManager`). Progress is shown on a saga-style map (`LevelNode`, `SagaHome`, `SagaIsland`, `LevelSagaIdle`).
- **Lose / revive**: when no plate can take a cake (`CheckFreeCake`, `FreeCake`, `OnlyNocake`, `PopupReviveLevel`, `LogEventLevelFail`).
- **Boosters** (`BoosterItem`, `BoosterConfig`, `BoosterHint`, `BoosterAnimManager`): **Hammer** (`OnHammerCake`, `PanelHammerShop`), **Cupcake Bomb** (`CupCakeBombBooster`), **Magic Whisk** (`MagicWhiskBooster`), **Firework** (`FireworkBooster`); pre-level boosters (`Prebooster`, `IconPreboosterController`, `PopupBuyPrebooster`).
- **Obstacles / level modifiers**: cake boxes, holders, slime plates, move-on-plate objects (`CakeBoxObstacle`, `CakeHolderObstacle`, `AddSlimePlateObject`, `HasSlimePlate`, `MoveOnPlateObstacle`, `CakeBlockType`, `InitCakeBlock`).
- **Extras**: sub-cakes (`AddSubCake`, `CheckSpawnSubcake`), "Board Treasure" dig mini-game (`DiggingCake`, `DiggingAnimServer`), server-cake events (`PopupServerCake`, `ItemCakeServer`).

## 3. App structure (from 1,655 source paths)

- `Scripts/Gameplay`: `CakeController`, `LevelManager`, `SlotManager`, `CellConnect`, `TargetManager`, `BoosterItem`, `PropController`, `ComboText`, `MainUI`, `HomeUI`
- `Scripts/Config`: `LevelConfig`, `MapConfig`, `QuestConfig`, `DecorConfig`, `SeasonPassConfig`, `ShopPackConfig`, `GiftConfig`, `DialogueConfig`
- `Scripts/UI/Home`: `BakeryPanel`/`BakeryItem`/`BakeryTab` (cake collection "Bakery"), `BoutiqueOpen` (skins), `DecorManager`, `QuestManager`
- `SagaScripts`: `SagaHome`, `SagaIsland`, `SagaBuild` (map progression, building decoration)
- `Scripts/UI/Team`: teams, chat, Team-vs-Team events (Nakama backend)
- Monetization: AppLovin MAX rewarded/interstitial ads, IAP shop, season passes (Season1..SS12), subscriptions

## 4. Clone scope decision

The rebuild implements the **core game loop** — grid of plates, 3-cake queue, drag/tap placement, slice consolidation, cake completion + serving, combo scoring, level targets, win/lose flow, hammer & shuffle boosters, level progression — with **original code and original procedurally-drawn art**. Server features (teams, seasons, shop/IAP, ads, dig mini-game, obstacles) are documented above but out of scope for the native rebuild.

## 5. Evidence ledger

| Evidence | ID / location |
|---|---|
| xapk artifact graph | `ev_bd9f13d06703692ccd13f343e195677402c859b8f255c9d9db7ca86528427470` |
| Analysis view (summary) | `ev_be137711d17bb5d9b8a91ff7d6d906cae47fa76a3c9e2fc85dfa216b34ae1137` |
| ZIP inventory | `ev_bce68dfa601bdfed7ebd143feeb17cf4ae765c595a4d3886847df8fedcc2fbad` |
| Exported bundle | `evidence/cake-sort-evidence.reabundle` (3 records, 20,749 bytes) |
| Raw extractions | `extracted/unity/` (metadata, manifest, `game_strings.txt`, `all_strings.txt`) |

Limitations: static analysis only (artifact never executed); JADX provider unavailable (`REA_JADX_MCP_JAR` not set), so DEX/SDK wrappers were not decompiled — game logic lives in IL2CPP native code and Unity assets; exact level-config numbers inside `data.unity3d` were not deserialized. Gameplay rules above are inferred from identifier evidence and the genre, and are marked as inference where exact constants (e.g. slices per cake) were not recoverable from strings alone.
