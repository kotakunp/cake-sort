using System;
using System.Collections.Generic;
using System.Linq;
using CakeSort.Core;
using UnityEditor;
using UnityEngine;

namespace CakeSort.EditorTools
{
    /// <summary>
    /// Headless checks for the endless rules (batches, linked pieces, unlocks, obstacles, boosters, saves)
    /// and the meta layer (customer orders, coins, bakery levels, daily treats).
    /// Run: Unity -batchmode -projectPath . -executeMethod CakeSort.EditorTools.EngineSelfTest.Run
    /// </summary>
    public static class EngineSelfTest
    {
        static int _failures, _passes;

        static void Check(string name, bool condition)
        {
            if (condition) { _passes++; Debug.Log("PASS  " + name); }
            else { _failures++; Debug.LogError("FAIL  " + name); }
        }

        /// <summary>Cake from (flavor, count) pairs, e.g. MakeCake(1, 2, 2, 4) = two of flavor 1 then four of flavor 2.</summary>
        static Cake MakeCake(params int[] runLengths)
        {
            var slices = new List<int>();
            for (int i = 0; i + 1 < runLengths.Length; i += 2)
                for (int k = 0; k < runLengths[i + 1]; k++) slices.Add(runLengths[i]);
            return new Cake(slices.ToArray());
        }

        static Piece One(params int[] runLengths) => new Piece(Link.Single, MakeCake(runLengths));

        static Engine With(Piece[] slots, params (int plate, int[] slices)[] plates)
        {
            var e = new Engine(42);
            foreach (var p in e.Plates) p.Slices.Clear();
            foreach (var (plate, slices) in plates) e.Plates[plate].Slices.AddRange(slices);
            for (int i = 0; i < Rules.SlotCount; i++) e.Slots[i] = slots[i];
            return e;
        }

        static Piece[] Slots(params Piece[] pieces) => pieces;

        /// <summary>Fills every cell except <paramref name="keepFree"/> with single slices that never match a neighbour.</summary>
        static void FillNoMerge(Engine e, params int[] keepFree)
        {
            for (int i = 0; i < e.Plates.Length; i++)
            {
                e.Plates[i].Slices.Clear();
                if (keepFree.Contains(i)) continue;
                int row = i / Rules.BoardCols, col = i % Rules.BoardCols;
                e.Plates[i].Slices.Add((row + 2 * col) % 5 + 3);
            }
        }

        [MenuItem("Cake Sort/Run Engine Self Test")]
        public static void Run()
        {
            _failures = _passes = 0;
            try { RunAll(); }
            catch (Exception ex) { _failures++; Debug.LogError("EXCEPTION " + ex); }
            Debug.Log($"[CakeSort][SelfTest] {_passes} passed, {_failures} failed");
            if (Application.isBatchMode) EditorApplication.Exit(_failures == 0 ? 0 : 1);
        }

        static void RunTutorial()
        {
            var e = Tutorial.NewGame(5);
            Check("tutorial: three scripted pieces waiting", e.Slots.All(s => s != null) && e.Slots[2].IsLinked);
            for (int step = 0; step < Tutorial.Moves.Length; step++)
            {
                var move = Tutorial.Moves[step];
                Check($"tutorial: step {step + 1} move is legal", e.CanPlace(move.Slot, move.Anchor));
                var events = e.Place(move.Slot, move.Anchor);
                Check($"tutorial: step {step + 1} serves exactly one cake", events.OfType<ServedEvent>().Count() == 1);
                Check($"tutorial: step {step + 1} has a visible merge", events.OfType<MergedEvent>().Any());
            }
            Check("tutorial: a normal batch follows the script", e.Batches == 2 && e.Slots.All(s => s != null) && e.State == GameState.Playing);
            Check("tutorial: only the chocolate trio is left", e.Plates.Count(pl => !pl.IsEmpty) == 1 && e.Plates[18].Slices.Count == 3 && e.Plates[18].Slices.All(s => s == 1));
        }

        /// <summary>Merges played back to the player must never undo each other (no back-and-forth shuttling).</summary>
        static void RunMergePlayback()
        {
            int reversals = 0, longest = 0, moves = 0;
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var e = new Engine(seed);
                var rng = new System.Random((int)seed);
                for (int m = 0; m < 150 && e.State == GameState.Playing; m++)
                {
                    var options = Enumerable.Range(0, Rules.SlotCount)
                        .SelectMany(s => Enumerable.Range(0, e.Plates.Length).Where(a => e.CanPlace(s, a)).Select(a => (s, a))).ToList();
                    if (options.Count == 0) break;
                    var (slot, anchor) = options[rng.Next(options.Count)];
                    var events = e.Place(slot, anchor);
                    moves++;
                    var merges = new List<MergedEvent>();
                    foreach (var ev in events)
                    {
                        if (ev is ServedEvent) merges.Clear();
                        if (!(ev is MergedEvent merge)) continue;
                        if (merges.Any(x => x.From == merge.To && x.To == merge.From && x.Flavor == merge.Flavor)) reversals++;
                        merges.Add(merge);
                        longest = Math.Max(longest, merges.Count);
                    }
                }
            }
            Debug.Log($"[CakeSort][SelfTest] merge playback over {moves} moves: reversals={reversals} longestChain={longest}");
            Check("merges: no slice is sent back the way it came", reversals == 0);
            Check("merges: chains between serves stay short", longest <= 12);
        }

        static void RunMeta()
        {
            {
                var e = new Engine(11);
                var book = new OrderBook(3);
                var changed = book.Refresh(e);
                Check("orders: three customers waiting", changed.Count == 3 && book.Active.All(o => o != null));
                Check("orders: customers are all different", book.Active.Select(o => o.Customer).Distinct().Count() == 3);
                var flavors = book.Active.Where(o => o.Kind == OrderKind.Flavor).Select(o => o.Flavor).ToList();
                Check("orders: flavor orders use dealt flavors only", flavors.All(f => f >= e.FirstActiveFlavor && f < e.UnlockedFlavors));
                Check("orders: no two orders for the same flavor", flavors.Distinct().Count() == flavors.Count);
                Check("orders: at most one special order", book.Active.Count(o => o.Kind != OrderKind.Flavor) <= MetaRules.MaxSpecialOrders);
                Check("orders: positive targets and rewards", book.Active.All(o => o.Target > 0 && o.Reward > 0 && o.Progress == 0));
                Check("orders: refresh keeps valid orders", book.Refresh(e).Count == 0);
            }
            {
                var e = new Engine(11);
                var book = new OrderBook(1);
                book.Active[0] = new Order { Kind = OrderKind.Flavor, Flavor = 2, Target = 2, Reward = 20, Customer = 0 };
                book.Active[1] = new Order { Kind = OrderKind.Combo, Target = 3, Reward = 45, Customer = 1 };
                book.Active[2] = new Order { Kind = OrderKind.Flavor, Flavor = 1, Target = 3, Reward = 30, Customer = 2 };
                var p = book.OnServed(e, 2, 1);
                Check("orders: matching cake moves its order", p.Any(x => x.Index == 0 && x.After.Progress == 1 && x.Completed == null));
                Check("orders: other flavors untouched", book.Active[2].Progress == 0);
                Check("orders: combo order records best chain", book.Active[1].Progress == 1);
                p = book.OnServed(e, 2, 3);
                var done = p.FirstOrDefault(x => x.Index == 0);
                Check("orders: finishing an order reports it with its reward", done != null && done.Completed != null && done.Completed.Reward == 20);
                Check("orders: finished order replaced by a new customer", done != null && done.Replacement != null && book.Active[0] != null && !book.Active[0].Done);
                Check("orders: x3 chain completes the combo order", p.Any(x => x.Index == 1 && x.Completed != null));
                Check("orders: completed count tracked", book.Completed == 2);
                Check("orders: new customers differ from waiting ones", book.Active.Select(o => o.Customer).Distinct().Count() == 3);
                Check("orders: single pieces don't count as linked", book.OnPlaced(e, new Piece(Link.Single, new Cake(1))).Count == 0);
            }
            {
                var e = new Engine(11);
                var book = new OrderBook(1);
                book.Active[0] = new Order { Kind = OrderKind.Linked, Target = 1, Reward = 7, Customer = 3 };
                book.Active[1] = new Order { Kind = OrderKind.Flavor, Flavor = 7, Target = 2, Reward = 20, Customer = 1 };
                book.Active[2] = new Order { Kind = OrderKind.AnyCakes, Target = 6, Reward = 24, Customer = 2 };
                var p = book.OnPlaced(e, new Piece(Link.Horizontal, new Cake(1), new Cake(2)));
                Check("orders: linked placement completes linked order", p.Count == 1 && p[0].Completed != null);
                var changed = book.Refresh(e);
                Check("orders: order for a flavor off the menu is swapped", changed.Contains(1) && !changed.Contains(2));
                var copy = OrderBook.Load(book.Save());
                Check("orders: save/load round trip", copy != null && copy.Save() == book.Save());
                Check("orders: corrupt save rejected", OrderBook.Load("o1|x") == null && OrderBook.Load("") == null);
            }
            {
                var p = new Profile();
                Check("profile: spend fails without coins", !p.Spend(10) && p.Coins == 0);
                p.Earn(120);
                Check("profile: spend succeeds with coins", p.Spend(100) && p.Coins == 20);
                LevelUp up = null;
                int need = p.StarsNeeded;
                for (int i = 0; i < need; i++) up = p.AddStar() ?? up;
                Check("profile: level up after enough stars", up != null && p.Level == 2 && p.Stars == 0);
                Check("profile: level up pays coins", up != null && p.Coins == 20 + MetaRules.LevelCoins(2));
                Check("profile: level 2 reward is a bomb", up != null && up.Booster == 1);
                Check("profile: next level needs more stars", p.StarsNeeded > need);

                var d1 = p.ClaimDaily(1000);
                Check("daily: first claim is day 1", d1 != null && d1.Day == 1 && d1.Coins == MetaRules.DailyCoinsFor(1));
                Check("daily: only once per day", p.ClaimDaily(1000) == null && p.PeekDaily(1000) == null);
                var d2 = p.ClaimDaily(1001);
                Check("daily: consecutive day continues the streak", d2 != null && d2.Day == 2);
                var d3 = p.ClaimDaily(1005);
                Check("daily: missed day restarts the streak", d3 != null && d3.Day == 1);
                for (int day = 1006; day <= 1011; day++) p.ClaimDaily(day);
                Check("daily: day 7 includes a booster", p.Streak == 7 && MetaRules.DailyBoosterFor(7) >= 0);
                var d8 = p.ClaimDaily(1012);
                Check("daily: streak wraps after day 7", d8 != null && d8.Day == 1);
                var copy = Profile.Load(p.Save());
                Check("profile: save/load round trip", copy != null && copy.Save() == p.Save());
                Check("profile: corrupt save rejected", Profile.Load("p1|1") == null);
                Check("shop: revives get dearer", MetaRules.ReviveCost(1) > MetaRules.ReviveCost(0));
            }
            {
                bool rampOk = true;
                for (int score = 0; score < 40000; score += 250)
                {
                    rampOk &= Rules.Pressure(score) >= 0 && Rules.Pressure(score) <= 1 && Rules.Pressure(score + 250) >= Rules.Pressure(score);
                    rampOk &= Rules.ActiveFlavors(score + 250) >= Rules.ActiveFlavors(score) && Rules.ActiveFlavors(score) <= Rules.MaxFlavors;
                    rampOk &= Rules.LinkedChance(score) < 0.5 && Rules.BoardFlavorBias(score) > 0.3;
                }
                Check("ramp: difficulty only rises and stays bounded", rampOk);
                Check("ramp: opening is gentle", Rules.Pressure(0) == 0 && Rules.ActiveFlavors(0) == 4);
                Check("ramp: long runs widen the menu", Rules.ActiveFlavors(30000) > Rules.ActiveFlavors(0));
            }
            {
                // Over a long advisor game orders keep flowing and stay valid as the menu rotates.
                var e = new Engine(77);
                var book = new OrderBook(9);
                book.Refresh(e);
                int completed = 0, bad = 0;
                for (int move = 0; move < 300 && e.State == GameState.Playing; move++)
                {
                    if (!Advisor.BestMove(e, out int slot, out int anchor)) break;
                    var piece = e.Slots[slot];
                    var events = e.Place(slot, anchor);
                    completed += book.OnPlaced(e, piece).Count(x => x.Completed != null);
                    foreach (var s in events.OfType<ServedEvent>()) completed += book.OnServed(e, s.Flavor, s.Combo).Count(x => x.Completed != null);
                    book.Refresh(e);
                    if (book.Active.Any(o => o == null || o.Done || (o.Kind == OrderKind.Flavor && (o.Flavor < e.FirstActiveFlavor || o.Flavor >= e.UnlockedFlavors)))) bad++;
                }
                Check($"orders: completed during a long game ({completed})", completed >= 8);
                Check("orders: always three valid open orders", bad == 0);
            }
        }
        static void RunAll()
        {
            RunMeta();
            RunTutorial();
            RunMergePlayback();
            {
                var e = new Engine(5);
                Check("new game starts with a full batch of three", e.Slots.All(s => s != null));
                Check("first batch has no linked pieces", e.Slots.All(s => !s.IsLinked));
                Check("new game starts with four flavors", e.UnlockedFlavors == 4 && e.NextUnlockFlavor == 4);
                Check("new game has an empty board", e.Plates.All(p => p.IsEmpty && p.Obstacle.Type == ObstacleType.None));
            }
            {
                var e = With(Slots(One(5, 3), One(6, 3), One(7, 3)), (0, new[] { 1, 1 }));
                Check("cannot place on occupied plate", e.Place(0, 0).Count == 0);
                var ev = e.Place(0, 6);
                Check("can place on empty plate", ev.OfType<PlacedEvent>().Count() == 1);
                Check("slot stays empty until the batch is used up", e.Slots[0] == null && e.Slots[1] != null);
                Check("no new batch after the first placement", !ev.OfType<BatchDealtEvent>().Any());
                Check("cannot place from an empty slot", e.Place(0, 12).Count == 0);
                e.Place(1, 12);
                ev = e.Place(2, 18);
                var dealt = ev.OfType<BatchDealtEvent>().SingleOrDefault();
                Check("placing the third piece deals a new batch", dealt != null && e.Slots.All(s => s != null));
                Check("batch event lists the new pieces", dealt != null && dealt.Pieces.Length == 3 && dealt.Pieces.All(p => p != null));
                Check("out-of-range anchor rejected", e.Place(0, 99).Count == 0 && e.Place(0, -1).Count == 0);
            }
            {
                var h = new Piece(Link.Horizontal, MakeCake(5, 2), MakeCake(6, 2));
                var v = new Piece(Link.Vertical, MakeCake(5, 2), MakeCake(6, 2));
                var e = With(Slots(h, v, One(7, 1)), (2, new[] { 1 }));
                Check("horizontal pair cannot hang off the right edge", !e.CanPlace(0, 4));
                Check("vertical pair cannot hang off the bottom edge", !e.CanPlace(1, 20));
                Check("pair needs both cells free", !e.CanPlace(0, 1) && !e.CanPlace(1, 2));
                Check("CellsFor maps pairs onto neighbours",
                    Engine.CellsFor(Link.Horizontal, 7).SequenceEqual(new[] { 7, 8 }) && Engine.CellsFor(Link.Vertical, 7).SequenceEqual(new[] { 7, 12 }));
                var ev = e.Place(0, 15);
                var placed = ev.OfType<PlacedEvent>().ToList();
                Check("horizontal pair fills two cells", placed.Count == 2 && placed[0].Plate == 15 && placed[1].Plate == 16);
                Check("pair halves keep their own slices",
                    e.Plates[15].Slices.SequenceEqual(new[] { 5, 5 }) && e.Plates[16].Slices.SequenceEqual(new[] { 6, 6 }));
                ev = e.Place(1, 9);
                Check("vertical pair fills the cell below", e.Plates[9].Slices.SequenceEqual(new[] { 5, 5 }) && e.Plates[14].Slices.SequenceEqual(new[] { 6, 6 }));
            }
            {
                var e = With(Slots(new Piece(Link.Horizontal, MakeCake(1, 3), MakeCake(1, 3)), One(5, 1), One(6, 1)));
                var ev = e.Place(0, 0);
                Check("linked halves of one flavor merge into a served cake", ev.OfType<ServedEvent>().Any(s => s.Flavor == 1));
                Check("after the serve both cells are free again", e.IsFree(0) && e.IsFree(1));
            }
            {
                var e = With(Slots(One(1, 2, 2, 4), One(3, 3, 4, 3), One(5, 6)), (0, new[] { 1, 1, 1, 1 }));
                var ev = e.Place(0, 1);
                Check("partial merge into plate 0", ev.OfType<MergedEvent>().Any(m => m.To == 0 && m.Flavor == 1 && m.Count == 2));
                Check("cake served from plate 0", ev.OfType<ServedEvent>().Any(s => s.Plate == 0 && s.Flavor == 1));
                Check("first serve scores 60", e.Score == 60);
                Check("leftover slices stay on plate 1", e.Plates[1].Slices.SequenceEqual(new[] { 2, 2, 2, 2 }));
            }
            {
                var e = With(Slots(One(1, 2, 2, 4), One(3, 3, 4, 3), One(5, 6)), (0, new[] { 1, 1, 1, 1 }));
                var ev = e.Place(0, 24);
                Check("no merge across the board", !ev.OfType<MergedEvent>().Any() && !ev.OfType<ServedEvent>().Any());
            }
            {
                var e = With(Slots(One(1, 1, 2, 5), One(3, 3, 4, 3), One(5, 6)),
                    (0, new[] { 1, 1, 1, 1, 1 }), (10, new[] { 2, 2, 2, 2, 2 }));
                var served = e.Place(0, 5).OfType<ServedEvent>().ToList();
                Check("cascade serves two cakes", served.Count == 2);
                Check("combo doubles the second cake", served.Count == 2 && served[1].Points == 120);
            }
            {
                var e = With(Slots(One(1, 3, 3, 3), One(4, 3, 5, 3), One(6, 6)), (0, new[] { 1, 1, 1, 2, 2, 2 }));
                var ev = e.Place(0, 1);
                Check("full plates exchange slices",
                    ev.OfType<MergedEvent>().Any(m => m.From == 0 && m.To == 1 && m.Count == 3 && m.GiveBack.SequenceEqual(new[] { 3, 3, 3 })));
                Check("exchange completes and serves plate 1", ev.OfType<ServedEvent>().Any(s => s.Plate == 1 && s.Flavor == 1));
                Check("displaced slices land on plate 0", e.Plates[0].Slices.OrderBy(x => x).SequenceEqual(new[] { 2, 2, 2, 3, 3, 3 }));
                Check("no plate exceeds six slices", e.Plates.All(p => p.Slices.Count <= Rules.SlicesPerCake));
            }
            {
                // last free cell filled by the last piece of the batch: the new batch can't fit anywhere
                var e = With(Slots(One(7, 1), null, null));
                FillNoMerge(e, 24);
                var ev = e.Place(0, 24);
                Check("full board with nothing that fits loses", e.State == GameState.Lost && ev.OfType<LostEvent>().Any());
                Check("lost game rejects placements", e.Place(0, 0).Count == 0);
                Check("revive resumes play", e.Revive() && e.State == GameState.Playing);
                Check("revive clears the centre 3x3", new[] { 6, 7, 8, 11, 12, 13, 16, 17, 18 }.All(i => e.Plates[i].IsEmpty));
                Check("revive leaves the border", !e.Plates[0].IsEmpty && !e.Plates[24].IsEmpty);
                Check("revive only works when lost", !e.Revive());
            }
            {
                // only isolated holes left: linked pieces can't fit, a single can
                var pair = new Piece(Link.Horizontal, MakeCake(5, 1), MakeCake(6, 1));
                var e = With(Slots(pair, pair, null));
                FillNoMerge(e, 0, 2, 4, 12);
                Check("pair onto a half-filled spot is rejected", e.Place(0, 0).Count == 0);
                Check("pair cannot use isolated holes", !e.FitsAnywhere(pair));
                Check("isolated holes with only pairs waiting ends the run", e.RefreshLost() && e.State == GameState.Lost);
                Check("booster resume reopens the run", e.ResumeForBooster() && e.State == GameState.Playing);
                Check("hammer frees a neighbour cell", e.Hammer(1));
                Check("after the hammer the pair fits again", e.FitsAnywhere(pair) && !e.RefreshLost());
            }
            {
                // fairness: a new batch dealt onto isolated holes always includes a piece that fits
                for (ulong seed = 1; seed <= 30; seed++)
                {
                    var e = new Engine(seed);
                    for (int i = 0; i < 3; i++) e.Slots[i] = null;
                    e.Slots[0] = One(7, 1);
                    FillNoMerge(e, 0, 2, 4, 12, 20);
                    e.Place(0, 20);
                    if (e.State != GameState.Playing || !e.AnyMoveAvailable())
                    {
                        Check($"fair batch for seed {seed}", false);
                        break;
                    }
                }
                Check("new batches on a tight board always contain a fitting piece", true);
            }
            {
                var e = With(Slots(One(0, 6), null, null));
                var unlocks = new List<FlavorUnlockedEvent>();
                var rewards = new List<RewardEvent>();
                for (int n = 0; n < 10; n++)
                {
                    for (int i = 0; i < 3; i++) e.Slots[i] = null;
                    e.Slots[0] = One(0, 6);
                    var ev = e.Place(0, 12);
                    unlocks.AddRange(ev.OfType<FlavorUnlockedEvent>());
                    rewards.AddRange(ev.OfType<RewardEvent>());
                }
                Check("ten served cakes reach 600 points", e.Score == 600 && e.CakesServed == 10);
                Check("600 points unlocks the fifth flavor", unlocks.Count == 1 && unlocks[0].Flavor == 4 && e.UnlockedFlavors == 5);
                Check("unlocking grants a booster reward", rewards.Count == 1 && rewards[0].Booster >= 0 && rewards[0].Booster < Rules.BoosterKinds);
                Check("progress moves on to the next goal", e.PreviousGoal == 600 && e.NextGoal == 1500 && e.NextUnlockFlavor == 5);
            }
            {
                var e = new Engine(11);
                bool ok = true;
                for (int n = 0; n < 300; n++)
                {
                    e.Shuffle();
                    foreach (var piece in e.Slots.Where(p => p != null))
                    foreach (var cake in piece.Cakes)
                        ok &= cake.Slices.Length >= 1 && cake.Slices.Length <= 6 && cake.Slices.All(f => f >= 0 && f < e.UnlockedFlavors);
                }
                Check("generated cakes use 1-6 slices of unlocked flavors only", ok);
            }
            {
                var e = With(Slots(One(3, 6), One(3, 6), One(3, 6)));
                e.Plates[6].Obstacle.Type = ObstacleType.CakeBox;
                e.Plates[6].Obstacle.Hp = 2;
                Check("cake box blocks placement", !e.CanPlace(0, 6));
                var ev = e.Place(0, 1);
                Check("adjacent serve damages the box", ev.OfType<BoxDamagedEvent>().Any(d => d.Plate == 6 && d.Hp == 1));
                ev = e.Place(1, 1);
                Check("second adjacent serve breaks the box",
                    ev.OfType<ObstacleClearedEvent>().Any(c => c.Plate == 6 && c.Type == ObstacleType.CakeBox) && e.IsFree(6));
            }
            {
                var e = With(Slots(One(3, 6), One(3, 6), One(3, 6)));
                e.Plates[24].Obstacle.Type = ObstacleType.Holder;
                e.Plates[24].Obstacle.Hp = 2;
                Check("holder blocks placement", !e.CanPlace(0, 24));
                var ev = e.Place(0, 0);
                Check("holder ticks down on a serve", ev.OfType<HolderTickEvent>().Any(t => t.Plate == 24 && t.Hp == 1));
                ev = e.Place(1, 0);
                Check("holder releases after two serves", ev.OfType<ObstacleClearedEvent>().Any(c => c.Plate == 24) && e.IsFree(24));
                var h = With(Slots(One(3, 6), null, null));
                h.Plates[3].Obstacle.Type = ObstacleType.Holder;
                h.Plates[3].Obstacle.Hp = 2;
                Check("hammer smashes a cloche", h.Hammer(3) && h.IsFree(3));
            }
            {
                var e = With(Slots(One(1, 4, 2, 2), One(4, 3, 5, 3), One(6, 6)), (0, new[] { 1, 1 }));
                e.Plates[0].Obstacle.Type = ObstacleType.Slime;
                var ev = e.Place(0, 1);
                Check("slime keeps its slices from leaving", !ev.OfType<MergedEvent>().Any(m => m.From == 0));
                var e2 = With(Slots(One(1, 2, 2, 4), One(4, 3, 5, 3), One(6, 6)), (0, new[] { 1, 1, 1, 1 }));
                e2.Plates[0].Obstacle.Type = ObstacleType.Slime;
                ev = e2.Place(0, 1);
                Check("slices can still flow into a slime plate", ev.OfType<MergedEvent>().Any(m => m.To == 0 && m.Count == 2));
                Check("serving on slime cleans it",
                    ev.OfType<ObstacleClearedEvent>().Any(c => c.Plate == 0 && c.Type == ObstacleType.Slime));
            }
            {
                var e = With(Slots(One(0, 6), One(0, 6), One(0, 6)),
                    (6, new[] { 1, 2 }), (12, new[] { 3 }), (18, new[] { 4, 4 }), (0, new[] { 5 }));
                Check("bomb hits a 3x3 area", e.Bomb(12));
                Check("bomb clears the 3x3 area", e.Plates[6].IsEmpty && e.Plates[12].IsEmpty && e.Plates[18].IsEmpty);
                Check("bomb leaves outside plates", e.Plates[0].Slices.SequenceEqual(new[] { 5 }));

                var w = With(Slots(One(0, 6), One(0, 6), One(0, 6)), (7, new[] { 2, 1, 1, 2, 1, 3 }));
                var wev = w.Whisk(7);
                Check("whisk mixes into the majority and serves", wev != null && wev.OfType<ServedEvent>().Any(s => s.Plate == 7 && s.Flavor == 1));
                Check("whisk refuses empty plates", w.Whisk(7) == null);

                var f = With(Slots(One(0, 6), One(0, 6), One(0, 6)), (3, new[] { 2, 2 }));
                int hit = f.Firework();
                Check("firework clears the only loaded plate", hit == 3 && f.Plates[3].IsEmpty);
                Check("firework with nothing to hit returns -1", f.Firework() == -1);

                var h = With(Slots(One(0, 6), One(0, 6), One(0, 6)), (0, new[] { 1, 1, 1 }));
                Check("hammer clears a plate", h.Hammer(0) && h.Plates[0].IsEmpty);
                Check("hammer refuses an empty plate", !h.Hammer(0));

                var s = With(Slots(null, One(0, 6), null));
                Check("shuffle rerolls only waiting pieces", s.Shuffle() && s.Slots[0] == null && s.Slots[1] != null && s.Slots[2] == null);
            }
            {
                var a = new Engine(99);
                var b = new Engine(99);
                Check("same seed -> same pieces", a.Save() == b.Save());
                var rng = new System.Random(3);
                for (int n = 0; n < 25 && a.State == GameState.Playing; n++)
                {
                    var moves = Enumerable.Range(0, 3).SelectMany(s => Enumerable.Range(0, 25).Where(x => a.CanPlace(s, x)).Select(x => (s, x))).ToList();
                    var (slot, anchor) = moves[rng.Next(moves.Count)];
                    a.Place(slot, anchor);
                }
                var restored = Engine.Load(a.Save());
                Check("save/load round-trips the full state", restored != null && restored.Save() == a.Save());
                if (restored != null && a.State == GameState.Playing && Advisor.BestMove(a, out int bs, out int ba))
                {
                    a.Place(bs, ba);
                    restored.Place(bs, ba);
                    Check("restored game continues identically", restored.Save() == a.Save());
                }
                Check("garbage save is rejected", Engine.Load("nonsense") == null && Engine.Load("") == null);
            }
            {
                // fuzz: random play never breaks invariants or hangs, and every run eventually ends
                bool ok = true;
                for (ulong seed = 1; seed <= 40 && ok; seed++)
                {
                    var e = new Engine(seed);
                    var rng = new System.Random((int)seed);
                    int moves = 0;
                    while (e.State == GameState.Playing && moves++ < 3000)
                    {
                        var options = Enumerable.Range(0, 3).SelectMany(s => Enumerable.Range(0, 25).Where(x => e.CanPlace(s, x)).Select(x => (s, x))).ToList();
                        if (options.Count == 0) { ok = false; Check($"fuzz seed {seed}: playing with no legal move", false); break; }
                        var (slot, anchor) = options[rng.Next(options.Count)];
                        e.Place(slot, anchor);
                        if (e.Plates.Any(p => p.Slices.Count > Rules.SlicesPerCake)
                            || e.Plates.Count(p => p.Obstacle.Type != ObstacleType.None) > Rules.MaxObstacles
                            || e.Plates.Any(p => p.Slices.Any(f => f < 0 || f >= e.UnlockedFlavors)))
                        {
                            ok = false;
                            Check($"fuzz seed {seed}: invariant broken at move {moves}", false);
                        }
                    }
                    if (e.State == GameState.Playing) { ok = false; Check($"fuzz seed {seed}: random play never ended", false); }
                }
                Check("fuzz: 40 random games keep invariants and end", ok);
            }
            {
                // a careful player keeps going: the advisor should survive far longer than random play
                var stats = new List<(int moves, int score, int unlocked, bool lost)>();
                var randomMoves = new List<int>();
                for (ulong seed = 1; seed <= 6; seed++)
                {
                    var e = new Engine(seed * 7919);
                    while (e.State == GameState.Playing && e.Moves < 400)
                    {
                        if (!Advisor.BestMove(e, out int s, out int a)) break;
                        e.Place(s, a);
                    }
                    stats.Add((e.Moves, e.Score, e.UnlockedFlavors, e.State == GameState.Lost));

                    var r = new Engine(seed * 7919);
                    var rng = new System.Random((int)seed);
                    while (r.State == GameState.Playing && r.Moves < 400)
                    {
                        var options = Enumerable.Range(0, 3).SelectMany(sl => Enumerable.Range(0, 25).Where(x => r.CanPlace(sl, x)).Select(x => (sl, x))).ToList();
                        var (slot, anchor) = options[rng.Next(options.Count)];
                        r.Place(slot, anchor);
                    }
                    randomMoves.Add(r.Moves);
                }
                Debug.Log("[CakeSort][SelfTest] advisor runs (moves/score/flavors/lost): " +
                          string.Join("  ", stats.Select(x => $"{x.moves}/{x.score}/{x.unlocked}/{(x.lost ? "L" : "-")}")) +
                          "  random moves: " + string.Join(",", randomMoves));
                double avgAdvisor = stats.Average(x => x.moves), avgRandom = randomMoves.Average();
                Check($"careful play lasts far longer than random play ({avgAdvisor:F0} vs {avgRandom:F0} moves)", avgAdvisor >= avgRandom * 4);
                Check("careful play unlocks every flavor in most runs", stats.Count(x => x.unlocked == Rules.MaxFlavors) >= stats.Count - 1);
            }
        }
    }
}
