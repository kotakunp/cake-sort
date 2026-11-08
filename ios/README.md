# Cake Sort Puzzle — native iOS

An original, dependency-free SwiftUI implementation of the cake slice-sorting puzzle. All cake art is drawn procedurally with SwiftUI `Canvas`; no external game code, names, images, or audio assets are used.

## Open and run

1. On macOS, install **Xcode 15 or newer**.
2. Open `CakeSort.xcodeproj` in this folder.
3. Select the **CakeSort** scheme and an iPhone simulator (or a signing-enabled iOS device).
4. Press **Cmd-R**. The deployment target is iOS 15.0 and the app is portrait-only.

If running on a device, choose your development team under **Signing & Capabilities**. No packages, CocoaPods, asset catalogs, or additional setup are needed.

## Run the engine tests

Select the **CakeSort** scheme and press **Cmd-U**, or choose **Product > Test**. The `CakeSortTests` XCTest target is hosted by the app and contains nine engine test groups covering placement, adjacency, merging, combo scoring, win/level progression, board-full loss, deterministic generation, boosters, and capacity.

## Controls

- Drag a waiting cake to an empty plate, or tap a cake and then tap an empty plate.
- Hammer: tap the booster, then an occupied plate.
- Shuffle: regenerate all three waiting cakes.
- Each level starts with three hammers and three shuffles.
- The speaker button toggles sound. Level, high score, and sound preference persist in user defaults.

## Handwritten project note

`CakeSort.xcodeproj/project.pbxproj` was authored by hand (object version 56). If a particular Xcode installation rejects it, create a fresh iOS **App** project with SwiftUI, iOS 15.0, and bundle identifier `com.example.cakesort`, then drag in the `CakeSort/` and `CakeSortTests/` folders. Replace the generated app entry point and ensure `CakeSort/Info.plist` is selected as the app target's Info.plist. Add an XCTest target named `CakeSortTests` if the recovery project does not already include one.

## Validation note

The Kotlin reference suite in `../shared` was run successfully on Windows (all 22 checks passed). The Swift sources and Xcode project were statically audited in this environment; compiling and running the iOS app requires Xcode on macOS.
