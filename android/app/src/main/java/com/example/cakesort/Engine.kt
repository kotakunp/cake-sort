package com.example.cakesort

import kotlin.random.Random

/** Original re-implementation of a cake slice-sorting puzzle core loop. Pure logic, no platform deps. */
object Rules {
    const val SLICES_PER_CAKE = 6
    const val BOARD_COLS = 5
    const val BOARD_ROWS = 5
    const val SLOT_COUNT = 3
    const val POINTS_PER_SLICE = 10
    const val FULL_CAKE_CHANCE = 0.05
    fun flavorsForLevel(level: Int) = minOf(3 + level / 2, 8)
    fun targetForLevel(level: Int) = 300 + level * 150
}

/** A cake is 6 wedge slices; slices[i] is a flavor id. Order matters visually (contiguous wedges). */
class Cake(val slices: IntArray) {
    init { require(slices.size == Rules.SLICES_PER_CAKE) }
    fun copy() = Cake(slices.copyOf())
}

/** A plate holds 0..6 slices (mixed flavors allowed). */
class Plate {
    val slices = mutableListOf<Int>()
    val isEmpty get() = slices.isEmpty()
    val isFull get() = slices.size == Rules.SLICES_PER_CAKE
    fun countOf(flavor: Int) = slices.count { it == flavor }
    fun isComplete() = isFull && slices.all { it == slices[0] }
}

sealed class GameEvent {
    data class Placed(val plate: Int) : GameEvent()
    data class Merged(val from: Int, val to: Int, val flavor: Int, val count: Int) : GameEvent()
    data class Served(val plate: Int, val flavor: Int, val points: Int, val combo: Int) : GameEvent()
    data class SlotRefilled(val slot: Int, val cake: Cake) : GameEvent()
    object Won : GameEvent()
    object Lost : GameEvent()
}

enum class GameState { PLAYING, WON, LOST }

class Engine(level: Int, seed: Long = 0L) {
    var level = level; private set
    val rng = Random(seed)
    var flavorCount = Rules.flavorsForLevel(level); private set
    var targetScore = Rules.targetForLevel(level); private set
    var score = 0; private set
    var cakesServed = 0; private set
    var comboChain = 0; private set
    var state = GameState.PLAYING; private set
    val plates = Array(Rules.BOARD_COLS * Rules.BOARD_ROWS) { Plate() }
    val slots = arrayOfNulls<Cake>(Rules.SLOT_COUNT)

    init { repeat(Rules.SLOT_COUNT) { slots[it] = generateCake() } }

    private fun generateCake(): Cake {
        val s = IntArray(Rules.SLICES_PER_CAKE)
        if (rng.nextDouble() < Rules.FULL_CAKE_CHANCE) {
            val f = rng.nextInt(flavorCount); s.fill(f); return Cake(s)
        }
        val a = rng.nextInt(flavorCount)
        var b = rng.nextInt(flavorCount - 1); if (b >= a) b++
        val cut = 1 + rng.nextInt(Rules.SLICES_PER_CAKE - 1) // contiguous wedges: [0,cut)=a, [cut,6)=b
        for (i in 0 until Rules.SLICES_PER_CAKE) s[i] = if (i < cut) a else b
        return Cake(s)
    }

    private fun neighbors(idx: Int): List<Int> {
        val r = idx / Rules.BOARD_COLS; val c = idx % Rules.BOARD_COLS
        val out = mutableListOf<Int>()
        if (r > 0) out += idx - Rules.BOARD_COLS
        if (r < Rules.BOARD_ROWS - 1) out += idx + Rules.BOARD_COLS
        if (c > 0) out += idx - 1
        if (c < Rules.BOARD_COLS - 1) out += idx + 1
        return out
    }

    fun canPlace(plateIdx: Int) = state == GameState.PLAYING && plateIdx in plates.indices && plates[plateIdx].isEmpty

    /** Place the cake from [slotIdx] onto empty plate [plateIdx]; returns ordered events. */
    fun place(slotIdx: Int, plateIdx: Int): List<GameEvent> {
        if (state != GameState.PLAYING) return emptyList()
        val cake = slots[slotIdx] ?: return emptyList()
        if (!canPlace(plateIdx)) return emptyList()
        val events = mutableListOf<GameEvent>()
        plates[plateIdx].slices.addAll(cake.slices.toList())
        slots[slotIdx] = null
        events += GameEvent.Placed(plateIdx)
        comboChain = 0
        resolve(events)
        // refill slot
        val fresh = generateCake(); slots[slotIdx] = fresh
        events += GameEvent.SlotRefilled(slotIdx, fresh)
        if (score >= targetScore) { state = GameState.WON; events += GameEvent.Won }
        else if (plates.all { !it.isEmpty }) { state = GameState.LOST; events += GameEvent.Lost }
        return events
    }

    /** Consolidate identical adjacent slices; serve completed cakes; cascade until stable. */
    private fun resolve(events: MutableList<GameEvent>) {
        while (true) {
            // 1. serve completed cakes (combo chain counts within one placement)
            var servedAny = false
            for (i in plates.indices) {
                val p = plates[i]
                if (p.isComplete()) {
                    comboChain++
                    val pts = Rules.POINTS_PER_SLICE * Rules.SLICES_PER_CAKE * comboChain
                    val flavor = p.slices[0]
                    p.slices.clear()
                    score += pts; cakesServed++
                    events += GameEvent.Served(i, flavor, pts, comboChain)
                    servedAny = true
                }
            }
            if (servedAny) continue
            // 2. single best merge, then loop
            val merge = findBestMerge() ?: break
            val from = merge.from; val to = merge.to
            val flavor = merge.flavor; val count = merge.count
            repeat(count) {
                plates[from].slices.remove(flavor)
                plates[to].slices.add(flavor)
            }
            events += GameEvent.Merged(from, to, flavor, count)
        }
    }

    /**
     * A merge drains flavor f fully from one adjacent plate into another (or completes a cake).
     * Loop-safe: every merge either empties a flavor from a plate or completes a cake (served next pass).
     */
    private fun findBestMerge(): MergeChoice? {
        var best: MergeChoice? = null
        for (i in plates.indices) {
            val a = plates[i]; if (a.isEmpty) continue
            for (j in neighbors(i)) {
                val b = plates[j]; if (b.isEmpty) continue
                val shared = a.slices.toSet() intersect b.slices.toSet()
                for (f in shared) {
                    // direction: drain smaller holder into larger holder
                    val swap = a.countOf(f) >= b.countOf(f)
                    val src = if (swap) b else a
                    val dst = if (swap) a else b
                    val srcIdx = if (swap) j else i
                    val dstIdx = if (swap) i else j
                    val room = Rules.SLICES_PER_CAKE - dst.slices.size
                    val movable = minOf(src.countOf(f), room)
                    if (movable <= 0) continue
                    val completes = dst.countOf(f) + movable == Rules.SLICES_PER_CAKE
                    val drains = movable == src.countOf(f)
                    if (!completes && !drains) continue
                    val choice = MergeChoice(srcIdx, dstIdx, f, movable, completes)
                    if (best == null || choice.priority > best!!.priority) best = choice
                }
            }
        }
        return best
    }

    private class MergeChoice(val from: Int, val to: Int, val flavor: Int, val count: Int, val completes: Boolean) {
        // completing merges first, then bigger moves; stable tie-break by indices
        val priority = (if (completes) 1000 else 0) + count * 10 - from
    }

    /** Booster: hammer — clear a plate's slices. */
    fun hammer(plateIdx: Int): Boolean {
        if (state != GameState.PLAYING || plateIdx !in plates.indices) return false
        if (plates[plateIdx].isEmpty) return false
        plates[plateIdx].slices.clear()
        return true
    }

    /** Booster: shuffle — regenerate all waiting slots. */
    fun shuffle() {
        if (state != GameState.PLAYING) return
        repeat(Rules.SLOT_COUNT) { slots[it] = generateCake() }
    }

    fun nextLevel() {
        if (state != GameState.WON) return
        level++
        flavorCount = Rules.flavorsForLevel(level)
        targetScore = Rules.targetForLevel(level)
        score = 0; cakesServed = 0; comboChain = 0
        plates.forEach { it.slices.clear() }
        repeat(Rules.SLOT_COUNT) { slots[it] = generateCake() }
        state = GameState.PLAYING
    }
}
