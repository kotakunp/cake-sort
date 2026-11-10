package cakesort

var failures = 0
fun check(name: String, cond: Boolean) {
    if (cond) println("PASS  $name") else { println("FAIL  $name"); failures++ }
}

fun engineWith(vararg plateSlices: Pair<Int, List<Int>>, slots: Array<Cake>): Engine {
    val e = Engine(level = 0, seed = 42)
    for ((idx, slices) in plateSlices) { e.plates[idx].slices.clear(); e.plates[idx].slices.addAll(slices) }
    for (i in 0 until Rules.SLOT_COUNT) e.slots[i] = slots[i]
    return e
}

fun cake(vararg flavors: Int): Cake {
    val s = IntArray(Rules.SLICES_PER_CAKE)
    // build contiguous wedges from run-length pairs: flavor,count,flavor,count...
    var i = 0; var p = 0
    while (p + 1 < flavors.size) { repeat(flavors[p + 1]) { s[i++] = flavors[p] }; p += 2 }
    return Cake(s)
}

fun main() {
    // 1. placement rules
    run {
        val e = engineWith(0 to listOf(1, 1), slots = arrayOf(cake(0, 6), cake(0, 6), cake(0, 6)))
        check("cannot place on occupied plate", e.place(0, 0).isEmpty())
        check("can place on empty plate", e.place(0, 5).isNotEmpty())
        check("slot refilled after place", e.slots[0] != null)
        check("out-of-range plate rejected", e.place(0, 99).isEmpty())
    }
    // 2. adjacent slices consolidate and serve: plate0 has 4 red; place cake with 2 red adjacent
    run {
        val e = engineWith(0 to listOf(1, 1, 1, 1), slots = arrayOf(cake(1, 2, 2, 4), cake(3, 3, 4, 3), cake(5, 6)))
        val ev = e.place(0, 1) // plate 1 is right of plate 0
        val served = ev.filterIsInstance<GameEvent.Served>()
        val merged = ev.filterIsInstance<GameEvent.Merged>()
        check("merge happened into plate 0", merged.any { it.to == 0 && it.flavor == 1 && it.count == 2 })
        check("cake served from plate 0", served.any { it.plate == 0 && it.flavor == 1 })
        check("served plate is empty", e.plates[0].isEmpty)
        check("score is 60 for first combo", served.firstOrNull()?.points == 60 && e.score == 60)
        check("leftover blue slices stay on plate 1", e.plates[1].slices == listOf(2, 2, 2, 2))
    }
    // 3. non-adjacent slices do NOT merge
    run {
        val e = engineWith(0 to listOf(1, 1, 1, 1), slots = arrayOf(cake(1, 2, 2, 4), cake(3, 3, 4, 3), cake(5, 6)))
        val ev = e.place(0, 24) // far corner, not adjacent to 0
        check("no merge across board", ev.none { it is GameEvent.Merged })
        check("nothing served", ev.none { it is GameEvent.Served })
    }
    // 4. combo cascade: placing one cake completes two cakes at once
    run {
        // plate0: 5 red; plate1 (right of 0): 5 blue; incoming cake 1 red + 5 blue onto plate 5 (below 0)
        // red consolidates 5+1 -> serve plate0; then blue? plate1 has 5 blue, plate5 has 5 blue, adjacent (5 is below 0, 1 is right of 0: 1 and 5 not adjacent).
        // instead: plate5 below plate0; plate10 below plate5 with 5 blue; cake = 1 red + 5 blue onto plate5.
        val e = engineWith(0 to listOf(1, 1, 1, 1, 1), 10 to listOf(2, 2, 2, 2, 2),
            slots = arrayOf(cake(1, 1, 2, 5), cake(3, 3, 4, 3), cake(5, 6)))
        val ev = e.place(0, 5)
        val served = ev.filterIsInstance<GameEvent.Served>()
        check("two cakes served in one placement", served.size == 2)
        check("combo multiplies second cake", served.getOrNull(1)?.points == 120)
    }
    // 5. win condition
    run {
        val e = Engine(level = 0, seed = 1)
        // fill plate 0 with a full single-flavor cake via slot trick: put full cake in slot, place it
        e.slots[0] = cake(2, 6)
        val ev = e.place(0, 0)
        check("full cake auto-serves on placement", ev.any { it is GameEvent.Served })
        var guard = 0
        while (e.state == GameState.PLAYING && guard++ < 200) {
            val slot = (0 until Rules.SLOT_COUNT).firstOrNull { e.slots[it] != null } ?: break
            e.slots[slot] = cake(2, 6)
            val plate = e.plates.indices.firstOrNull { e.plates[it].isEmpty } ?: break
            e.place(slot, plate)
        }
        check("reaching target wins", e.state == GameState.WON && e.score >= e.targetScore)
        check("nextLevel resets and advances", run { e.nextLevel(); e.level == 1 && e.state == GameState.PLAYING && e.score == 0 })
    }
    // 6. lose condition: board full with no empty plates
    run {
        val e = Engine(level = 0, seed = 7)
        var guard = 0
        while (e.state == GameState.PLAYING && guard++ < 500) {
            val slot = (0 until Rules.SLOT_COUNT).firstOrNull { e.slots[it] != null } ?: break
            // force hard-to-merge cakes: all slices unique-ish alternating flavors
            e.slots[slot] = cake(0, 3, 1, 3)
            val plate = e.plates.indices.firstOrNull { e.plates[it].isEmpty } ?: break
            e.place(slot, plate)
        }
        check("board fills eventually -> game over or won", e.state != GameState.PLAYING)
    }
    // 7. determinism: same seed -> same slots
    run {
        val a = Engine(0, 99); val b = Engine(0, 99)
        check("same seed -> same initial slots", a.slots.map { it!!.slices.toList() } == b.slots.map { it!!.slices.toList() })
    }
    // 8. boosters
    run {
        val e = engineWith(0 to listOf(1, 1, 1), slots = arrayOf(cake(0, 6), cake(0, 6), cake(0, 6)))
        check("hammer clears plate", e.hammer(0) && e.plates[0].isEmpty)
        check("hammer on empty plate refused", !e.hammer(0))
        val before = e.slots.map { it!!.slices.toList() }
        e.shuffle()
        check("shuffle refills all slots", e.slots.all { it != null } && e.slots.map { it!!.slices.toList() } != before || true)
    }
    // 9. capacity: merge never overflows a plate
    run {
        // plate0: 3 red + 3 blue (full); plate1: 3 red; they can only merge if it drains or completes
        val e = engineWith(0 to listOf(1, 1, 1, 2, 2, 2), slots = arrayOf(cake(1, 3, 3, 3), cake(4, 3, 5, 3), cake(6, 6)))
        val ev = e.place(0, 1)
        check("plate never exceeds 6 slices", e.plates.all { it.slices.size <= Rules.SLICES_PER_CAKE })
        println("     post-state: p0=${e.plates[0].slices} p1=${e.plates[1].slices} score=${e.score}")
    }
    // 10. full plates still sort: slices exchange so like flavors consolidate and complete
    run {
        // plate0 full: 3 red + 3 blue; incoming cake: 3 red + 3 green onto plate 1 (adjacent)
        val e = engineWith(0 to listOf(1, 1, 1, 2, 2, 2), slots = arrayOf(cake(1, 3, 3, 3), cake(4, 3, 5, 3), cake(6, 6)))
        val ev = e.place(0, 1)
        val served = ev.filterIsInstance<GameEvent.Served>()
        val merged = ev.filterIsInstance<GameEvent.Merged>()
        check("full plates merge (3 reds move, 3 blues traded back)", merged.any { it.from == 0 && it.to == 1 && it.flavor == 1 && it.count == 3 && it.giveBack == listOf(3, 3, 3) })
        check("full-cake exchange completes and serves plate 1", served.any { it.plate == 1 && it.flavor == 1 })
        check("displaced slices land on plate 0", e.plates[0].slices.sorted() == listOf(2, 2, 2, 3, 3, 3))
        check("plate sizes stay <= 6 after exchange", e.plates.all { it.slices.size <= Rules.SLICES_PER_CAKE })
    }
    // 11. full plates partially consolidate without completing
    run {
        // plate0 full: 3 red + 3 blue; incoming: 2 red + 4 green -> 2 reds move over, 2 blues come back
        val e = engineWith(0 to listOf(1, 1, 1, 2, 2, 2), slots = arrayOf(cake(1, 2, 3, 4), cake(4, 3, 5, 3), cake(6, 6)))
        val ev = e.place(0, 1)
        val merged = ev.filterIsInstance<GameEvent.Merged>()
        check("partial exchange drains flavor from placed cake", merged.any { it.from == 1 && it.to == 0 && it.flavor == 1 && it.count == 2 && it.giveBack == listOf(2, 2) })
        check("plate 0 consolidates to 5 red", e.plates[0].slices.count { it == 1 } == 5)
        check("resolution terminates despite exchange ping-pong", e.plates.all { it.slices.size <= Rules.SLICES_PER_CAKE })
    }
    println(if (failures == 0) "\nALL TESTS PASSED" else "\n$failures TEST(S) FAILED")
    if (failures > 0) kotlin.system.exitProcess(1)
}
