// Meta layer on the game screen: customer order cards, the coin pill and bakery level badge, and the
// shop / level-up / daily-treat popups. Rules live in Core/Meta.cs; this file only presents them.
using System;
using System.Collections;
using System.Collections.Generic;
using CakeSort.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CakeSort.Game
{
    public partial class GameController
    {
        static readonly string[] BoosterNames = { "Hammer", "Cupcake Bomb", "Magic Whisk", "Firework", "Shuffle" };
        static readonly string[] BoosterInfo =
        {
            "Smash one plate, gift box or cloche.",
            "Blast every plate in a 3x3 area.",
            "Turn a mixed plate into one flavor.",
            "Clear a random plate instantly.",
            "Swap the three waiting pieces.",
        };
        static readonly string[] CustomerNames = { "Bunny", "Bear", "Panda", "Fox" };

        Profile _profile;
        OrderBook _orders;
        readonly OrderCard[] _orderCards = new OrderCard[MetaRules.OrderSlots];
        readonly Order[] _orderShown = new Order[MetaRules.OrderSlots];
        readonly bool[] _orderLocked = new bool[MetaRules.OrderSlots];
        readonly Queue<LevelUp> _pendingLevelUps = new Queue<LevelUp>();
        int _paidRevives, _orderAnimations, _levelShown = 1, _starsShown;
        float _coinsShown, _coinsGoal;
        bool _smokeOrderShot;

        RectTransform _coinPill, _levelBadge;
        Text _coinText, _levelText;
        Image _levelRing;

        GameObject _levelUp, _daily, _shop;
        Text _levelUpTitle, _levelUpCoins, _shopName, _shopInfo, _shopPrice, _shopNote, _dailyNote;
        Image _levelUpBooster, _shopIcon;
        RectTransform _levelUpChest, _levelUpRays, _shopBuy;
        readonly DailyTile[] _dailyTiles = new DailyTile[MetaRules.DailyDays];
        int _shopBooster = -1;
        Text _titleCoins, _titleLevel;

        class OrderCard
        {
            public RectTransform Root, Body, IconSpot, Stamp;
            public Image Avatar, Icon, Extra1, Extra2, Fill;
            public Text Count, Reward, Big;
        }

        class DailyTile
        {
            public RectTransform Root;
            public Image Bg, Frame, Icon;
            public Text Day, Amount;
        }

        static int Today => (int)(DateTime.Now.Date - new DateTime(2020, 1, 1)).TotalDays;

        void LoadMeta()
        {
            _profile = _smokeDir == null ? Profile.Load(PlayerPrefs.GetString("profile", "")) : null;
            _profile = _profile ?? new Profile();
            _orders = _smokeDir == null ? OrderBook.Load(PlayerPrefs.GetString("orders", "")) : null;
            _orders = _orders ?? new OrderBook(_smokeDir != null ? 99UL : (ulong)DateTime.Now.Ticks ^ 0x5EEDUL);
            _paidRevives = _smokeDir == null ? PlayerPrefs.GetInt("paidRevives", 0) : 0;
            _coinsShown = _coinsGoal = _profile.Coins;
            _levelShown = _profile.Level;
            _starsShown = _profile.Stars;
        }

        void SaveMeta()
        {
            PlayerPrefs.SetString("profile", _profile.Save());
            PlayerPrefs.SetString("orders", _orders.Save());
            PlayerPrefs.SetInt("paidRevives", _paidRevives);
        }

        // =====================================================================
        // HUD pieces
        // =====================================================================

        void BuildCoinPill(RectTransform bar)
        {
            var pill = UI.Img(bar, "CoinPill", Art.Rounded(), new Color(1f, 0.98f, 0.94f, 0.95f));
            UI.Place(pill.rectTransform, new Vector2(0, 1), new Vector2(175, -66), new Vector2(300, 86));
            UI.DropShadow(pill, 5, 0.25f);
            var coin = UI.Img(pill.rectTransform, "Coin", Art.Coin());
            coin.preserveAspect = true;
            UI.Place(coin.rectTransform, new Vector2(0, 0.5f), new Vector2(40, 2), new Vector2(84, 84));
            _coinText = UI.Txt(pill.rectTransform, "Coins", "0", 40, Palette.Ink);
            UI.Stretch(_coinText.rectTransform, 0.3f, 0.96f, 0, 1);
            _coinPill = coin.rectTransform;
        }

        void BuildLevelBadge(RectTransform bar)
        {
            var track = UI.Img(bar, "LevelTrack", Art.Circle(), new Color(1f, 0.98f, 0.94f, 0.95f));
            UI.Place(track.rectTransform, new Vector2(0, 1), new Vector2(92, -222), new Vector2(136, 136));
            UI.DropShadow(track, 5, 0.3f);
            var groove = UI.Img(track.rectTransform, "Groove", Art.Circle(), Palette.Hex(0xE9D3B4));
            UI.Stretch(groove.rectTransform, 0.06f, 0.94f, 0.06f, 0.94f);
            _levelRing = UI.Img(track.rectTransform, "Ring", Art.Circle(), Palette.Mint);
            UI.Stretch(_levelRing.rectTransform, 0.06f, 0.94f, 0.06f, 0.94f);
            _levelRing.type = Image.Type.Filled;
            _levelRing.fillMethod = Image.FillMethod.Radial360;
            _levelRing.fillOrigin = (int)Image.Origin360.Top;
            _levelRing.fillClockwise = true;
            var inner = UI.Img(track.rectTransform, "Inner", Art.Circle(), Palette.Cream);
            UI.Stretch(inner.rectTransform, 0.17f, 0.83f, 0.17f, 0.83f);
            var star = UI.Img(track.rectTransform, "Star", Art.Star(), Palette.Gold);
            UI.Stretch(star.rectTransform, 0.14f, 0.86f, 0.14f, 0.86f);
            UI.DropShadow(star, 3, 0.3f);
            _levelText = UI.Txt(track.rectTransform, "Level", "1", 40, Color.white, TextAnchor.MiddleCenter, true, 2.5f);
            UI.Fill(_levelText.rectTransform);
            _levelText.rectTransform.anchoredPosition = new Vector2(0, -3);
            _levelBadge = track.rectTransform;
        }

        void BuildOrders(RectTransform bar)
        {
            var row = UI.Rect("Orders", bar);
            UI.Place(row, new Vector2(0.5f, 1), new Vector2(0, -392), new Vector2(1040, 186));
            for (int i = 0; i < _orderCards.Length; i++)
            {
                var card = new OrderCard { Root = UI.Rect("Order" + i, row) };
                UI.Place(card.Root, new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 347f, 0), new Vector2(330, 186));
                var body = UI.Img(card.Root, "Body", Art.Rounded(), new Color(1f, 0.98f, 0.94f, 0.97f));
                UI.Fill(body.rectTransform);
                UI.DropShadow(body, 6, 0.25f);
                card.Body = body.rectTransform;

                var avatarBg = UI.Img(card.Body, "AvatarBg", Art.Circle(), Palette.Hex(0xFFE3EC));
                UI.Place(avatarBg.rectTransform, new Vector2(0, 0.5f), new Vector2(82, 14), new Vector2(136, 136));
                card.Avatar = UI.Img(avatarBg.rectTransform, "Avatar", Art.Customer(i));
                card.Avatar.preserveAspect = true;
                UI.Stretch(card.Avatar.rectTransform, 0.02f, 0.98f, 0.02f, 0.98f);

                var chip = UI.Img(card.Body, "RewardChip", Art.Rounded(), Palette.Hex(0xFFE8A3));
                UI.Place(chip.rectTransform, new Vector2(0, 0), new Vector2(82, 30), new Vector2(128, 44));
                var coin = UI.Img(chip.rectTransform, "Coin", Art.Coin());
                coin.preserveAspect = true;
                UI.Place(coin.rectTransform, new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(44, 44));
                card.Reward = UI.Txt(chip.rectTransform, "Reward", "+0", 28, Palette.Ink);
                UI.Stretch(card.Reward.rectTransform, 0.3f, 1f, 0, 1);

                card.IconSpot = UI.Rect("IconSpot", card.Body);
                UI.Place(card.IconSpot, new Vector2(1, 0.5f), new Vector2(-84, 26), new Vector2(116, 116));
                card.Extra1 = UI.Img(card.IconSpot, "Extra1", Art.Cake(0));
                card.Extra2 = UI.Img(card.IconSpot, "Extra2", Art.Cake(1));
                card.Icon = UI.Img(card.IconSpot, "Icon", Art.Cake(2));
                foreach (var img in new[] { card.Icon, card.Extra1, card.Extra2 }) { img.preserveAspect = true; UI.Fill(img.rectTransform); }
                card.Big = UI.Txt(card.IconSpot, "Big", "x3", 66, Palette.Gold, TextAnchor.MiddleCenter, true, 4f);
                UI.Fill(card.Big.rectTransform);

                card.Count = UI.Txt(card.Body, "Count", "0/3", 32, Palette.Ink);
                UI.Place(card.Count.rectTransform, new Vector2(1, 0.5f), new Vector2(-84, -48), new Vector2(150, 40));
                var barTrack = UI.Img(card.Body, "BarTrack", Art.Rounded(), Palette.Hex(0xE9D3B4));
                UI.Place(barTrack.rectTransform, new Vector2(1, 0.5f), new Vector2(-84, -76), new Vector2(132, 16));
                barTrack.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                card.Fill = UI.Img(barTrack.rectTransform, "Fill", Art.Rounded(), Palette.Mint);
                card.Fill.rectTransform.anchorMin = Vector2.zero;
                card.Fill.rectTransform.anchorMax = new Vector2(0, 1);
                card.Fill.rectTransform.pivot = new Vector2(0, 0.5f);
                card.Fill.rectTransform.sizeDelta = Vector2.zero;

                var stamp = UI.Txt(card.Body, "Stamp", "DONE!", 64, Palette.Mint, TextAnchor.MiddleCenter, true, 4f);
                UI.Place(stamp.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 4), new Vector2(330, 120));
                stamp.rectTransform.localRotation = Quaternion.Euler(0, 0, 10f);
                stamp.gameObject.SetActive(false);
                card.Stamp = stamp.rectTransform;
                _orderCards[i] = card;
            }
        }

        void RenderOrderCard(int i)
        {
            var card = _orderCards[i];
            var o = _orderShown[i];
            card.Root.gameObject.SetActive(o != null);
            if (o == null) return;
            card.Avatar.sprite = Art.Customer(o.Customer);
            card.Reward.text = "+" + o.Reward;
            card.Extra1.gameObject.SetActive(false);
            card.Extra2.gameObject.SetActive(false);
            card.Icon.gameObject.SetActive(true);
            card.Big.gameObject.SetActive(false);
            card.Icon.color = Color.white;
            var icon = card.Icon.rectTransform;
            UI.Fill(icon);
            switch (o.Kind)
            {
                case OrderKind.Flavor:
                    card.Icon.sprite = Art.Cake(o.Flavor);
                    card.Count.text = $"{o.Progress}/{o.Target}";
                    break;
                case OrderKind.AnyCakes:
                    card.Icon.sprite = Art.Cake(_engine.FirstActiveFlavor + 2);
                    card.Extra1.sprite = Art.Cake(_engine.FirstActiveFlavor);
                    card.Extra2.sprite = Art.Cake(_engine.FirstActiveFlavor + 1);
                    foreach (var (img, x) in new[] { (card.Extra1, -0.22f), (card.Extra2, 0.22f) })
                    {
                        img.gameObject.SetActive(true);
                        UI.Stretch(img.rectTransform, 0.1f + x, 0.8f + x, 0.22f, 0.92f);
                    }
                    UI.Stretch(icon, 0.12f, 0.88f, -0.06f, 0.7f);
                    card.Icon.transform.SetAsLastSibling();
                    card.Count.text = $"{o.Progress}/{o.Target}";
                    break;
                case OrderKind.Combo:
                    card.Icon.gameObject.SetActive(false);
                    card.Big.gameObject.SetActive(true);
                    card.Big.text = "x" + o.Target;
                    card.Count.text = "COMBO";
                    break;
                case OrderKind.Linked:
                    card.Icon.sprite = Art.Rounded();
                    card.Icon.color = Palette.Hex(0xFF9EB8);
                    card.Icon.preserveAspect = false;
                    UI.Stretch(icon, 0.02f, 0.98f, 0.26f, 0.74f);
                    foreach (var (img, x) in new[] { (card.Extra1, -0.22f), (card.Extra2, 0.22f) })
                    {
                        img.gameObject.SetActive(true);
                        img.sprite = Art.Plate();
                        UI.Stretch(img.rectTransform, 0.28f + x, 0.72f + x, 0.28f, 0.72f);
                        img.transform.SetAsLastSibling();
                    }
                    card.Count.text = $"{o.Progress}/{o.Target}";
                    break;
            }
            card.Icon.preserveAspect = o.Kind != OrderKind.Linked;
            float progress = o.Target > 0 ? Mathf.Clamp01(o.Progress / (float)o.Target) : 0f;
            card.Fill.rectTransform.sizeDelta = new Vector2(132f * progress, 0);
            card.Fill.enabled = progress > 0f;
        }

        void ShowAllOrders(bool animate)
        {
            for (int i = 0; i < _orderCards.Length; i++)
            {
                if (_orderLocked[i]) continue;
                _orderShown[i] = _orders.Active[i]?.Copy();
                RenderOrderCard(i);
                if (animate) StartCoroutine(PopIn(_orderCards[i].Root, i * 0.08f));
            }
        }

        IEnumerator PopIn(RectTransform rect, float delay)
        {
            rect.localScale = Vector3.zero;
            yield return Tween.Run(0.35f, t => { if (rect) rect.localScale = Vector3.one * t; }, Ease.OutBack, delay);
        }

        void RenderMetaHud()
        {
            _coinsShown = Mathf.MoveTowards(_coinsShown, _coinsGoal, Mathf.Max(30f, Mathf.Abs(_coinsGoal - _coinsShown) * 5f) * Time.deltaTime);
            _coinText.text = Mathf.RoundToInt(_coinsShown).ToString("N0");
            _levelText.text = _levelShown.ToString();
            float fill = _starsShown / (float)MetaRules.StarsForLevel(_levelShown);
            _levelRing.fillAmount = Mathf.MoveTowards(_levelRing.fillAmount, fill, Time.deltaTime * 1.5f);
            if (_levelUp.activeSelf) _levelUpRays.localRotation = Quaternion.Euler(0, 0, Time.time * 25f);
        }

        void SyncMetaHud()
        {
            _coinsGoal = _profile.Coins;
            _levelShown = _profile.Level;
            _starsShown = _profile.Stars;
        }

        // =====================================================================
        // Order progress
        // =====================================================================

        /// <summary>Applies rewards for finished orders to the profile at once; the cards catch up visually.</summary>
        void ApplyOrderProgress(List<OrderProgress> changes, Vector3 fromScreen, int flavor)
        {
            foreach (var change in changes)
            {
                if (change.Completed != null)
                {
                    _profile.Earn(change.Completed.Reward);
                    _profile.OrdersDone++;
                    var up = _profile.AddStar();
                    if (up != null) _pendingLevelUps.Enqueue(up);
                }
                StartCoroutine(OrderFlight(change, fromScreen, flavor));
            }
        }

        IEnumerator OrderFlight(OrderProgress change, Vector3 from, int flavor)
        {
            int i = change.Index;
            var source = change.Before.Kind;
            _orderAnimations++;
            _orderLocked[i] = true;
            var card = _orderCards[i];
            Sprite sprite = source == OrderKind.Linked ? Art.Plate() : source == OrderKind.Combo ? Art.Star() : Art.Cake(flavor);
            var flying = UI.Img(_fx, "ToOrder", sprite, source == OrderKind.Combo ? Palette.Gold : Color.white);
            flying.preserveAspect = true;
            UI.Place(flying.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110, 110));
            flying.rectTransform.position = from;
            var end = card.IconSpot.position;
            var control = Vector3.Lerp(from, end, 0.5f) + new Vector3(0, 160f, 0) * _root.lossyScale.y;
            yield return Tween.Run(0.5f, t =>
            {
                if (flying == null) return;
                var a = Vector3.Lerp(from, control, t);
                var b = Vector3.Lerp(control, end, t);
                flying.rectTransform.position = Vector3.Lerp(a, b, t);
                flying.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 0.55f, t);
            }, Ease.InOutSine);
            Destroy(flying.gameObject);
            _orderShown[i] = change.After;
            RenderOrderCard(i);
            Play(AudioTones.Pop(), 1.3f, 0.6f);
            StartCoroutine(Tween.Punch(card.Root, 0.18f, 0.3f));
            if (change.Completed != null) yield return CompleteOrder(change);
            _orderLocked[i] = false;
            _orderAnimations--;
        }

        IEnumerator CompleteOrder(OrderProgress change)
        {
            int i = change.Index;
            var card = _orderCards[i];
            card.Stamp.gameObject.SetActive(true);
            card.Stamp.localScale = Vector3.one * 2f;
            Play(AudioTones.Win(), 1.25f, 0.55f);
            Haptics.Play(HapticKind.Success);
            yield return Tween.Run(0.25f, t => card.Stamp.localScale = Vector3.one * Mathf.Lerp(2f, 1f, t), Ease.OutBack);
            Burst(card.Root.position, Palette.Gold, 14, 160f);
            Burst(card.Root.position, Palette.Mint, 8, 120f);
            if (_smokeDir != null && !_smokeOrderShot) { _smokeOrderShot = true; SmokeCapture("11_order_done.png"); }

            int reward = change.Completed.Reward;
            const int pieces = 6;
            for (int k = 0; k < pieces; k++)
            {
                int amount = reward / pieces + (k < reward % pieces ? 1 : 0);
                StartCoroutine(FlyIcon(Art.Coin(), card.Root.position, _coinPill, 70f, k * 0.06f, () =>
                {
                    _coinsGoal += amount;
                    Play(AudioTones.Pop(), 1.7f, 0.35f);
                    StartCoroutine(Tween.Punch(_coinPill, 0.15f, 0.2f));
                }));
            }
            yield return FlyIcon(Art.Star(), card.Root.position, _levelBadge, 90f, 0.15f, () =>
            {
                _starsShown++;
                if (_starsShown >= MetaRules.StarsForLevel(_levelShown)) { _starsShown = 0; _levelShown++; _levelRing.fillAmount = 0f; }
                Play(AudioTones.Chime(), 1.4f, 0.5f);
                StartCoroutine(Tween.Punch(_levelBadge, 0.3f, 0.35f));
            }, Palette.Gold);
            yield return new WaitForSeconds(0.15f);
            yield return Tween.Run(0.2f, t => card.Root.localScale = Vector3.one * (1f - t), Ease.InCubic);
            card.Stamp.gameObject.SetActive(false);
            _orderShown[i] = change.Replacement;
            RenderOrderCard(i);
            yield return PopIn(card.Root, 0f);
        }

        IEnumerator FlyIcon(Sprite sprite, Vector3 from, RectTransform target, float size, float delay, Action onLand, Color? tint = null)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            var icon = UI.Img(_fx, "Fly", sprite, tint ?? Color.white);
            icon.preserveAspect = true;
            UI.Place(icon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            var start = from + (Vector3)(UnityEngine.Random.insideUnitCircle * 40f * _root.lossyScale.x);
            var end = target.position;
            var control = Vector3.Lerp(start, end, 0.5f) + new Vector3(UnityEngine.Random.Range(-120f, 120f), 140f, 0) * _root.lossyScale.y;
            yield return Tween.Run(0.5f, t =>
            {
                if (icon == null) return;
                var a = Vector3.Lerp(start, control, t);
                var b = Vector3.Lerp(control, end, t);
                icon.rectTransform.position = Vector3.Lerp(a, b, t);
                icon.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 0.6f, t);
            }, Ease.InCubic);
            if (icon != null) Destroy(icon.gameObject);
            onLand?.Invoke();
        }

        /// <summary>Swaps out orders whose flavor has left the menu (after an unlock or a new run).</summary>
        void RefreshOrders(bool animate)
        {
            var changed = _orders.Refresh(_engine);
            foreach (int i in changed)
            {
                if (_orderLocked[i]) continue;
                _orderShown[i] = _orders.Active[i].Copy();
                RenderOrderCard(i);
                if (animate) StartCoroutine(PopIn(_orderCards[i].Root, 0.1f));
            }
        }

        // =====================================================================
        // Popups: level up, daily treat, shop
        // =====================================================================

        void BuildMetaOverlays()
        {
            _levelUp = Overlay("LevelUp", out var panel, 1100);
            var glow = UI.Img(panel, "Glow", Art.SoftDot(), new Color(1f, 0.93f, 0.6f, 0.9f));
            UI.Place(glow.rectTransform, new Vector2(0.5f, 0.58f), Vector2.zero, new Vector2(620, 620));
            var rays = UI.Img(panel, "Rays", Art.Sparkle(), new Color(1f, 0.82f, 0.3f, 0.55f));
            UI.Place(rays.rectTransform, new Vector2(0.5f, 0.58f), Vector2.zero, new Vector2(860, 860));
            _levelUpRays = rays.rectTransform;
            var chest = UI.Img(panel, "Chest", Art.Chest());
            chest.preserveAspect = true;
            UI.Place(chest.rectTransform, new Vector2(0.5f, 0.58f), Vector2.zero, new Vector2(420, 420));
            _levelUpChest = chest.rectTransform;
            Ribbon(panel, "LEVEL UP!", Palette.Hex(0x4FB8A0));
            _levelUpTitle = UI.Txt(panel, "Title", "Bakery level 2", 60, Palette.Ink);
            UI.Place(_levelUpTitle.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 390), new Vector2(820, 76));
            var coin = UI.Img(panel, "Coin", Art.Coin());
            coin.preserveAspect = true;
            UI.Place(coin.rectTransform, new Vector2(0.5f, 0), new Vector2(-230, 300), new Vector2(84, 84));
            _levelUpCoins = UI.Txt(panel, "Coins", "+100", 50, Palette.Ink, TextAnchor.MiddleLeft);
            UI.Place(_levelUpCoins.rectTransform, new Vector2(0.5f, 0), new Vector2(-80, 300), new Vector2(200, 70));
            _levelUpBooster = UI.Img(panel, "Booster", Art.Icon("bomb"));
            _levelUpBooster.preserveAspect = true;
            UI.Place(_levelUpBooster.rectTransform, new Vector2(0.5f, 0), new Vector2(110, 300), new Vector2(96, 96));
            var plus = UI.Txt(panel, "BoosterCount", "+1", 50, Palette.Ink, TextAnchor.MiddleLeft);
            UI.Place(plus.rectTransform, new Vector2(0.5f, 0), new Vector2(230, 300), new Vector2(120, 70));
            var collect = UI.Button(panel, "Collect", "COLLECT", Palette.Mint, 64, CloseLevelUp);
            UI.Place(collect.rect, new Vector2(0.5f, 0), new Vector2(0, 140), new Vector2(520, 160));

            _daily = Overlay("Daily", out panel, 1000);
            Ribbon(panel, "DAILY TREAT", Palette.Hex(0xF29A4A));
            for (int d = 0; d < _dailyTiles.Length; d++)
            {
                int row = d < 4 ? 0 : 1, col = d < 4 ? d : d - 4;
                float x = row == 0 ? (col - 1.5f) * 196f : (col - 1f) * 196f;
                var tile = new DailyTile { Root = UI.Rect("Day" + (d + 1), panel) };
                UI.Place(tile.Root, new Vector2(0.5f, 1), new Vector2(x, row == 0 ? -300 : -540), new Vector2(180, 220));
                tile.Frame = UI.Img(tile.Root, "Frame", Art.Rounded(), Palette.Gold);
                UI.Fill(tile.Frame.rectTransform);
                tile.Frame.rectTransform.offsetMin = new Vector2(-8, -8);
                tile.Frame.rectTransform.offsetMax = new Vector2(8, 8);
                tile.Bg = UI.Img(tile.Root, "Bg", Art.Rounded(), Palette.Cream);
                UI.Fill(tile.Bg.rectTransform);
                UI.DropShadow(tile.Bg, 4, 0.2f);
                tile.Day = UI.Txt(tile.Root, "Label", "DAY " + (d + 1), 30, Palette.Ink);
                UI.Place(tile.Day.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -28), new Vector2(170, 40));
                tile.Icon = UI.Img(tile.Root, "Icon", d == MetaRules.DailyDays - 1 ? Art.Chest() : Art.Coin());
                tile.Icon.preserveAspect = true;
                UI.Place(tile.Icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -2), new Vector2(100, 100));
                tile.Amount = UI.Txt(tile.Root, "Amount", "+" + MetaRules.DailyCoinsFor(d + 1), 34, Palette.Ink);
                UI.Place(tile.Amount.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(170, 44));
                _dailyTiles[d] = tile;
            }
            _dailyNote = UI.Txt(panel, "Note", "Come back tomorrow for a bigger treat!", 36, new Color(0.31f, 0.16f, 0.12f, 0.8f), TextAnchor.MiddleCenter, false);
            UI.Place(_dailyNote.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 300), new Vector2(820, 56));
            var claim = UI.Button(panel, "Claim", "CLAIM", Palette.Mint, 64, () => StartCoroutine(ClaimDaily()));
            UI.Place(claim.rect, new Vector2(0.5f, 0), new Vector2(0, 140), new Vector2(520, 160));

            _shop = Overlay("Shop", out panel, 1000);
            Ribbon(panel, "OUT OF BOOSTERS", Palette.Sky);
            var iconBg = UI.Img(panel, "IconBg", Art.Circle(), Palette.CreamDark);
            UI.Place(iconBg.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -320), new Vector2(260, 260));
            _shopIcon = UI.Img(iconBg.rectTransform, "Icon", Art.Icon("hammer"));
            _shopIcon.preserveAspect = true;
            UI.Stretch(_shopIcon.rectTransform, 0.1f, 0.9f, 0.1f, 0.9f);
            _shopName = UI.Txt(panel, "Name", "", 58, Palette.Ink);
            UI.Place(_shopName.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -505), new Vector2(820, 70));
            _shopInfo = UI.Txt(panel, "Info", "", 38, new Color(0.31f, 0.16f, 0.12f, 0.85f), TextAnchor.MiddleCenter, false);
            UI.Place(_shopInfo.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -565), new Vector2(820, 50));
            _shopNote = UI.Txt(panel, "Note", "Serve customer orders to earn coins!", 34, Palette.PinkDark, TextAnchor.MiddleCenter, true);
            UI.Place(_shopNote.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 385), new Vector2(820, 50));
            var buy = UI.Button(panel, "Buy", "BUY", Palette.Mint, 56, BuyFromShop);
            UI.Place(buy.rect, new Vector2(0.5f, 0), new Vector2(0, 270), new Vector2(560, 150));
            _shopPrice = buy.label;
            _shopPrice.alignment = TextAnchor.MiddleCenter;
            var buyCoin = UI.Img(buy.label.transform.parent, "Coin", Art.Coin());
            buyCoin.preserveAspect = true;
            UI.Place(buyCoin.rectTransform, new Vector2(0, 0.5f), new Vector2(80, 0), new Vector2(84, 84));
            _shopBuy = buy.rect;
            var later = UI.Button(panel, "Later", "NOT NOW", Palette.Hex(0xB59A88), 44, CloseShop);
            UI.Place(later.rect, new Vector2(0.5f, 0), new Vector2(0, 110), new Vector2(420, 120));
        }

        void BuildTitleMeta(RectTransform safe)
        {
            var coins = UI.Img(safe, "TitleCoins", Art.Rounded(), new Color(1f, 0.98f, 0.94f, 0.95f));
            UI.Place(coins.rectTransform, new Vector2(0, 1), new Vector2(190, -70), new Vector2(320, 90));
            UI.DropShadow(coins, 5, 0.25f);
            var coin = UI.Img(coins.rectTransform, "Coin", Art.Coin());
            coin.preserveAspect = true;
            UI.Place(coin.rectTransform, new Vector2(0, 0.5f), new Vector2(42, 2), new Vector2(86, 86));
            _titleCoins = UI.Txt(coins.rectTransform, "Amount", "0", 42, Palette.Ink);
            UI.Stretch(_titleCoins.rectTransform, 0.3f, 0.96f, 0, 1);

            var level = UI.Img(safe, "TitleLevel", Art.Rounded(), new Color(1f, 0.98f, 0.94f, 0.95f));
            UI.Place(level.rectTransform, new Vector2(1, 1), new Vector2(-210, -70), new Vector2(360, 90));
            UI.DropShadow(level, 5, 0.25f);
            var star = UI.Img(level.rectTransform, "Star", Art.Star(), Palette.Gold);
            UI.Place(star.rectTransform, new Vector2(0, 0.5f), new Vector2(46, 2), new Vector2(70, 70));
            _titleLevel = UI.Txt(level.rectTransform, "Level", "BAKERY LV 1", 36, Palette.Ink);
            UI.Stretch(_titleLevel.rectTransform, 0.24f, 0.97f, 0, 1);
        }

        void RenderTitleMeta()
        {
            _titleCoins.text = _profile.Coins.ToString("N0");
            _titleLevel.text = $"BAKERY LV {_profile.Level}";
        }

        IEnumerator ShowLevelUp(LevelUp up)
        {
            while (_orderAnimations > 0) yield return null;
            _overlayOpen = true;
            _levelUpTitle.text = $"Bakery level {up.Level}";
            _levelUpCoins.text = "+" + up.Coins;
            _levelUpBooster.sprite = Art.Icon(BoosterIcons[Mathf.Clamp(up.Booster, 0, 4)]);
            _levelShown = up.Level;
            Play(AudioTones.Win());
            Haptics.Play(HapticKind.Success);
            StartCoroutine(Confetti());
            yield return ShowOverlay(_levelUp);
            if (_smokeDir != null) SmokeCapture("12_levelup.png");
            StartCoroutine(Tween.Punch(_levelUpChest, 0.25f, 0.5f));
            while (_levelUp.activeSelf) yield return null;
            _overlayOpen = false;
            SyncMetaHud();
            yield return GrantReward(up.Booster);
            SaveRun();
        }

        void CloseLevelUp()
        {
            if (!_levelUp.activeSelf) return;
            Play(AudioTones.Click());
            StartCoroutine(HideOverlay(_levelUp));
        }

        IEnumerator ShowDailyIfReady()
        {
            var treat = _profile.PeekDaily(Today);
            if (treat == null) yield break;
            for (int d = 0; d < _dailyTiles.Length; d++)
            {
                var tile = _dailyTiles[d];
                int day = d + 1;
                bool claimed = day < treat.Day, today = day == treat.Day;
                tile.Frame.gameObject.SetActive(today);
                tile.Bg.color = today ? Palette.Hex(0xFFF2C2) : claimed ? Palette.Hex(0xE8DCCB) : Palette.Cream;
                tile.Icon.color = claimed ? new Color(1, 1, 1, 0.4f) : Color.white;
                tile.Amount.text = claimed ? "GOT IT" : "+" + MetaRules.DailyCoinsFor(day);
                tile.Root.localScale = Vector3.one;
            }
            _daily.SetActive(false);
            yield return ShowOverlay(_daily);
            StartCoroutine(Breathe(_dailyTiles[treat.Day - 1].Root));
        }

        IEnumerator ClaimDaily()
        {
            var treat = _profile.ClaimDaily(Today);
            if (treat == null) { StartCoroutine(HideOverlay(_daily)); yield break; }
            if (treat.Booster >= 0) _boosters[treat.Booster]++;
            Play(AudioTones.Win(), 1.1f, 0.7f);
            Haptics.Play(HapticKind.Success);
            var tile = _dailyTiles[treat.Day - 1];
            Burst(tile.Root.position, Palette.Gold, 16, 180f);
            tile.Amount.text = "GOT IT";
            SaveRun();
            PlayerPrefs.Save();
            yield return new WaitForSeconds(0.5f);
            yield return HideOverlay(_daily);
            var target = (RectTransform)_titleCoins.transform.parent;
            for (int k = 0; k < 6; k++)
                StartCoroutine(FlyIcon(Art.Coin(), tile.Root.position, target, 80f, k * 0.06f, () =>
                {
                    Play(AudioTones.Pop(), 1.7f, 0.35f);
                    RenderTitleMeta();
                    StartCoroutine(Tween.Punch(target, 0.12f, 0.2f));
                }));
            _coinsShown = _coinsGoal = _profile.Coins;
        }

        void OpenShop(int booster)
        {
            _shopBooster = booster;
            int price = MetaRules.BoosterPrice(booster);
            bool affordable = _profile.Coins >= price;
            _shopIcon.sprite = Art.Icon(BoosterIcons[booster]);
            _shopName.text = BoosterNames[booster];
            _shopInfo.text = BoosterInfo[booster];
            _shopPrice.text = "    BUY  " + price;
            _shopNote.gameObject.SetActive(!affordable);
            _shopBuy.Find("Face").GetComponent<Image>().color = affordable ? Palette.Mint : new Color(0.72f, 0.7f, 0.68f);
            _overlayOpen = true;
            Play(AudioTones.Click());
            StartCoroutine(ShowOverlay(_shop));
        }

        void BuyFromShop()
        {
            if (_shopBooster < 0 || !_shop.activeSelf) return;
            if (!_profile.Spend(MetaRules.BoosterPrice(_shopBooster)))
            {
                Play(AudioTones.Error());
                Haptics.Play(HapticKind.Error);
                StartCoroutine(Tween.Shake(_shopBuy, 10f, 0.3f));
                return;
            }
            int booster = _shopBooster;
            _boosters[booster]++;
            _coinsGoal = _profile.Coins;
            Play(AudioTones.Chime(), 1.3f);
            Haptics.Play(HapticKind.Success);
            SaveRun();
            CloseShop();
            RenderBoosters();
            Burst(_boosterUI[booster].Button.position, Palette.Gold, 12, 120f);
            StartCoroutine(Tween.Punch(_boosterUI[booster].Button, 0.35f, 0.4f));
        }

        void CloseShop()
        {
            if (!_shop.activeSelf) return;
            Play(AudioTones.Click());
            _shopBooster = -1;
            StartCoroutine(CloseShopRoutine());
        }

        IEnumerator CloseShopRoutine()
        {
            yield return HideOverlay(_shop);
            _overlayOpen = _lose.activeSelf || _unlock.activeSelf || _levelUp.activeSelf;
        }
    }
}
