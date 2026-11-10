// Shared building blocks for the game screen: art loading, procedural UI sprites, tweens, UI helpers.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CakeSort.Game
{
    public static class Palette
    {
        public static readonly Color Ink = Hex(0x4E2A1E);
        public static readonly Color Cream = Hex(0xFFF6E6);
        public static readonly Color CreamDark = Hex(0xF6DFC0);
        public static readonly Color Pink = Hex(0xFF5C8A);
        public static readonly Color PinkDark = Hex(0xD63A6A);
        public static readonly Color Gold = Hex(0xFFC93C);
        public static readonly Color Mint = Hex(0x4FCB8C);
        public static readonly Color Sky = Hex(0x55B7F2);
        public static readonly Color Glow = Hex(0xFFE27A);

        static readonly Color[] Flavors =
        {
            Hex(0xE53935), Hex(0x6D4C41), Hex(0xFFE082), Hex(0x7CC46A),
            Hex(0x5C6BC0), Hex(0xF48FB1), Hex(0xFB8C00), Hex(0xAB47BC),
        };

        public static Color Flavor(int flavor) => Flavors[Mathf.Abs(flavor) % Flavors.Length];

        public static Color Hex(uint rgb, float alpha = 1f) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
    }

    /// <summary>Generated artwork lives in Resources/Art; small UI shapes are rasterized at runtime.</summary>
    public static class Art
    {
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        static Sprite Load(string name)
        {
            if (Cache.TryGetValue(name, out var cached)) return cached;
            var tex = Resources.Load<Texture2D>("Art/" + name);
            if (tex == null)
            {
                Debug.LogError($"[CakeSort] Missing art: Resources/Art/{name}.png");
                tex = Texture2D.whiteTexture;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect);
            Cache[name] = sprite;
            return sprite;
        }

        public static Sprite Cake(int flavor) => Load("cake_" + Mathf.Abs(flavor) % 8);
        public static Sprite Wedge(int flavor) => Load("wedge_" + Mathf.Abs(flavor) % 8);
        public static Sprite Plate() => Load("plate");
        public static Sprite Tray() => Load("tray");
        public static Sprite Background() => Load("background");
        public static Sprite Box() => Load("box");
        public static Sprite Cloche() => Load("cloche");
        public static Sprite Slime() => Load("slime");
        public static Sprite Logo() => Load("logo");
        public static Sprite MascotHappy() => Load("mascot_happy");
        public static Sprite MascotSad() => Load("mascot_sad");
        public static Sprite Icon(string name) => Load("icon_" + name);
        public static Sprite Coin() => Load("coin");
        public static Sprite Chest() => Load("chest");
        public static Sprite Hand() => Load("hand");
        public static Sprite Customer(int index) => Load("customer_" + Mathf.Abs(index) % 4);

        // ---------- procedural shapes ----------

        static Sprite Procedural(string key, int size, Func<float, float, Color> shade, Vector4 border = default)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = shade((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
            tex.SetPixels(pixels);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, border);
            Cache[key] = sprite;
            return sprite;
        }

        static float Edge(float signedDistance, float softness) => Mathf.Clamp01(0.5f - signedDistance / softness);

        public static Sprite Circle() => Procedural("circle", 128, (x, y) =>
            new Color(1, 1, 1, Edge(Mathf.Sqrt(x * x + y * y) - 0.97f, 0.03f)));

        public static Sprite Shadow() => Procedural("shadow", 128, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float a = Mathf.Clamp01(1f - d);
            return new Color(0.22f, 0.11f, 0.06f, a * a * 0.55f);
        });

        public static Sprite GlowRing() => Procedural("glow", 128, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float a = Mathf.Exp(-Mathf.Pow((d - 0.80f) / 0.12f, 2f));
            return new Color(1, 1, 1, a);
        });

        public static Sprite SoftDot() => Procedural("softdot", 64, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float a = Mathf.Clamp01(1f - d);
            return new Color(1, 1, 1, a * a);
        });

        public static Sprite Sparkle() => Procedural("sparkle", 96, (x, y) =>
        {
            float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
            float star = Mathf.Clamp01(1f - (Mathf.Sqrt(ax) + Mathf.Sqrt(ay)) * 1.05f);
            float core = Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y) * 3f);
            return new Color(1, 1, 1, Mathf.Clamp01(star * 2.4f + core));
        });

        public static Sprite Star() => Procedural("star", 128, (x, y) =>
        {
            // Five-point star: compare the pixel radius against the star outline at its angle.
            float angle = Mathf.Atan2(x, y);
            float sector = Mathf.Repeat(angle, Mathf.PI * 2f / 5f) / (Mathf.PI * 2f / 5f);
            float t = Mathf.Abs(sector - 0.5f) * 2f;
            float outline = Mathf.Lerp(0.42f, 0.96f, t * t);
            float d = Mathf.Sqrt(x * x + y * y);
            float a = Edge(d - outline, 0.035f);
            float shade = 1f - Mathf.Clamp01(d) * 0.25f + (y > 0 ? 0.1f : 0f);
            return new Color(shade, shade, shade, a);
        });

        /// <summary>White rounded rectangle with a nine-slice border, for panels, pills, and buttons.</summary>
        public static Sprite Rounded() => Procedural("rounded", 128, (x, y) =>
        {
            const float radius = 0.5f; // in normalized units: 32 px of the 128 px texture
            float qx = Mathf.Max(Mathf.Abs(x) - (1f - radius), 0f);
            float qy = Mathf.Max(Mathf.Abs(y) - (1f - radius), 0f);
            float d = Mathf.Sqrt(qx * qx + qy * qy) - radius + 0.02f;
            return new Color(1, 1, 1, Edge(d, 0.03f));
        }, new Vector4(40, 40, 40, 40));

        public static Sprite White()
        {
            if (Cache.TryGetValue("white", out var cached)) return cached;
            var tex = Texture2D.whiteTexture;
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f, 100f);
            Cache["white"] = sprite;
            return sprite;
        }
    }

    public static class Ease
    {
        public static float Linear(float t) => t;
        public static float OutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        public static float InCubic(float t) => t * t * t;
        public static float InOutSine(float t) => -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f;

        public static float OutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        public static float OutElastic(float t)
        {
            if (t <= 0f || t >= 1f) return t;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * (2f * Mathf.PI / 3f)) + 1f;
        }
    }

    public static class Tween
    {
        /// <summary>Drives <paramref name="step"/> from 0 to 1 over <paramref name="duration"/> seconds.</summary>
        public static IEnumerator Run(float duration, Action<float> step, Func<float, float> ease = null,
            float delay = 0f)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            ease = ease ?? Ease.OutCubic;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                step(ease(Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }
            step(ease(1f));
        }

        /// <summary>Squash-and-stretch pop that settles back to the starting scale.</summary>
        public static IEnumerator Punch(Transform target, float strength = 0.18f, float duration = 0.35f)
        {
            if (target == null) yield break;
            var baseScale = Vector3.one;
            float elapsed = 0f;
            while (elapsed < duration && target != null)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float wave = Mathf.Sin(t * Mathf.PI * 3f) * (1f - t) * strength;
                target.localScale = new Vector3(baseScale.x * (1f + wave), baseScale.y * (1f - wave * 0.6f), 1f);
                yield return null;
            }
            if (target != null) target.localScale = baseScale;
        }

        /// <summary>Squash-and-stretch for 3D objects around their current scale.</summary>
        public static IEnumerator Punch3D(Transform target, float strength = 0.18f, float duration = 0.35f)
        {
            if (target == null) yield break;
            var baseScale = target.localScale;
            float elapsed = 0f;
            while (elapsed < duration && target != null)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float wave = Mathf.Sin(t * Mathf.PI * 3f) * (1f - t) * strength;
                target.localScale = new Vector3(baseScale.x * (1f + wave), baseScale.y * (1f - wave * 0.8f), baseScale.z * (1f + wave));
                yield return null;
            }
            if (target != null) target.localScale = baseScale;
        }

        /// <summary>
        /// Landing squash: flattens by <paramref name="amount"/>, then springs back with a damped wobble around
        /// <paramref name="rest"/>. Always settles on <paramref name="rest"/>, so overlapping calls can't drift the scale.
        /// </summary>
        public static IEnumerator Squash3D(Transform target, Vector3 rest, float amount = 0.2f, float duration = 0.42f)
        {
            if (target == null) yield break;
            float elapsed = 0f;
            while (elapsed < duration && target != null)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float s = amount * Mathf.Exp(-4.5f * t) * Mathf.Cos(t * Mathf.PI * 3.4f);
                target.localScale = new Vector3(rest.x * (1f + s * 0.55f), rest.y * (1f - s), rest.z * (1f + s * 0.55f));
                yield return null;
            }
            if (target != null) target.localScale = rest;
        }

        public static IEnumerator Shake3D(Transform target, float magnitude, float duration)
        {
            if (target == null) yield break;
            var origin = target.localPosition;
            float elapsed = 0f;
            while (elapsed < duration && target != null)
            {
                elapsed += Time.deltaTime;
                float falloff = 1f - elapsed / duration;
                var r = UnityEngine.Random.insideUnitCircle * magnitude * falloff;
                target.localPosition = origin + new Vector3(r.x, 0f, r.y);
                yield return null;
            }
            if (target != null) target.localPosition = origin;
        }

        public static IEnumerator Shake(RectTransform target, float magnitude, float duration)
        {
            if (target == null) yield break;
            var origin = target.anchoredPosition;
            float elapsed = 0f;
            while (elapsed < duration && target != null)
            {
                elapsed += Time.deltaTime;
                float falloff = 1f - elapsed / duration;
                target.anchoredPosition = origin + UnityEngine.Random.insideUnitCircle * magnitude * falloff;
                yield return null;
            }
            if (target != null) target.anchoredPosition = origin;
        }
    }

    /// <summary>Forwards pointer callbacks to delegates so UI can be wired in code.</summary>
    public class PointerRelay : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Action<PointerEventData> Down, Up, Click, BeginDrag, Drag, EndDrag;

        public void OnPointerDown(PointerEventData e) => Down?.Invoke(e);
        public void OnPointerUp(PointerEventData e) => Up?.Invoke(e);
        public void OnPointerClick(PointerEventData e) => Click?.Invoke(e);
        public void OnBeginDrag(PointerEventData e) => BeginDrag?.Invoke(e);
        public void OnDrag(PointerEventData e) => Drag?.Invoke(e);
        public void OnEndDrag(PointerEventData e) => EndDrag?.Invoke(e);
    }

    /// <summary>Keeps its RectTransform inside the device safe area (notch, home indicator).</summary>
    public class SafeArea : MonoBehaviour
    {
        Rect _applied;

        void Update()
        {
            var area = Screen.safeArea;
            if (area == _applied || Screen.width == 0 || Screen.height == 0) return;
            _applied = area;
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            rect.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }

    public static class UI
    {
        static Font _font;
        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image Img(Transform parent, string name, Sprite sprite, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color ?? Color.white;
            image.raycastTarget = false;
            if (sprite != null && sprite.border != Vector4.zero) image.type = Image.Type.Sliced;
            return image;
        }

        public static Text Txt(Transform parent, string name, string content, int size, Color color,
            TextAnchor anchor = TextAnchor.MiddleCenter, bool bold = true, float outline = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = Font;
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            if (outline > 0f)
            {
                var o = go.AddComponent<Outline>();
                o.effectColor = Palette.Ink;
                o.effectDistance = new Vector2(outline, -outline);
                var s = go.AddComponent<Shadow>();
                s.effectColor = new Color(0.2f, 0.08f, 0.04f, 0.6f);
                s.effectDistance = new Vector2(0, -outline * 2f);
            }
            return text;
        }

        public static void Stretch(RectTransform rect, float xMin, float xMax, float yMin, float yMax)
        {
            rect.anchorMin = new Vector2(xMin, yMin);
            rect.anchorMax = new Vector2(xMax, yMax);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        public static void Fill(RectTransform rect) => Stretch(rect, 0, 1, 0, 1);

        /// <summary>Fixed-size rect anchored at a normalized point of its parent.</summary>
        public static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        public static void DropShadow(Graphic graphic, float distance = 6f, float alpha = 0.35f)
        {
            var s = graphic.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0.25f, 0.1f, 0.05f, alpha);
            s.effectDistance = new Vector2(0, -distance);
        }

        /// <summary>Chunky rounded button with a press-down response.</summary>
        public static (RectTransform rect, Text label) Button(Transform parent, string name, string label,
            Color fill, int fontSize, Action onClick)
        {
            var rect = Rect(name, parent);
            var shadow = Img(rect, "Base", Art.Rounded(), Color.Lerp(fill, Color.black, 0.28f));
            Stretch(shadow.rectTransform, 0, 1, 0, 1);
            shadow.rectTransform.offsetMax = new Vector2(0, -10);
            var face = Img(rect, "Face", Art.Rounded(), fill);
            Fill(face.rectTransform);
            face.rectTransform.offsetMin = new Vector2(0, 10);
            face.raycastTarget = true;
            var shine = Img(face.rectTransform, "Shine", Art.Rounded(), new Color(1, 1, 1, 0.22f));
            Stretch(shine.rectTransform, 0.04f, 0.96f, 0.52f, 0.93f);
            var text = Txt(face.rectTransform, "Label", label, fontSize, Color.white, TextAnchor.MiddleCenter, true, 3f);
            Fill(text.rectTransform);

            var relay = face.gameObject.AddComponent<PointerRelay>();
            relay.Down = _ => rect.localScale = Vector3.one * 0.94f;
            relay.Up = _ => rect.localScale = Vector3.one;
            relay.Click = _ =>
            {
                Haptics.Play(HapticKind.Selection);
                onClick?.Invoke();
            };
            return (rect, text);
        }
    }
}
