// CakeSort core game rules for Unity. Original code, pure C#, no UnityEngine dependencies.
// Endless mode: score by serving complete cakes, unlock new flavors at score milestones, and keep
// going as long as the next pieces fit. Pieces arrive in batches of three (the next batch is dealt
// only once all three are placed); some pieces are two plates linked together. Merges use the
// slice exchange with a repeated-state guard, matching the native rebuilds.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CakeSort.Core
{
    public static class Rules
    {
        public const int SlicesPerCake = 6;
        public const int BoardCols = 5;
        public const int BoardRows = 5;
        public const int SlotCount = 3;
        public const int PointsPerSlice = 10;
        public const int StartFlavors = 4;
        public const int MaxFlavors = 8;
        public const int MilestoneStep = 4000;
        public const int MaxObstacles = 4;
        public const int BoosterKinds = 5;

        // Score needed to unlock each flavor; the first four are available from the start.
        static readonly int[] Unlocks = { 0, 0, 0, 0, 600, 1500, 3000, 5000 };

        public static int UnlockScore(int flavor) => Unlocks[Math.Max(0, Math.Min(flavor, MaxFlavors - 1))];

        /// <summary>Late-run difficulty ramp: 0 early in a run, rising to 1 as the score climbs.</summary>
        public static double Pressure(int score) => Math.Max(0.0, Math.Min(1.0, (score - 2000) / 8000.0));

        /// <summary>Share of pieces that arrive as two linked plates; grows with the ramp.</summary>
        public static double LinkedChance(int score) => 0.14 + 0.2 * Pressure(score);

        /// <summary>How often a new cake's main flavor is drawn from what is already on the board; drops with the ramp.</summary>
        public static double BoardFlavorBias(int score) => 0.7 - 0.25 * Pressure(score);

        /// <summary>New cakes draw from the most recently unlocked flavors only, so each unlock rotates the menu.
        /// Long runs widen the menu to five, six and then seven flavors, which is what eventually fills the board.</summary>
        public static int ActiveFlavors(int score) => score < 7000 ? 4 : score < 14000 ? 5 : score < 22000 ? 6 : 7;
    }

    /// <summary>A cake on a fresh plate: 1..6 wedge slices, possibly of several flavors.</summary>
    public class Cake
    {
        public readonly int[] Slices;
        public Cake(params int[] slices)
        {
            if (slices.Length < 1 || slices.Length > Rules.SlicesPerCake) throw new ArgumentException("A cake has one to six slices");
            Slices = (int[])slices.Clone();
        }
    }

    public enum Link { Single, Horizontal, Vertical }

    /// <summary>What the player drags: one plate, or two plates linked side by side / one above the other.</summary>
    public class Piece
    {
        public readonly Cake[] Cakes;
        public readonly Link Link;

        public Piece(Link link, params Cake[] cakes)
        {
            if (cakes.Length != (link == Link.Single ? 1 : 2)) throw new ArgumentException("Single pieces hold one cake, linked pieces two");
            Link = link;
            Cakes = cakes;
        }

        public static Piece Single(params int[] slices) => new Piece(Link.Single, new Cake(slices));
        public bool IsLinked => Link != Link.Single;

        /// <summary>Board offset (in cells) of the second plate relative to the first.</summary>
        public static (int dRow, int dCol) Offset(Link link) =>
            link == Link.Horizontal ? (0, 1) : link == Link.Vertical ? (1, 0) : (0, 0);
    }

    public enum ObstacleType { None, CakeBox, Holder, Slime }

    public class Obstacle
    {
        public ObstacleType Type = ObstacleType.None;
        public int Hp; // CakeBox: hits remaining. Holder: serves remaining until release.
    }

    /// <summary>A board cell: 0..6 slices (mixed flavors allowed) plus an optional obstacle.</summary>
    public class Plate
    {
        public readonly List<int> Slices = new List<int>();
        public readonly Obstacle Obstacle = new Obstacle();
        public bool IsEmpty => Slices.Count == 0;
        public bool IsFull => Slices.Count == Rules.SlicesPerCake;
        public bool Blocked => Obstacle.Type == ObstacleType.CakeBox || Obstacle.Type == ObstacleType.Holder;
        public int CountOf(int flavor) => Slices.Count(s => s == flavor);
        public bool IsComplete() => IsFull && Slices.All(s => s == Slices[0]);
    }

    public abstract class GameEvent { }
    public class PlacedEvent : GameEvent { public int Plate, Slot, Part; public Cake Cake; }
    public class MergedEvent : GameEvent { public int From, To, Flavor, Count; public List<int> GiveBack = new List<int>(); }
    public class ServedEvent : GameEvent { public int Plate, Flavor, Points, Combo; }
    public class BatchDealtEvent : GameEvent { public Piece[] Pieces; }
    public class BoxDamagedEvent : GameEvent { public int Plate, Hp; }
    public class ObstacleClearedEvent : GameEvent { public int Plate; public ObstacleType Type; }
    public class ObstacleSpawnedEvent : GameEvent { public int Plate, Hp; public ObstacleType Type; }
    public class HolderTickEvent : GameEvent { public int Plate, Hp; }
    public class FlavorUnlockedEvent : GameEvent { public int Flavor; }
    public class MilestoneEvent : GameEvent { public int Index; }
    public class RewardEvent : GameEvent { public int Booster; }
    public class LostEvent : GameEvent { }

    public enum GameState { Playing, Lost }

    /// <summary>Deterministic RNG: identical seeds produce identical game sequences.</summary>
    public struct SplitMix64
    {
        private ulong _state;
        public SplitMix64(ulong seed) { _state = seed; }
        public ulong StateValue => _state;

        public ulong Next()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public double NextDouble() => (Next() >> 11) * (1.0 / 9007199254740992.0);

        public int NextInt(int upperBound)
        {
            if (upperBound <= 0) throw new ArgumentException("Upper bound must be positive");
            return (int)(Next() % (ulong)upperBound);
        }
    }

    public class Engine
    {
        private SplitMix64 _rng;
        public int Score { get; private set; }
        public int CakesServed { get; private set; }
        public int ComboChain { get; private set; }
        public int UnlockedFlavors { get; private set; } = Rules.StartFlavors;
        public int Milestones { get; private set; }
        public int Batches { get; private set; }
        public int Moves { get; private set; }
        public GameState State { get; private set; } = GameState.Playing;
        public readonly Plate[] Plates;
        public readonly Piece[] Slots;

        public Engine(ulong seed = 0)
        {
            _rng = new SplitMix64(seed);
            Plates = Enumerable.Range(0, Rules.BoardCols * Rules.BoardRows).Select(_ => new Plate()).ToArray();
            Slots = new Piece[Rules.SlotCount];
            DealBatch(null);
        }

        // ------------------------------------------------------------------ progress

        /// <summary>Flavor the next goal unlocks, or -1 once every flavor is out (goals become bonus milestones).</summary>
        public int NextUnlockFlavor => UnlockedFlavors < Rules.MaxFlavors ? UnlockedFlavors : -1;

        public int NextGoal => UnlockedFlavors < Rules.MaxFlavors
            ? Rules.UnlockScore(UnlockedFlavors)
            : Rules.UnlockScore(Rules.MaxFlavors - 1) + (Milestones + 1) * Rules.MilestoneStep;

        public int PreviousGoal => UnlockedFlavors < Rules.MaxFlavors
            ? Rules.UnlockScore(UnlockedFlavors - 1)
            : Rules.UnlockScore(Rules.MaxFlavors - 1) + Milestones * Rules.MilestoneStep;

        private void CheckProgress(List<GameEvent> events)
        {
            while (UnlockedFlavors < Rules.MaxFlavors && Score >= Rules.UnlockScore(UnlockedFlavors))
            {
                int flavor = UnlockedFlavors++;
                events.Add(new FlavorUnlockedEvent { Flavor = flavor });
                events.Add(new RewardEvent { Booster = _rng.NextInt(Rules.BoosterKinds) });
                if (flavor >= 5) SpawnObstacle(events);
            }
            while (UnlockedFlavors == Rules.MaxFlavors && Score >= NextGoal)
            {
                Milestones++;
                events.Add(new MilestoneEvent { Index = Milestones });
                if (Milestones % 2 == 1) events.Add(new RewardEvent { Booster = _rng.NextInt(Rules.BoosterKinds) });
                SpawnObstacle(events);
            }
        }

        /// <summary>Drops a new obstacle on a free cell, but never crowds a board that is already tight.</summary>
        private void SpawnObstacle(List<GameEvent> events)
        {
            if (Plates.Count(p => p.Obstacle.Type != ObstacleType.None) >= Rules.MaxObstacles) return;
            var free = Enumerable.Range(0, Plates.Length)
                .Where(i => Plates[i].IsEmpty && Plates[i].Obstacle.Type == ObstacleType.None).ToList();
            if (free.Count < 8) return;
            int plate = free[_rng.NextInt(free.Count)];
            var roll = _rng.NextInt(3);
            var type = roll == 0 ? ObstacleType.CakeBox : roll == 1 ? ObstacleType.Slime : ObstacleType.Holder;
            int hp = type == ObstacleType.Slime ? 0 : 2;
            Plates[plate].Obstacle.Type = type;
            Plates[plate].Obstacle.Hp = hp;
            events.Add(new ObstacleSpawnedEvent { Plate = plate, Type = type, Hp = hp });
        }

        // ------------------------------------------------------------------ pieces

        private void DealBatch(List<GameEvent> events)
        {
            FillSlotsFairly(Enumerable.Range(0, Rules.SlotCount).ToArray());
            Batches++;
            events?.Add(new BatchDealtEvent { Pieces = (Piece[])Slots.Clone() });
        }

        /// <summary>
        /// Generates pieces for the given slots so that every waiting piece can be placed in some order.
        /// Merges only ever empty cells, so a batch that fits on the current free cells always fits in play:
        /// a careful player is never handed an impossible batch.
        /// </summary>
        private void FillSlotsFairly(int[] slots)
        {
            for (int attempt = 0; attempt < 16; attempt++)
            {
                foreach (int s in slots) Slots[s] = GeneratePiece();
                if (CanPlaceAll(Slots)) return;
            }
            // Still stuck: unlink pairs one by one, then fall back to making sure at least one piece fits.
            foreach (int s in slots)
            {
                if (CanPlaceAll(Slots)) return;
                if (Slots[s].IsLinked) Slots[s] = new Piece(Link.Single, Slots[s].Cakes[0]);
            }
            if (!AnyMoveAvailable() && Plates.Any(IsFree)) Slots[slots[0]] = new Piece(Link.Single, Slots[slots[0]].Cakes[0]);
        }

        /// <summary>True when all the given (non-null) pieces can be placed on the current free cells in some order.</summary>
        public bool CanPlaceAll(IEnumerable<Piece> pieces)
        {
            var waiting = pieces.Where(p => p != null).ToList();
            var free = Plates.Select(IsFree).ToArray();
            return PlaceAllFrom(waiting, free);
        }

        private static bool PlaceAllFrom(List<Piece> waiting, bool[] free)
        {
            if (waiting.Count == 0) return true;
            for (int w = 0; w < waiting.Count; w++)
            {
                var piece = waiting[w];
                var rest = new List<Piece>(waiting);
                rest.RemoveAt(w);
                for (int anchor = 0; anchor < free.Length; anchor++)
                {
                    var cells = CellsFor(piece.Link, anchor);
                    if (cells == null || !cells.All(c => free[c])) continue;
                    foreach (int c in cells) free[c] = false;
                    bool ok = PlaceAllFrom(rest, free);
                    foreach (int c in cells) free[c] = true;
                    if (ok) return true;
                }
            }
            return false;
        }

        private Piece GeneratePiece()
        {
            if (Batches > 0 && _rng.NextDouble() < Rules.LinkedChance(Score))
            {
                var link = _rng.NextInt(2) == 0 ? Link.Horizontal : Link.Vertical;
                return new Piece(link, GenerateCake(), GenerateCake());
            }
            return new Piece(Link.Single, GenerateCake());
        }

        private Cake GenerateCake()
        {
            // 2..5 slices (mostly 3-4) of one to three flavors.
            double roll = _rng.NextDouble();
            int count = roll < 0.25 ? 2 : roll < 0.65 ? 3 : roll < 0.95 ? 4 : 5;
            double flavorRoll = _rng.NextDouble(), pressure = Rules.Pressure(Score);
            int distinct = flavorRoll < 0.35 - 0.15 * pressure ? 1 : flavorRoll < 0.92 - 0.12 * pressure || count < 3 ? 2 : 3;
            distinct = Math.Min(distinct, Rules.ActiveFlavors(Score) - 1);

            var flavors = new List<int> { PickFlavor() };
            while (flavors.Count < distinct)
            {
                int f = flavors.Count == 1 && _rng.NextDouble() < 0.4 ? PickFlavor() : RandomActiveFlavor();
                if (!flavors.Contains(f)) flavors.Add(f);
            }
            var slices = new int[count];
            // each flavor gets at least one slice, the main flavor takes the remainder
            int index = 0;
            for (int k = flavors.Count - 1; k >= 1; k--)
            {
                int share = 1 + _rng.NextInt(Math.Max(1, (count - index - k) / 2 + 1));
                share = Math.Min(share, count - index - k);
                for (int n = 0; n < share; n++) slices[index++] = flavors[k];
            }
            while (index < count) slices[index++] = flavors[0];
            Array.Reverse(slices);
            return new Cake(slices);
        }

        private int PickFlavor()
        {
            if (_rng.NextDouble() < Rules.BoardFlavorBias(Score))
            {
                var onBoard = Plates.Where(p => !p.IsEmpty && !p.Blocked).SelectMany(p => p.Slices).ToList();
                if (onBoard.Count > 0) return onBoard[_rng.NextInt(onBoard.Count)];
            }
            return RandomActiveFlavor();
        }

        /// <summary>Lowest flavor index still on the menu for new cakes.</summary>
        public int FirstActiveFlavor => Math.Max(0, UnlockedFlavors - Rules.ActiveFlavors(Score));

        private int RandomActiveFlavor() => FirstActiveFlavor + _rng.NextInt(UnlockedFlavors - FirstActiveFlavor);

        // ------------------------------------------------------------------ placement

        private bool IsFree(Plate p) => p.IsEmpty && !p.Blocked;

        public bool IsFree(int plate) => plate >= 0 && plate < Plates.Length && IsFree(Plates[plate]);

        /// <summary>Cells a piece would cover with its first plate on <paramref name="anchor"/>, or null if it leaves the board.</summary>
        public static int[] CellsFor(Link link, int anchor)
        {
            if (anchor < 0 || anchor >= Rules.BoardCols * Rules.BoardRows) return null;
            if (link == Link.Single) return new[] { anchor };
            var (dRow, dCol) = Piece.Offset(link);
            int row = anchor / Rules.BoardCols + dRow, col = anchor % Rules.BoardCols + dCol;
            if (row >= Rules.BoardRows || col >= Rules.BoardCols) return null;
            return new[] { anchor, row * Rules.BoardCols + col };
        }

        public bool Fits(Piece piece, int anchor)
        {
            if (piece == null) return false;
            var cells = CellsFor(piece.Link, anchor);
            return cells != null && cells.All(IsFree);
        }

        public bool FitsAnywhere(Piece piece) => piece != null && Enumerable.Range(0, Plates.Length).Any(a => Fits(piece, a));

        public bool CanPlace(int slot, int anchor) =>
            State == GameState.Playing && slot >= 0 && slot < Rules.SlotCount && Fits(Slots[slot], anchor);

        public bool AnyMoveAvailable() => Enumerable.Range(0, Rules.SlotCount).Any(s => FitsAnywhere(Slots[s]));

        /// <summary>Place the piece from a slot with its first plate on <paramref name="anchor"/>; returns ordered events.</summary>
        public List<GameEvent> Place(int slot, int anchor)
        {
            var events = new List<GameEvent>();
            if (!CanPlace(slot, anchor)) return events;
            var piece = Slots[slot];
            var cells = CellsFor(piece.Link, anchor);
            for (int k = 0; k < cells.Length; k++)
            {
                Plates[cells[k]].Slices.AddRange(piece.Cakes[k].Slices);
                events.Add(new PlacedEvent { Plate = cells[k], Slot = slot, Part = k, Cake = piece.Cakes[k] });
            }
            Slots[slot] = null;
            Moves++;
            ComboChain = 0;
            Resolve(events);
            CheckProgress(events);
            if (Slots.All(s => s == null)) DealBatch(events);
            RefreshLost(events);
            return events;
        }

        /// <summary>Ends the run when none of the waiting pieces fits anywhere.</summary>
        public bool RefreshLost(List<GameEvent> events = null)
        {
            if (State != GameState.Playing || AnyMoveAvailable()) return false;
            State = GameState.Lost;
            events?.Add(new LostEvent());
            return true;
        }

        /// <summary>Consolidate identical adjacent slices; serve completed cakes; cascade until stable.</summary>
        private void Resolve(List<GameEvent> events)
        {
            // Loop guard: slice exchanges can shuttle slices between plates. When a board state repeats, the merges
            // since its first occurrence cancel out, so they are dropped rather than played back as a back-and-forth.
            var seenStates = new Dictionary<string, int>();
            var moved = new HashSet<(int from, int to, int flavor)>();
            while (true)
            {
                bool servedAny = false;
                for (int i = 0; i < Plates.Length; i++)
                {
                    var p = Plates[i];
                    if (!p.IsComplete()) continue;
                    ComboChain++;
                    int points = Rules.PointsPerSlice * Rules.SlicesPerCake * ComboChain;
                    int flavor = p.Slices[0];
                    p.Slices.Clear();
                    Score += points;
                    CakesServed++;
                    events.Add(new ServedEvent { Plate = i, Flavor = flavor, Points = points, Combo = ComboChain });
                    servedAny = true;
                    OnServed(i, events);
                }
                if (servedAny) { seenStates.Clear(); moved.Clear(); continue; }

                var keyBuilder = new StringBuilder();
                // Slice order on a plate isn't visible, so compare plates as sorted multisets.
                foreach (var p in Plates) { keyBuilder.Append(string.Join(",", p.Slices.OrderBy(s => s))).Append('|'); }
                string key = keyBuilder.ToString();
                if (seenStates.TryGetValue(key, out int firstSeen))
                {
                    events.RemoveRange(firstSeen, events.Count - firstSeen);
                    break;
                }
                seenStates[key] = events.Count;

                var merge = FindBestMerge(moved);
                if (merge == null) break;
                moved.Add((merge.From, merge.To, merge.Flavor));
                for (int k = 0; k < merge.Count; k++)
                {
                    Plates[merge.From].Slices.Remove(merge.Flavor);
                    Plates[merge.To].Slices.Add(merge.Flavor);
                }
                foreach (var g in merge.GiveBack)
                {
                    Plates[merge.To].Slices.Remove(g);
                    Plates[merge.From].Slices.Add(g);
                }
                events.Add(new MergedEvent
                {
                    From = merge.From, To = merge.To, Flavor = merge.Flavor,
                    Count = merge.Count, GiveBack = new List<int>(merge.GiveBack)
                });
            }
        }

        private static IEnumerable<int> Neighbors(int index)
        {
            int row = index / Rules.BoardCols, column = index % Rules.BoardCols;
            if (row > 0) yield return index - Rules.BoardCols;
            if (row < Rules.BoardRows - 1) yield return index + Rules.BoardCols;
            if (column > 0) yield return index - 1;
            if (column < Rules.BoardCols - 1) yield return index + 1;
        }

        /// <summary>
        /// A merge drains flavor f fully from one adjacent plate into another (or completes a cake).
        /// The destination takes k f-slices; if it was full, it hands k non-f slices back in exchange,
        /// so merges work even between two full plates. Slime plates hold their slices stuck:
        /// slices can move in but not out until the slime is cleaned.
        /// </summary>
        /// <summary>
        /// Picks the best merge, skipping any that would send a flavor straight back along a move already made since
        /// the last serve (unless it completes a cake): on screen that reads as slices shuttling to and fro.
        /// </summary>
        private MergeChoice FindBestMerge(HashSet<(int from, int to, int flavor)> moved)
        {
            MergeChoice best = null;
            for (int i = 0; i < Plates.Length; i++)
            {
                var a = Plates[i];
                if (a.IsEmpty || a.Blocked) continue;
                foreach (int j in Neighbors(i))
                {
                    var b = Plates[j];
                    if (b.IsEmpty || b.Blocked) continue;
                    var shared = a.Slices.Distinct().Where(b.Slices.Contains).ToList();
                    foreach (int f in shared)
                    {
                        // direction: drain smaller holder into larger holder; ties drain j into i
                        bool swap = a.CountOf(f) >= b.CountOf(f);
                        var src = swap ? b : a;
                        var dst = swap ? a : b;
                        int srcIdx = swap ? j : i;
                        int dstIdx = swap ? i : j;
                        if (src.Obstacle.Type == ObstacleType.Slime) continue; // stuck, can't give
                        int room = Rules.SlicesPerCake - dst.Slices.Count;
                        int tradable = dst.Slices.Count - dst.CountOf(f);
                        int movable = Math.Min(src.CountOf(f), room + tradable);
                        if (movable <= 0) continue;
                        bool completes = dst.CountOf(f) + movable == Rules.SlicesPerCake;
                        bool drains = movable == src.CountOf(f);
                        if (!completes && !drains) continue;
                        int giveBackCount = movable - Math.Min(movable, room);
                        var giveBack = dst.Slices.Where(s => s != f).Take(giveBackCount).ToList();
                        if (!completes && moved.Contains((dstIdx, srcIdx, f))) continue;
                        var choice = new MergeChoice(srcIdx, dstIdx, f, movable, giveBack, completes);
                        if (best == null || choice.Priority > best.Priority) best = choice;
                    }
                }
            }
            return best;
        }

        private class MergeChoice
        {
            public readonly int From, To, Flavor, Count;
            public readonly List<int> GiveBack;
            public readonly int Priority;

            public MergeChoice(int from, int to, int flavor, int count, List<int> giveBack, bool completes)
            {
                From = from; To = to; Flavor = flavor; Count = count; GiveBack = giveBack;
                // completing merges first, then bigger moves; stable tie-break by indices
                Priority = (completes ? 1000 : 0) + count * 10 - from;
            }
        }

        /// <summary>Serving a cake damages adjacent boxes, counts down holders, and cleans the plate's slime.</summary>
        private void OnServed(int plate, List<GameEvent> events)
        {
            var obstacle = Plates[plate].Obstacle;
            if (obstacle.Type == ObstacleType.Slime)
            {
                obstacle.Type = ObstacleType.None;
                events.Add(new ObstacleClearedEvent { Plate = plate, Type = ObstacleType.Slime });
            }

            foreach (int j in Neighbors(plate))
            {
                var o = Plates[j].Obstacle;
                if (o.Type != ObstacleType.CakeBox) continue;
                o.Hp--;
                if (o.Hp <= 0)
                {
                    o.Type = ObstacleType.None;
                    events.Add(new ObstacleClearedEvent { Plate = j, Type = ObstacleType.CakeBox });
                }
                else events.Add(new BoxDamagedEvent { Plate = j, Hp = o.Hp });
            }

            for (int i = 0; i < Plates.Length; i++)
            {
                var o = Plates[i].Obstacle;
                if (o.Type != ObstacleType.Holder) continue;
                o.Hp--;
                if (o.Hp <= 0)
                {
                    o.Type = ObstacleType.None;
                    events.Add(new ObstacleClearedEvent { Plate = i, Type = ObstacleType.Holder });
                }
                else events.Add(new HolderTickEvent { Plate = i, Hp = o.Hp });
            }
        }

        // ------------------------------------------------------------------ boosters

        private bool InRange(int plate) => plate >= 0 && plate < Plates.Length;

        /// <summary>Booster: hammer — clear a plate's slices, knock a hit off a box, or smash a cloche.</summary>
        public bool Hammer(int plateIndex)
        {
            if (State != GameState.Playing || !InRange(plateIndex)) return false;
            var plate = Plates[plateIndex];
            if (plate.Obstacle.Type == ObstacleType.CakeBox)
            {
                plate.Obstacle.Hp--;
                if (plate.Obstacle.Hp <= 0) plate.Obstacle.Type = ObstacleType.None;
                return true;
            }
            if (plate.Obstacle.Type == ObstacleType.Holder)
            {
                plate.Obstacle.Type = ObstacleType.None;
                return true;
            }
            if (plate.IsEmpty) return false;
            plate.Slices.Clear();
            if (plate.Obstacle.Type == ObstacleType.Slime) plate.Obstacle.Type = ObstacleType.None;
            return true;
        }

        /// <summary>Booster: cupcake bomb — clear all slices in the 3x3 area around a plate.</summary>
        public bool Bomb(int plateIndex)
        {
            if (State != GameState.Playing || !InRange(plateIndex)) return false;
            int row = plateIndex / Rules.BoardCols, column = plateIndex % Rules.BoardCols;
            bool hit = false;
            for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                int r = row + dr, c = column + dc;
                if (r < 0 || r >= Rules.BoardRows || c < 0 || c >= Rules.BoardCols) continue;
                var plate = Plates[r * Rules.BoardCols + c];
                if (plate.Obstacle.Type == ObstacleType.CakeBox)
                {
                    plate.Obstacle.Hp--;
                    if (plate.Obstacle.Hp <= 0) plate.Obstacle.Type = ObstacleType.None;
                    hit = true;
                }
                else if (plate.Obstacle.Type == ObstacleType.Slime)
                {
                    plate.Obstacle.Type = ObstacleType.None;
                    hit = true;
                }
                if (!plate.IsEmpty) { plate.Slices.Clear(); hit = true; }
            }
            return hit;
        }

        /// <summary>
        /// Booster: magic whisk — mix a plate so all its slices become its majority flavor, then resolve.
        /// Returns the resolution events, or null when the whisk can't be used on that plate.
        /// </summary>
        public List<GameEvent> Whisk(int plateIndex)
        {
            if (State != GameState.Playing || !InRange(plateIndex)) return null;
            var plate = Plates[plateIndex];
            if (plate.IsEmpty || plate.Blocked) return null;
            int majority = MajorityFlavor(plate);
            for (int i = 0; i < plate.Slices.Count; i++) plate.Slices[i] = majority;
            var events = new List<GameEvent>();
            ComboChain = 0;
            Resolve(events);
            CheckProgress(events);
            return events;
        }

        public static int MajorityFlavor(Plate plate) => plate.Slices
            .GroupBy(s => s)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First().Key;

        /// <summary>Booster: firework — launch a rocket that clears a random non-empty plate.</summary>
        public int Firework()
        {
            if (State != GameState.Playing) return -1;
            var candidates = Enumerable.Range(0, Plates.Length).Where(i => !Plates[i].IsEmpty && !Plates[i].Blocked).ToList();
            if (candidates.Count == 0) return -1;
            int pick = candidates[_rng.NextInt(candidates.Count)];
            Plates[pick].Slices.Clear();
            return pick;
        }

        /// <summary>Booster: shuffle — reroll the pieces still waiting in this batch.</summary>
        public bool Shuffle()
        {
            if (State != GameState.Playing || Slots.All(s => s == null)) return false;
            FillSlotsFairly(Enumerable.Range(0, Rules.SlotCount).Where(i => Slots[i] != null).ToArray());
            return true;
        }

        /// <summary>Second chance after losing: clears the slices in the central 3x3 area and resumes play.</summary>
        public bool Revive()
        {
            if (State != GameState.Lost) return false;
            for (int r = 1; r <= 3; r++)
            for (int c = 1; c <= 3; c++)
            {
                var plate = Plates[r * Rules.BoardCols + c];
                if (!plate.Blocked) plate.Slices.Clear();
            }
            State = GameState.Playing;
            return true;
        }

        /// <summary>Resume a lost run so the player can try a booster; call <see cref="RefreshLost"/> afterwards.</summary>
        public bool ResumeForBooster()
        {
            if (State != GameState.Lost) return false;
            State = GameState.Playing;
            return true;
        }

        // ------------------------------------------------------------------ save / load

        public string Save()
        {
            var sb = new StringBuilder();
            sb.Append("v2|").Append(_rng.StateValue.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(Score).Append('|').Append(CakesServed).Append('|').Append(UnlockedFlavors).Append('|')
              .Append(Milestones).Append('|').Append(Batches).Append('|').Append(Moves).Append('|')
              .Append((int)State).Append('|');
            sb.Append(string.Join("/", Plates.Select(p => $"{string.Join(",", p.Slices)}:{(int)p.Obstacle.Type}:{p.Obstacle.Hp}")));
            sb.Append('|');
            sb.Append(string.Join("/", Slots.Select(s => s == null ? "-" :
                $"{(int)s.Link}:{string.Join(":", s.Cakes.Select(c => string.Join(",", c.Slices)))}")));
            return sb.ToString();
        }

        /// <summary>Restores a saved run; returns null when the text is not a valid save.</summary>
        public static Engine Load(string text)
        {
            try
            {
                var parts = text.Split('|');
                if (parts.Length != 11 || parts[0] != "v2") return null;
                var e = new Engine(0);
                e._rng = new SplitMix64(ulong.Parse(parts[1], CultureInfo.InvariantCulture));
                e.Score = int.Parse(parts[2]);
                e.CakesServed = int.Parse(parts[3]);
                e.UnlockedFlavors = int.Parse(parts[4]);
                e.Milestones = int.Parse(parts[5]);
                e.Batches = int.Parse(parts[6]);
                e.Moves = int.Parse(parts[7]);
                e.State = (GameState)int.Parse(parts[8]);
                var plates = parts[9].Split('/');
                if (plates.Length != e.Plates.Length) return null;
                for (int i = 0; i < plates.Length; i++)
                {
                    var f = plates[i].Split(':');
                    var p = e.Plates[i];
                    p.Slices.Clear();
                    if (f[0].Length > 0) p.Slices.AddRange(f[0].Split(',').Select(int.Parse));
                    p.Obstacle.Type = (ObstacleType)int.Parse(f[1]);
                    p.Obstacle.Hp = int.Parse(f[2]);
                }
                var slots = parts[10].Split('/');
                if (slots.Length != Rules.SlotCount) return null;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == "-") { e.Slots[i] = null; continue; }
                    var f = slots[i].Split(':');
                    var cakes = f.Skip(1).Select(c => new Cake(c.Split(',').Select(int.Parse).ToArray())).ToArray();
                    e.Slots[i] = new Piece((Link)int.Parse(f[0]), cakes);
                }
                if (e.State == GameState.Playing && e.Slots.All(s => s == null)) e.DealBatch(null);
                return e;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public Engine Clone() => Load(Save());

        /// <summary>A run that starts from a given board and first batch (the guided tutorial); later batches deal normally.</summary>
        public static Engine Prepared(ulong seed, IDictionary<int, int[]> plates, Piece[] firstBatch)
        {
            var e = new Engine(seed);
            foreach (var p in e.Plates) p.Slices.Clear();
            foreach (var kv in plates) e.Plates[kv.Key].Slices.AddRange(kv.Value);
            for (int s = 0; s < Rules.SlotCount; s++) e.Slots[s] = s < firstBatch.Length ? firstBatch[s] : null;
            return e;
        }
    }
}
