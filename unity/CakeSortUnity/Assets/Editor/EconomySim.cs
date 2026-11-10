using System;
using System.Collections.Generic;
using System.Linq;
using CakeSort.Core;
using UnityEditor;
using UnityEngine;

namespace CakeSort.EditorTools
{
    /// <summary>
    /// Economy and progression simulation: players of three skill levels play many runs with the real rules,
    /// customer orders, coins, levels, daily treats, revives and boosters. Prints per-tier pacing numbers.
    /// Run: Unity -batchmode -projectPath . -executeMethod CakeSort.EditorTools.EconomySim.Run
    /// </summary>
    public static class EconomySim
    {
        // Mirrors the game screen's booster grant and the time a real player spends per move (drag + animations).
        static readonly int[] StartBoosters = { 2, 1, 1, 1, 2 };
        const float SecondsPerMove = 3.5f;
        const int RunsPerDay = 3;
        const int Runs = 15;
        const int MoveCap = 900;
        static int PlayersPerTier = 5;
        static float _clock;

        class Tier { public string Name; public double Mistakes; }

        class Stats
        {
            public readonly List<int> Moves = new List<int>(), Orders = new List<int>(), CoinsPerRun = new List<int>(), Scores = new List<int>(), FirstLoss = new List<int>();
            public readonly List<int> FirstLevelUpRun = new List<int>();
            public readonly List<float> FirstLevelUpMinutes = new List<float>();
            public readonly int[] LevelAtRun = new int[Runs];
            public int ServeCoins, OrderCoins, LevelCoins, DailyCoins, SpentRevive, SpentShop, PaidRevives, BoostersUsed, BoostersEarned, RunsTotal;
            public readonly List<int> FinalCoins = new List<int>();
        }

        [MenuItem("Cake Sort/Run Economy Simulation")]
        public static void Run()
        {
            var tiers = new[]
            {
                new Tier { Name = "casual", Mistakes = 0.35 },
                new Tier { Name = "average", Mistakes = 0.15 },
                new Tier { Name = "strong", Mistakes = 0.0 },
            };
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-econPlayers");
            if (at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out int n)) PlayersPerTier = Math.Max(1, n);
            var started = DateTime.Now;
            foreach (var tier in tiers)
            {
                var stats = new Stats();
                for (int player = 0; player < PlayersPerTier; player++) SimulatePlayer(tier, (ulong)(1000 + player * 77), stats);
                Report(tier, stats);
            }
            Debug.Log($"[CakeSort][Economy] done in {(DateTime.Now - started).TotalSeconds:F0}s");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static void SimulatePlayer(Tier tier, ulong seed, Stats stats)
        {
            var rng = new System.Random((int)seed);
            var profile = new Profile();
            var orders = new OrderBook(seed ^ 0x5EED);
            var boosters = (int[])StartBoosters.Clone();
            int firstLevelRun = -1;
            float minutes = 0f;
            for (int run = 0; run < Runs; run++)
            {
                if (run % RunsPerDay == 0)
                {
                    var treat = profile.ClaimDaily(run / RunsPerDay);
                    stats.DailyCoins += treat.Coins;
                    if (treat.Booster >= 0) { boosters[treat.Booster]++; stats.BoostersEarned++; }
                }
                int coinsAtStart = profile.Coins;
                var e = new Engine(seed * 1000 + (ulong)run);
                orders.Refresh(e);
                bool freeRevive = true;
                int paidRevives = 0, moves = 0, ordersThisRun = 0, firstLoss = -1;
                while (moves < MoveCap)
                {
                    if (e.State == GameState.Lost)
                    {
                        if (firstLoss < 0) firstLoss = moves;
                        if (freeRevive) { freeRevive = false; e.Revive(); continue; }
                        if (UseBooster(e, boosters, rng)) { stats.BoostersUsed++; e.RefreshLost(); continue; }
                        int cost = MetaRules.ReviveCost(paidRevives);
                        // Players keep a cushion: they only pay for a revive they can comfortably afford.
                        if (paidRevives < MetaRules.MaxPaidRevives && profile.Coins >= cost * 3 / 2 && profile.Spend(cost))
                        {
                            stats.SpentRevive += cost;
                            stats.PaidRevives++;
                            paidRevives++;
                            e.Revive();
                            continue;
                        }
                        break;
                    }
                    if (!PickMove(e, tier, rng, out int slot, out int anchor)) { e.RefreshLost(); if (e.State != GameState.Lost) break; continue; }
                    var piece = e.Slots[slot];
                    var events = e.Place(slot, anchor);
                    moves++;
                    _clock = minutes + moves * SecondsPerMove / 60f;
                    ordersThisRun += Apply(orders.OnPlaced(e, piece), profile, stats, run, ref firstLevelRun, boosters);
                    foreach (var ev in events)
                    {
                        if (ev is ServedEvent served)
                        {
                            int coins = MetaRules.CoinsForServe(served.Combo);
                            profile.Earn(coins);
                            stats.ServeCoins += coins;
                            profile.CakesServed++;
                            ordersThisRun += Apply(orders.OnServed(e, served.Flavor, served.Combo), profile, stats, run, ref firstLevelRun, boosters);
                        }
                        else if (ev is RewardEvent reward) { boosters[reward.Booster]++; stats.BoostersEarned++; }
                    }
                    orders.Refresh(e);
                }
                minutes += moves * SecondsPerMove / 60f;
                // Between runs, a player low on rescue boosters buys one when it costs under half their coins.
                foreach (int b in new[] { 1, 3, 0 })
                {
                    if (boosters[0] + boosters[1] + boosters[3] >= 2) break;
                    int price = MetaRules.BoosterPrice(b);
                    if (profile.Coins >= price * 2 && profile.Spend(price)) { boosters[b]++; stats.SpentShop += price; }
                }
                stats.Moves.Add(moves);
                stats.Scores.Add(e.Score);
                stats.FirstLoss.Add(firstLoss < 0 ? moves : firstLoss);
                stats.Orders.Add(ordersThisRun);
                stats.CoinsPerRun.Add(profile.Coins - coinsAtStart);
                stats.LevelAtRun[run] += profile.Level;
                stats.RunsTotal++;
            }
            if (firstLevelRun < 0) stats.FirstLevelUpRun.Add(Runs + 1);
            stats.FinalCoins.Add(profile.Coins);
        }

        static int Apply(List<OrderProgress> changes, Profile profile, Stats stats, int run, ref int firstLevelRun, int[] boosters)
        {
            int done = 0;
            foreach (var change in changes)
            {
                if (change.Completed == null) continue;
                done++;
                profile.Earn(change.Completed.Reward);
                stats.OrderCoins += change.Completed.Reward;
                profile.OrdersDone++;
                var up = profile.AddStar();
                if (up == null) continue;
                stats.LevelCoins += up.Coins;
                boosters[up.Booster]++;
                stats.BoostersEarned++;
                if (firstLevelRun < 0) { firstLevelRun = run; stats.FirstLevelUpRun.Add(run + 1); stats.FirstLevelUpMinutes.Add(_clock); }
            }
            return done;
        }

        static bool PickMove(Engine e, Tier tier, System.Random rng, out int slot, out int anchor)
        {
            if (rng.NextDouble() < tier.Mistakes)
            {
                var options = Enumerable.Range(0, Rules.SlotCount)
                    .SelectMany(s => Enumerable.Range(0, e.Plates.Length).Where(a => e.CanPlace(s, a)).Select(a => (s, a))).ToList();
                if (options.Count > 0) { (slot, anchor) = options[rng.Next(options.Count)]; return true; }
            }
            return Advisor.BestMove(e, out slot, out anchor);
        }

        /// <summary>A stuck player clears room with a bomb, firework or hammer if they have one.</summary>
        static bool UseBooster(Engine e, int[] boosters, System.Random rng)
        {
            if (!e.ResumeForBooster()) return false;
            int busiest = Enumerable.Range(0, e.Plates.Length).OrderByDescending(i => e.Plates[i].Slices.Count).First();
            if (boosters[1] > 0 && e.Bomb(12)) { boosters[1]--; return true; }
            if (boosters[3] > 0 && e.Firework() >= 0) { boosters[3]--; return true; }
            if (boosters[0] > 0 && e.Hammer(busiest)) { boosters[0]--; return true; }
            if (boosters[4] > 0 && e.Shuffle()) { boosters[4]--; return true; }
            e.RefreshLost();
            return false;
        }

        static double FirstLossMinutes(Stats s) => s.FirstLoss.OrderBy(v => v).ElementAt(s.FirstLoss.Count / 2) * SecondsPerMove / 60.0;

        static string Median(List<int> values)
        {
            if (values.Count == 0) return "-";
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2].ToString();
        }

        static void Report(Tier tier, Stats s)
        {
            double runs = s.RunsTotal;
            double meanMoves = s.Moves.Average();
            double minutesPerRun = meanMoves * SecondsPerMove / 60.0;
            double ordersPerRun = s.Orders.Average();
            int earned = s.ServeCoins + s.OrderCoins + s.LevelCoins + s.DailyCoins;
            string levels = string.Join(" ", new[] { 1, 3, 6, 9, 15 }.Select(r => $"r{r}:{s.LevelAtRun[r - 1] / (double)PlayersPerTier:F1}"));
            Debug.Log($"[CakeSort][Economy] {tier.Name}: moves/run median {Median(s.Moves)} mean {meanMoves:F0} ({minutesPerRun:F1} min), capped {s.Moves.Count(m => m >= MoveCap)}/{s.Moves.Count}, score median {Median(s.Scores)}, first loss at move {Median(s.FirstLoss)} (~{FirstLossMinutes(s):F0} min) | " +
                      $"orders/run {ordersPerRun:F1} (one per {(ordersPerRun > 0 ? minutesPerRun / ordersPerRun : 0):F1} min) | " +
                      $"coins earned/run {earned / runs:F0} (serve {s.ServeCoins / runs:F0}, orders {s.OrderCoins / runs:F0}, levels {s.LevelCoins / runs:F0}, daily {s.DailyCoins / runs:F0}) | " +
                      $"spent/run revive {s.SpentRevive / runs:F0} shop {s.SpentShop / runs:F0}, paid revives {s.PaidRevives / runs:F2}/run | " +
                      $"boosters used {s.BoostersUsed / runs:F1}/run earned {s.BoostersEarned / runs:F1}/run | " +
                      $"first level-up run {Median(s.FirstLevelUpRun)} (~{(s.FirstLevelUpMinutes.Count > 0 ? s.FirstLevelUpMinutes.Average() : 0):F0} min) | level {levels} | coins after {Runs} runs {Median(s.FinalCoins)}");
        }
    }
}
