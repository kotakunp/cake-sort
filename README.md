# Cake Sort — Analysis & Native Rebuild

This workspace contains:

| Path | What it is |
|---|---|
| `cake-sort.xapk` | Original analyzed artifact (untouched) |
| `REVERSE_ENGINEERING_REPORT.md` | Full findings: identity, engine, mechanics, screens, systems, evidence ledger |
| `evidence/cake-sort-evidence.reabundle` | REA evidence bundle (3 records, SHA-256 pinned artifact graph) |
| `extracted/` | Extracted xapk members + recovered metadata strings (`unity/game_strings.txt`, `unity/all_strings.txt`) |
| `shared/Engine.kt`, `shared/EngineTest.kt` | Original, validated game-engine implementation (22 unit tests, all passing) used as the contract for both platforms |
| `android/` | Native Android rebuild (Kotlin, zero dependencies, custom Canvas rendering) |
| `ios/` | Native iOS rebuild (Swift, SwiftUI, Xcode project + XCTest target) |
| `tools/kotlinc/` | Standalone Kotlin compiler used to validate the engine on this machine |

## The rebuild

Both apps implement the same core loop observed in the original: a 5x5 board of plates, a queue of 3 cakes, drag/tap placement, automatic consolidation of matching adjacent slices, serving completed cakes for combo-multiplied points, level target scores, win/lose flow, and hammer + shuffle boosters. All code and all art are original (procedurally drawn) — no assets or code were taken from the analyzed package. Server-backed features of the original (teams, seasons, shop, ads) are documented in the report but intentionally out of scope.

## Validate the shared engine

```powershell
C:\Users\kotak\Desktop\cake-sort\tools\kotlinc\bin\kotlinc.bat shared\Engine.kt shared\EngineTest.kt -include-runtime -d shared\engine-test.jar
java -jar shared\engine-test.jar
```

Expected: `ALL TESTS PASSED` (22 checks: placement, merging, serving, combos, win/lose, boosters, determinism, capacity).

## Build the apps

- **Android**: open `android/` in Android Studio (Hedgehog+), let it sync AGP, run on a device/emulator with API 24+. See `android/README.md`.
- **iOS**: open `ios/CakeSort.xcodeproj` in Xcode 15+, run the `CakeSort` scheme; `Cmd-U` runs the engine tests. See `ios/README.md`.
