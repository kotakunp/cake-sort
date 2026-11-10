// Builds and drives the whole game in code: the 2.5D board and pieces, HUD, input, event-driven
// animation, boosters, and overlays. Endless mode: serve cakes for points, unlock new flavors, and keep
// going as long as every batch of three pieces finds a place.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CakeSort.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CakeSort.Game
{
    public partial class GameController : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<GameController>() != null) return;
            new GameObject("CakeSort").AddComponent<GameController>();
        }

        const int Cells = Rules.BoardCols * Rules.BoardRows;
        const float BaseShelfZ = -4.25f, MaxExtraGap = 1.1f;
        static float ShelfZ = BaseShelfZ;
        const float ShelfScale = 0.9f;
        const float Pitch = 58f, FieldOfView = 38f;
        const float TrayHalf = 2.78f, CounterHalfX = 2.7f, CounterHalfZ = 0.95f;
        const float TableTop = -0.36f, TableThickness = 0.3f, TableHalfZ = 11f;
        const float DragLift = 0.6f;
        const float DragForward = 0.55f;
        const float TopHud = 520f, BottomHud = 215f;
        const int UILayer = 5;
        static readonly float[] SlotX = { -1.78f, 0f, 1.78f };
        static readonly string[] ComboWords = { "", "", "Sweet!", "Delicious!", "Amazing!", "Sugar Rush!" };
        static readonly string[] CakeNames =
        {
            "Cherry Bliss", "Choco Fudge", "Vanilla Cloud", "Matcha Dream",
            "Blueberry Swirl", "Strawberry Kiss", "Orange Zest", "Grape Velvet",
        };

        enum Booster { Hammer, Bomb, Whisk, Firework, Shuffle }
        static readonly int[] BoosterStart = { 2, 1, 1, 1, 2 };
        static readonly string[] BoosterIcons = { "hammer", "bomb", "whisk", "firework", "shuffle" };

        // ---------------- state ----------------
        Engine _engine;
        readonly List<int>[] _show = new List<int>[Cells];
        readonly ObstacleType[] _obsType = new ObstacleType[Cells];
        readonly int[] _obsHp = new int[Cells];
        readonly int[] _boosters = new int[5];
        Booster? _armed;
        bool _busy, _playing, _revived, _soundOn = true, _overlayOpen, _dragging;
        int _best, _maxCombo;
        float _scoreShown, _scoreGoal, _idleTimer, _bannerTimer;
        int _selectedSlot = -1, _pressSlot = -1, _dragSlot = -1, _hoverAnchor = -1;
        int _hintSlot = -1, _hintAnchor = -1;
        Vector2 _pressPos;
        Vector2Int _fittedFor;
        readonly Queue<int> _pendingUnlocks = new Queue<int>();
        string _bannerOverride;

        // ---------------- world ----------------
        Camera _cam, _bgCam;
        Transform _world, _board, _shelf, _table;
        readonly Cell[] _cells = new Cell[Cells];
        readonly PieceView[] _pieces = new PieceView[Rules.SlotCount];
        readonly MeshRenderer[] _slotGlows = new MeshRenderer[Rules.SlotCount];
        MaterialPropertyBlock _glowBlock;

        // ---------------- ui ----------------
        RectTransform _root, _safe, _game, _fx, _badges, _banner, _goalFill, _goalIcon, _scoreTarget;
        CanvasGroup _gameGroup;
        Text _scoreText, _bestText, _goalText, _bannerText, _goalMark;
        Image _goalCake;
        readonly BoosterUI[] _boosterUI = new BoosterUI[5];
        GameObject _title, _unlock, _lose, _pause;
        Text _titleBest, _playLabel, _pauseSound, _pauseHaptics, _loseScore, _loseBest, _unlockName, _reviveLabel;
        RectTransform _newGameButton, _useBoosterButton, _unlockRays;
        Image _unlockCake;
        AudioSource _sfx, _music;

        class Cell
        {
            public Vector3 Position;
            public Transform Root;
            public PlateView View;
            public ObstacleView Obstacle;
            public MeshRenderer Glow;
            public Color GlowColor;
            public Image Badge;
            public Text BadgeText;
        }

        class BoosterUI
        {
            public RectTransform Rect, Button;
            public Image Ring, Icon, CountBg;
            public Text Count;
        }

        // ---------------- smoke test ----------------
        string _smokeDir;
        bool _smokeQuick, _smokeRecord;
        float _smokeSpeed = 2f;

        void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;
            Input.multiTouchEnabled = false;
            QualitySettings.antiAliasing = 4;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-smoketest") _smokeDir = args[i + 1];
            _smokeQuick = System.Array.IndexOf(args, "-smokequick") >= 0;
            _smokeRecord = System.Array.IndexOf(args, "-smokerecord") >= 0;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-smokespeed" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float speed))
                    _smokeSpeed = Mathf.Clamp(speed, 0.25f, 4f);
            // An unfocused window would otherwise pause the player loop and stall the automated run.
            if (_smokeDir != null) Application.runInBackground = true;
        }

        void Start()
        {
            _best = PlayerPrefs.GetInt("best", 0);
            _soundOn = PlayerPrefs.GetInt("sound", 1) == 1;
            Haptics.Enabled = PlayerPrefs.GetInt("haptics", 1) == 1;
            for (int i = 0; i < Cells; i++) _show[i] = new List<int>();
            LoadBoosters();
            LoadMeta();
            LoadTutorial();
            if (_smokeDir == null)
            {
                var saved = PlayerPrefs.GetString("run", "");
                _engine = saved.Length > 0 ? Engine.Load(saved) : null;
                if (_engine != null && _engine.State != GameState.Playing) _engine = null;
                _revived = _engine != null && PlayerPrefs.GetInt("revived", 0) == 1;
            }
            if (_engine == null) { _engine = NewEngine(); _revived = false; }

            BuildWorld();
            FitCamera();
            BuildHud();
            ResetRunState();
            SyncShow();
            RenderAll();
            _orders.Refresh(_engine);
            ShowAllOrders(false);
            SyncMetaHud();
            _coinsShown = _coinsGoal;
            ShowTitle();
            if (_smokeDir != null) StartCoroutine(SmokeTest());
        }

        Engine NewEngine() => new Engine(_smokeDir != null ? 12345UL : (ulong)System.DateTime.Now.Ticks);

        void LoadBoosters()
        {
            var parts = _smokeDir == null ? PlayerPrefs.GetString("boosters", "").Split(',') : new string[0];
            for (int i = 0; i < 5; i++)
                _boosters[i] = parts.Length == 5 && int.TryParse(parts[i], out int n) ? Mathf.Max(0, n) : BoosterStart[i];
        }

        void SaveRun()
        {
            PlayerPrefs.SetString("run", _engine.Save());
            PlayerPrefs.SetInt("revived", _revived ? 1 : 0);
            PlayerPrefs.SetString("boosters", string.Join(",", _boosters));
            PlayerPrefs.SetInt("best", _best);
            SaveMeta();
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused || _engine == null) return;
            SaveRun();
            PlayerPrefs.Save();
        }

        void OnApplicationQuit()
        {
            if (_engine == null) return;
            SaveRun();
            PlayerPrefs.Save();
        }

        // =====================================================================
        // World construction
        // =====================================================================

        static Vector3 CellPosition(int index)
        {
            int row = index / Rules.BoardCols, col = index % Rules.BoardCols;
            return new Vector3(col - (Rules.BoardCols - 1) / 2f, 0f, (Rules.BoardRows - 1) / 2f - row);
        }

        static Vector3 SlotHome(int slot) => new Vector3(SlotX[slot], 0f, ShelfZ);

        void BuildWorld()
        {
            _bgCam = new GameObject("Background Camera").AddComponent<Camera>();
            _bgCam.clearFlags = CameraClearFlags.SolidColor;
            _bgCam.backgroundColor = Palette.Cream;
            _bgCam.orthographic = true;
            _bgCam.depth = -10;
            _bgCam.cullingMask = 1 << UILayer;

            _cam = new GameObject("Main Camera").AddComponent<Camera>();
            _cam.gameObject.tag = "MainCamera";
            _cam.clearFlags = CameraClearFlags.Depth;
            _cam.cullingMask = ~(1 << UILayer);
            _cam.fieldOfView = FieldOfView;
            _cam.nearClipPlane = 0.3f;
            _cam.farClipPlane = 120f;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;

            var bgCanvasGo = new GameObject("BackgroundCanvas", typeof(Canvas), typeof(CanvasScaler));
            var bgCanvas = bgCanvasGo.GetComponent<Canvas>();
            bgCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            bgCanvas.worldCamera = _bgCam;
            bgCanvas.planeDistance = 5f;
            ConfigureScaler(bgCanvasGo.GetComponent<CanvasScaler>());
            var bgRoot = (RectTransform)bgCanvasGo.transform;
            var bg = UI.Img(bgRoot, "Background", Art.Background());
            bg.rectTransform.anchorMin = bg.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            var fitter = bg.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 1080f / 1920f;
            var wash = UI.Img(bgRoot, "Wash", Art.White(), new Color(1f, 0.95f, 0.88f, 0.3f));
            UI.Fill(wash.rectTransform);
            SetLayer(bgCanvasGo.transform, UILayer);

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _music = gameObject.AddComponent<AudioSource>();
            _music.clip = AudioTones.Music();
            _music.loop = true;
            _music.volume = 0.32f;
            if (_soundOn) _music.Play();

            _glowBlock = new MaterialPropertyBlock();
            _world = new GameObject("World").transform;
            _board = Make.Node("Board", _world);
            _table = Make.Mesh("Table", _world, Meshes.Slab(16f, TableHalfZ, 0.4f, TableThickness, 0.1f), Mats.Table(32f, TableHalfZ * 2f)).transform;
            _table.localPosition = new Vector3(0, TableTop - TableThickness, 9f - TableHalfZ);
            var trayShadow = Make.Decal("TrayShadow", _board, Mats.Shadow(), 1f, TableTop + 0.008f);
            trayShadow.transform.localScale = new Vector3(7.6f, 1f, 7.6f);
            var tray = Make.Mesh("Tray", _board, Meshes.Slab(TrayHalf, TrayHalf, 0.42f, 0.36f, 0.07f), Mats.Tray());
            tray.transform.localPosition = new Vector3(0, -0.36f, 0);

            for (int i = 0; i < Cells; i++)
            {
                var cell = new Cell { Position = CellPosition(i) };
                cell.Root = Make.Node("Cell" + i, _board, cell.Position);
                cell.Glow = Make.Decal("Glow", cell.Root, Mats.Glow(), 1.32f, 0.006f);
                cell.Glow.gameObject.SetActive(false);
                cell.View = PlateView.Create(cell.Root, "Plate");
                cell.Obstacle = ObstacleView.Create(cell.Root);
                _cells[i] = cell;
            }

            _shelf = Make.Node("Shelf", _world, new Vector3(0, 0, ShelfZ));
            var shelfShadow = Make.Decal("ShelfShadow", _shelf, Mats.Shadow(), 1f, TableTop + 0.008f);
            shelfShadow.transform.localScale = new Vector3(7f, 1f, 2.9f);
            var counter = Make.Mesh("Counter", _shelf, Meshes.Slab(CounterHalfX, CounterHalfZ, 0.45f, -TableTop, 0.07f), Mats.Shelf());
            counter.transform.localPosition = new Vector3(0, TableTop, 0);
            for (int s = 0; s < Rules.SlotCount; s++)
            {
                _slotGlows[s] = Make.Decal("SlotGlow" + s, _shelf, Mats.Glow(), 1.95f, 0.006f);
                _slotGlows[s].transform.localPosition = new Vector3(SlotX[s], 0.006f, 0);
                _slotGlows[s].gameObject.SetActive(false);
            }
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayer(child, layer);
        }

        static void ConfigureScaler(CanvasScaler scaler)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = Screen.height / (float)Mathf.Max(1, Screen.width) < 1.6f ? 1f : 0f;
        }

        float HudScale => Screen.height / (float)Mathf.Max(1, Screen.width) < 1.6f ? Screen.height / 1920f : Screen.width / 1080f;

        /// <summary>
        /// Frames the tray edge to edge between the top HUD and the booster bar, with the waiting counter below it.
        /// Leftover height on tall screens widens the gap to the counter (the counter may then reach the screen
        /// edges, the tray never shrinks); whatever is still left is split evenly above and below.
        /// </summary>
        void FitCamera()
        {
            _fittedFor = new Vector2Int(Screen.width, Screen.height);
            var safe = Screen.safeArea;
            float h = Mathf.Max(1, Screen.height);
            float topPx = (Screen.height - safe.yMax) + TopHud * HudScale;
            float bottomPx = safe.yMin + BottomHud * HudScale;
            var band = new Rect(0.02f, bottomPx / h + 0.01f, 0.96f, 1f - (topPx + bottomPx) / h - 0.02f);

            _cam.fieldOfView = FieldOfView;
            _cam.transform.rotation = Quaternion.Euler(Pitch, 0f, 0f);
            var tray = TrayPoints();
            float baseDistance = FitTo(tray, CounterPoints(BaseShelfZ), band, out _);
            float lo = 0f, hi = MaxExtraGap;
            for (int i = 0; i < 16; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (FitTo(tray, CounterPoints(BaseShelfZ - mid), band, out _) <= baseDistance * 1.002f) lo = mid; else hi = mid;
            }
            ShelfZ = BaseShelfZ - lo;
            FitTo(tray, CounterPoints(ShelfZ), band, out var position);
            _cam.transform.position = position;

            if (_shelf != null) _shelf.localPosition = new Vector3(0, 0, ShelfZ);
            for (int s = 0; s < _pieces.Length; s++)
                if (_pieces[s] != null && s != _dragSlot && !_busy) _pieces[s].Root.position = SlotHome(s);

            // The cloth ends behind the score so the bakery stays visible across the top of the screen.
            float backZ = 9f;
            var ray = _cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height - topPx * 0.55f, 0f));
            if (new Plane(Vector3.up, new Vector3(0, TableTop, 0)).Raycast(ray, out float d)) backZ = Mathf.Min(ray.GetPoint(d).z, 9f);
            if (_table != null) _table.localPosition = new Vector3(0, TableTop - TableThickness, backZ - TableHalfZ);
        }

        static List<Vector3> TrayPoints() => Box(TrayHalf, TrayHalf, 0f);
        static List<Vector3> CounterPoints(float shelfZ) => Box(CounterHalfX, CounterHalfZ, shelfZ);

        static List<Vector3> Box(float halfX, float halfZ, float z)
        {
            var points = new List<Vector3>();
            foreach (float y in new[] { TableTop, 0.55f })
            foreach (var (sx, sz) in new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) })
                points.Add(new Vector3(sx * halfX, y, z + sz * halfZ));
            return points;
        }

        /// <summary>
        /// Closest camera distance (at the current rotation) where the tray fits the band's width, the counter
        /// fits the full screen width, and both fit the band's height; the view is centred in the band.
        /// </summary>
        float FitTo(List<Vector3> tray, List<Vector3> counter, Rect band, out Vector3 position)
        {
            var all = new List<Vector3>(tray);
            all.AddRange(counter);
            var forward = _cam.transform.forward;
            var focus = new Vector3(0f, 0f, -0.9f);
            float distance = 20f;
            bool Fits()
            {
                Extents(tray, out var tMin, out var tMax);
                Extents(counter, out var cMin, out var cMax);
                Extents(all, out var aMin, out var aMax);
                return tMax.x - tMin.x <= band.width && cMax.x - cMin.x <= 0.994f && aMax.y - aMin.y <= band.height;
            }
            for (int pass = 0; pass < 4; pass++)
            {
                float lo = 4f, hi = 90f;
                for (int i = 0; i < 32; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    _cam.transform.position = focus - forward * mid;
                    if (Fits()) hi = mid; else lo = mid;
                }
                distance = hi;
                _cam.transform.position = focus - forward * distance;
                Extents(all, out var mn, out var mx);
                var centre = (mn + mx) * 0.5f;
                float worldH = 2f * distance * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float worldW = worldH * _cam.aspect;
                focus += _cam.transform.up * ((centre.y - band.center.y) * worldH) + _cam.transform.right * ((centre.x - 0.5f) * worldW);
            }
            position = focus - forward * distance;
            _cam.transform.position = position;
            return distance;
        }
        void Extents(List<Vector3> points, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in points)
            {
                var v = _cam.WorldToViewportPoint(p);
                min = Vector2.Min(min, v);
                max = Vector2.Max(max, v);
            }
        }

        // =====================================================================
        // HUD construction
        // =====================================================================

        void BuildHud()
        {
            var canvasGo = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            ConfigureScaler(canvasGo.GetComponent<CanvasScaler>());
            _root = (RectTransform)canvasGo.transform;

            _safe = UI.Rect("SafeArea", _root);
            UI.Fill(_safe);
            _safe.gameObject.AddComponent<SafeArea>();
            _game = UI.Rect("Game", _safe);
            UI.Fill(_game);
            _gameGroup = _game.gameObject.AddComponent<CanvasGroup>();

            _badges = UI.Rect("Badges", _game);
            UI.Fill(_badges);
            for (int i = 0; i < Cells; i++)
            {
                var badge = UI.Img(_badges, "Badge" + i, Art.Circle(), Palette.PinkDark);
                badge.rectTransform.sizeDelta = new Vector2(62, 62);
                UI.DropShadow(badge, 3);
                var text = UI.Txt(badge.rectTransform, "N", "2", 36, Color.white, TextAnchor.MiddleCenter, true, 2f);
                UI.Fill(text.rectTransform);
                badge.gameObject.SetActive(false);
                _cells[i].Badge = badge;
                _cells[i].BadgeText = text;
            }

            BuildTopBar();
            BuildBoosters();
            BuildBanner();

            _fx = UI.Rect("FX", _root);
            UI.Fill(_fx);

            BuildTitle();
            BuildUnlock();
            BuildLose();
            BuildPause();
            BuildMetaOverlays();
            BuildTutorial();
        }

        void BuildTopBar()
        {
            var bar = UI.Rect("TopBar", _game);
            bar.anchorMin = new Vector2(0, 1);
            bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(0.5f, 1);
            bar.sizeDelta = new Vector2(0, TopHud);
            bar.anchoredPosition = Vector2.zero;

            BuildCoinPill(bar);

            var pause = UI.Img(bar, "Pause", Art.Circle(), Palette.Cream);
            UI.Place(pause.rectTransform, new Vector2(1, 1), new Vector2(-82, -66), new Vector2(104, 104));
            UI.DropShadow(pause, 6);
            pause.raycastTarget = true;
            for (int i = 0; i < 2; i++)
            {
                var barImg = UI.Img(pause.rectTransform, "Bar" + i, Art.Rounded(), Palette.Ink);
                UI.Place(barImg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(i == 0 ? -14 : 14, 0), new Vector2(18, 44));
            }
            var pauseRelay = pause.gameObject.AddComponent<PointerRelay>();
            pauseRelay.Click = _ => { if (_playing && !_busy && !_overlayOpen) OpenPause(); };

            _scoreText = UI.Txt(bar, "Score", "0", 110, Color.white, TextAnchor.MiddleCenter, true, 5f);
            UI.Place(_scoreText.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(520, 140));
            _scoreTarget = _scoreText.rectTransform;
            _bestText = UI.Txt(bar, "Best", "BEST 0", 30, Color.white, TextAnchor.MiddleCenter, true, 2f);
            UI.Place(_bestText.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -156), new Vector2(420, 40));

            var panel = UI.Img(bar, "GoalPanel", Art.Rounded(), new Color(1f, 0.98f, 0.94f, 0.95f));
            UI.Place(panel.rectTransform, new Vector2(0.5f, 1), new Vector2(40, -222), new Vector2(700, 92));
            UI.DropShadow(panel, 6, 0.25f);
            var track = UI.Img(panel.rectTransform, "Track", Art.Rounded(), Palette.Hex(0xE9D3B4));
            UI.Stretch(track.rectTransform, 0.03f, 0.9f, 0.2f, 0.8f);
            track.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var fill = UI.Img(track.rectTransform, "Fill", Art.Rounded(), Palette.Pink);
            fill.rectTransform.anchorMin = new Vector2(0, 0);
            fill.rectTransform.anchorMax = new Vector2(0, 1);
            fill.rectTransform.pivot = new Vector2(0, 0.5f);
            fill.rectTransform.sizeDelta = Vector2.zero;
            var fillShine = UI.Img(fill.rectTransform, "Shine", Art.Rounded(), new Color(1, 1, 1, 0.3f));
            UI.Stretch(fillShine.rectTransform, 0.02f, 0.98f, 0.55f, 0.9f);
            _goalFill = fill.rectTransform;
            _goalText = UI.Txt(track.rectTransform, "Goal", "NEXT CAKE", 30, Color.white, TextAnchor.MiddleCenter, true, 2.5f);
            UI.Fill(_goalText.rectTransform);

            var iconBg = UI.Img(panel.rectTransform, "GoalIcon", Art.Circle(), Palette.Cream);
            UI.Place(iconBg.rectTransform, new Vector2(1f, 0.5f), new Vector2(-6, 0), new Vector2(132, 132));
            UI.DropShadow(iconBg, 5, 0.3f);
            var ring = UI.Img(iconBg.rectTransform, "Ring", Art.Circle(), Palette.Hex(0xF2C14E));
            UI.Stretch(ring.rectTransform, 0.04f, 0.96f, 0.04f, 0.96f);
            var inner = UI.Img(iconBg.rectTransform, "Inner", Art.Circle(), Palette.Cream);
            UI.Stretch(inner.rectTransform, 0.1f, 0.9f, 0.1f, 0.9f);
            _goalCake = UI.Img(iconBg.rectTransform, "Cake", Art.Cake(4));
            UI.Stretch(_goalCake.rectTransform, 0.13f, 0.87f, 0.13f, 0.87f);
            _goalMark = UI.Txt(iconBg.rectTransform, "Mark", "?", 64, Color.white, TextAnchor.MiddleCenter, true, 3f);
            UI.Fill(_goalMark.rectTransform);
            _goalIcon = iconBg.rectTransform;
            BuildLevelBadge(bar);
            BuildOrders(bar);
        }

        void BuildBoosters()
        {
            var bar = UI.Rect("Boosters", _game);
            bar.anchorMin = new Vector2(0, 0);
            bar.anchorMax = new Vector2(1, 0);
            bar.pivot = new Vector2(0.5f, 0);
            bar.sizeDelta = new Vector2(-40, 170);
            bar.anchoredPosition = new Vector2(0, 26);
            _boosterBar = bar;

            for (int i = 0; i < 5; i++)
            {
                var ui = new BoosterUI { Rect = UI.Rect("Booster" + i, bar) };
                ui.Rect.anchorMin = new Vector2(i / 5f, 0);
                ui.Rect.anchorMax = new Vector2((i + 1) / 5f, 1);
                ui.Rect.offsetMin = ui.Rect.offsetMax = Vector2.zero;

                ui.Ring = UI.Img(ui.Rect, "Ring", Art.GlowRing(), Palette.Gold);
                UI.Place(ui.Ring.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(210, 210));
                ui.Ring.gameObject.SetActive(false);

                var button = UI.Img(ui.Rect, "Button", Art.Circle(), Palette.Cream);
                UI.Place(button.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(148, 148));
                UI.DropShadow(button, 8, 0.3f);
                button.raycastTarget = true;
                ui.Button = button.rectTransform;
                var inner = UI.Img(button.rectTransform, "Inner", Art.Circle(), Palette.CreamDark);
                UI.Stretch(inner.rectTransform, 0.07f, 0.93f, 0.07f, 0.93f);
                ui.Icon = UI.Img(button.rectTransform, "Icon", Art.Icon(BoosterIcons[i]));
                UI.Stretch(ui.Icon.rectTransform, 0.1f, 0.9f, 0.1f, 0.9f);
                ui.Icon.preserveAspect = true;
                ui.CountBg = UI.Img(button.rectTransform, "CountBg", Art.Circle(), Palette.Pink);
                UI.Place(ui.CountBg.rectTransform, new Vector2(0.86f, 0.14f), Vector2.zero, new Vector2(58, 58));
                UI.DropShadow(ui.CountBg, 3);
                ui.Count = UI.Txt(ui.CountBg.rectTransform, "Count", "3", 34, Color.white, TextAnchor.MiddleCenter, true, 2f);
                UI.Fill(ui.Count.rectTransform);

                int index = i;
                var relay = button.gameObject.AddComponent<PointerRelay>();
                relay.Down = _ => button.rectTransform.localScale = Vector3.one * 0.92f;
                relay.Up = _ => button.rectTransform.localScale = Vector3.one;
                relay.Click = _ => OnBoosterTapped((Booster)index);
                _boosterUI[i] = ui;
            }
        }

        void BuildBanner()
        {
            _banner = UI.Rect("Banner", _game);
            _banner.anchorMin = _banner.anchorMax = new Vector2(0.5f, 0);
            _banner.sizeDelta = new Vector2(820, 84);
            _banner.anchoredPosition = new Vector2(0, 240);
            var bg = UI.Img(_banner, "Bg", Art.Rounded(), new Color(0.3f, 0.15f, 0.1f, 0.85f));
            UI.Fill(bg.rectTransform);
            _bannerText = UI.Txt(_banner, "Text", "", 38, Color.white);
            UI.Fill(_bannerText.rectTransform);
            _banner.gameObject.SetActive(false);
        }

        GameObject Overlay(string name, out RectTransform panel, float panelHeight)
        {
            var overlay = UI.Rect(name, _root);
            UI.Fill(overlay);
            var scrim = UI.Img(overlay, "Scrim", Art.White(), new Color(0.18f, 0.07f, 0.04f, 0.55f));
            UI.Fill(scrim.rectTransform);
            scrim.raycastTarget = true;
            overlay.gameObject.AddComponent<CanvasGroup>();
            var safe = UI.Rect("Safe", overlay);
            UI.Fill(safe);
            safe.gameObject.AddComponent<SafeArea>();
            var card = UI.Img(safe, "Panel", Art.Rounded(), Palette.Cream);
            UI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(880, panelHeight));
            UI.DropShadow(card, 14, 0.4f);
            var inner = UI.Img(card.rectTransform, "Inner", Art.Rounded(), Palette.Hex(0xFFEFD8));
            UI.Stretch(inner.rectTransform, 0.025f, 0.975f, 0.02f, 0.98f);
            panel = card.rectTransform;
            overlay.gameObject.SetActive(false);
            return overlay.gameObject;
        }

        void Ribbon(RectTransform panel, string title, Color color)
        {
            var ribbon = UI.Img(panel, "Ribbon", Art.Rounded(), color);
            UI.Place(ribbon.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(760, 130));
            UI.DropShadow(ribbon, 8, 0.35f);
            var shine = UI.Img(ribbon.rectTransform, "Shine", Art.Rounded(), new Color(1, 1, 1, 0.22f));
            UI.Stretch(shine.rectTransform, 0.03f, 0.97f, 0.52f, 0.92f);
            var text = UI.Txt(ribbon.rectTransform, "Title", title, 62, Color.white, TextAnchor.MiddleCenter, true, 4f);
            UI.Fill(text.rectTransform);
        }

        void BuildTitle()
        {
            var title = UI.Rect("Title", _root);
            UI.Fill(title);
            title.gameObject.AddComponent<CanvasGroup>();
            var veil = UI.Img(title, "Veil", Art.White(), new Color(1f, 0.95f, 0.88f, 0.55f));
            UI.Fill(veil.rectTransform);
            veil.raycastTarget = true;
            var safe = UI.Rect("Safe", title);
            UI.Fill(safe);
            safe.gameObject.AddComponent<SafeArea>();

            var logo = UI.Img(safe, "Logo", Art.Logo());
            logo.preserveAspect = true;
            UI.Place(logo.rectTransform, new Vector2(0.5f, 0.8f), Vector2.zero, new Vector2(960, 470));
            StartCoroutine(Bob(logo.rectTransform, 14f, 2.4f));

            var mascotShadow = UI.Img(safe, "MascotShadow", Art.Shadow());
            UI.Place(mascotShadow.rectTransform, new Vector2(0.5f, 0.47f), new Vector2(0, -230), new Vector2(380, 90));
            var mascot = UI.Img(safe, "Mascot", Art.MascotHappy());
            mascot.preserveAspect = true;
            UI.Place(mascot.rectTransform, new Vector2(0.5f, 0.49f), Vector2.zero, new Vector2(540, 540));
            StartCoroutine(Bob(mascot.rectTransform, 10f, 1.7f));

            var play = UI.Button(safe, "Play", "PLAY", Palette.Pink, 84, () => StartCoroutine(BeginGame(false)));
            UI.Place(play.rect, new Vector2(0.5f, 0.22f), Vector2.zero, new Vector2(580, 186));
            _playLabel = play.label;
            StartCoroutine(Breathe(play.rect));

            var fresh = UI.Button(safe, "NewGame", "NEW GAME", Palette.Sky, 46, () => StartCoroutine(BeginGame(true)));
            UI.Place(fresh.rect, new Vector2(0.5f, 0.22f), new Vector2(0, -160), new Vector2(400, 116));
            _newGameButton = fresh.rect;

            _titleBest = UI.Txt(safe, "Best", "Best score 0", 38, new Color(0.31f, 0.16f, 0.12f, 0.85f), TextAnchor.MiddleCenter, true);
            UI.Place(_titleBest.rectTransform, new Vector2(0.5f, 0.22f), new Vector2(0, -270), new Vector2(700, 56));
            BuildTitleMeta(safe);
            _title = title.gameObject;
        }

        void BuildUnlock()
        {
            _unlock = Overlay("Unlock", out var panel, 1080);
            var glow = UI.Img(panel, "Glow", Art.SoftDot(), new Color(1f, 0.93f, 0.6f, 0.9f));
            UI.Place(glow.rectTransform, new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(640, 640));
            var rays = UI.Img(panel, "Rays", Art.Sparkle(), new Color(1f, 0.82f, 0.3f, 0.55f));
            UI.Place(rays.rectTransform, new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(860, 860));
            _unlockRays = rays.rectTransform;
            _unlockCake = UI.Img(panel, "Cake", Art.Cake(4));
            UI.Place(_unlockCake.rectTransform, new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(430, 430));
            UI.DropShadow(_unlockCake, 12, 0.35f);
            Ribbon(panel, "NEW CAKE!", Palette.Pink);
            _unlockName = UI.Txt(panel, "Name", "", 66, Palette.Ink);
            UI.Place(_unlockName.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 345), new Vector2(820, 80));
            var sub = UI.Txt(panel, "Sub", "is now on the menu!", 40, new Color(0.31f, 0.16f, 0.12f, 0.8f), TextAnchor.MiddleCenter, false);
            UI.Place(sub.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 280), new Vector2(820, 56));
            var yum = UI.Button(panel, "Yum", "YUM!", Palette.Mint, 64, CloseUnlock);
            UI.Place(yum.rect, new Vector2(0.5f, 0), new Vector2(0, 140), new Vector2(520, 160));
        }

        void BuildLose()
        {
            _lose = Overlay("Lose", out var panel, 1240);
            Ribbon(panel, "OUT OF SPACE!", Palette.Hex(0x9C7BD8));
            var mascot = UI.Img(panel, "Mascot", Art.MascotSad());
            mascot.preserveAspect = true;
            UI.Place(mascot.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -290), new Vector2(330, 330));
            _loseScore = UI.Txt(panel, "Score", "", 64, Palette.Ink);
            UI.Place(_loseScore.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -505), new Vector2(820, 80));
            _loseBest = UI.Txt(panel, "Best", "", 42, Palette.PinkDark);
            UI.Place(_loseBest.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -570), new Vector2(820, 56));

            var revive = UI.Button(panel, "Revive", "FREE REVIVE", Palette.Sky, 50, () => StartCoroutine(Revive()));
            UI.Place(revive.rect, new Vector2(0.5f, 0), new Vector2(0, 480), new Vector2(640, 140));
            _reviveLabel = revive.label;
            var boosters = UI.Button(panel, "UseBooster", "USE A BOOSTER", Palette.Mint, 50, () => StartCoroutine(ResumeForBooster()));
            UI.Place(boosters.rect, new Vector2(0.5f, 0), new Vector2(0, 320), new Vector2(640, 140));
            _useBoosterButton = boosters.rect;
            var again = UI.Button(panel, "NewGame", "NEW GAME", Palette.Pink, 50, () => StartCoroutine(NewGameFromLose()));
            UI.Place(again.rect, new Vector2(0.5f, 0), new Vector2(0, 160), new Vector2(640, 140));
        }

        void BuildPause()
        {
            _pause = Overlay("Pause", out var panel, 1030);
            Ribbon(panel, "PAUSED", Palette.Hex(0xF29A4A));
            var resume = UI.Button(panel, "Resume", "RESUME", Palette.Mint, 56, ClosePause);
            UI.Place(resume.rect, new Vector2(0.5f, 1), new Vector2(0, -245), new Vector2(600, 150));
            var sound = UI.Button(panel, "Sound", "SOUND: ON", Palette.Sky, 52, ToggleSound);
            UI.Place(sound.rect, new Vector2(0.5f, 1), new Vector2(0, -415), new Vector2(600, 150));
            _pauseSound = sound.label;
            var haptics = UI.Button(panel, "Haptics", "VIBRATION: ON", Palette.Sky, 52, ToggleHaptics);
            UI.Place(haptics.rect, new Vector2(0.5f, 1), new Vector2(0, -585), new Vector2(600, 150));
            _pauseHaptics = haptics.label;
            var restart = UI.Button(panel, "Restart", "NEW GAME", Palette.Pink, 52, () =>
            {
                ClosePause();
                StartCoroutine(StartNewRun());
            });
            UI.Place(restart.rect, new Vector2(0.5f, 1), new Vector2(0, -755), new Vector2(600, 150));
        }

        // =====================================================================
        // Screens and run flow
        // =====================================================================

        void ShowTitle()
        {
            _title.SetActive(true);
            _gameGroup.alpha = 0f;
            bool resume = _engine.Moves > 0;
            _playLabel.text = resume ? "CONTINUE" : "PLAY";
            _newGameButton.gameObject.SetActive(resume);
            _titleBest.text = _best > 0 ? $"Best score {_best:N0}" : "Sort the slices, serve the cakes!";
            RenderTitleMeta();
            StartCoroutine(ShowDailyIfReady());
        }

        IEnumerator BeginGame(bool fresh)
        {
            Play(AudioTones.Pop());
            var group = _title.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            yield return Tween.Run(0.35f, t => { group.alpha = 1f - t; _gameGroup.alpha = t; });
            _title.SetActive(false);
            group.alpha = 1f;
            group.blocksRaycasts = true;
            _playing = true;
            if (_daily.activeSelf) yield return HideOverlay(_daily);
            if (!_tutorialDone) { yield return StartTutorialRun(); yield break; }
            if (fresh) yield return StartNewRun();
            else yield return IntroBoard();
            if (_profile.OrdersDone == 0) ShowBanner("Serve customer orders to earn coins!", 4.5f);
        }

        IEnumerator StartNewRun(Engine engine = null)
        {
            Play(AudioTones.Click());
            if (engine == null) EndTutorial();
            _engine = engine ?? NewEngine();
            _revived = false;
            _paidRevives = 0;
            ResetRunState();
            SyncShow();
            foreach (var piece in _pieces) if (piece != null) Destroy(piece.Root.gameObject);
            for (int s = 0; s < _pieces.Length; s++) _pieces[s] = null;
            RenderAll();
            RefreshOrders(true);
            SaveRun();
            yield return IntroBoard();
        }

        IEnumerator IntroBoard()
        {
            _busy = true;
            for (int i = 0; i < Cells; i++)
            {
                var root = _cells[i].Root;
                root.localScale = Vector3.zero;
                int row = i / Rules.BoardCols, col = i % Rules.BoardCols;
                StartCoroutine(Tween.Run(0.35f, t => { if (root) root.localScale = Vector3.one * t; }, Ease.OutBack, (row + col) * 0.035f));
            }
            foreach (var piece in _pieces) if (piece != null) Destroy(piece.Root.gameObject);
            for (int s = 0; s < _pieces.Length; s++) _pieces[s] = null;
            yield return new WaitForSeconds(0.35f);
            yield return DealPieces();
            yield return new WaitForSeconds(0.2f);
            _busy = false;
            _idleTimer = 0f;
        }

        IEnumerator ShowOverlay(GameObject overlay)
        {
            overlay.SetActive(true);
            var group = overlay.GetComponent<CanvasGroup>();
            var panel = overlay.transform.Find("Safe/Panel");
            group.alpha = 0f;
            if (panel != null) panel.localScale = Vector3.one * 0.6f;
            yield return Tween.Run(0.4f, t =>
            {
                group.alpha = Mathf.Clamp01(t * 2f);
                if (panel != null) panel.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, t);
            }, Ease.OutBack);
        }

        IEnumerator HideOverlay(GameObject overlay)
        {
            var group = overlay.GetComponent<CanvasGroup>();
            yield return Tween.Run(0.2f, t => group.alpha = 1f - t);
            overlay.SetActive(false);
            group.alpha = 1f;
        }

        void OpenPause()
        {
            Play(AudioTones.Click());
            _pauseSound.text = _soundOn ? "SOUND: ON" : "SOUND: OFF";
            _pauseHaptics.text = Haptics.Enabled ? "VIBRATION: ON" : "VIBRATION: OFF";
            StartCoroutine(ShowOverlay(_pause));
        }

        void ClosePause()
        {
            Play(AudioTones.Click());
            StartCoroutine(HideOverlay(_pause));
        }

        void ToggleSound()
        {
            _soundOn = !_soundOn;
            PlayerPrefs.SetInt("sound", _soundOn ? 1 : 0);
            _pauseSound.text = _soundOn ? "SOUND: ON" : "SOUND: OFF";
            if (_soundOn) { _music.Play(); Play(AudioTones.Click()); }
            else _music.Stop();
        }

        void ToggleHaptics()
        {
            Haptics.Enabled = !Haptics.Enabled;
            PlayerPrefs.SetInt("haptics", Haptics.Enabled ? 1 : 0);
            _pauseHaptics.text = Haptics.Enabled ? "VIBRATION: ON" : "VIBRATION: OFF";
            Haptics.Play(HapticKind.Medium);
        }

        IEnumerator ShowUnlock(int flavor)
        {
            _overlayOpen = true;
            _unlockCake.sprite = Art.Cake(flavor);
            _unlockName.text = CakeNames[Mathf.Clamp(flavor, 0, CakeNames.Length - 1)];
            Play(AudioTones.Win());
            Haptics.Play(HapticKind.Success);
            StartCoroutine(Confetti());
            yield return ShowOverlay(_unlock);
            StartCoroutine(Tween.Punch(_unlockCake.rectTransform, 0.25f, 0.5f));
            while (_unlock.activeSelf) yield return null;
            _overlayOpen = false;
        }

        void CloseUnlock()
        {
            if (!_unlock.activeSelf) return;
            Play(AudioTones.Click());
            StartCoroutine(HideOverlay(_unlock));
        }

        IEnumerator ShowLose()
        {
            yield return new WaitForSeconds(0.45f);
            Play(AudioTones.Lose());
            Haptics.Play(HapticKind.Error);
            _loseScore.text = $"Score {_engine.Score:N0}";
            _loseBest.text = _engine.Score >= _best && _engine.Score > 0 ? "NEW BEST!" : $"Best {_best:N0}";
            int cost = MetaRules.ReviveCost(_paidRevives);
            bool canRevive = !_revived || _profile.Coins >= cost;
            _reviveLabel.text = _revived ? $"REVIVE  {cost} COINS" : "FREE REVIVE";
            _reviveLabel.transform.parent.GetComponent<Image>().color = canRevive ? Palette.Sky : new Color(0.7f, 0.7f, 0.7f);
            _reviveLabel.transform.parent.parent.gameObject.SetActive(!_revived || _paidRevives < MetaRules.MaxPaidRevives);
            _useBoosterButton.gameObject.SetActive(_boosters.Sum() > 0);
            _overlayOpen = true;
            yield return ShowOverlay(_lose);
        }

        IEnumerator Revive()
        {
            if (_engine.State != GameState.Lost) yield break;
            if (_revived)
            {
                if (_paidRevives >= MetaRules.MaxPaidRevives) yield break;
                if (!_profile.Spend(MetaRules.ReviveCost(_paidRevives)))
                {
                    Play(AudioTones.Error());
                    StartCoroutine(Tween.Shake((RectTransform)_reviveLabel.transform.parent.parent, 10f, 0.3f));
                    yield break;
                }
                _paidRevives++;
                _coinsGoal = _profile.Coins;
            }
            _engine.Revive();
            _revived = true;
            yield return HideOverlay(_lose);
            _overlayOpen = false;
            Play(AudioTones.Boom());
            Haptics.Play(HapticKind.Heavy);
            for (int r = 1; r <= 3; r++)
            for (int c = 1; c <= 3; c++)
            {
                int i = r * Rules.BoardCols + c;
                if (_show[i].Count > 0) Burst(Scr(_cells[i].Position), Palette.Flavor(_show[i][0]), 8, 160f);
            }
            StartCoroutine(Tween.Shake3D(_board, 0.12f, 0.3f));
            SyncShow();
            RenderAll();
            SaveRun();
        }

        IEnumerator ResumeForBooster()
        {
            if (!_engine.ResumeForBooster()) yield break;
            Play(AudioTones.Click());
            yield return HideOverlay(_lose);
            _overlayOpen = false;
            ShowBanner("Use a booster to make room!", 3f);
        }

        IEnumerator NewGameFromLose()
        {
            yield return HideOverlay(_lose);
            _overlayOpen = false;
            yield return StartNewRun();
        }

        void ResetRunState()
        {
            _armed = null;
            _selectedSlot = -1;
            _maxCombo = 0;
            _pendingUnlocks.Clear();
            _scoreShown = _scoreGoal = _engine.Score;
            ClearHint();
        }

        void ShowBanner(string text, float seconds)
        {
            _bannerOverride = text;
            _bannerTimer = seconds;
            RenderBoosters();
        }

        // =====================================================================
        // Input
        // =====================================================================

        bool CanAct => _playing && !_busy && !_overlayOpen && _engine.State == GameState.Playing && !_pause.activeSelf;

        Vector3 Scr(Vector3 world)
        {
            var p = _cam.WorldToScreenPoint(world);
            p.z = 0f;
            return p;
        }

        bool RayToPlane(Vector2 screen, float height, out Vector3 hit)
        {
            var ray = _cam.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, new Vector3(0, height, 0));
            hit = default;
            if (!plane.Raycast(ray, out float distance)) return false;
            hit = ray.GetPoint(distance);
            return true;
        }

        static int CellAt(float x, float z)
        {
            int col = Mathf.RoundToInt(x + (Rules.BoardCols - 1) / 2f);
            int row = Mathf.RoundToInt((Rules.BoardRows - 1) / 2f - z);
            if (col < 0 || col >= Rules.BoardCols || row < 0 || row >= Rules.BoardRows) return -1;
            return row * Rules.BoardCols + col;
        }

        int CellAtScreen(Vector2 screen) => RayToPlane(screen, 0.1f, out var hit) ? CellAt(hit.x, hit.z) : -1;

        int SlotAtScreen(Vector2 screen)
        {
            if (!RayToPlane(screen, 0.05f, out var hit) || Mathf.Abs(hit.z - ShelfZ) > 1.15f) return -1;
            for (int s = 0; s < SlotX.Length; s++) if (Mathf.Abs(hit.x - SlotX[s]) < 0.9f) return s;
            return -1;
        }

        static bool OverUI()
        {
            var es = EventSystem.current;
            if (es == null) return false;
            if (Input.touchCount > 0) return es.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
            return es.IsPointerOverGameObject();
        }

        void HandleInput()
        {
            if (!CanAct)
            {
                if (_dragging) CancelDrag();
                _pressSlot = -1;
                return;
            }
            Vector2 pos = Input.mousePosition;
            if (Input.GetMouseButtonDown(0) && !OverUI())
            {
                if (_tutorialStep >= TutorialOutroStep) EndTutorial();
                ClearHint();
                int slot = SlotAtScreen(pos);
                if (slot >= 0 && !TutorialAllows(slot)) { TutorialNudge(); slot = -1; }
                if (slot >= 0 && _engine.Slots[slot] != null && _pieces[slot] != null && !_armed.HasValue)
                {
                    _pressSlot = slot;
                    _pressPos = pos;
                }
                else
                {
                    int cell = CellAtScreen(pos);
                    if (cell >= 0) OnCellTapped(cell);
                    else if (_selectedSlot >= 0) { _selectedSlot = -1; RenderSelection(); }
                }
            }
            if (_pressSlot >= 0 && Input.GetMouseButton(0))
            {
                if (!_dragging && (pos - _pressPos).magnitude > Mathf.Max(12f, Screen.height * 0.012f)) BeginDrag(_pressSlot);
                if (_dragging) DragTo(pos);
            }
            if (Input.GetMouseButtonUp(0) && _pressSlot >= 0)
            {
                if (_dragging) EndDrag();
                else OnSlotTapped(_pressSlot);
                _pressSlot = -1;
            }
        }

        void BeginDrag(int slot)
        {
            if (_tutorialStep >= TutorialOutroStep) EndTutorial();
            _dragging = true;
            _dragSlot = slot;
            _selectedSlot = -1;
            _armed = null;
            _hoverAnchor = -1;
            ClearHint();
            RenderBoosters();
            RenderSelection();
            Play(AudioTones.Pop(), 1.2f);
            Haptics.Play(HapticKind.Light);
            Haptics.Prepare();
        }

        void DragTo(Vector2 screen)
        {
            var view = _pieces[_dragSlot];
            if (view == null || !RayToPlane(screen, 0f, out var hit)) return;
            var target = hit + new Vector3(0f, DragLift, DragForward);
            if (_hoverAnchor >= 0)
            {
                // Gentle magnet towards the cells it would land on, so the drop target feels sticky.
                var cells = Engine.CellsFor(_engine.Slots[_dragSlot].Link, _hoverAnchor);
                var centre = Vector3.zero;
                foreach (int c in cells) centre += _cells[c].Position;
                centre = centre / cells.Length + Vector3.up * DragLift;
                target = Vector3.Lerp(target, centre, 0.3f);
            }
            var previous = view.Root.position;
            view.Root.position = Vector3.Lerp(previous, target, 1f - Mathf.Exp(-24f * Time.deltaTime));
            float overBoard = Mathf.InverseLerp(ShelfZ + 0.6f, -2.4f, view.Root.position.z);
            view.Root.localScale = Vector3.one * Mathf.Lerp(ShelfScale, 1f, overBoard);
            // Lean into the motion like a tray carried by hand, easing back upright when it stops.
            var velocity = (view.Root.position - previous) / Mathf.Max(Time.deltaTime, 0.001f);
            var lean = Quaternion.Euler(Mathf.Clamp(velocity.z * 2.2f, -16f, 16f), 0f, Mathf.Clamp(-velocity.x * 2.2f, -16f, 16f));
            view.Root.rotation = Quaternion.Slerp(view.Root.rotation, lean, 1f - Mathf.Exp(-10f * Time.deltaTime));

            var first = view.FirstPlatePosition;
            int anchor = CellAt(first.x, first.z - DragForward * 0f);
            int hover = anchor >= 0 && _engine.CanPlace(_dragSlot, anchor) && TutorialAllows(_dragSlot, anchor) ? anchor : -1;
            if (hover == _hoverAnchor) return;
            _hoverAnchor = hover;
            if (hover >= 0) { Play(AudioTones.Click(), 1.4f, 0.5f); Haptics.Play(HapticKind.Selection); }
            RenderHighlights();
        }

        void EndDrag()
        {
            int slot = _dragSlot, anchor = _hoverAnchor;
            _dragging = false;
            _dragSlot = -1;
            _hoverAnchor = -1;
            RenderHighlights();
            if (anchor >= 0 && _engine.CanPlace(slot, anchor)) StartCoroutine(PlaceRoutine(slot, anchor));
            else
            {
                StartCoroutine(ReturnPiece(slot, true));
                if (TutorialGuiding) TutorialNudge();
            }
        }

        void CancelDrag()
        {
            int slot = _dragSlot;
            _dragging = false;
            _dragSlot = -1;
            _hoverAnchor = -1;
            RenderHighlights();
            if (slot >= 0) StartCoroutine(ReturnPiece(slot, false));
        }

        IEnumerator ReturnPiece(int slot, bool error)
        {
            var view = _pieces[slot];
            if (view == null) yield break;
            if (error) { Play(AudioTones.Error()); Haptics.Play(HapticKind.Warning); }
            var start = view.Root.position;
            var startRotation = view.Root.rotation;
            float startScale = view.Root.localScale.x;
            var home = SlotHome(slot);
            yield return Tween.Run(0.26f, t =>
            {
                if (view.Root == null) return;
                view.Root.position = Vector3.LerpUnclamped(start, home, t);
                view.Root.rotation = Quaternion.Slerp(startRotation, Quaternion.identity, Mathf.Clamp01(t));
                view.Root.localScale = Vector3.one * Mathf.LerpUnclamped(startScale, ShelfScale, t);
            }, Ease.OutBack);
        }

        void OnSlotTapped(int slot)
        {
            if (!CanAct || _engine.Slots[slot] == null) return;
            if (!TutorialAllows(slot)) { TutorialNudge(); return; }
            if (_tutorialStep >= TutorialOutroStep) EndTutorial();
            _armed = null;
            _selectedSlot = _selectedSlot == slot ? -1 : slot;
            Play(AudioTones.Click());
            Haptics.Play(HapticKind.Selection);
            RenderBoosters();
            RenderSelection();
        }

        int AnchorCovering(int slot, int cell)
        {
            var piece = _engine.Slots[slot];
            if (piece == null) return -1;
            var (dRow, dCol) = Piece.Offset(piece.Link);
            var candidates = new List<int> { cell };
            if (piece.IsLinked) candidates.Add(cell - dRow * Rules.BoardCols - dCol);
            foreach (int anchor in candidates)
            {
                var covered = Engine.CellsFor(piece.Link, anchor);
                if (covered != null && covered.Contains(cell) && _engine.CanPlace(slot, anchor)) return anchor;
            }
            return -1;
        }

        void OnCellTapped(int cell)
        {
            if (!CanAct) return;
            if (_armed.HasValue)
            {
                StartCoroutine(UseTargetedBooster(_armed.Value, cell));
                return;
            }
            if (_selectedSlot < 0)
            {
                StartCoroutine(Tween.Punch3D(_cells[cell].View.CakeRoot, 0.12f, 0.25f));
                return;
            }
            int anchor = AnchorCovering(_selectedSlot, cell);
            if (anchor >= 0 && !TutorialAllows(_selectedSlot, anchor)) { anchor = -1; TutorialNudge(); }
            if (anchor >= 0)
            {
                int slot = _selectedSlot;
                _selectedSlot = -1;
                StartCoroutine(PlaceRoutine(slot, anchor));
            }
            else
            {
                Play(AudioTones.Error());
                Haptics.Play(HapticKind.Error);
                StartCoroutine(Tween.Shake3D(_cells[cell].Root, 0.06f, 0.25f));
            }
        }

        void OnBoosterTapped(Booster booster)
        {
            if (!CanAct || TutorialActive) return;
            ClearHint();
            int index = (int)booster;
            if (_boosters[index] <= 0)
            {
                _armed = null;
                OpenShop(index);
                RenderBoosters();
                RenderHighlights();
                return;
            }
            Play(AudioTones.Click());
            _selectedSlot = -1;
            switch (booster)
            {
                case Booster.Firework:
                    _armed = null;
                    StartCoroutine(UseFirework());
                    break;
                case Booster.Shuffle:
                    _armed = null;
                    StartCoroutine(UseShuffle());
                    break;
                default:
                    _armed = _armed == booster ? (Booster?)null : booster;
                    break;
            }
            RenderSelection();
            RenderBoosters();
            RenderHighlights();
        }

        // =====================================================================
        // Event playback
        // =====================================================================

        IEnumerator PlaceRoutine(int slot, int anchor)
        {
            _busy = true;
            ClearHint();
            var view = _pieces[slot];
            var piece = _engine.Slots[slot];
            var events = _engine.Place(slot, anchor);
            if (events.Count == 0 || view == null)
            {
                _busy = false;
                if (view != null) StartCoroutine(ReturnPiece(slot, true));
                yield break;
            }
            _pieces[slot] = null;
            RenderSelection();

            var cells = Engine.CellsFor(piece.Link, anchor);
            var target = Vector3.zero;
            foreach (int c in cells) target += _cells[c].Position;
            target /= cells.Length;
            // Linked plates ride on the platter; sink it into the tray so the plates land exactly at cell height.
            if (piece.IsLinked) target += Vector3.down * PieceView.PlatterHeight;
            var linkedOrders = _orders.OnPlaced(_engine, piece);
            var start = view.Root.position;
            var startRotation = view.Root.rotation;
            float startScale = view.Root.localScale.x;
            float arc = start.y > 0.2f ? 0f : 0.45f;
            Play(AudioTones.Whoosh(), 1.1f, 0.6f);
            yield return Tween.Run(arc > 0f ? 0.24f : 0.13f, t =>
            {
                view.Root.position = Vector3.Lerp(start, target, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * arc;
                view.Root.rotation = Quaternion.Slerp(startRotation, Quaternion.identity, t);
                view.Root.localScale = Vector3.one * Mathf.Lerp(startScale, 1f, t);
                view.UpdateShadows(0f);
            }, arc > 0f ? Ease.InOutSine : Ease.InCubic);
            if (view.Platter != null)
            {
                // The platter stays behind for a moment and melts into sparkles once its plates are on the tray.
                var platter = view.Platter;
                platter.SetParent(_world, true);
                StartCoroutine(MeltPlatter(platter));
                Burst(Scr(platter.position), Palette.Pink, 10, 130f);
                Play(AudioTones.Pop(), 1.45f, 0.6f);
            }
            Destroy(view.Root.gameObject);
            ApplyOrderProgress(linkedOrders, Scr(target), 0);

            yield return PlayEvents(events);
            FinishTurn();
        }

        IEnumerator MeltPlatter(Transform platter)
        {
            var baseScale = platter.localScale;
            var basePosition = platter.position;
            yield return Tween.Run(0.26f, t =>
            {
                if (platter == null) return;
                platter.localScale = new Vector3(baseScale.x * (1f - t * 0.4f), baseScale.y * (1f - t), baseScale.z * (1f - t * 0.4f));
                platter.position = basePosition + Vector3.down * 0.06f * t;
            }, Ease.InCubic);
            if (platter != null) Destroy(platter.gameObject);
        }

        IEnumerator PlayEvents(List<GameEvent> events)
        {
            for (int e = 0; e < events.Count; e++)
            {
                switch (events[e])
                {
                    case PlacedEvent placed:
                        _show[placed.Plate].Clear();
                        _show[placed.Plate].AddRange(placed.Cake.Slices);
                        RenderCell(placed.Plate);
                        Play(AudioTones.Thud(), 1f + placed.Part * 0.1f);
                        Haptics.Play(HapticKind.Medium);
                        Dust(Scr(_cells[placed.Plate].Position));
                        StartCoroutine(Tween.Squash3D(_cells[placed.Plate].View.Root, Vector3.one, 0.2f, 0.42f));
                        if (e + 1 >= events.Count || !(events[e + 1] is PlacedEvent)) yield return new WaitForSeconds(0.16f);
                        break;

                    case MergedEvent merge:
                        yield return AnimateMerge(merge);
                        break;

                    case ServedEvent served:
                        yield return AnimateServe(served);
                        break;

                    case BoxDamagedEvent damaged:
                        _obsHp[damaged.Plate] = damaged.Hp;
                        Play(AudioTones.Thud(), 1.3f);
                        Haptics.Play(HapticKind.Rigid);
                        RenderCell(damaged.Plate);
                        Burst(Scr(_cells[damaged.Plate].Position + Vector3.up * 0.5f), Palette.Hex(0xF4A6B8), 8, 120f);
                        StartCoroutine(Tween.Punch3D(_cells[damaged.Plate].Obstacle.Box, 0.25f, 0.3f));
                        yield return new WaitForSeconds(0.12f);
                        break;

                    case HolderTickEvent tick:
                        _obsHp[tick.Plate] = tick.Hp;
                        RenderCell(tick.Plate);
                        StartCoroutine(Tween.Punch(_cells[tick.Plate].Badge.rectTransform, 0.4f, 0.3f));
                        StartCoroutine(Tween.Punch3D(_cells[tick.Plate].Obstacle.Dome, 0.12f, 0.3f));
                        break;

                    case ObstacleClearedEvent cleared:
                        yield return AnimateObstacleCleared(cleared.Plate, cleared.Type);
                        break;

                    case ObstacleSpawnedEvent spawned:
                        yield return AnimateSpawn(spawned);
                        break;

                    case BatchDealtEvent _:
                        yield return DealPieces();
                        break;

                    case FlavorUnlockedEvent unlocked:
                        _pendingUnlocks.Enqueue(unlocked.Flavor);
                        break;

                    case RewardEvent reward:
                        StartCoroutine(GrantReward(reward.Booster));
                        break;

                    case MilestoneEvent _:
                        Play(AudioTones.Win(), 1.2f, 0.6f);
                        Popup("BONUS!", Scr(Vector3.up * 0.5f), 110);
                        StartCoroutine(Confetti());
                        break;
                }
            }
        }

        static List<int> IndicesOf(List<int> list, int flavor)
        {
            var result = new List<int>();
            for (int i = 0; i < list.Count; i++) if (list[i] == flavor) result.Add(i);
            return result;
        }

        IEnumerator AnimateMerge(MergedEvent merge)
        {
            var from = _cells[merge.From].View;
            var to = _cells[merge.To].View;

            // Departure poses of the wedges that leave each plate, taken before the views change.
            var startIdx = IndicesOf(from.Shown, merge.Flavor);
            startIdx = startIdx.Skip(Mathf.Max(0, startIdx.Count - merge.Count)).ToList();
            var starts = startIdx.Select(i => (from.WedgePosition(i), from.WedgeRotation(i))).ToList();
            var gives = new List<(Vector3, Quaternion)>();
            var taken = new HashSet<int>();
            foreach (int g in merge.GiveBack)
            {
                int idx = Enumerable.Range(0, to.Shown.Count).LastOrDefault(i => to.Shown[i] == g && !taken.Contains(i));
                taken.Add(idx);
                gives.Add((to.WedgePosition(idx), to.WedgeRotation(idx)));
            }

            for (int k = 0; k < merge.Count; k++) _show[merge.From].Remove(merge.Flavor);
            foreach (int g in merge.GiveBack) _show[merge.To].Remove(g);
            for (int k = 0; k < merge.Count; k++) _show[merge.To].Add(merge.Flavor);
            _show[merge.From].AddRange(merge.GiveBack);

            // Final layouts, with the arriving wedges reserved hidden until their flight lands.
            var toAfter = PlateView.GroupByFlavor(_show[merge.To]);
            var toArrivals = IndicesOf(toAfter, merge.Flavor);
            toArrivals = toArrivals.Skip(Mathf.Max(0, toArrivals.Count - merge.Count)).ToList();
            var fromAfter = PlateView.GroupByFlavor(_show[merge.From]);
            var fromArrivals = new List<int>();
            foreach (int g in merge.GiveBack)
                fromArrivals.Add(Enumerable.Range(0, fromAfter.Count).Last(i => fromAfter[i] == g && !fromArrivals.Contains(i)));
            from.SetSlices(_show[merge.From], fromArrivals);
            to.SetSlices(_show[merge.To], toArrivals);

            const float duration = 0.34f, stagger = 0.06f;
            var flights = new List<IEnumerator>();
            for (int k = 0; k < merge.Count; k++)
                flights.Add(FlyWedge(merge.Flavor, starts[Mathf.Min(k, starts.Count - 1)], to, toArrivals[Mathf.Min(k, toArrivals.Count - 1)], duration, k * stagger));
            for (int k = 0; k < merge.GiveBack.Count; k++)
                flights.Add(FlyWedge(merge.GiveBack[k], gives[k], from, fromArrivals[k], duration, 0.08f + k * stagger));
            foreach (var flight in flights) StartCoroutine(flight);
            Play(AudioTones.Whoosh(), 1.3f, 0.6f);
            float longest = Mathf.Max((merge.Count - 1) * stagger, merge.GiveBack.Count > 0 ? 0.08f + (merge.GiveBack.Count - 1) * stagger : 0f);
            yield return new WaitForSeconds(duration + longest);
            Play(AudioTones.Pop(), 1f + merge.Count * 0.06f);
            Haptics.Play(HapticKind.Soft);
            yield return new WaitForSeconds(0.06f);
        }

        /// <summary>
        /// Flies one wedge in a hop from its old pose to its resting place on <paramref name="dest"/>, then shows the
        /// reserved wedge there and squashes the cake. The flying copy exists from the first frame, so nothing blinks.
        /// </summary>
        IEnumerator FlyWedge(int flavor, (Vector3 position, Quaternion rotation) start, PlateView dest, int index, float duration, float delay)
        {
            var wedge = PlateView.MakeWedge(_world, flavor).transform;
            wedge.position = start.position;
            wedge.rotation = start.rotation;
            if (delay > 0f) yield return new WaitForSeconds(delay);
            float distance = Vector3.Distance(start.position, dest.RestPosition(index));
            float height = 0.35f + 0.3f * distance;
            yield return Tween.Run(duration, t =>
            {
                if (wedge == null) return;
                var end = dest.RestPosition(index);
                float hop = Mathf.Sin(t * Mathf.PI);
                wedge.position = Vector3.Lerp(start.position, end, t) + Vector3.up * hop * height;
                wedge.rotation = Quaternion.Slerp(start.rotation, dest.RestRotation(index), t) * Quaternion.Euler(-hop * 24f, 0, 0);
                wedge.localScale = new Vector3(1f + hop * 0.08f, 1f + hop * 0.14f, 1f + hop * 0.08f);
            }, Ease.InOutSine);
            if (wedge != null) Destroy(wedge.gameObject);
            dest.Reveal(index);
            StartCoroutine(Tween.Squash3D(dest.CakeRoot, Vector3.one, 0.12f, 0.32f));
        }
        IEnumerator AnimateServe(ServedEvent served)
        {
            var cell = _cells[served.Plate];
            var cake = cell.View.CakeRoot;
            _maxCombo = Mathf.Max(_maxCombo, served.Combo);
            Play(AudioTones.Chime(), 1f + (served.Combo - 1) * 0.12f);
            var rest = new Vector3(0, PlateView.PlateTop, 0);

            // Anticipation: the finished cake sinks and widens a touch before it springs up.
            yield return Tween.Run(0.09f, t =>
            {
                cake.localScale = new Vector3(1f + 0.09f * t, 1f - 0.16f * t, 1f + 0.09f * t);
                cell.View.SetFlash(t * 0.25f);
            }, Ease.OutCubic);
            StartCoroutine(ShockRing(cell.Root, Palette.Flavor(served.Flavor)));
            yield return Tween.Run(0.3f, t =>
            {
                cake.localPosition = rest + Vector3.up * 0.55f * t;
                cake.localRotation = Quaternion.Euler(0, 200f * t, 0);
                float y = Mathf.LerpUnclamped(0.84f, 1.1f, t), xz = Mathf.LerpUnclamped(1.09f, 1.06f, t);
                cake.localScale = new Vector3(xz, y, xz);
                cell.View.SetFlash(0.25f + 0.25f * t);
            }, Ease.OutBack);

            var screen = Scr(cell.Position + Vector3.up * 0.75f);
            Popup(served.Combo > 1 ? $"+{served.Points}  x{served.Combo}" : $"+{served.Points}", screen, 58);
            if (served.Combo >= 2) StartCoroutine(ComboCallout(served.Combo));
            int coins = MetaRules.CoinsForServe(served.Combo);
            _profile.Earn(coins);
            _profile.CakesServed++;
            _coinsGoal += coins;
            ApplyOrderProgress(_orders.OnServed(_engine, served.Flavor, served.Combo), screen, served.Flavor);

            // Poof: the cake keeps spinning as it shrinks away; sparkles carry the points up to the score.
            yield return Tween.Run(0.16f, t =>
            {
                cake.localRotation = Quaternion.Euler(0, 200f + 160f * t, 0);
                cake.localScale = new Vector3(1.06f, 1.1f, 1.06f) * Mathf.Lerp(1f, 0f, t);
                cell.View.SetFlash(0.5f + 0.5f * t);
            }, Ease.InCubic);
            Burst(screen, Palette.Flavor(served.Flavor), 16, 220f);
            Burst(screen, Palette.Gold, 8, 170f);
            Haptics.Play(served.Combo >= 2 ? HapticKind.Heavy : HapticKind.Rigid);
            _show[served.Plate].Clear();
            RenderCell(served.Plate);
            cake.localPosition = rest;
            cake.localRotation = Quaternion.identity;
            cake.localScale = Vector3.one;
            cell.View.SetFlash(0f);
            StartCoroutine(Tween.Squash3D(cell.View.Root, Vector3.one, 0.1f, 0.3f));
            StartCoroutine(SparklesToScore(screen, served.Points));
            yield return new WaitForSeconds(0.05f);
        }

        /// <summary>An expanding, fading ring on the table under a served cake.</summary>
        IEnumerator ShockRing(Transform cell, Color color)
        {
            var ring = Make.Decal("ShockRing", cell, Mats.Glow(), 1.2f, 0.01f);
            var block = new MaterialPropertyBlock();
            color = Color.Lerp(color, Color.white, 0.45f);
            yield return Tween.Run(0.45f, t =>
            {
                if (ring == null) return;
                float size = Mathf.Lerp(1.1f, 2.3f, t);
                ring.transform.localScale = new Vector3(size, 1f, size);
                color.a = (1f - t) * 0.95f;
                block.SetColor("_Color", color);
                ring.SetPropertyBlock(block);
            }, Ease.OutCubic);
            if (ring != null) Destroy(ring.gameObject);
        }

        /// <summary>A short stream of gold stars curving up into the score; the first arrival bumps the counter.</summary>
        IEnumerator SparklesToScore(Vector3 start, int points)
        {
            const int count = 5;
            var end = _scoreTarget.position;
            float scale = _root.lossyScale.x;
            for (int i = 0; i < count; i++)
            {
                var star = UI.Img(_fx, "ScoreStar", Art.Sparkle(), Color.Lerp(Palette.Gold, Color.white, 0.25f));
                UI.Place(star.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(66, 66));
                var from = start + (Vector3)(Random.insideUnitCircle * 40f * scale);
                star.rectTransform.position = from;
                var control = Vector3.Lerp(from, end, 0.45f) + new Vector3(Random.Range(-220f, 220f), 140f, 0) * scale;
                bool first = i == 0;
                StartCoroutine(Tween.Run(0.5f, t =>
                {
                    if (star == null) return;
                    star.rectTransform.position = Vector3.Lerp(Vector3.Lerp(from, control, t), Vector3.Lerp(control, end, t), t);
                    star.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.15f, 0.45f, t);
                    star.rectTransform.localRotation = Quaternion.Euler(0, 0, 360f * t);
                }, Ease.InCubic));
                StartCoroutine(ArriveAtScore(star, 0.5f, first ? points : 0));
                yield return new WaitForSeconds(0.04f);
            }
        }

        IEnumerator ArriveAtScore(Image star, float delay, int points)
        {
            yield return new WaitForSeconds(delay);
            if (star != null) Destroy(star.gameObject);
            if (points <= 0) yield break;
            _scoreGoal = Mathf.Min(_scoreGoal + points, _engine.Score);
            Play(AudioTones.Pop(), 1.5f, 0.5f);
            Burst(_scoreTarget.position, Palette.Gold, 8, 110f);
            StartCoroutine(Tween.Punch(_scoreTarget, 0.3f, 0.35f));
        }
        IEnumerator ComboCallout(int combo)
        {
            string word = ComboWords[Mathf.Clamp(combo, 0, ComboWords.Length - 1)];
            var text = UI.Txt(_fx, "Combo", word, 120, Palette.Gold, TextAnchor.MiddleCenter, true, 6f);
            text.rectTransform.position = Scr(new Vector3(0, 0.5f, 0.6f));
            text.rectTransform.sizeDelta = new Vector2(1000, 200);
            text.rectTransform.localRotation = Quaternion.Euler(0, 0, Random.Range(-8f, 8f));
            StartCoroutine(Tween.Shake3D(_board, 0.03f + combo * 0.02f, 0.3f));
            Haptics.Play(HapticKind.Success);
            yield return Tween.Run(0.35f, t => { if (text) text.rectTransform.localScale = Vector3.one * t; }, Ease.OutElastic);
            yield return new WaitForSeconds(0.45f);
            var basePos = text.rectTransform.position;
            yield return Tween.Run(0.3f, t =>
            {
                if (text == null) return;
                text.rectTransform.position = basePos + Vector3.up * 80f * t * _root.lossyScale.y;
                var c = text.color;
                c.a = 1f - t;
                text.color = c;
            }, Ease.InCubic);
            if (text != null) Destroy(text.gameObject);
        }

        IEnumerator AnimateObstacleCleared(int plate, ObstacleType type)
        {
            var cell = _cells[plate];
            var o = cell.Obstacle;
            var screen = Scr(cell.Position + Vector3.up * 0.4f);
            Play(AudioTones.Boom(), 1.4f, 0.7f);
            Haptics.Play(HapticKind.Heavy);
            switch (type)
            {
                case ObstacleType.Slime:
                    Burst(screen, Palette.Hex(0x9BE15D), 14, 170f);
                    yield return Tween.Run(0.25f, t => o.Slime.localScale = new Vector3(0.95f, 1f, 0.95f) * (1f - t), Ease.InCubic);
                    break;
                case ObstacleType.CakeBox:
                    Burst(screen, Palette.Hex(0xF4A6B8), 14, 190f);
                    Burst(screen, Palette.Hex(0xD9343F), 8, 150f);
                    var lidStart = o.BoxLid.localPosition;
                    yield return Tween.Run(0.32f, t =>
                    {
                        o.BoxLid.localPosition = lidStart + new Vector3(0.25f * t, 1.1f * t, 0);
                        o.BoxLid.localRotation = Quaternion.Euler(-40f * t, 0, -70f * t);
                        o.Box.localScale = Vector3.one * (1f - t);
                    }, Ease.OutCubic);
                    break;
                case ObstacleType.Holder:
                    Burst(screen, Palette.Gold, 12, 170f);
                    Burst(screen, Color.white, 8, 140f);
                    yield return Tween.Run(0.32f, t =>
                    {
                        o.Dome.localPosition = new Vector3(0, 0.04f + 0.9f * t, 0);
                        o.Cloche.localScale = Vector3.one * (1f - t * t);
                    }, Ease.OutCubic);
                    break;
            }
            _obsType[plate] = ObstacleType.None;
            RenderCell(plate);
            StartCoroutine(Tween.Punch3D(cell.View.Root, 0.2f, 0.35f));
            yield return new WaitForSeconds(0.08f);
        }

        IEnumerator AnimateSpawn(ObstacleSpawnedEvent spawned)
        {
            _obsType[spawned.Plate] = spawned.Type;
            _obsHp[spawned.Plate] = spawned.Hp;
            RenderCell(spawned.Plate);
            var cell = _cells[spawned.Plate];
            var o = cell.Obstacle;
            var node = spawned.Type == ObstacleType.CakeBox ? o.Box : spawned.Type == ObstacleType.Holder ? o.Cloche : o.Slime;
            var home = node.localPosition;
            var scale = node.localScale;
            Play(AudioTones.Whoosh(), 0.8f, 0.7f);
            if (spawned.Type == ObstacleType.Slime)
                yield return Tween.Run(0.35f, t => node.localScale = scale * t, Ease.OutBack);
            else
                yield return Tween.Run(0.38f, t => node.localPosition = home + Vector3.up * (1f - t) * 3f, Ease.InCubic);
            node.localPosition = home;
            node.localScale = scale;
            Play(AudioTones.Thud(), 0.9f);
            Haptics.Play(HapticKind.Medium);
            StartCoroutine(Tween.Shake3D(_board, 0.07f, 0.25f));
            StartCoroutine(Tween.Punch3D(node, 0.2f, 0.35f));
            Burst(Scr(cell.Position), Color.white, 10, 140f);
            ShowBanner(spawned.Type == ObstacleType.CakeBox ? "A gift box! Serve cakes next to it to open it"
                : spawned.Type == ObstacleType.Holder ? "A cloche! Serve two cakes anywhere to lift it"
                : "Slime! Slices here are stuck until you serve a cake on it", 3.5f);
            yield return new WaitForSeconds(0.2f);
        }

        IEnumerator DealPieces()
        {
            bool any = false;
            for (int s = 0; s < Rules.SlotCount; s++)
            {
                var piece = _engine.Slots[s];
                if (piece == null || (_pieces[s] != null && _pieces[s].Piece == piece)) continue;
                if (_pieces[s] != null) Destroy(_pieces[s].Root.gameObject);
                var view = PieceView.Create(_world, piece);
                view.Root.localScale = Vector3.one * ShelfScale;
                var home = SlotHome(s);
                var start = home + new Vector3(7f, 0, 0);
                view.Root.position = start;
                _pieces[s] = view;
                any = true;
                StartCoroutine(DealOne(view, start, home, s * 0.09f));
                StartCoroutine(PlayDelayed(AudioTones.Pop(), 0.9f + s * 0.12f, s * 0.09f + 0.2f));
            }
            if (any) yield return new WaitForSeconds(0.5f);
        }

        /// <summary>Slides a new piece onto the counter, leaning back as it brakes, then settling with a small squash.</summary>
        IEnumerator DealOne(PieceView view, Vector3 start, Vector3 home, float delay)
        {
            yield return Tween.Run(0.46f, t =>
            {
                if (view.Root == null) return;
                view.Root.position = Vector3.LerpUnclamped(start, home, t);
                float brake = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                view.Root.rotation = Quaternion.Euler(0, 0, -14f * brake * (1f - Mathf.Clamp01(t) * 0.5f));
            }, Ease.OutBack, delay);
            if (view.Root == null) yield break;
            view.Root.rotation = Quaternion.identity;
            yield return Tween.Squash3D(view.Root, Vector3.one * ShelfScale, 0.12f, 0.3f);
        }

        IEnumerator PlayDelayed(AudioClip clip, float pitch, float delay)
        {
            yield return new WaitForSeconds(delay);
            Play(clip, pitch, 0.6f);
        }

        IEnumerator GrantReward(int booster)
        {
            booster = Mathf.Clamp(booster, 0, 4);
            var icon = UI.Img(_fx, "Reward", Art.Icon(BoosterIcons[booster]));
            icon.preserveAspect = true;
            UI.Place(icon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200, 200));
            var start = new Vector3(Screen.width * 0.5f, Screen.height * 0.55f, 0);
            var end = _boosterUI[booster].Button.position;
            icon.rectTransform.position = start;
            icon.rectTransform.localScale = Vector3.zero;
            yield return Tween.Run(0.3f, t => icon.rectTransform.localScale = Vector3.one * t, Ease.OutBack);
            yield return new WaitForSeconds(0.25f);
            Play(AudioTones.Whoosh(), 1.2f, 0.6f);
            yield return Tween.Run(0.45f, t =>
            {
                icon.rectTransform.position = Vector3.Lerp(start, end, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 120f * _root.lossyScale.y;
                icon.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 0.6f, t);
            }, Ease.InCubic);
            Destroy(icon.gameObject);
            _boosters[booster]++;
            RenderBoosters();
            Play(AudioTones.Chime(), 1.5f, 0.6f);
            Haptics.Play(HapticKind.Light);
            Burst(end, Palette.Gold, 10, 110f);
            StartCoroutine(Tween.Punch(_boosterUI[booster].Button, 0.35f, 0.4f));
        }

        void FinishTurn()
        {
            _engine.RefreshLost();
            _scoreGoal = _engine.Score;
            SyncShow();
            RenderAll();
            if (_engine.Score > _best) _best = _engine.Score;
            RefreshOrders(true);
            SaveRun();
            StartCoroutine(AfterTurn());
        }

        IEnumerator AfterTurn()
        {
            while (_pendingUnlocks.Count > 0) yield return ShowUnlock(_pendingUnlocks.Dequeue());
            while (_pendingLevelUps.Count > 0) yield return ShowLevelUp(_pendingLevelUps.Dequeue());
            _busy = false;
            _idleTimer = 0f;
            TutorialAfterTurn();
            if (_engine.State == GameState.Lost) yield return ShowLose();
        }

        // =====================================================================
        // Boosters
        // =====================================================================

        IEnumerator UseTargetedBooster(Booster booster, int plate)
        {
            int index = (int)booster;
            var cellPos = Scr(_cells[plate].Position + Vector3.up * 0.25f);
            switch (booster)
            {
                case Booster.Hammer:
                {
                    var obstacle = _engine.Plates[plate].Obstacle.Type;
                    if (!_engine.Hammer(plate)) { RejectBooster(plate); yield break; }
                    _busy = true;
                    _boosters[index]--;
                    _armed = null;
                    RenderBoosters();
                    RenderHighlights();
                    yield return SwingIcon("hammer", cellPos);
                    Play(AudioTones.Thud(), 0.8f);
                    Haptics.Play(HapticKind.Heavy);
                    StartCoroutine(Tween.Shake3D(_board, 0.09f, 0.25f));
                    Color color = obstacle == ObstacleType.CakeBox ? Palette.Hex(0xF4A6B8) :
                        obstacle == ObstacleType.Holder ? Palette.Gold :
                        _show[plate].Count > 0 ? Palette.Flavor(_show[plate][0]) : Palette.Cream;
                    Burst(cellPos, color, 14, 200f);
                    break;
                }
                case Booster.Bomb:
                {
                    var before = _engine.Plates.Select(p => new List<int>(p.Slices)).ToArray();
                    if (!_engine.Bomb(plate)) { RejectBooster(plate); yield break; }
                    _busy = true;
                    _boosters[index]--;
                    _armed = null;
                    RenderBoosters();
                    RenderHighlights();
                    yield return SwingIcon("bomb", cellPos);
                    Play(AudioTones.Boom());
                    Haptics.Play(HapticKind.Heavy);
                    StartCoroutine(Tween.Shake3D(_board, 0.16f, 0.4f));
                    var flash = UI.Img(_fx, "Flash", Art.SoftDot(), new Color(1f, 0.95f, 0.7f, 0.9f));
                    UI.Place(flash.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 700));
                    flash.rectTransform.position = cellPos;
                    StartCoroutine(FadeAndDestroy(flash, 0.35f));
                    for (int i = 0; i < Cells; i++)
                        if (before[i].Count > 0 && _engine.Plates[i].Slices.Count == 0)
                            Burst(Scr(_cells[i].Position), Palette.Flavor(before[i][0]), 10, 180f);
                    break;
                }
                case Booster.Whisk:
                {
                    var plateState = _engine.Plates[plate];
                    if (plateState.IsEmpty || plateState.Blocked) { RejectBooster(plate); yield break; }
                    int majority = Engine.MajorityFlavor(plateState);
                    int size = plateState.Slices.Count;
                    var events = _engine.Whisk(plate);
                    if (events == null) { RejectBooster(plate); yield break; }
                    _busy = true;
                    _boosters[index]--;
                    _armed = null;
                    RenderBoosters();
                    RenderHighlights();
                    yield return SpinIcon("whisk", cellPos);
                    _show[plate].Clear();
                    for (int k = 0; k < size; k++) _show[plate].Add(majority);
                    RenderCell(plate);
                    Play(AudioTones.Chime(), 1.4f, 0.6f);
                    Haptics.Play(HapticKind.Medium);
                    Burst(cellPos, Palette.Flavor(majority), 12, 150f);
                    StartCoroutine(Tween.Punch3D(_cells[plate].View.CakeRoot, 0.25f, 0.4f));
                    yield return new WaitForSeconds(0.2f);
                    yield return PlayEvents(events);
                    break;
                }
            }
            FinishTurn();
        }

        void RejectBooster(int plate)
        {
            Play(AudioTones.Error());
            Haptics.Play(HapticKind.Error);
            StartCoroutine(Tween.Shake3D(_cells[plate].Root, 0.06f, 0.25f));
        }

        IEnumerator UseFirework()
        {
            var before = _engine.Plates.Select(p => new List<int>(p.Slices)).ToArray();
            int target = _engine.Firework();
            if (target < 0) { Play(AudioTones.Error()); yield break; }
            _busy = true;
            _boosters[(int)Booster.Firework]--;
            RenderBoosters();

            var rocket = UI.Img(_fx, "Rocket", Art.Icon("firework"));
            rocket.preserveAspect = true;
            UI.Place(rocket.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150, 150));
            var start = _boosterUI[(int)Booster.Firework].Button.position;
            var end = Scr(_cells[target].Position + Vector3.up * 0.25f);
            rocket.rectTransform.position = start;
            Play(AudioTones.Whoosh(), 0.8f);
            var dir = end - start;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 45f;
            var control = Vector3.Lerp(start, end, 0.5f) + Vector3.up * 300f * _root.lossyScale.y;
            yield return Tween.Run(0.5f, t =>
            {
                var a = Vector3.Lerp(start, control, t);
                var b = Vector3.Lerp(control, end, t);
                rocket.rectTransform.position = Vector3.Lerp(a, b, t);
                rocket.rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
                if (Random.value < 0.6f) Trail(rocket.rectTransform.position);
            }, Ease.InOutSine);
            Destroy(rocket.gameObject);
            Play(AudioTones.Boom(), 1.2f);
            Haptics.Play(HapticKind.Heavy);
            Burst(end, Palette.Gold, 18, 240f);
            if (before[target].Count > 0) Burst(end, Palette.Flavor(before[target][0]), 12, 200f);
            StartCoroutine(Tween.Shake3D(_board, 0.1f, 0.3f));
            FinishTurn();
        }

        IEnumerator UseShuffle()
        {
            if (_engine.Slots.All(s => s == null)) { Play(AudioTones.Error()); yield break; }
            _busy = true;
            _boosters[(int)Booster.Shuffle]--;
            RenderBoosters();
            Play(AudioTones.Whoosh());
            var views = _pieces.Where(v => v != null).ToList();
            yield return Tween.Run(0.2f, t =>
            {
                foreach (var v in views)
                {
                    v.Root.localScale = Vector3.one * ShelfScale * (1f - t);
                    v.Root.localRotation = Quaternion.Euler(0, 180f * t, 0);
                }
            }, Ease.InCubic);
            foreach (var v in views) Destroy(v.Root.gameObject);
            for (int s = 0; s < _pieces.Length; s++) _pieces[s] = null;
            _engine.Shuffle();
            Haptics.Play(HapticKind.Light);
            yield return DealPieces();
            Play(AudioTones.Pop(), 1.2f);
            FinishTurn();
        }

        IEnumerator SwingIcon(string icon, Vector3 target)
        {
            var image = UI.Img(_fx, "Swing", Art.Icon(icon));
            image.preserveAspect = true;
            UI.Place(image.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240, 240));
            var offset = new Vector3(90f, 120f, 0) * _root.lossyScale.x;
            image.rectTransform.position = target + offset;
            image.rectTransform.localScale = Vector3.zero;
            yield return Tween.Run(0.18f, t => image.rectTransform.localScale = Vector3.one * t, Ease.OutBack);
            yield return Tween.Run(0.14f, t => image.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-30f, 50f, t)), Ease.InCubic);
            yield return Tween.Run(0.1f, t => image.rectTransform.position = Vector3.Lerp(target + offset, target + offset * 0.4f, t));
            StartCoroutine(FadeAndDestroy(image, 0.2f));
        }

        IEnumerator SpinIcon(string icon, Vector3 target)
        {
            var image = UI.Img(_fx, "Spin", Art.Icon(icon));
            image.preserveAspect = true;
            UI.Place(image.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220, 220));
            image.rectTransform.position = target;
            Play(AudioTones.Whoosh(), 1.4f);
            yield return Tween.Run(0.55f, t =>
            {
                image.rectTransform.localScale = Vector3.one * Mathf.Sin(t * Mathf.PI) * 1.1f;
                image.rectTransform.localRotation = Quaternion.Euler(0, 0, -720f * t);
                if (Random.value < 0.4f) Sparkle(target, Palette.Gold, 90f);
            }, Ease.InOutSine);
            Destroy(image.gameObject);
        }

        // =====================================================================
        // Effects (screen-space particles over the 3D scene)
        // =====================================================================

        void Burst(Vector3 position, Color color, int count, float radius)
        {
            for (int i = 0; i < count; i++) Sparkle(position, color, radius);
        }

        void Sparkle(Vector3 position, Color color, float radius)
        {
            bool star = Random.value < 0.5f;
            var p = UI.Img(_fx, "P", star ? Art.Sparkle() : Art.Circle(), Color.Lerp(color, Color.white, Random.Range(0f, 0.35f)));
            float size = star ? Random.Range(34f, 64f) : Random.Range(14f, 28f);
            UI.Place(p.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            p.rectTransform.position = position;
            var dir = Random.insideUnitCircle.normalized;
            float dist = Random.Range(0.4f, 1f) * radius * _root.lossyScale.x;
            float life = Random.Range(0.35f, 0.6f);
            float spin = Random.Range(-360f, 360f);
            StartCoroutine(Tween.Run(life, t =>
            {
                if (p == null) return;
                p.rectTransform.position = position + (Vector3)(dir * dist * t) + Vector3.down * 60f * t * t * _root.lossyScale.y;
                p.rectTransform.localScale = Vector3.one * (1f - t * 0.7f);
                p.rectTransform.localRotation = Quaternion.Euler(0, 0, spin * t);
                var c = p.color;
                c.a = 1f - t * t;
                p.color = c;
            }, Ease.OutCubic));
            Destroy(p.gameObject, life + 0.05f);
        }

        void Dust(Vector3 position)
        {
            for (int i = 0; i < 6; i++)
            {
                var d = UI.Img(_fx, "Dust", Art.SoftDot(), new Color(1f, 0.97f, 0.9f, 0.85f));
                float size = Random.Range(40f, 70f);
                UI.Place(d.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
                float angle = i / 6f * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
                var dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) * 0.5f, 0);
                var start = position + dir * 60f * _root.lossyScale.x;
                var end = position + dir * 130f * _root.lossyScale.x;
                StartCoroutine(Tween.Run(0.35f, t =>
                {
                    if (d == null) return;
                    d.rectTransform.position = Vector3.Lerp(start, end, t);
                    var c = d.color;
                    c.a = 0.85f * (1f - t);
                    d.color = c;
                }));
                Destroy(d.gameObject, 0.4f);
            }
        }

        void Trail(Vector3 position)
        {
            var d = UI.Img(_fx, "Trail", Art.SoftDot(), new Color(1f, 0.85f, 0.4f, 0.9f));
            UI.Place(d.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46, 46));
            d.rectTransform.position = position;
            StartCoroutine(FadeAndDestroy(d, 0.3f));
        }

        void Popup(string content, Vector3 position, int size)
        {
            var text = UI.Txt(_fx, "Popup", content, size, Color.white, TextAnchor.MiddleCenter, true, 3.5f);
            text.rectTransform.sizeDelta = new Vector2(600, 120);
            text.rectTransform.position = position;
            StartCoroutine(Tween.Run(0.9f, t =>
            {
                if (text == null) return;
                text.rectTransform.position = position + Vector3.up * (60f + 110f * t) * _root.lossyScale.y;
                text.rectTransform.localScale = Vector3.one * (t < 0.15f ? Ease.OutBack(t / 0.15f) : 1f);
                var c = text.color;
                c.a = t < 0.65f ? 1f : 1f - (t - 0.65f) / 0.35f;
                text.color = c;
            }, Ease.Linear));
            Destroy(text.gameObject, 0.95f);
        }

        IEnumerator FadeAndDestroy(Graphic graphic, float duration)
        {
            var start = graphic.color;
            yield return Tween.Run(duration, t =>
            {
                if (graphic == null) return;
                var c = start;
                c.a = start.a * (1f - t);
                graphic.color = c;
            });
            if (graphic != null) Destroy(graphic.gameObject);
        }

        IEnumerator Confetti()
        {
            Color[] colors = { Palette.Pink, Palette.Gold, Palette.Mint, Palette.Sky, Palette.Hex(0xAB47BC), Color.white };
            float width = _root.rect.width, height = _root.rect.height;
            for (int i = 0; i < 90; i++)
            {
                var piece = UI.Img(_root, "Confetti", Art.White(), colors[i % colors.Length]);
                piece.rectTransform.anchorMin = piece.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                piece.rectTransform.sizeDelta = new Vector2(Random.Range(16f, 26f), Random.Range(26f, 42f));
                var start = new Vector2(Random.Range(-width / 2f, width / 2f), height / 2f + Random.Range(20f, 400f));
                float fall = height + 500f;
                float sway = Random.Range(30f, 90f);
                float life = Random.Range(2.2f, 3.4f);
                float spin = Random.Range(-720f, 720f);
                float phase = Random.Range(0f, 6f);
                StartCoroutine(Tween.Run(life, t =>
                {
                    if (piece == null) return;
                    piece.rectTransform.anchoredPosition = start + new Vector2(Mathf.Sin(phase + t * 9f) * sway, -fall * t);
                    piece.rectTransform.localRotation = Quaternion.Euler(0, 0, spin * t);
                    piece.rectTransform.localScale = new Vector3(Mathf.Cos(phase + t * 14f), 1f, 1f);
                }, Ease.Linear));
                Destroy(piece.gameObject, life + 0.1f);
                if (i % 10 == 0) yield return null;
            }
        }

        IEnumerator Bob(RectTransform rect, float amplitude, float period)
        {
            var origin = rect.anchoredPosition;
            float phase = Random.Range(0f, 6f);
            while (rect != null)
            {
                rect.anchoredPosition = origin + Vector2.up * Mathf.Sin(Time.time * Mathf.PI * 2f / period + phase) * amplitude;
                yield return null;
            }
        }

        IEnumerator Breathe(RectTransform rect)
        {
            while (rect != null)
            {
                if (rect.localScale.x > 0.95f)
                    rect.localScale = Vector3.one * (1f + Mathf.Sin(Time.time * 4f) * 0.035f);
                yield return null;
            }
        }

        void Play(AudioClip clip, float pitch = 1f, float volume = 1f)
        {
            if (!_soundOn || clip == null) return;
            _sfx.pitch = pitch;
            _sfx.PlayOneShot(clip, volume);
        }

        // =====================================================================
        // Rendering
        // =====================================================================

        void SyncShow()
        {
            for (int i = 0; i < Cells; i++)
            {
                _show[i].Clear();
                _show[i].AddRange(_engine.Plates[i].Slices);
                _obsType[i] = _engine.Plates[i].Obstacle.Type;
                _obsHp[i] = _engine.Plates[i].Obstacle.Hp;
            }
        }

        void RenderAll()
        {
            for (int i = 0; i < Cells; i++) RenderCell(i);
            RenderPieces();
            RenderBoosters();
            RenderSelection();
        }

        void RenderCell(int i)
        {
            var cell = _cells[i];
            var type = _obsType[i];
            var o = cell.Obstacle;
            cell.View.Root.gameObject.SetActive(type != ObstacleType.CakeBox);
            cell.View.SetSlices(type == ObstacleType.CakeBox ? null : _show[i]);

            o.Box.gameObject.SetActive(type == ObstacleType.CakeBox);
            if (type == ObstacleType.CakeBox)
            {
                o.Box.localScale = Vector3.one;
                o.Box.localPosition = Vector3.zero;
                bool cracked = _obsHp[i] <= 1;
                o.BoxLid.localPosition = new Vector3(0, cracked ? 0.47f : 0.42f, 0);
                o.BoxLid.localRotation = cracked ? Quaternion.Euler(-10f, 0, 9f) : Quaternion.identity;
            }
            o.Cloche.gameObject.SetActive(type == ObstacleType.Holder);
            if (type == ObstacleType.Holder)
            {
                o.Cloche.localScale = Vector3.one;
                o.Cloche.localPosition = Vector3.zero;
                o.Dome.localPosition = new Vector3(0, 0.04f, 0);
            }
            o.Slime.gameObject.SetActive(type == ObstacleType.Slime);
            if (type == ObstacleType.Slime)
            {
                float y = _show[i].Count > 0 ? PlateView.PlateTop + PlateView.WedgeHeight + 0.012f : PlateView.PlateTop + 0.01f;
                o.Slime.localPosition = new Vector3(0, y, 0);
                o.Slime.localScale = new Vector3(0.95f, 1f, 0.95f);
            }

            bool badge = type == ObstacleType.CakeBox || type == ObstacleType.Holder;
            cell.Badge.gameObject.SetActive(badge);
            if (badge)
            {
                cell.Badge.color = type == ObstacleType.CakeBox ? Palette.PinkDark : Palette.Hex(0xC8962E);
                cell.BadgeText.text = _obsHp[i].ToString();
            }
        }

        void RenderPieces()
        {
            for (int s = 0; s < Rules.SlotCount; s++)
            {
                var piece = _engine.Slots[s];
                var view = _pieces[s];
                if (piece == null)
                {
                    if (view != null) Destroy(view.Root.gameObject);
                    _pieces[s] = null;
                    continue;
                }
                if (view == null || view.Piece != piece)
                {
                    if (view != null) Destroy(view.Root.gameObject);
                    view = PieceView.Create(_world, piece);
                    _pieces[s] = view;
                }
                if (s != _dragSlot)
                {
                    view.Root.position = SlotHome(s) + Vector3.up * (s == _selectedSlot ? 0.22f : 0f);
                    view.Root.localScale = Vector3.one * ShelfScale;
                    view.Root.localRotation = Quaternion.identity;
                }
            }
        }

        void RenderSelection()
        {
            for (int s = 0; s < Rules.SlotCount; s++)
            {
                var view = _pieces[s];
                bool lifted = s == _selectedSlot;
                if (view != null && s != _dragSlot)
                {
                    var target = SlotHome(s) + Vector3.up * (lifted ? 0.22f : 0f);
                    var rootRef = view.Root;
                    var from = rootRef.position;
                    StartCoroutine(Tween.Run(0.15f, t => { if (rootRef) rootRef.position = Vector3.Lerp(from, target, t); }));
                }
                _slotGlows[s].gameObject.SetActive(s != _dragSlot && (s == _selectedSlot || s == _hintSlot));
            }
            RenderHighlights();
        }

        void RenderBoosters()
        {
            for (int i = 0; i < 5; i++)
            {
                var ui = _boosterUI[i];
                int count = _boosters[i];
                ui.Count.text = count.ToString();
                ui.CountBg.color = count > 0 ? Palette.Pink : new Color(0.6f, 0.55f, 0.5f);
                ui.Icon.color = count > 0 ? Color.white : new Color(1, 1, 1, 0.45f);
                ui.Ring.gameObject.SetActive(_armed.HasValue && (int)_armed.Value == i);
            }
            string message = _armed == Booster.Hammer ? "Tap a cake, box or cloche to smash it"
                : _armed == Booster.Bomb ? "Tap a plate to blast a 3x3 area"
                : _armed == Booster.Whisk ? "Tap a cake to whisk it into one flavor"
                : _bannerTimer > 0f ? _bannerOverride : null;
            _banner.gameObject.SetActive(message != null);
            if (message != null) _bannerText.text = message;
        }

        void RenderHighlights()
        {
            var glow = new Color[Cells];
            if (_selectedSlot >= 0 && _engine.Slots[_selectedSlot] != null)
            {
                var piece = _engine.Slots[_selectedSlot];
                for (int a = 0; a < Cells; a++)
                    if (_engine.CanPlace(_selectedSlot, a))
                        foreach (int c in Engine.CellsFor(piece.Link, a)) glow[c] = Palette.Glow;
            }
            if (_armed.HasValue)
                for (int i = 0; i < Cells; i++)
                    if (TargetableBy(_armed.Value, i)) glow[i] = Palette.Glow;
            if (_hintSlot >= 0 && _hintAnchor >= 0 && _engine.Slots[_hintSlot] != null)
                foreach (int c in Engine.CellsFor(_engine.Slots[_hintSlot].Link, _hintAnchor)) glow[c] = Palette.Glow;
            if (_hoverAnchor >= 0 && _dragSlot >= 0 && _engine.Slots[_dragSlot] != null)
                foreach (int c in Engine.CellsFor(_engine.Slots[_dragSlot].Link, _hoverAnchor)) glow[c] = Color.white;
            for (int i = 0; i < Cells; i++)
            {
                _cells[i].GlowColor = glow[i];
                _cells[i].Glow.gameObject.SetActive(glow[i].a > 0f);
            }
        }

        bool TargetableBy(Booster booster, int i)
        {
            var plate = _engine.Plates[i];
            switch (booster)
            {
                case Booster.Hammer: return !plate.IsEmpty || plate.Obstacle.Type == ObstacleType.CakeBox || plate.Obstacle.Type == ObstacleType.Holder;
                case Booster.Bomb: return true;
                case Booster.Whisk: return !plate.IsEmpty && !plate.Blocked;
                default: return false;
            }
        }

        // =====================================================================
        // Per-frame: score count-up, goal bar, glows, input, idle hint
        // =====================================================================

        void Update()
        {
            if (_engine == null) return;
            if (_fittedFor.x != Screen.width || _fittedFor.y != Screen.height) FitCamera();

            _scoreShown = Mathf.MoveTowards(_scoreShown, _scoreGoal, Mathf.Max(60f, Mathf.Abs(_scoreGoal - _scoreShown) * 6f) * Time.deltaTime);
            int shown = Mathf.RoundToInt(_scoreShown);
            _scoreText.text = shown.ToString("N0");
            _bestText.text = $"BEST {Mathf.Max(_best, shown):N0}";
            int previous = _engine.PreviousGoal, next = _engine.NextGoal;
            float progress = Mathf.Clamp01((_scoreShown - previous) / Mathf.Max(1f, next - previous));
            var track = (RectTransform)_goalFill.parent;
            _goalFill.sizeDelta = new Vector2(track.rect.width * progress, 0);
            int flavor = _engine.NextUnlockFlavor;
            _goalText.text = flavor >= 0 ? $"NEXT CAKE  {shown:N0} / {next:N0}" : $"BONUS GIFT  {shown:N0} / {next:N0}";
            _goalCake.sprite = flavor >= 0 ? Art.Cake(flavor) : Art.Box();
            _goalCake.color = flavor >= 0 ? new Color(0.36f, 0.25f, 0.22f, 1f) : Color.white;
            _goalMark.text = flavor >= 0 ? "?" : "";
            RenderMetaHud();

            float pulse = 0.6f + Mathf.Sin(Time.time * 6f) * 0.35f;
            foreach (var cell in _cells)
            {
                if (!cell.Glow.gameObject.activeSelf) continue;
                var c = cell.GlowColor;
                c.a = pulse;
                _glowBlock.SetColor("_Color", c);
                cell.Glow.SetPropertyBlock(_glowBlock);
            }
            foreach (var g in _slotGlows)
            {
                if (!g.gameObject.activeSelf) continue;
                _glowBlock.SetColor("_Color", new Color(Palette.Glow.r, Palette.Glow.g, Palette.Glow.b, pulse));
                g.SetPropertyBlock(_glowBlock);
            }
            foreach (var ui in _boosterUI)
                if (ui.Ring.gameObject.activeSelf) ui.Ring.rectTransform.localScale = Vector3.one * (1f + Mathf.Sin(Time.time * 8f) * 0.06f);
            if (_unlock.activeSelf) _unlockRays.localRotation = Quaternion.Euler(0, 0, Time.time * 25f);

            if (_bannerTimer > 0f)
            {
                _bannerTimer -= Time.deltaTime;
                if (_bannerTimer <= 0f) RenderBoosters();
            }

            HandleInput();

            if (CanAct && !_dragging && _selectedSlot < 0 && !_armed.HasValue && _hintSlot < 0 && _smokeDir == null)
            {
                _idleTimer += Time.deltaTime;
                if (_idleTimer > 7f) ShowHint();
            }
            if (_hintSlot >= 0 && _hintSlot != _dragSlot && _pieces[_hintSlot] != null && !_busy)
                _pieces[_hintSlot].Root.position = SlotHome(_hintSlot) + Vector3.up * (0.12f + Mathf.Sin(Time.time * 5f) * 0.08f);
        }

        void LateUpdate()
        {
            if (_engine == null) return;
            foreach (var piece in _pieces) piece?.UpdateShadows(0f);
            foreach (var c in _cells) c.View.Tick(Time.deltaTime);
            for (int i = 0; i < Cells; i++)
            {
                var cell = _cells[i];
                if (!cell.Badge.gameObject.activeSelf) continue;
                float height = _obsType[i] == ObstacleType.Holder ? 0.62f : 0.66f;
                cell.Badge.rectTransform.position = Scr(cell.Root.position + new Vector3(0.33f, height, 0.28f));
            }
        }

        void ShowHint()
        {
            if (!Advisor.BestMove(_engine, out _hintSlot, out _hintAnchor)) { _hintSlot = _hintAnchor = -1; _idleTimer = 0f; return; }
            RenderSelection();
        }

        void ClearHint()
        {
            _idleTimer = 0f;
            if (TutorialGuiding && !_busy) return;
            if (_hintSlot < 0 && _hintAnchor < 0) return;
            int slot = _hintSlot;
            _hintSlot = _hintAnchor = -1;
            if (slot >= 0 && _pieces[slot] != null && slot != _dragSlot) _pieces[slot].Root.position = SlotHome(slot);
            RenderSelection();
        }

        // =====================================================================
        // Automated smoke test (command line: -smoketest <outputDir>)
        // =====================================================================

        IEnumerator SmokeTest()
        {
            Debug.Log("[CakeSort][Smoke] start");
            string Shot(string name) => System.IO.Path.Combine(_smokeDir, name);
            void Capture(string name) => SmokeCapture(name);
            yield return new WaitForSecondsRealtime(1.5f);
            if (_daily.activeSelf)
            {
                Capture("00_daily.png");
                yield return ClaimDaily();
                yield return new WaitForSecondsRealtime(1.0f);
            }
            Capture("01_title.png");
            yield return new WaitForSecondsRealtime(0.3f);
            yield return BeginGame(false);
            yield return new WaitForSecondsRealtime(0.4f);
            Capture("02_board.png");
            if (_smokeRecord) { yield return SmokeRecord(); yield break; }
            Time.timeScale = _smokeSpeed;

            int moves = 0, unlocks = 0, maxObstacles = 0, linkedPlaced = 0, levelUps = 0;
            bool dragShot = false, actionShot = false, unlockShot = false, boostersDone = false, obstacleShot = false;
            var rng = new System.Random(7);
            while (_engine.State == GameState.Playing && moves < 400)
            {
                while (_busy || _overlayOpen)
                {
                    if (_unlock.activeSelf && _unlock.GetComponent<CanvasGroup>().alpha >= 1f)
                    {
                        unlocks++;
                        if (!unlockShot)
                        {
                            yield return new WaitForSecondsRealtime(0.5f);
                            Capture("05_unlock.png");
                            unlockShot = true;
                            yield return new WaitForSecondsRealtime(0.3f);
                        }
                        CloseUnlock();
                        yield return new WaitForSecondsRealtime(0.3f);
                    }
                    if (_levelUp.activeSelf && _levelUp.GetComponent<CanvasGroup>().alpha >= 1f)
                    {
                        levelUps++;
                        yield return new WaitForSecondsRealtime(0.3f);
                        CloseLevelUp();
                        yield return new WaitForSecondsRealtime(0.3f);
                    }
                    if (_lose.activeSelf) break;
                    yield return null;
                }
                if (_engine.State != GameState.Playing) break;

                int slot, anchor;
                bool careful = moves < 160;
                if (careful)
                {
                    if (TutorialGuiding && (_tutorialStep == 0 || _tutorialStep == 2))
                    {
                        yield return new WaitForSeconds(1.1f);
                        Capture(_tutorialStep == 0 ? "14_tutorial.png" : "15_tutorial_linked.png");
                    }
                    if (!NextBotMove(out slot, out anchor)) break;
                }
                else
                {
                    var options = Enumerable.Range(0, 3).SelectMany(s => Enumerable.Range(0, Cells).Where(a => _engine.CanPlace(s, a)).Select(a => (s, a))).ToList();
                    if (options.Count == 0) break;
                    (slot, anchor) = options[rng.Next(options.Count)];
                }

                if (_engine.Slots[slot] != null && _engine.Slots[slot].IsLinked) linkedPlaced++;
                if (!dragShot && _engine.Slots[slot] != null && _engine.Slots[slot].IsLinked)
                {
                    yield return SimulateDrag(slot, anchor);
                    Capture("03_drag_linked.png");
                    yield return new WaitForSecondsRealtime(0.2f);
                    _dragging = false;
                    _dragSlot = -1;
                    _hoverAnchor = -1;
                    RenderHighlights();
                    yield return PlaceRoutine(slot, anchor);
                    dragShot = true;
                }
                else
                {
                    yield return SimulateDrag(slot, anchor);
                    _dragging = false;
                    _dragSlot = -1;
                    _hoverAnchor = -1;
                    RenderHighlights();
                    var routine = StartCoroutine(PlaceRoutine(slot, anchor));
                    if (!actionShot && moves >= 5)
                    {
                        yield return new WaitForSecondsRealtime(0.2f);
                        Capture("04_action.png");
                        actionShot = true;
                    }
                    yield return routine;
                }
                moves++;
                maxObstacles = Mathf.Max(maxObstacles, _obsType.Count(t => t != ObstacleType.None));
                if (_smokeQuick && moves >= 30)
                {
                    while (_busy || _overlayOpen) { SmokeDismiss(); yield return null; }
                    yield return new WaitForSecondsRealtime(0.4f);
                    Capture("06_midgame.png");
                    yield return new WaitForSecondsRealtime(0.5f);
                    Application.Quit(0);
                    yield break;
                }

                if (!boostersDone && moves == 40 && _engine.State == GameState.Playing)
                {
                    boostersDone = true;
                    while (_busy || _overlayOpen) { SmokeDismiss(); yield return null; }
                    yield return new WaitForSecondsRealtime(0.3f);
                    Capture("06_midgame.png");
                    int loaded = Enumerable.Range(0, Cells).FirstOrDefault(i => !_engine.Plates[i].IsEmpty && !_engine.Plates[i].Blocked);
                    _armed = Booster.Whisk;
                    RenderHighlights();
                    RenderBoosters();
                    yield return new WaitForSecondsRealtime(0.25f);
                    Capture("07_booster_armed.png");
                    yield return UseTargetedBooster(Booster.Whisk, loaded);
                    while (_busy || _overlayOpen) { SmokeDismiss(); yield return null; }
                    yield return UseFirework();
                    while (_busy || _overlayOpen) { SmokeDismiss(); yield return null; }
                    _armed = Booster.Bomb;
                    var bomb = StartCoroutine(UseTargetedBooster(Booster.Bomb, 12));
                    yield return new WaitForSecondsRealtime(0.2f);
                    Capture("08_bomb.png");
                    yield return bomb;
                    Debug.Log($"[CakeSort][Smoke] boosters after use: {string.Join(",", _boosters)}");
                    while (_busy || _overlayOpen) { SmokeDismiss(); yield return null; }
                    int empty = System.Array.FindIndex(_boosters, b => b == 0);
                    if (empty >= 0)
                    {
                        // Early coins don't cover a booster yet; top up so the purchase path is exercised.
                        int price = MetaRules.BoosterPrice(empty);
                        if (_profile.Coins < price) { _profile.Earn(price - _profile.Coins); _coinsGoal = _profile.Coins; }
                        OnBoosterTapped((Booster)empty);
                        yield return new WaitForSecondsRealtime(0.7f);
                        Capture("13_shop.png");
                        int before = _boosters[empty];
                        BuyFromShop();
                        CloseShop();
                        yield return new WaitForSecondsRealtime(0.5f);
                        Debug.Log($"[CakeSort][Smoke] shop: booster {empty} {before}->{_boosters[empty]} coins={_profile.Coins}");
                    }
                }

                if (!obstacleShot && _obsType.Count(t => t != ObstacleType.None) >= 1 && !_busy && !_overlayOpen)
                {
                    yield return new WaitForSecondsRealtime(0.4f);
                    Capture("09_obstacles.png");
                    obstacleShot = true;
                }
            }
            while (!_lose.activeSelf && _engine.State == GameState.Lost) yield return null;
            yield return new WaitForSecondsRealtime(1.2f);
            Capture("10_gameover.png");
            Debug.Log($"[CakeSort][Smoke] moves={moves} served={_engine.CakesServed} score={_engine.Score} flavors={_engine.UnlockedFlavors} " +
                      $"unlockOverlays={unlocks} linkedPlaced={linkedPlaced} maxObstacles={maxObstacles} state={_engine.State} maxCombo={_maxCombo} best={_best}");
            Debug.Log($"[CakeSort][Smoke] haptics: {string.Join(" ", System.Enum.GetNames(typeof(HapticKind)).Select((n, i) => n + "=" + Haptics.Counts[i]))}");
            Debug.Log($"[CakeSort][Smoke] meta: coins={_profile.Coins} level={_profile.Level} stars={_profile.Stars} orders={_profile.OrdersDone} levelUps={levelUps} streak={_profile.Streak}");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        void SmokeCapture(string name)
        {
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_smokeDir, name));
            Debug.Log("[CakeSort][Smoke] shot " + name);
        }

        void SmokeDismiss()
        {
            if (_unlock.activeSelf && _unlock.GetComponent<CanvasGroup>().alpha >= 1f) CloseUnlock();
            if (_levelUp.activeSelf && _levelUp.GetComponent<CanvasGroup>().alpha >= 1f) CloseLevelUp();
        }

        /// <summary>
        /// Plays a few careful moves at real speed with fixed-step time and writes every frame as a JPG,
        /// so animation timing can be reviewed frame by frame.
        /// </summary>
        IEnumerator SmokeRecord()
        {
            var dir = System.IO.Path.Combine(_smokeDir, "frames");
            System.IO.Directory.CreateDirectory(dir);
            Time.timeScale = 1f;
            Time.captureFramerate = 30;
            bool recording = true;
            int frame = 0;
            IEnumerator Recorder()
            {
                var wait = new WaitForEndOfFrame();
                while (recording)
                {
                    yield return wait;
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"f{frame++:D4}.jpg"), tex.EncodeToJPG(82));
                    Destroy(tex);
                }
            }
            StartCoroutine(Recorder());
            int moves = 0;
            int limit = 14;
            for (int i = 0; i < System.Environment.GetCommandLineArgs().Length - 1; i++)
                if (System.Environment.GetCommandLineArgs()[i] == "-smokemoves") int.TryParse(System.Environment.GetCommandLineArgs()[i + 1], out limit);
            while (moves < limit && _engine.State == GameState.Playing)
            {
                while (_busy || _overlayOpen) { SmokeDismiss(); yield return null; }
                if (!NextBotMove(out int slot, out int anchor)) break;
                Debug.Log($"[CakeSort][Smoke] rec move {moves} frame {frame}");
                yield return SimulateDrag(slot, anchor);
                _dragging = false;
                _dragSlot = -1;
                _hoverAnchor = -1;
                RenderHighlights();
                yield return PlaceRoutine(slot, anchor);
                moves++;
            }
            while (_busy || _overlayOpen) { SmokeDismiss(); yield return null; }
            for (int i = 0; i < 20; i++) yield return null;
            recording = false;
            Debug.Log($"[CakeSort][Smoke] recorded {frame} frames over {moves} moves");
            yield return null;
            Application.Quit(0);
        }

        /// <summary>Drags a piece like a finger would: through the real drag code, along an eased path with a short hover.</summary>
        IEnumerator SimulateDrag(int slot, int anchor)
        {
            BeginDrag(slot);
            var cells = Engine.CellsFor(_engine.Slots[slot].Link, anchor);
            var centre = Vector3.zero;
            foreach (int c in cells) centre += _cells[c].Position;
            centre /= cells.Length;
            Vector2 ScreenFor(Vector3 rootTarget) => _cam.WorldToScreenPoint(rootTarget - new Vector3(0f, 0f, DragForward));
            Vector2 from = ScreenFor(SlotHome(slot)), to = ScreenFor(new Vector3(centre.x, 0f, centre.z));
            var bend = new Vector2((to.x - from.x) * -0.15f, Screen.height * 0.03f);
            float duration = 0.42f, elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Ease.InOutSine(Mathf.Clamp01(elapsed / duration));
                var point = Vector2.Lerp(from, to, t) + bend * Mathf.Sin(t * Mathf.PI);
                DragTo(point);
                yield return null;
            }
            for (float hover = 0f; hover < 0.14f; hover += Time.deltaTime)
            {
                DragTo(to);
                yield return null;
            }
            _hoverAnchor = anchor;
            RenderHighlights();
        }
    }
}
