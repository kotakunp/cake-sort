// Move evaluation shared by the in-game hint, the automated smoke test, and the editor self-test.
using System.Linq;

namespace CakeSort.Core
{
    public static class Advisor
    {
        /// <summary>Simulates every legal placement on a copy of the game and picks the one that leaves the best board.</summary>
        public static bool BestMove(Engine engine, out int bestSlot, out int bestAnchor)
        {
            bestSlot = bestAnchor = -1;
            double bestValue = double.NegativeInfinity;
            string snapshot = engine.Save();
            for (int s = 0; s < Rules.SlotCount; s++)
            {
                if (engine.Slots[s] == null) continue;
                for (int a = 0; a < engine.Plates.Length; a++)
                {
                    if (!engine.CanPlace(s, a)) continue;
                    var copy = Engine.Load(snapshot);
                    copy.Place(s, a);
                    double value = Evaluate(copy) + (copy.Score - engine.Score) * 1.5;
                    if (value > bestValue) { bestValue = value; bestSlot = s; bestAnchor = a; }
                }
            }
            return bestSlot >= 0;
        }

        static double Evaluate(Engine e)
        {
            if (e.State == GameState.Lost) return -1e6;
            double value = 0;
            foreach (var p in e.Plates)
            {
                if (p.Blocked) continue;
                if (p.IsEmpty) { value += 14; continue; }
                int distinct = p.Slices.Distinct().Count();
                value -= (distinct - 1) * 9;
                // plates close to a full single flavor are worth keeping
                value += p.Slices.GroupBy(x => x).Max(g => g.Count()) * 1.5;
            }
            // the rest of the batch must still fit, otherwise this move ends the run
            if (!e.CanPlaceAll(e.Slots)) value -= 5000;
            return value;
        }
    }
}
