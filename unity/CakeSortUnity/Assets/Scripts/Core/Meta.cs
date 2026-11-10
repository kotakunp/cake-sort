// CakeSort meta progression. Original code, pure C#, no UnityEngine dependencies.
// Customer orders give each run short-term goals; coins, bakery levels and a daily treat give reasons
// to come back. Orders persist between runs and are refreshed when their flavor leaves the menu.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CakeSort.Core
{
    public enum OrderKind { Flavor, AnyCakes, Combo, Linked }

    public class Order
    {
        public OrderKind Kind;
        public int Flavor = -1;
        public int Target, Progress, Reward, Customer;
        public bool Done => Progress >= Target;
        public Order Copy() => (Order)MemberwiseClone();
    }

    /// <summary>An order that moved forward; <see cref="Completed"/> holds its final state when it was finished.</summary>
    public class OrderProgress
    {
        public int Index;
        public Order Before, After;
        public Order Completed;
        public Order Replacement;
    }

    public class LevelUp { public int Level, Coins, Booster; }

    public class DailyTreat { public int Day, Coins, Booster = -1; }

    public static class MetaRules
    {
        public const int OrderSlots = 3;
        public const int Customers = 4;
        public const int MaxSpecialOrders = 1;

        // Hammer, Bomb, Whisk, Firework, Shuffle (same order as the booster bar).
        static readonly int[] BoosterPrices = { 150, 300, 200, 200, 120 };
        static readonly int[] DailyCoins = { 40, 50, 60, 80, 100, 120, 250 };
        public const int RevivePrice = 300;
        /// <summary>Paid revives allowed per run after the free one, so coins can't buy an endless run.</summary>
        public const int MaxPaidRevives = 2;

        public static int BoosterPrice(int booster) => BoosterPrices[Math.Max(0, Math.Min(booster, BoosterPrices.Length - 1))];

        /// <summary>Paid revives get dearer within a run: 300, 600, 900...</summary>
        public static int ReviveCost(int paidRevivesThisRun) => RevivePrice * (paidRevivesThisRun + 1);

        public static int CoinsForServe(int combo) => 1 + Math.Max(0, combo - 1);

        /// <summary>Stars needed to go from <paramref name="level"/> to the next level.</summary>
        public static int StarsForLevel(int level) => 2 + 3 * Math.Max(1, level);

        public static int LevelCoins(int newLevel) => 40 + 10 * newLevel;

        /// <summary>Level-up booster rotates through the bar, starting with the Bomb at level 2.</summary>
        public static int LevelBooster(int newLevel) => ((newLevel - 1) % Rules.BoosterKinds + Rules.BoosterKinds) % Rules.BoosterKinds;

        public const int DailyDays = 7;
        public static int DailyCoinsFor(int day) => DailyCoins[Math.Max(1, Math.Min(day, DailyDays)) - 1];
        public static int DailyBoosterFor(int day) => day == DailyDays ? 1 : -1;
    }

    /// <summary>Three customer orders. Progress comes from served cakes, combos and linked placements.</summary>
    public class OrderBook
    {
        public readonly Order[] Active = new Order[MetaRules.OrderSlots];
        public int Completed { get; private set; }
        private SplitMix64 _rng;

        public OrderBook(ulong seed) { _rng = new SplitMix64(seed); }

        static bool OnMenu(Engine e, int flavor) => flavor >= e.FirstActiveFlavor && flavor < e.UnlockedFlavors;

        /// <summary>Fills empty slots and swaps orders whose flavor is no longer dealt. Returns the changed slots.</summary>
        public List<int> Refresh(Engine e)
        {
            var changed = new List<int>();
            for (int i = 0; i < Active.Length; i++)
            {
                var o = Active[i];
                if (o != null && !(o.Kind == OrderKind.Flavor && !OnMenu(e, o.Flavor))) continue;
                Active[i] = null;
                Active[i] = Make(e, i);
                changed.Add(i);
            }
            return changed;
        }

        Order Make(Engine e, int index)
        {
            var others = Active.Where((o, j) => j != index && o != null).ToList();
            var customers = Enumerable.Range(0, MetaRules.Customers).Where(c => others.All(o => o.Customer != c)).ToList();
            int customer = customers[_rng.NextInt(customers.Count)];
            var freeFlavors = Enumerable.Range(e.FirstActiveFlavor, e.UnlockedFlavors - e.FirstActiveFlavor)
                .Where(f => others.All(o => o.Kind != OrderKind.Flavor || o.Flavor != f)).ToList();
            bool specialAllowed = others.Count(o => o.Kind != OrderKind.Flavor) < MetaRules.MaxSpecialOrders;
            int bonus = Math.Min(2, e.Score / 4000);

            double roll = _rng.NextDouble();
            if (!specialAllowed || roll < 0.7)
            {
                if (freeFlavors.Count > 0)
                {
                    int target = 3 + _rng.NextInt(2) + bonus;
                    return new Order { Kind = OrderKind.Flavor, Flavor = freeFlavors[_rng.NextInt(freeFlavors.Count)], Target = target, Reward = 5 * target, Customer = customer };
                }
                roll = 0.75;
            }
            if (roll < 0.8)
            {
                int target = 8 + 2 * _rng.NextInt(3);
                return new Order { Kind = OrderKind.AnyCakes, Target = target, Reward = 2 * target, Customer = customer };
            }
            if (roll < 0.9)
            {
                int target = e.Score < 3000 ? 2 : 2 + _rng.NextInt(2);
                return new Order { Kind = OrderKind.Combo, Target = target, Reward = target == 2 ? 20 : 35, Customer = customer };
            }
            int linked = 3 + _rng.NextInt(3);
            return new Order { Kind = OrderKind.Linked, Target = linked, Reward = 5 * linked, Customer = customer };
        }

        public List<OrderProgress> OnServed(Engine e, int flavor, int combo)
        {
            return Advance(e, o =>
                o.Kind == OrderKind.Flavor && o.Flavor == flavor ? o.Progress + 1
                : o.Kind == OrderKind.AnyCakes ? o.Progress + 1
                : o.Kind == OrderKind.Combo ? Math.Max(o.Progress, combo)
                : o.Progress);
        }

        public List<OrderProgress> OnPlaced(Engine e, Piece piece)
        {
            if (piece == null || !piece.IsLinked) return new List<OrderProgress>();
            return Advance(e, o => o.Kind == OrderKind.Linked ? o.Progress + 1 : o.Progress);
        }

        /// <summary>Applies progress, then replaces any finished order with a new customer straight away.</summary>
        List<OrderProgress> Advance(Engine e, Func<Order, int> next)
        {
            var result = new List<OrderProgress>();
            for (int i = 0; i < Active.Length; i++)
            {
                var o = Active[i];
                if (o == null || o.Done) continue;
                int progress = Math.Min(o.Target, next(o));
                if (progress == o.Progress) continue;
                var change = new OrderProgress { Index = i, Before = o.Copy() };
                o.Progress = progress;
                change.After = o.Copy();
                if (o.Done)
                {
                    Completed++;
                    change.Completed = o.Copy();
                    Active[i] = null;
                    Active[i] = Make(e, i);
                    change.Replacement = Active[i].Copy();
                }
                result.Add(change);
            }
            return result;
        }

        public string Save()
        {
            var orders = Active.Select(o => o == null ? "-" :
                string.Join(",", (int)o.Kind, o.Flavor, o.Target, o.Progress, o.Reward, o.Customer));
            return "o1|" + _rng.StateValue.ToString(CultureInfo.InvariantCulture) + "|" + Completed + "|" + string.Join("/", orders);
        }

        public static OrderBook Load(string text)
        {
            try
            {
                var parts = text.Split('|');
                if (parts.Length != 4 || parts[0] != "o1") return null;
                var book = new OrderBook(0) { _rng = new SplitMix64(ulong.Parse(parts[1], CultureInfo.InvariantCulture)), Completed = int.Parse(parts[2]) };
                var orders = parts[3].Split('/');
                if (orders.Length != MetaRules.OrderSlots) return null;
                for (int i = 0; i < orders.Length; i++)
                {
                    if (orders[i] == "-") continue;
                    var f = orders[i].Split(',').Select(int.Parse).ToArray();
                    book.Active[i] = new Order { Kind = (OrderKind)f[0], Flavor = f[1], Target = f[2], Progress = f[3], Reward = f[4], Customer = f[5] };
                }
                return book;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>Everything that persists between runs apart from boosters and the run itself.</summary>
    public class Profile
    {
        public int Coins, Level = 1, Stars, Streak, LastDailyDay = -1, OrdersDone, CakesServed;

        public int StarsNeeded => MetaRules.StarsForLevel(Level);

        public void Earn(int coins) => Coins += Math.Max(0, coins);

        public bool Spend(int price)
        {
            if (price < 0 || Coins < price) return false;
            Coins -= price;
            return true;
        }

        /// <summary>Adds one star; on a level-up pays the coin reward and returns it (the caller grants the booster).</summary>
        public LevelUp AddStar()
        {
            Stars++;
            if (Stars < StarsNeeded) return null;
            Stars -= StarsNeeded;
            Level++;
            var reward = new LevelUp { Level = Level, Coins = MetaRules.LevelCoins(Level), Booster = MetaRules.LevelBooster(Level) };
            Coins += reward.Coins;
            return reward;
        }

        /// <summary>Today's treat, or null if already claimed today. Missing a day restarts the streak at day 1.</summary>
        public DailyTreat PeekDaily(int today)
        {
            if (LastDailyDay == today) return null;
            int day = LastDailyDay == today - 1 ? Streak % MetaRules.DailyDays + 1 : 1;
            return new DailyTreat { Day = day, Coins = MetaRules.DailyCoinsFor(day), Booster = MetaRules.DailyBoosterFor(day) };
        }

        public DailyTreat ClaimDaily(int today)
        {
            var treat = PeekDaily(today);
            if (treat == null) return null;
            Streak = treat.Day;
            LastDailyDay = today;
            Coins += treat.Coins;
            return treat;
        }

        public string Save() => string.Join("|", "p1", Coins, Level, Stars, Streak, LastDailyDay, OrdersDone, CakesServed);

        public static Profile Load(string text)
        {
            try
            {
                var p = text.Split('|');
                if (p.Length != 8 || p[0] != "p1") return null;
                return new Profile
                {
                    Coins = int.Parse(p[1]), Level = Math.Max(1, int.Parse(p[2])), Stars = int.Parse(p[3]), Streak = int.Parse(p[4]),
                    LastDailyDay = int.Parse(p[5]), OrdersDone = int.Parse(p[6]), CakesServed = int.Parse(p[7]),
                };
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
