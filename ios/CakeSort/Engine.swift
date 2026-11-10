import Foundation

/// Pure game rules. This type has no UI or platform dependencies.
enum Rules {
    static let slicesPerCake = 6
    static let boardCols = 5
    static let boardRows = 5
    static let slotCount = 3
    static let pointsPerSlice = 10
    static let fullCakeChance = 0.05

    static func flavorsForLevel(_ level: Int) -> Int {
        min(3 + level / 2, 8)
    }

    static func targetForLevel(_ level: Int) -> Int {
        300 + level * 150
    }
}

/// A cake contains six flavor ids. Array order defines the contiguous wedges.
struct Cake: Equatable {
    let slices: [Int]

    init(_ slices: [Int]) {
        precondition(slices.count == Rules.slicesPerCake, "A cake must have six slices")
        self.slices = slices
    }

    func copy() -> Cake {
        Cake(slices)
    }
}

/// A plate holds between zero and six slices, potentially of mixed flavors.
struct Plate: Equatable {
    var slices: [Int] = []

    var isEmpty: Bool { slices.isEmpty }
    var isFull: Bool { slices.count == Rules.slicesPerCake }

    func countOf(_ flavor: Int) -> Int {
        slices.reduce(into: 0) { count, slice in
            if slice == flavor { count += 1 }
        }
    }

    func isComplete() -> Bool {
        isFull && slices.allSatisfy { $0 == slices[0] }
    }
}

enum GameEvent: Equatable {
    case placed(plate: Int)
    case merged(from: Int, to: Int, flavor: Int, count: Int, giveBack: [Int])
    case served(plate: Int, flavor: Int, points: Int, combo: Int)
    case slotRefilled(slot: Int, cake: Cake)
    case won
    case lost
}

enum GameState: Equatable {
    case playing
    case won
    case lost
}

/// Small deterministic generator: identical seeds produce identical game sequences.
struct SplitMix64: RandomNumberGenerator {
    private var state: UInt64

    init(seed: UInt64) {
        state = seed
    }

    mutating func next() -> UInt64 {
        state &+= 0x9E3779B97F4A7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58476D1CE4E5B9
        z = (z ^ (z >> 27)) &* 0x94D049BB133111EB
        return z ^ (z >> 31)
    }

    mutating func nextDouble() -> Double {
        Double(next() >> 11) * (1.0 / 9_007_199_254_740_992.0)
    }

    mutating func nextInt(_ upperBound: Int) -> Int {
        precondition(upperBound > 0, "Upper bound must be positive")
        return Int(next() % UInt64(upperBound))
    }
}

final class Engine {
    private(set) var level: Int
    private var rng: SplitMix64
    private(set) var flavorCount: Int
    private(set) var targetScore: Int
    private(set) var score = 0
    private(set) var cakesServed = 0
    private(set) var comboChain = 0
    private(set) var state: GameState = .playing
    var plates = Array(repeating: Plate(), count: Rules.boardCols * Rules.boardRows)
    var slots = Array<Cake?>(repeating: nil, count: Rules.slotCount)

    init(level: Int, seed: UInt64 = 0) {
        self.level = level
        self.rng = SplitMix64(seed: seed)
        self.flavorCount = Rules.flavorsForLevel(level)
        self.targetScore = Rules.targetForLevel(level)
        for slot in 0..<Rules.slotCount {
            slots[slot] = generateCake()
        }
    }

    private func generateCake() -> Cake {
        var slices = Array(repeating: 0, count: Rules.slicesPerCake)
        if rng.nextDouble() < Rules.fullCakeChance {
            let flavor = rng.nextInt(flavorCount)
            for index in slices.indices { slices[index] = flavor }
            return Cake(slices)
        }

        let first = rng.nextInt(flavorCount)
        var second = rng.nextInt(flavorCount - 1)
        if second >= first { second += 1 }
        let cut = 1 + rng.nextInt(Rules.slicesPerCake - 1)
        for index in 0..<Rules.slicesPerCake {
            slices[index] = index < cut ? first : second
        }
        return Cake(slices)
    }

    private func neighbors(_ index: Int) -> [Int] {
        let row = index / Rules.boardCols
        let column = index % Rules.boardCols
        var result: [Int] = []
        if row > 0 { result.append(index - Rules.boardCols) }
        if row < Rules.boardRows - 1 { result.append(index + Rules.boardCols) }
        if column > 0 { result.append(index - 1) }
        if column < Rules.boardCols - 1 { result.append(index + 1) }
        return result
    }

    func canPlace(_ plateIndex: Int) -> Bool {
        state == .playing && plates.indices.contains(plateIndex) && plates[plateIndex].isEmpty
    }

    /// Places a waiting cake and returns the complete, ordered resolution event list.
    func place(slot slotIndex: Int, plate plateIndex: Int) -> [GameEvent] {
        guard state == .playing else { return [] }
        guard let cake = slots[slotIndex] else { return [] }
        guard canPlace(plateIndex) else { return [] }

        var events: [GameEvent] = []
        plates[plateIndex].slices.append(contentsOf: cake.slices)
        slots[slotIndex] = nil
        events.append(.placed(plate: plateIndex))
        comboChain = 0
        resolve(events: &events)

        let fresh = generateCake()
        slots[slotIndex] = fresh
        events.append(.slotRefilled(slot: slotIndex, cake: fresh))
        if score >= targetScore {
            state = .won
            events.append(.won)
        } else if plates.allSatisfy({ !$0.isEmpty }) {
            state = .lost
            events.append(.lost)
        }
        return events
    }

    private func resolve(events: inout [GameEvent]) {
        // Loop guard: slice exchanges can shuttle slices between two plates; stop on a repeated board state.
        var seenStates = Set<String>()
        while true {
            // Serve every complete plate before considering another merge.
            var servedAny = false
            for index in plates.indices where plates[index].isComplete() {
                comboChain += 1
                let points = Rules.pointsPerSlice * Rules.slicesPerCake * comboChain
                let flavor = plates[index].slices[0]
                plates[index].slices.removeAll()
                score += points
                cakesServed += 1
                events.append(.served(plate: index, flavor: flavor, points: points, combo: comboChain))
                servedAny = true
            }
            if servedAny {
                seenStates.removeAll()
                continue
            }

            let stateKey = plates.map { plate in plate.slices.map(String.init).joined(separator: ",") }
                .joined(separator: "|")
            if !seenStates.insert(stateKey).inserted { break }

            guard let merge = findBestMerge() else { break }
            for _ in 0..<merge.count {
                if let sliceIndex = plates[merge.from].slices.firstIndex(of: merge.flavor) {
                    plates[merge.from].slices.remove(at: sliceIndex)
                }
                plates[merge.to].slices.append(merge.flavor)
            }
            for returned in merge.giveBack {
                if let sliceIndex = plates[merge.to].slices.firstIndex(of: returned) {
                    plates[merge.to].slices.remove(at: sliceIndex)
                }
                plates[merge.from].slices.append(returned)
            }
            events.append(.merged(from: merge.from, to: merge.to,
                                  flavor: merge.flavor, count: merge.count, giveBack: merge.giveBack))
        }
    }

    /// Finds the highest-priority legal merge while retaining Kotlin's stable tie-breaking.
    private func findBestMerge() -> MergeChoice? {
        var best: MergeChoice?
        for firstIndex in plates.indices {
            let first = plates[firstIndex]
            if first.isEmpty { continue }
            for secondIndex in neighbors(firstIndex) {
                let second = plates[secondIndex]
                if second.isEmpty { continue }

                // Array-order de-duplication matches Kotlin's insertion-ordered Set iteration.
                var shared: [Int] = []
                for flavor in first.slices where !shared.contains(flavor) && second.slices.contains(flavor) {
                    shared.append(flavor)
                }
                for flavor in shared {
                    // Drain the smaller holder into the larger; ties drain second into first.
                    let swap = first.countOf(flavor) >= second.countOf(flavor)
                    let source = swap ? second : first
                    let destination = swap ? first : second
                    let sourceIndex = swap ? secondIndex : firstIndex
                    let destinationIndex = swap ? firstIndex : secondIndex
                    // Capacity: free room plus, when full, the non-matching slices it can trade back.
                    let room = Rules.slicesPerCake - destination.slices.count
                    let tradable = destination.slices.count - destination.countOf(flavor)
                    let movable = min(source.countOf(flavor), room + tradable)
                    if movable <= 0 { continue }
                    let completes = destination.countOf(flavor) + movable == Rules.slicesPerCake
                    let drains = movable == source.countOf(flavor)
                    if !completes && !drains { continue }

                    // Slices that overflow past free room are swapped back, first non-matching first.
                    let giveBackCount = movable - min(movable, room)
                    let giveBack = Array(destination.slices.filter { $0 != flavor }.prefix(giveBackCount))
                    let choice = MergeChoice(from: sourceIndex, to: destinationIndex,
                                             flavor: flavor, count: movable, giveBack: giveBack,
                                             completes: completes)
                    if best == nil || choice.priority > best!.priority {
                        best = choice
                    }
                }
            }
        }
        return best
    }

    private struct MergeChoice {
        let from: Int
        let to: Int
        let flavor: Int
        let count: Int
        let giveBack: [Int]
        let completes: Bool

        var priority: Int {
            (completes ? 1000 : 0) + count * 10 - from
        }
    }

    func hammer(_ plateIndex: Int) -> Bool {
        guard state == .playing, plates.indices.contains(plateIndex) else { return false }
        guard !plates[plateIndex].isEmpty else { return false }
        plates[plateIndex].slices.removeAll()
        return true
    }

    func shuffle() {
        guard state == .playing else { return }
        for slot in 0..<Rules.slotCount {
            slots[slot] = generateCake()
        }
    }

    func nextLevel() {
        guard state == .won else { return }
        level += 1
        flavorCount = Rules.flavorsForLevel(level)
        targetScore = Rules.targetForLevel(level)
        score = 0
        cakesServed = 0
        comboChain = 0
        for index in plates.indices { plates[index].slices.removeAll() }
        for slot in 0..<Rules.slotCount {
            slots[slot] = generateCake()
        }
        state = .playing
    }
}
