# Cake Sort Puzzle

An original, native Android implementation of a cake slice-sorting puzzle. All game code is written for this project and every visual is drawn procedurally with Android `Canvas` or a small original vector drawable. There are no third-party libraries, AndroidX dependencies, asset packs, or network permissions.

## Open and run in Android Studio

1. Open a recent Android Studio version with Android SDK 34 installed (Android Studio Koala or newer is recommended).
2. Choose **File > Open** and select this `android` directory: `C:\Users\kotak\Desktop\cake-sort\android`.
3. Let Gradle sync. On the first sync, Android Studio downloads the Android Gradle Plugin 8.5.2 and Kotlin plugin 2.0.0 if they are not cached. Use Android Studio's embedded JDK.
4. Select the `app` run configuration, choose an emulator or device running Android 7.0 (API 24) or newer, and click **Run**.

The app is portrait-only and has no runtime permission prompts. Internet access is needed only for the initial Gradle/plugin dependency sync, not by the game itself.

## Project files

- `settings.gradle.kts` — plugin repositories and the single `:app` module.
- `build.gradle.kts` — AGP 8.5.2 and Kotlin 2.0.0 plugin versions.
- `gradle.properties` — JVM and Android build settings.
- `app/build.gradle.kts` — application ID, API 24 minimum, API 34 target/compile SDK, and Java/Kotlin bytecode settings. There are no application dependencies.
- `app/proguard-rules.pro` — placeholder for optional release shrinking rules.
- `app/src/main/AndroidManifest.xml` — launcher `MainActivity`, portrait lock, theme, label, and icon.
- `app/src/main/java/com/example/cakesort/Engine.kt` — the pure Kotlin game rules. It is an exact copy of `shared/Engine.kt` except for its package declaration.
- `app/src/main/java/com/example/cakesort/MainActivity.kt` — dependency-free `android.app.Activity` hosting the game and forwarding lifecycle events.
- `app/src/main/java/com/example/cakesort/GameView.kt` — responsive Canvas layout, procedural cake/plate art, animations, touch input, boosters, overlays, sound, and preferences.
- `app/src/main/res/drawable/ic_launcher.xml` — original vector cake/plate launcher icon.
- `app/src/main/res/values/strings.xml` — application label.
- `app/src/main/res/values/colors.xml` — theme colors.
- `app/src/main/res/values/themes.xml` — platform Material light theme with no action bar; no AndroidX theme is required.

## How to play

- **Drag and drop:** press one of the three waiting cakes, drag its ghost over an empty plate, and release. Empty plates are highlighted while dragging.
- **Tap controls:** tap a waiting cake, then tap an empty plate. Tap the selected waiting cake again to deselect it.
- **Sorting:** matching slices on orthogonally adjacent plates consolidate automatically. A plate containing six matching slices is served and leaves an empty plate. Cascaded serves in one placement earn combo points.
- **Goal:** reach the score target to complete the level. The game ends if every plate is occupied before the target is reached.
- **Hammer:** tap Hammer, then a non-empty plate to clear it. Three hammers are available per level.
- **Shuffle:** tap Shuffle to regenerate all three waiting cakes. Three shuffles are available per level.
- **Overlays:** **Next Level** advances after a win; **Try Again** starts the same level afresh after a loss.
- **Sound:** tap the speaker in the top HUD to mute/unmute feedback.

The HUD shows the current level, score/target with a progress bar, high score, and cakes served. Current level, high score, and sound preference are saved with `SharedPreferences`; an in-progress board is intentionally not saved.

## Animation and audio design

`GameEvent.Merged` creates short, traceable wedge flights between the source and destination plates, with matching source/destination rings. `GameEvent.Served` creates a 320 ms scale/fade cake and a floating score/combo label. The win overlay waits until the final event animation has finished. Animation uses `SystemClock`, event lists, and `postInvalidateOnAnimation()`; no coroutines or animation libraries are required.

Audio uses Android's `ToneGenerator`, with separate short system cues for placement, merging, serving, loss, and a three-note win arpeggio. `ToneGenerator` exposes predefined system tones rather than arbitrary oscillator frequencies, so the cues are distinct feedback tones rather than exact 440/660/880 Hz synthesis.

## Engine validation

The shared behavioral suite was compiled against the **copied app engine**, with only the test copy's package line changed in a temporary directory. The original files in `shared` were not modified. An automated source comparison also confirmed that the app engine differs from `shared/Engine.kt` only in the package declaration.

Result: **22 PASS lines, zero failures, `ALL TESTS PASSED`**. Coverage includes placement rejection/refills, adjacent merging, serving, combo scoring, non-adjacent behavior, win/level progression, board exhaustion, deterministic seeds, boosters, and plate capacity.

A local APK build was not run on the authoring machine because it has neither an Android SDK nor Gradle installed. The activity/view sources and resources were manually reviewed for Android API and Kotlin compile correctness; Android Studio performs the final platform build during sync/run.
