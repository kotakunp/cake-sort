import AudioServices
import SwiftUI

struct MergeFlight: Identifiable, Equatable {
    let id = UUID()
    let from: Int
    let to: Int
    let flavor: Int
    let count: Int
}

struct ServeAnimation: Identifiable, Equatable {
    let id = UUID()
    let plate: Int
    let flavor: Int
}

struct ComboPopup: Identifiable, Equatable {
    let id = UUID()
    let plate: Int
    let text: String
}

@MainActor
final class GameViewModel: ObservableObject {
    static let gameCoordinateSpace = "CakeSortGame"

    @AppStorage("savedLevel") private var savedLevel = 0
    @AppStorage("highScore") private(set) var highScore = 0
    @AppStorage("soundEnabled") var soundEnabled = true

    @Published private(set) var plates: [Plate]
    @Published private(set) var slots: [Cake?]
    @Published private(set) var score: Int
    @Published private(set) var targetScore: Int
    @Published private(set) var cakesServed: Int
    @Published private(set) var state: GameState

    @Published private(set) var selectedSlot: Int?
    @Published private(set) var isBusy = false
    @Published private(set) var hammerCount = 3
    @Published private(set) var shuffleCount = 3
    @Published private(set) var hammerArmed = false

    @Published private(set) var draggingSlot: Int?
    @Published private(set) var dragLocation = CGPoint.zero
    @Published private(set) var highlightedPlate: Int?
    @Published private(set) var shakingSlot: Int?
    @Published private(set) var shakingPlate: Int?

    @Published private(set) var mergeFlight: MergeFlight?
    @Published private(set) var serveAnimation: ServeAnimation?
    @Published private(set) var comboPopups: [ComboPopup] = []

    private(set) var engine: Engine
    var plateFrames: [Int: CGRect] = [:]

    var level: Int { engine.level }

    init(seed: UInt64 = UInt64(Date().timeIntervalSince1970)) {
        let restoredLevel = UserDefaults.standard.integer(forKey: "savedLevel")
        let restoredEngine = Engine(level: restoredLevel, seed: seed)
        engine = restoredEngine
        plates = restoredEngine.plates
        slots = restoredEngine.slots
        score = restoredEngine.score
        targetScore = restoredEngine.targetScore
        cakesServed = restoredEngine.cakesServed
        state = restoredEngine.state
    }

    func toggleSound() {
        soundEnabled.toggle()
        objectWillChange.send()
    }

    func selectSlot(_ slot: Int) {
        guard state == .playing, !isBusy, slots.indices.contains(slot), slots[slot] != nil else { return }
        hammerArmed = false
        selectedSlot = selectedSlot == slot ? nil : slot
    }

    func beginDrag(slot: Int, at location: CGPoint) {
        guard state == .playing, !isBusy, slots.indices.contains(slot), slots[slot] != nil else { return }
        selectedSlot = nil
        hammerArmed = false
        draggingSlot = slot
        dragLocation = location
    }

    func updateDrag(to location: CGPoint) {
        guard draggingSlot != nil else { return }
        dragLocation = location
        highlightedPlate = plateFrames.first { _, frame in
            frame.contains(location)
        }.flatMap { index, _ in engine.canPlace(index) ? index : nil }
    }

    func endDrag(at location: CGPoint) {
        guard let slot = draggingSlot else { return }
        let plate = plateFrames.first { _, frame in frame.contains(location) }?.key
        draggingSlot = nil
        highlightedPlate = nil
        if let plate = plate, engine.canPlace(plate) {
            place(slot: slot, plate: plate)
        } else {
            rejectDrop(slot: slot, plate: plate)
        }
    }

    func tapPlate(_ plate: Int) {
        guard state == .playing, !isBusy else { return }
        if hammerArmed {
            useHammer(on: plate)
            return
        }
        guard let slot = selectedSlot else { return }
        if engine.canPlace(plate) {
            place(slot: slot, plate: plate)
        } else {
            rejectDrop(slot: slot, plate: plate)
        }
    }

    func armHammer() {
        guard state == .playing, !isBusy, hammerCount > 0 else { return }
        selectedSlot = nil
        hammerArmed.toggle()
    }

    func shuffle() {
        guard state == .playing, !isBusy, shuffleCount > 0 else { return }
        selectedSlot = nil
        hammerArmed = false
        engine.shuffle()
        shuffleCount -= 1
        withAnimation(.easeInOut(duration: 0.2)) {
            slots = engine.slots
        }
        playSound(1104)
    }

    func nextLevel() {
        guard state == .won, !isBusy else { return }
        engine.nextLevel()
        resetLevelControls()
        synchronizeFromEngine()
    }

    func retry() {
        guard state == .lost, !isBusy else { return }
        engine = Engine(level: level, seed: UInt64(Date().timeIntervalSince1970))
        resetLevelControls()
        synchronizeFromEngine()
    }

    private func resetLevelControls() {
        hammerCount = 3
        shuffleCount = 3
        hammerArmed = false
        selectedSlot = nil
        mergeFlight = nil
        serveAnimation = nil
        comboPopups = []
    }

    private func useHammer(on plate: Int) {
        guard hammerCount > 0, engine.hammer(plate) else {
            rejectDrop(slot: nil, plate: plate)
            return
        }
        hammerCount -= 1
        hammerArmed = false
        withAnimation(.easeOut(duration: 0.2)) {
            plates = engine.plates
        }
        playSound(1057)
    }

    private func place(slot: Int, plate: Int) {
        guard !isBusy, let cake = slots[slot], engine.canPlace(plate) else { return }
        let events = engine.place(slot: slot, plate: plate)
        guard !events.isEmpty else { return }
        selectedSlot = nil
        isBusy = true
        Task {
            await animatePlacement(events, slot: slot, cake: cake)
        }
    }

    /// Replays the ordered engine events against snapshots, then commits the authoritative state.
    private func animatePlacement(_ events: [GameEvent], slot: Int, cake: Cake) async {
        for event in events {
            switch event {
            case .placed(let plate):
                withAnimation(.easeOut(duration: 0.15)) {
                    plates[plate].slices = cake.slices
                    slots[slot] = nil
                }
                playSound(1104)
                await pause(milliseconds: 140)

            case .merged(let from, let to, let flavor, let count):
                mergeFlight = MergeFlight(from: from, to: to, flavor: flavor, count: count)
                await pause(milliseconds: 200)
                withAnimation(.easeInOut(duration: 0.08)) {
                    for _ in 0..<count {
                        if let index = plates[from].slices.firstIndex(of: flavor) {
                            plates[from].slices.remove(at: index)
                        }
                        plates[to].slices.append(flavor)
                    }
                    mergeFlight = nil
                }

            case .served(let plate, let flavor, let points, let combo):
                let popup = ComboPopup(plate: plate,
                                       text: combo > 1 ? "Combo x\(combo)  +\(points)" : "+\(points)")
                withAnimation(.spring(response: 0.25, dampingFraction: 0.65)) {
                    comboPopups.append(popup)
                    serveAnimation = ServeAnimation(plate: plate, flavor: flavor)
                }
                playSound(1057)
                await pause(milliseconds: 300)
                plates[plate].slices.removeAll()
                score += points
                cakesServed += 1
                serveAnimation = nil
                Task { @MainActor in
                    await pause(milliseconds: 450)
                    comboPopups.removeAll { $0.id == popup.id }
                }

            case .slotRefilled(let refilledSlot, let fresh):
                withAnimation(.spring(response: 0.3, dampingFraction: 0.75)) {
                    slots[refilledSlot] = fresh
                }

            case .won:
                state = .won
                playSound(1113)

            case .lost:
                state = .lost
                playSound(1057)
            }
        }
        synchronizeFromEngine()
        isBusy = false
    }

    private func synchronizeFromEngine() {
        plates = engine.plates
        slots = engine.slots
        score = engine.score
        targetScore = engine.targetScore
        cakesServed = engine.cakesServed
        state = engine.state
        savedLevel = engine.level
        if score > highScore { highScore = score }
    }

    private func rejectDrop(slot: Int?, plate: Int?) {
        withAnimation(.easeInOut(duration: 0.055).repeatCount(5, autoreverses: true)) {
            shakingSlot = slot
            shakingPlate = plate
        }
        Task { @MainActor in
            await pause(milliseconds: 320)
            withAnimation(.easeOut(duration: 0.08)) {
                shakingSlot = nil
                shakingPlate = nil
            }
        }
    }

    private func playSound(_ identifier: SystemSoundID) {
        guard soundEnabled else { return }
        AudioServicesPlaySystemSound(identifier)
    }

    private func pause(milliseconds: UInt64) async {
        try? await Task.sleep(nanoseconds: milliseconds * 1_000_000)
    }
}
