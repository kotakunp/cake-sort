// Guided first game on the play screen: a speech bubble with the mascot, a looping hand that shows the drag,
// and input limited to the move being taught. The script itself lives in Core/Tutorial.cs.
using System.Collections;
using CakeSort.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CakeSort.Game
{
    public partial class GameController
    {
        const int TutorialOutroStep = 3;

        int _tutorialStep = -1;
        bool _tutorialDone;
        RectTransform _tutorialBubble, _hand, _boosterBar;
        Text _tutorialText, _tutorialTap;
        CanvasGroup _tutorialGroup, _handGroup;
        Coroutine _handLoop;

        bool TutorialActive => _tutorialStep >= 0;
        bool TutorialGuiding => _tutorialStep >= 0 && _tutorialStep < Tutorial.Moves.Length;
        Tutorial.Move TutorialMove => Tutorial.Moves[Mathf.Clamp(_tutorialStep, 0, Tutorial.Moves.Length - 1)];

        void LoadTutorial()
        {
            // Players who already have progress from before the tutorial existed skip it.
            _tutorialDone = _smokeDir == null && (PlayerPrefs.GetInt("tutorial", 0) == 1 || _profile.CakesServed > 0 || _best > 0);
        }

        void BuildTutorial()
        {
            _tutorialBubble = UI.Rect("TutorialBubble", _game);
            _tutorialBubble.anchorMin = _tutorialBubble.anchorMax = new Vector2(0.5f, 0f);
            _tutorialBubble.pivot = new Vector2(0.5f, 0f);
            _tutorialBubble.sizeDelta = new Vector2(1000, 200);
            _tutorialBubble.anchoredPosition = new Vector2(0, 22);
            _tutorialGroup = _tutorialBubble.gameObject.AddComponent<CanvasGroup>();
            _tutorialGroup.blocksRaycasts = false;
            var card = UI.Img(_tutorialBubble, "Card", Art.Rounded(), Palette.Cream);
            UI.Fill(card.rectTransform);
            card.rectTransform.offsetMin = new Vector2(150, 0);
            UI.DropShadow(card, 8, 0.35f);
            var inner = UI.Img(card.rectTransform, "Inner", Art.Rounded(), Palette.Hex(0xFFEFD8));
            UI.Stretch(inner.rectTransform, 0.015f, 0.985f, 0.05f, 0.95f);
            var mascot = UI.Img(_tutorialBubble, "Mascot", Art.MascotHappy());
            mascot.preserveAspect = true;
            UI.Place(mascot.rectTransform, new Vector2(0f, 0.5f), new Vector2(110, 20), new Vector2(250, 250));
            _tutorialText = UI.Txt(card.rectTransform, "Text", "", 40, Palette.Ink, TextAnchor.MiddleCenter, true);
            UI.Stretch(_tutorialText.rectTransform, 0.05f, 0.95f, 0.18f, 0.95f);
            _tutorialText.lineSpacing = 1.05f;
            _tutorialTap = UI.Txt(card.rectTransform, "Tap", "TAP TO PLAY", 30, Palette.PinkDark, TextAnchor.MiddleCenter, true);
            UI.Stretch(_tutorialTap.rectTransform, 0f, 1f, 0.02f, 0.24f);
            _tutorialBubble.gameObject.SetActive(false);

            var hand = UI.Img(_fx, "Hand", Art.Hand());
            hand.preserveAspect = true;
            _hand = hand.rectTransform;
            _hand.anchorMin = _hand.anchorMax = new Vector2(0.5f, 0.5f);
            _hand.sizeDelta = new Vector2(190, 190);
            // Pivot on the fingertip so the hand's position is where it "touches".
            _hand.pivot = new Vector2(0.4f, 0.97f);
            _handGroup = hand.gameObject.AddComponent<CanvasGroup>();
            _handGroup.blocksRaycasts = false;
            hand.gameObject.SetActive(false);
        }

        IEnumerator StartTutorialRun()
        {
            yield return StartNewRun(Tutorial.NewGame(_smokeDir != null ? 12345UL : (ulong)System.DateTime.Now.Ticks));
            _tutorialStep = 0;
            _boosterBar.gameObject.SetActive(false);
            _tutorialBubble.gameObject.SetActive(true);
            ShowTutorialStep();
        }

        void ShowTutorialStep()
        {
            bool outro = _tutorialStep >= TutorialOutroStep;
            _tutorialText.text = outro ? Tutorial.Outro : TutorialMove.Text;
            _tutorialTap.gameObject.SetActive(outro);
            StartCoroutine(PopBubble());
            if (_handLoop != null) StopCoroutine(_handLoop);
            _handLoop = null;
            _hand.gameObject.SetActive(false);
            if (outro)
            {
                _hintSlot = _hintAnchor = -1;
                RenderSelection();
                return;
            }
            _hintSlot = TutorialMove.Slot;
            _hintAnchor = TutorialMove.Anchor;
            RenderSelection();
            _handLoop = StartCoroutine(HandLoop(_tutorialStep));
        }

        IEnumerator PopBubble()
        {
            _tutorialGroup.alpha = 0f;
            yield return Tween.Run(0.35f, t =>
            {
                _tutorialGroup.alpha = Mathf.Clamp01(t * 2f);
                _tutorialBubble.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, t);
            }, Ease.OutBack);
        }

        /// <summary>Shows the drag again and again: press on the piece, carry it to the target, lift off.</summary>
        IEnumerator HandLoop(int step)
        {
            while (_busy) yield return null;
            _hand.gameObject.SetActive(true);
            var move = TutorialMove;
            var cells = Engine.CellsFor(_engine.Slots[move.Slot] != null ? _engine.Slots[move.Slot].Link : Link.Single, move.Anchor);
            var centre = Vector3.zero;
            foreach (int c in cells) centre += _cells[c].Position;
            centre /= cells.Length;
            while (_tutorialStep == step)
            {
                var from = Scr(SlotHome(move.Slot) + Vector3.up * 0.25f);
                var to = Scr(centre + Vector3.up * 0.25f);
                _hand.position = from;
                _hand.localScale = Vector3.one;
                yield return Tween.Run(0.2f, t => _handGroup.alpha = HandVisible ? t : 0f);
                yield return Tween.Run(0.14f, t => _hand.localScale = Vector3.one * Mathf.Lerp(1f, 0.84f, t));
                yield return Tween.Run(0.85f, t =>
                {
                    _hand.position = Vector3.Lerp(from, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 60f * _root.lossyScale.y;
                    _handGroup.alpha = HandVisible ? 1f : 0f;
                }, Ease.InOutSine);
                yield return Tween.Run(0.14f, t => _hand.localScale = Vector3.one * Mathf.Lerp(0.84f, 1f, t));
                yield return new WaitForSeconds(0.25f);
                yield return Tween.Run(0.25f, t => _handGroup.alpha = HandVisible ? 1f - t : 0f);
                yield return new WaitForSeconds(0.45f);
            }
        }

        bool HandVisible => !_dragging && !_busy && !_pause.activeSelf;

        /// <summary>During a guided step only the taught piece may move, and only onto the taught spot.</summary>
        bool TutorialAllows(int slot, int anchor = -1) =>
            !TutorialGuiding || (slot == TutorialMove.Slot && (anchor < 0 || anchor == TutorialMove.Anchor));

        void TutorialNudge()
        {
            StartCoroutine(Tween.Punch(_tutorialBubble, 0.12f, 0.3f));
        }

        void TutorialAfterTurn()
        {
            if (!TutorialActive || _tutorialStep >= TutorialOutroStep) return;
            _tutorialStep++;
            ShowTutorialStep();
        }

        /// <summary>Ends the tutorial for good (finished, skipped with NEW GAME, or the first move after the outro).</summary>
        void EndTutorial()
        {
            if (!TutorialActive) return;
            _tutorialStep = -1;
            _tutorialDone = true;
            if (_smokeDir == null) PlayerPrefs.SetInt("tutorial", 1);
            if (_handLoop != null) StopCoroutine(_handLoop);
            _handLoop = null;
            _hand.gameObject.SetActive(false);
            _hintSlot = _hintAnchor = -1;
            StartCoroutine(HideTutorialBubble());
        }

        IEnumerator HideTutorialBubble()
        {
            yield return Tween.Run(0.2f, t => _tutorialGroup.alpha = 1f - t);
            _tutorialBubble.gameObject.SetActive(false);
            _boosterBar.gameObject.SetActive(true);
            for (int i = 0; i < _boosterUI.Length; i++) StartCoroutine(PopIn(_boosterUI[i].Rect, i * 0.05f));
            RenderBoosters();
        }

        /// <summary>The scripted move during guided steps, otherwise the advisor's pick (used by the automated runs).</summary>
        bool NextBotMove(out int slot, out int anchor)
        {
            if (TutorialGuiding)
            {
                slot = TutorialMove.Slot;
                anchor = TutorialMove.Anchor;
                return _engine.CanPlace(slot, anchor);
            }
            return Advisor.BestMove(_engine, out slot, out anchor);
        }
    }
}
