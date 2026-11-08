import XCTest
@testable import CakeSort

final class EngineTests: XCTestCase {
    private func engineWith(plates plateSlices: [Int: [Int]] = [:], slots: [Cake]) -> Engine {
        let engine = Engine(level: 0, seed: 42)
        for (index, slices) in plateSlices {
            engine.plates[index].slices = slices
        }
        for index in 0..<Rules.slotCount {
            engine.slots[index] = slots[index]
        }
        return engine
    }

    /// Builds six contiguous wedges from flavor,count,flavor,count... run-length pairs.
    private func cake(_ runs: Int...) -> Cake {
        var slices = Array(repeating: 0, count: Rules.slicesPerCake)
        var output = 0
        var position = 0
        while position + 1 < runs.count {
            for _ in 0..<runs[position + 1] {
                slices[output] = runs[position]
                output += 1
            }
            position += 2
        }
        return Cake(slices)
    }

    // 1. Placement rules.
    func testPlacementRules() {
        let full = cake(0, 6)
        let engine = engineWith(plates: [0: [1, 1]], slots: [full, full, full])
        XCTAssertTrue(engine.place(slot: 0, plate: 0).isEmpty)
        XCTAssertFalse(engine.place(slot: 0, plate: 5).isEmpty)
        XCTAssertNotNil(engine.slots[0])
        XCTAssertTrue(engine.place(slot: 0, plate: 99).isEmpty)
    }

    // 2. Four red plus two adjacent red merges and serves.
    func testAdjacentMergeAndServe() {
        let engine = engineWith(
            plates: [0: [1, 1, 1, 1]],
            slots: [cake(1, 2, 2, 4), cake(3, 3, 4, 3), cake(5, 6)]
        )
        let events = engine.place(slot: 0, plate: 1)
        XCTAssertTrue(events.contains { event in
            if case .merged(_, let to, let flavor, let count) = event {
                return to == 0 && flavor == 1 && count == 2
            }
            return false
        })
        let served = events.compactMap { event -> (plate: Int, flavor: Int, points: Int, combo: Int)? in
            if case .served(let plate, let flavor, let points, let combo) = event {
                return (plate, flavor, points, combo)
            }
            return nil
        }
        XCTAssertTrue(served.contains { $0.plate == 0 && $0.flavor == 1 })
        XCTAssertTrue(engine.plates[0].isEmpty)
        XCTAssertEqual(served.first?.points, 60)
        XCTAssertEqual(engine.score, 60)
        XCTAssertEqual(engine.plates[1].slices, [2, 2, 2, 2])
    }

    // 3. Matching flavors on non-adjacent plates do not merge.
    func testNonAdjacentPlatesDoNotMerge() {
        let engine = engineWith(
            plates: [0: [1, 1, 1, 1]],
            slots: [cake(1, 2, 2, 4), cake(3, 3, 4, 3), cake(5, 6)]
        )
        let events = engine.place(slot: 0, plate: 24)
        XCTAssertFalse(events.contains {
            if case .merged = $0 { return true }
            return false
        })
        XCTAssertFalse(events.contains {
            if case .served = $0 { return true }
            return false
        })
    }

    // 4. Two immediate serves score 60 and then 120.
    func testComboCascadeScoring() {
        let engine = engineWith(
            plates: [0: [1, 1, 1, 1, 1], 10: [2, 2, 2, 2, 2]],
            slots: [cake(1, 1, 2, 5), cake(3, 3, 4, 3), cake(5, 6)]
        )
        let events = engine.place(slot: 0, plate: 5)
        let points = events.compactMap { event -> Int? in
            if case .served(_, _, let points, _) = event { return points }
            return nil
        }
        XCTAssertEqual(points.count, 2)
        XCTAssertEqual(points, [60, 120])
    }

    // 5. Reaching the target wins; nextLevel resets and advances.
    func testWinAndNextLevel() {
        let engine = Engine(level: 0, seed: 1)
        engine.slots[0] = cake(2, 6)
        XCTAssertTrue(engine.place(slot: 0, plate: 0).contains {
            if case .served = $0 { return true }
            return false
        })

        var guardCount = 0
        while engine.state == .playing && guardCount < 200 {
            guardCount += 1
            guard let slot = (0..<Rules.slotCount).first(where: { engine.slots[$0] != nil }) else { break }
            engine.slots[slot] = cake(2, 6)
            guard let plate = engine.plates.indices.first(where: { engine.plates[$0].isEmpty }) else { break }
            _ = engine.place(slot: slot, plate: plate)
        }
        XCTAssertEqual(engine.state, .won)
        XCTAssertGreaterThanOrEqual(engine.score, engine.targetScore)
        engine.nextLevel()
        XCTAssertEqual(engine.level, 1)
        XCTAssertEqual(engine.state, .playing)
        XCTAssertEqual(engine.score, 0)
    }

    // 6. Filling the final board space without a serve loses immediately.
    func testBoardFullIsGameOver() {
        let engine = Engine(level: 0, seed: 7)
        for index in 0..<24 {
            engine.plates[index].slices = [0, 0, 0, 1, 1, 1]
        }
        engine.slots[0] = cake(0, 3, 1, 3)
        let events = engine.place(slot: 0, plate: 24)
        XCTAssertEqual(engine.state, .lost)
        XCTAssertTrue(events.contains(.lost))
        XCTAssertEqual(engine.score, 0)
    }

    // 7. A seed completely determines generated waiting cakes.
    func testSeededDeterminism() {
        let first = Engine(level: 0, seed: 99)
        let second = Engine(level: 0, seed: 99)
        XCTAssertEqual(first.slots.map { $0?.slices }, second.slots.map { $0?.slices })
    }

    // 8. Hammer and shuffle booster rules.
    func testHammerAndShuffle() {
        let engine = engineWith(
            plates: [0: [1, 1, 1]],
            slots: [cake(0, 6), cake(0, 6), cake(0, 6)]
        )
        XCTAssertTrue(engine.hammer(0))
        XCTAssertTrue(engine.plates[0].isEmpty)
        XCTAssertFalse(engine.hammer(0))
        let before = engine.slots.map { $0?.slices }
        engine.shuffle()
        XCTAssertTrue(engine.slots.allSatisfy { $0 != nil })
        XCTAssertNotEqual(engine.slots.map { $0?.slices }, before)
    }

    // 9. A legal merge can never push a plate above six slices.
    func testCapacityNeverExceeded() {
        let engine = engineWith(
            plates: [0: [1, 1, 1, 2, 2, 2]],
            slots: [cake(1, 3, 3, 3), cake(4, 3, 5, 3), cake(6, 6)]
        )
        _ = engine.place(slot: 0, plate: 1)
        XCTAssertTrue(engine.plates.allSatisfy { $0.slices.count <= Rules.slicesPerCake })
    }
}
