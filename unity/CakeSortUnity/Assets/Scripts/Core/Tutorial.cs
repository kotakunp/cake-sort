// The guided first game: a prepared board and first batch where each scripted placement teaches one idea.
// After the three scripted pieces, batches are dealt normally and the run carries on as a regular game.
using System.Collections.Generic;

namespace CakeSort.Core
{
    public static class Tutorial
    {
        public struct Move
        {
            public int Slot, Anchor;
            public string Text;
        }

        /// <summary>
        /// 1) two cherry slices next to four cherry slices: they merge and the cake is served.
        /// 2) a mixed cake beside four vanilla: the vanilla pair completes that cake, the chocolate slice stays.
        /// 3) a linked pair: its chocolate joins the leftover slice, and its matcha completes the matcha plate below.
        /// </summary>
        public static readonly Move[] Moves =
        {
            new Move { Slot = 0, Anchor = 12, Text = "Drag the cake onto the glowing plate.\nMatching slices slide together!" },
            new Move { Slot = 1, Anchor = 13, Text = "Six of a kind is served! Mixed cakes split up:\neach flavor goes to its match." },
            new Move { Slot = 2, Anchor = 18, Text = "Linked plates need two empty spots\nside by side. Drop it here!" },
        };

        public const string Outro = "Great job! Customers up top pay coins for orders.\nDon't let the tray fill up!";

        public static Engine NewGame(ulong seed) => Engine.Prepared(seed,
            new Dictionary<int, int[]>
            {
                { 11, new[] { 0, 0, 0, 0 } },
                { 14, new[] { 2, 2, 2, 2 } },
                { 24, new[] { 3, 3, 3 } },
            },
            new[]
            {
                Piece.Single(0, 0),
                Piece.Single(1, 2, 2),
                new Piece(Link.Horizontal, new Cake(1, 1), new Cake(3, 3, 3)),
            });
    }
}
