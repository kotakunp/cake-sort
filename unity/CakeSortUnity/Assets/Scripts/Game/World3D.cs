// 2.5D presentation: procedural meshes, materials, and the 3D views for plates, linked pieces and obstacles.
// Everything is generated at runtime from the top-down artwork in Resources/Art.
using System;
using System.Collections.Generic;
using CakeSort.Core;
using UnityEngine;

namespace CakeSort.Game
{
    public static class Meshes
    {
        static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        static Mesh Cached(string key, Func<Mesh> build)
        {
            if (!Cache.TryGetValue(key, out var mesh)) Cache[key] = mesh = build();
            return mesh;
        }

        /// <summary>Accumulates vertices; triangles are wound automatically to face along their vertex normals.</summary>
        class Builder
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector3> _n = new List<Vector3>();
            readonly List<Vector2> _uv = new List<Vector2>();
            readonly List<Color> _c = new List<Color>();
            readonly List<List<int>> _subs = new List<List<int>>();

            public int Add(Vector3 v, Vector3 n, Vector2 uv, float shade = 1f)
            {
                _v.Add(v); _n.Add(n.normalized); _uv.Add(uv); _c.Add(new Color(shade, shade, shade, 1f));
                return _v.Count - 1;
            }

            public void Tri(int sub, int a, int b, int c)
            {
                while (_subs.Count <= sub) _subs.Add(new List<int>());
                var face = Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]);
                if (Vector3.Dot(face, _n[a] + _n[b] + _n[c]) < 0f) { int t = b; b = c; c = t; }
                _subs[sub].Add(a); _subs[sub].Add(b); _subs[sub].Add(c);
            }

            public void Quad(int sub, int a, int b, int c, int d) { Tri(sub, a, b, c); Tri(sub, a, c, d); }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_v);
                mesh.SetNormals(_n);
                mesh.SetUVs(0, _uv);
                mesh.SetColors(_c);
                mesh.subMeshCount = _subs.Count;
                for (int i = 0; i < _subs.Count; i++) mesh.SetTriangles(_subs[i], i);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        static Vector3 Dir(float radians) => new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians));

        /// <summary>
        /// One sixth of a round cake, centred on +Z. Submesh 0 is the frosted top (planar-mapped onto the
        /// whole-cake artwork), submesh 1 the sides and cut faces (mapped onto a layered sponge strip).
        /// </summary>
        public static Mesh Wedge(float radius, float height) => Cached($"wedge{radius}{height}", () =>
        {
            var b = new Builder();
            const int seg = 10;
            float a0 = 60f * Mathf.Deg2Rad, a1 = 120f * Mathf.Deg2Rad;
            Vector2 TopUV(Vector3 p) => new Vector2(0.5f + p.x / (2f * radius) * 0.97f, 0.5f + p.z / (2f * radius) * 0.97f);

            float lip = height * 0.12f;
            float topRadius = radius - lip * 0.4f;
            int centre = b.Add(new Vector3(0, height, 0), Vector3.up, new Vector2(0.5f, 0.5f));
            var top = new int[seg + 1];
            for (int i = 0; i <= seg; i++)
            {
                var p = Dir(Mathf.Lerp(a0, a1, i / (float)seg)) * topRadius + Vector3.up * height;
                top[i] = b.Add(p, Vector3.up, TopUV(p));
            }
            for (int i = 0; i < seg; i++) b.Tri(0, centre, top[i], top[i + 1]);

            // rounded frosting lip just under the top edge
            var lipTop = new int[seg + 1];
            var lipBottom = new int[seg + 1];
            var sideTop = new int[seg + 1];
            var sideBottom = new int[seg + 1];
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)seg);
                var d = Dir(a);
                float u = i / (float)seg * 2f;
                lipTop[i] = b.Add(d * topRadius + Vector3.up * height, (d * 0.55f + Vector3.up).normalized, new Vector2(u, 1f));
                lipBottom[i] = b.Add(d * radius + Vector3.up * (height - lip), (d + Vector3.up * 0.35f).normalized, new Vector2(u, 1f - lip / height));
                sideTop[i] = b.Add(d * radius + Vector3.up * (height - lip), d, new Vector2(u, 1f - lip / height));
                sideBottom[i] = b.Add(d * radius, d, new Vector2(u, 0f), 0.78f);
            }
            for (int i = 0; i < seg; i++)
            {
                b.Quad(1, lipTop[i], lipTop[i + 1], lipBottom[i + 1], lipBottom[i]);
                b.Quad(1, sideTop[i], sideTop[i + 1], sideBottom[i + 1], sideBottom[i]);
            }

            foreach (var (angle, normalAngle) in new[] { (a0, a0 - Mathf.PI / 2f), (a1, a1 + Mathf.PI / 2f) })
            {
                var n = Dir(normalAngle);
                var edge = Dir(angle) * radius;
                int c0 = b.Add(Vector3.zero, n, new Vector2(0f, 0f), 0.8f);
                int c1 = b.Add(Vector3.up * height, n, new Vector2(0f, 1f));
                int e1 = b.Add(edge + Vector3.up * height, n, new Vector2(1f, 1f));
                int e0 = b.Add(edge, n, new Vector2(1f, 0f), 0.8f);
                b.Quad(1, c0, c1, e1, e0);
            }
            return b.Build("Wedge");
        });

        /// <summary>Surface of revolution around Y from an (radius, height) profile; planar UVs over the top view.</summary>
        public static Mesh Lathe(string key, Vector2[] profile, float uvRadius, int segments = 48) => Cached("lathe" + key, () =>
        {
            var b = new Builder();
            var rings = new int[profile.Length, segments + 1];
            for (int k = 0; k < profile.Length; k++)
            {
                var t = profile[Mathf.Min(k + 1, profile.Length - 1)] - profile[Mathf.Max(k - 1, 0)];
                var n2 = new Vector2(-t.y, t.x).normalized;
                for (int j = 0; j <= segments; j++)
                {
                    float a = j / (float)segments * Mathf.PI * 2f;
                    float c = Mathf.Cos(a), s = Mathf.Sin(a);
                    var p = new Vector3(profile[k].x * c, profile[k].y, profile[k].x * s);
                    var n = new Vector3(n2.x * c, n2.y, n2.x * s);
                    var uv = new Vector2(0.5f + p.x / (2f * uvRadius), 0.5f + p.z / (2f * uvRadius));
                    rings[k, j] = b.Add(p, n.sqrMagnitude > 0.0001f ? n : Vector3.up, uv);
                }
            }
            for (int k = 0; k < profile.Length - 1; k++)
            for (int j = 0; j < segments; j++)
                b.Quad(0, rings[k, j], rings[k, j + 1], rings[k + 1, j + 1], rings[k + 1, j]);
            return b.Build("Lathe " + key);
        });

        public static Mesh Plate() => Lathe("plate", new[]
        {
            new Vector2(0f, 0.034f), new Vector2(0.28f, 0.034f), new Vector2(0.34f, 0.04f), new Vector2(0.39f, 0.058f),
            new Vector2(0.43f, 0.078f), new Vector2(0.455f, 0.084f), new Vector2(0.466f, 0.078f), new Vector2(0.46f, 0.062f),
            new Vector2(0.42f, 0.04f), new Vector2(0.3f, 0.012f), new Vector2(0.27f, 0f), new Vector2(0f, 0f),
        }, 0.468f);

        public static Mesh Dome() => Lathe("dome", BuildDome(0.43f, 0.5f), 0.45f);

        static Vector2[] BuildDome(float radius, float height)
        {
            var points = new List<Vector2>();
            for (int i = 0; i <= 12; i++)
            {
                float a = i / 12f * Mathf.PI / 2f;
                points.Add(new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * height));
            }
            return points.ToArray();
        }

        public static Mesh GoldRing() => Lathe("ring", new[]
        {
            new Vector2(0.405f, 0f), new Vector2(0.405f, 0.035f), new Vector2(0.43f, 0.055f), new Vector2(0.46f, 0.045f),
            new Vector2(0.47f, 0.02f), new Vector2(0.46f, 0f),
        }, 0.5f, 40);

        public static Mesh Knob() => Lathe("knob", new[]
        {
            new Vector2(0f, 0f), new Vector2(0.035f, 0f), new Vector2(0.03f, 0.04f), new Vector2(0.05f, 0.07f),
            new Vector2(0.06f, 0.1f), new Vector2(0.05f, 0.13f), new Vector2(0.03f, 0.148f), new Vector2(0f, 0.155f),
        }, 0.1f, 24);

        /// <summary>Rounded-rectangle slab (pivot at the bottom centre). Submesh 0: top (planar UVs), submesh 1: bevel and sides.</summary>
        public static Mesh Slab(float halfX, float halfZ, float radius, float height, float bevel) =>
            Cached($"slab{halfX}/{halfZ}/{radius}/{height}/{bevel}", () =>
            {
                radius = Mathf.Min(radius, Mathf.Min(halfX, halfZ));
                bevel = Mathf.Min(bevel, radius * 0.9f, height * 0.9f);
                const int cornerSeg = 10;
                var outline = new List<(Vector3 centre, Vector3 dir)>();
                var corners = new[] { (1, 1), (-1, 1), (-1, -1), (1, -1) };
                for (int c = 0; c < 4; c++)
                {
                    var centre = new Vector3(corners[c].Item1 * (halfX - radius), 0, corners[c].Item2 * (halfZ - radius));
                    for (int i = 0; i <= cornerSeg; i++)
                        outline.Add((centre, Dir((c * 90f + i * 90f / cornerSeg) * Mathf.Deg2Rad)));
                }

                var b = new Builder();
                Vector2 TopUV(Vector3 p) => new Vector2(0.5f + p.x / (2f * halfX), 0.5f + p.z / (2f * halfZ));
                int middle = b.Add(new Vector3(0, height, 0), Vector3.up, new Vector2(0.5f, 0.5f));
                int count = outline.Count;
                var topRing = new int[count];
                for (int i = 0; i < count; i++)
                {
                    var p = outline[i].centre + outline[i].dir * (radius - bevel) + Vector3.up * height;
                    topRing[i] = b.Add(p, Vector3.up, TopUV(p));
                }
                for (int i = 0; i < count; i++) b.Tri(0, middle, topRing[i], topRing[(i + 1) % count]);

                var bevelIn = new int[count];
                var bevelOut = new int[count];
                var wallTop = new int[count];
                var wallBottom = new int[count];
                for (int i = 0; i < count; i++)
                {
                    var (centre, dir) = outline[i];
                    float u = i / (float)count;
                    bevelIn[i] = b.Add(centre + dir * (radius - bevel) + Vector3.up * height, (dir * 0.35f + Vector3.up).normalized, new Vector2(u, 1f));
                    bevelOut[i] = b.Add(centre + dir * radius + Vector3.up * (height - bevel), (dir + Vector3.up * 0.4f).normalized, new Vector2(u, 0.9f));
                    wallTop[i] = b.Add(centre + dir * radius + Vector3.up * (height - bevel), dir, new Vector2(u, 0.9f));
                    wallBottom[i] = b.Add(centre + dir * radius, dir, new Vector2(u, 0f), 0.7f);
                }
                for (int i = 0; i < count; i++)
                {
                    int j = (i + 1) % count;
                    b.Quad(1, bevelIn[i], bevelIn[j], bevelOut[j], bevelOut[i]);
                    b.Quad(1, wallTop[i], wallTop[j], wallBottom[j], wallBottom[i]);
                }
                return b.Build("Slab");
            });

        /// <summary>Box with its pivot at the bottom centre. Submesh 0: top face, submesh 1: four sides (u 0..1 per side).</summary>
        public static Mesh Box(float halfX, float height, float halfZ) => Cached($"box{halfX}/{height}/{halfZ}", () =>
        {
            var b = new Builder();
            var t0 = b.Add(new Vector3(-halfX, height, -halfZ), Vector3.up, new Vector2(0, 0));
            var t1 = b.Add(new Vector3(halfX, height, -halfZ), Vector3.up, new Vector2(1, 0));
            var t2 = b.Add(new Vector3(halfX, height, halfZ), Vector3.up, new Vector2(1, 1));
            var t3 = b.Add(new Vector3(-halfX, height, halfZ), Vector3.up, new Vector2(0, 1));
            b.Quad(0, t0, t1, t2, t3);
            var sides = new[]
            {
                (Vector3.back, new Vector3(-halfX, 0, -halfZ), new Vector3(halfX, 0, -halfZ)),
                (Vector3.right, new Vector3(halfX, 0, -halfZ), new Vector3(halfX, 0, halfZ)),
                (Vector3.forward, new Vector3(halfX, 0, halfZ), new Vector3(-halfX, 0, halfZ)),
                (Vector3.left, new Vector3(-halfX, 0, halfZ), new Vector3(-halfX, 0, -halfZ)),
            };
            foreach (var (n, a, c) in sides)
            {
                int p0 = b.Add(a, n, new Vector2(0, 0), 0.75f);
                int p1 = b.Add(c, n, new Vector2(1, 0), 0.75f);
                int p2 = b.Add(c + Vector3.up * height, n, new Vector2(1, 1));
                int p3 = b.Add(a + Vector3.up * height, n, new Vector2(0, 1));
                b.Quad(1, p0, p1, p2, p3);
            }
            return b.Build("Box");
        });

        /// <summary>Unit quad on the XZ plane facing up; u runs along +X and v along +Z.</summary>
        public static Mesh Flat() => Cached("flat", () =>
        {
            var b = new Builder();
            int a = b.Add(new Vector3(-0.5f, 0, -0.5f), Vector3.up, new Vector2(0, 0));
            int c = b.Add(new Vector3(0.5f, 0, -0.5f), Vector3.up, new Vector2(1, 0));
            int d = b.Add(new Vector3(0.5f, 0, 0.5f), Vector3.up, new Vector2(1, 1));
            int e = b.Add(new Vector3(-0.5f, 0, 0.5f), Vector3.up, new Vector2(0, 1));
            b.Quad(0, a, c, d, e);
            return b.Build("Flat");
        });
    }

    public static class Mats
    {
        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        static Shader _lit, _transparent, _glass;

        static Shader Find(ref Shader cache, string resource, string fallback)
        {
            if (cache != null) return cache;
            cache = Resources.Load<Shader>("Shaders/" + resource);
            if (cache == null || !cache.isSupported)
            {
                Debug.LogError($"[CakeSort] Shader {resource} missing or unsupported, falling back to {fallback}");
                cache = Shader.Find(fallback);
            }
            return cache;
        }

        public static Texture2D Art(string name)
        {
            if (Textures.TryGetValue(name, out var tex)) return tex;
            tex = Resources.Load<Texture2D>("Art/" + name);
            if (tex == null) { Debug.LogError("[CakeSort] Missing texture Art/" + name); tex = Texture2D.whiteTexture; }
            Textures[name] = tex;
            return tex;
        }

        public static Material Lit(string key, Texture texture, Color color, float spec = 0.2f, float gloss = 24f, float rim = 0.2f)
        {
            if (Cache.TryGetValue(key, out var m)) return m;
            m = new Material(Find(ref _lit, "CakeLit", "Unlit/Texture")) { name = key, mainTexture = texture ?? Texture2D.whiteTexture };
            m.SetColor("_Color", color);
            m.SetFloat("_Spec", spec);
            m.SetFloat("_Gloss", gloss);
            m.SetFloat("_Rim", rim);
            return Cache[key] = m;
        }

        public static Material Transparent(string key, Texture texture, Color color)
        {
            if (Cache.TryGetValue(key, out var m)) return m;
            m = new Material(Find(ref _transparent, "CakeTransparent", "Sprites/Default")) { name = key, mainTexture = texture ?? Texture2D.whiteTexture };
            m.SetColor("_Color", color);
            return Cache[key] = m;
        }

        public static Material Glass()
        {
            if (Cache.TryGetValue("glass", out var m)) return m;
            m = new Material(Find(ref _glass, "CakeGlass", "Sprites/Default")) { name = "glass" };
            return Cache["glass"] = m;
        }

        // Sponge, filling and frosting colors per flavor (cherry, chocolate, vanilla, matcha, blueberry, strawberry, orange, grape).
        static readonly uint[,] Layers =
        {
            { 0xF4DDB0, 0xD32F2F, 0xC62828 },
            { 0x6B4129, 0xF3E3CF, 0x3E2418 },
            { 0xF7E3A6, 0xFFFDF5, 0xFFF1C9 },
            { 0xB9D88E, 0xFFF8E8, 0x74AD4F },
            { 0xF3E0B8, 0x5463C0, 0x3949AB },
            { 0xFFD3DD, 0xFFFFFF, 0xF06292 },
            { 0xFFE1A8, 0xFB8C00, 0xFFA726 },
            { 0xE3CCEB, 0x8E24AA, 0x9C4DCC },
        };

        /// <summary>Layered sponge strip used for the sides and cut faces of a wedge.</summary>
        static Texture2D SideStrip(int flavor)
        {
            string key = "strip" + flavor;
            if (Textures.TryGetValue(key, out var tex)) return tex;
            const int w = 64, h = 128;
            tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, name = key };
            var sponge = Palette.Hex(Layers[flavor, 0]);
            var filling = Palette.Hex(Layers[flavor, 1]);
            var frosting = Palette.Hex(Layers[flavor, 2]);
            var rng = new System.Random(flavor * 977 + 13);
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    Color c;
                    float wave = Mathf.Sin(x / (float)w * Mathf.PI * 4f + flavor) * 0.008f;
                    float vv = v + wave;
                    if (vv > 0.86f) c = frosting;
                    else if (vv > 0.66f && vv < 0.74f) c = filling;
                    else if (vv > 0.3f && vv < 0.39f) c = filling;
                    else
                    {
                        // crumb texture in the sponge
                        float n = (float)rng.NextDouble();
                        c = sponge * (0.94f + n * 0.1f);
                        if (n > 0.93f) c *= 0.85f;
                    }
                    if (Mathf.Abs(vv - 0.86f) < 0.012f) c = Color.Lerp(c, Color.white, 0.25f);
                    c.a = 1f;
                    pixels[y * w + x] = c;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(true);
            return Textures[key] = tex;
        }

        static Texture2D RibbonStrip()
        {
            if (Textures.TryGetValue("ribbon", out var tex)) return tex;
            const int w = 64, h = 16;
            tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var pink = Palette.Hex(0xF7B6C8);
            var red = Palette.Hex(0xD9343F);
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                bool ribbon = Mathf.Abs(u - 0.5f) < 0.1f;
                var c = ribbon ? red : pink;
                if (ribbon && Mathf.Abs(u - 0.5f) > 0.075f) c *= 0.85f;
                c.a = 1f;
                pixels[y * w + x] = c;
            }
            tex.SetPixels(pixels);
            tex.Apply(true);
            return Textures["ribbon"] = tex;
        }

        static readonly Material[][] FlavorPairs = new Material[8][];

        /// <summary>{top, sides} materials for a wedge of the given flavor.</summary>
        public static Material[] Wedge(int flavor)
        {
            flavor = Mathf.Abs(flavor) % 8;
            return FlavorPairs[flavor] ?? (FlavorPairs[flavor] = new[]
            {
                Lit("cakeTop" + flavor, Art("cake_" + flavor), Color.white, 0.22f, 20f, 0.12f),
                Lit("cakeSide" + flavor, SideStrip(flavor), Color.white, 0.08f, 10f, 0.16f),
            });
        }

        public static Material PlateMat() => Lit("plate", Art("plate"), Color.white, 0.55f, 70f, 0.22f);
        public static Material[] Tray() => new[]
        {
            Lit("trayTop", Art("tray"), Color.white, 0.12f, 18f, 0.05f),
            Lit("traySide", null, Palette.Hex(0x9A5F33), 0.2f, 20f, 0.15f),
        };
        public static Material[] Shelf() => new[]
        {
            Lit("shelfTop", Art("tray"), Palette.Hex(0xFFF1DE), 0.15f, 16f, 0.08f),
            Lit("shelfSide", null, Palette.Hex(0xD7A272), 0.2f, 18f, 0.15f),
        };

        /// <summary>Gingham tablecloth (two checks per tile, 1.6 tiles per world unit) with a wooden table edge.</summary>
        public static Material[] Table(float sizeX, float sizeZ)
        {
            if (!Cache.TryGetValue("tableTop", out var top))
            {
                top = Lit("tableTop", Gingham(), Color.white, 0.04f, 8f, 0.02f);
                top.mainTextureScale = new Vector2(sizeX, sizeZ) * 1.6f;
            }
            return new[] { top, Lit("tableSide", null, Palette.Hex(0xB9825A), 0.2f, 18f, 0.1f) };
        }

        static Texture2D Gingham()
        {
            if (Textures.TryGetValue("gingham", out var tex)) return tex;
            const int size = 128;
            tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8, name = "gingham",
            };
            var paper = Palette.Hex(0xFFFBF2);
            var stripe = Palette.Hex(0xDDF0E6);
            var cross = Palette.Hex(0xBFE2D2);
            var rng = new System.Random(4242);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool vx = (x / (size / 2)) == 0, vy = (y / (size / 2)) == 0;
                var c = vx && vy ? cross : vx || vy ? stripe : paper;
                // woven threads: alternating light and dark per pixel row/column, plus a little fibre noise
                float weave = ((x + y) & 1) == 0 ? 1.015f : 0.985f;
                float fibre = 0.985f + (float)rng.NextDouble() * 0.03f;
                c *= weave * fibre;
                c.a = 1f;
                pixels[y * size + x] = c;
            }
            tex.SetPixels(pixels);
            tex.Apply(true);
            return Textures["gingham"] = tex;
        }        public static Material[] Platter() => new[]
        {
            Lit("platterTop", null, Palette.Hex(0xFF9EB8), 0.45f, 50f, 0.25f),
            Lit("platterSide", null, Palette.Hex(0xF26F96), 0.45f, 50f, 0.25f),
        };
        public static Material[] GiftBox() => new[]
        {
            Lit("boxTop", Art("box"), Color.white, 0.2f, 24f, 0.12f),
            Lit("boxSide", RibbonStrip(), Color.white, 0.2f, 24f, 0.15f),
        };
        public static Material Gold() => Lit("gold", null, Palette.Hex(0xE7B33E), 1.1f, 60f, 0.35f);
        public static Material Shadow() => Transparent("shadow", CakeSort.Game.Art.Shadow().texture, new Color(1f, 1f, 1f, 0.85f));
        public static Material Glow() => Transparent("glow", CakeSort.Game.Art.GlowRing().texture, Palette.Glow);
        public static Material Slime() => Transparent("slime", Art("slime"), Color.white);
    }

    static class Make
    {
        public static Transform Node(string name, Transform parent, Vector3 localPosition = default)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }

        public static MeshRenderer Mesh(string name, Transform parent, Mesh mesh, params Material[] materials)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        public static MeshRenderer Decal(string name, Transform parent, Material material, float size, float y)
        {
            var r = Mesh(name, parent, Meshes.Flat(), material);
            r.transform.localPosition = new Vector3(0, y, 0);
            r.transform.localScale = new Vector3(size, 1f, size);
            return r;
        }
    }

    /// <summary>
    /// A plate with up to six wedges, grouped by flavor so like slices sit together. When the slices change, wedges
    /// that stay on the plate glide round to their new places (see <see cref="Tick"/>); wedges that are about to
    /// arrive by flight can be reserved hidden and shown on landing with <see cref="Reveal"/>.
    /// </summary>
    public class PlateView
    {
        public const float PlateTop = 0.036f;
        public const float WedgeRadius = 0.395f;
        public const float WedgeHeight = 0.32f;
        const float Gap = 0.012f;
        const float GlideRate = 14f;
        const int Max = Rules.SlicesPerCake;

        public Transform Root, CakeRoot;
        public MeshRenderer Shadow;
        readonly Transform[] _pivots = new Transform[Max];
        readonly MeshRenderer[] _wedges = new MeshRenderer[Max];
        readonly int[] _flavor = new int[Max];
        readonly int[] _place = new int[Max];
        readonly float[] _angle = new float[Max];
        readonly bool[] _hidden = new bool[Max];
        readonly int[] _wedgeAt = new int[Max];
        public readonly List<int> Shown = new List<int>();
        static MaterialPropertyBlock _block;

        public static PlateView Create(Transform parent, string name, bool shadow = true)
        {
            var view = new PlateView { Root = Make.Node(name, parent) };
            if (shadow) view.Shadow = Make.Decal("Shadow", view.Root, Mats.Shadow(), 1.25f, 0.004f);
            Make.Mesh("Plate", view.Root, Meshes.Plate(), Mats.PlateMat());
            view.CakeRoot = Make.Node("Cake", view.Root, new Vector3(0, PlateTop, 0));
            for (int s = 0; s < Max; s++)
            {
                view._pivots[s] = Make.Node("Slot" + s, view.CakeRoot);
                view._wedges[s] = MakeWedge(view._pivots[s], 0);
                view._wedges[s].transform.localPosition = new Vector3(0, 0, Gap);
                view._wedges[s].gameObject.SetActive(false);
                view._flavor[s] = -1;
                view._place[s] = -1;
            }
            return view;
        }

        public static MeshRenderer MakeWedge(Transform parent, int flavor) =>
            Make.Mesh("Wedge", parent, Meshes.Wedge(WedgeRadius, WedgeHeight), Mats.Wedge(flavor));

        /// <summary>A loose whole cake of one flavor (six wedges), centred on its own node.</summary>
        public static Transform MakeCake(Transform parent, int flavor)
        {
            var root = Make.Node("LooseCake", parent);
            for (int s = 0; s < Max; s++)
            {
                var pivot = Make.Node("Slot" + s, root);
                pivot.localRotation = Quaternion.Euler(0, 60f * s, 0);
                MakeWedge(pivot, flavor).transform.localPosition = new Vector3(0, 0, Gap);
            }
            return root;
        }

        /// <summary>
        /// Shows <paramref name="slices"/> (grouped by flavor). Wedges already on the plate keep their identity and
        /// glide to their new places; new ones appear in place (or stay hidden when their index is in
        /// <paramref name="hidden"/>), and wedges no longer needed vanish at once.
        /// </summary>
        public void SetSlices(IList<int> slices, ICollection<int> hidden = null)
        {
            var next = slices == null ? new List<int>() : GroupByFlavor(slices);
            var claimed = new bool[Max];
            var assign = new int[next.Count];
            for (int i = 0; i < next.Count; i++)
            {
                assign[i] = -1;
                if (hidden != null && hidden.Contains(i)) continue;
                float target = 60f * i, bestDistance = float.MaxValue;
                for (int w = 0; w < Max; w++)
                {
                    if (claimed[w] || _place[w] < 0 || _hidden[w] || _flavor[w] != next[i]) continue;
                    float d = Mathf.Abs(Mathf.DeltaAngle(_angle[w], target));
                    if (d < bestDistance) { bestDistance = d; assign[i] = w; }
                }
                if (assign[i] >= 0) claimed[assign[i]] = true;
            }
            for (int w = 0; w < Max; w++)
            {
                if (claimed[w]) continue;
                _place[w] = -1;
                _hidden[w] = false;
                _wedges[w].gameObject.SetActive(false);
            }
            for (int i = 0; i < next.Count; i++)
            {
                int w = assign[i];
                if (w < 0)
                {
                    w = System.Array.FindIndex(claimed, c => !c);
                    claimed[w] = true;
                    assign[i] = w;
                    if (_flavor[w] != next[i]) _wedges[w].sharedMaterials = Mats.Wedge(next[i]);
                    _flavor[w] = next[i];
                    _angle[w] = 60f * i;
                    _pivots[w].localRotation = Quaternion.Euler(0, _angle[w], 0);
                    _hidden[w] = hidden != null && hidden.Contains(i);
                    _wedges[w].gameObject.SetActive(!_hidden[w]);
                }
                _place[w] = i;
                _wedgeAt[i] = w;
            }
            Shown.Clear();
            Shown.AddRange(next);
        }

        /// <summary>Shows a wedge that was reserved hidden in <see cref="SetSlices"/>.</summary>
        public void Reveal(int index)
        {
            if (index < 0 || index >= Shown.Count) return;
            int w = _wedgeAt[index];
            _hidden[w] = false;
            _wedges[w].gameObject.SetActive(true);
        }

        /// <summary>Advances the glide of wedges towards their places.</summary>
        public void Tick(float deltaTime)
        {
            float k = 1f - Mathf.Exp(-GlideRate * deltaTime);
            for (int w = 0; w < Max; w++)
            {
                if (_place[w] < 0) continue;
                float target = 60f * _place[w];
                if (Mathf.Abs(Mathf.DeltaAngle(_angle[w], target)) < 0.2f) _angle[w] = target;
                else _angle[w] = Mathf.LerpAngle(_angle[w], target, k);
                _pivots[w].localRotation = Quaternion.Euler(0, _angle[w], 0);
            }
        }

        int WedgeAt(int index) => Shown.Count == 0 ? 0 : _wedgeAt[Mathf.Clamp(index, 0, Shown.Count - 1)];

        public Vector3 WedgePosition(int index) => _wedges[WedgeAt(index)].transform.position;
        public Quaternion WedgeRotation(int index) => _wedges[WedgeAt(index)].transform.rotation;

        /// <summary>Where the wedge at <paramref name="index"/> rests once it has settled.</summary>
        public Vector3 RestPosition(int index) => CakeRoot.TransformPoint(Quaternion.Euler(0, 60f * index, 0) * new Vector3(0, 0, Gap));
        public Quaternion RestRotation(int index) => CakeRoot.rotation * Quaternion.Euler(0, 60f * index, 0);

        public void SetFlash(float amount)
        {
            _block = _block ?? new MaterialPropertyBlock();
            _block.SetFloat("_Flash", amount);
            foreach (var w in _wedges) w.SetPropertyBlock(amount > 0f ? _block : null);
        }

        /// <summary>Keeps the blob shadow on the surface below while the plate is lifted.</summary>
        public void UpdateShadow(float groundY)
        {
            if (Shadow == null) return;
            var p = Root.position;
            float lift = Mathf.Max(0f, p.y - groundY);
            Shadow.transform.position = new Vector3(p.x + lift * 0.12f, groundY + 0.004f, p.z - lift * 0.18f);
            float scale = 1.25f * (1f + lift * 0.25f);
            Shadow.transform.localScale = new Vector3(scale, 1f, scale);
        }

        public static List<int> GroupByFlavor(IList<int> slices)
        {
            var order = new List<int>();
            foreach (int f in slices) if (!order.Contains(f)) order.Add(f);
            var grouped = new List<int>(slices.Count);
            foreach (int f in order) foreach (int s in slices) if (s == f) grouped.Add(s);
            return grouped;
        }
    }
    /// <summary>A waiting piece: one plate, or two plates on a shared pink platter (linked pair).</summary>
    public class PieceView
    {
        public Transform Root;
        public Piece Piece;
        public PlateView[] Plates;
        public Transform Platter, PlatterShadow;
        public const float PlatterHeight = 0.05f;

        public static Vector3[] Offsets(Link link)
        {
            switch (link)
            {
                case Link.Horizontal: return new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0) };
                case Link.Vertical: return new[] { new Vector3(0, 0, 0.5f), new Vector3(0, 0, -0.5f) };
                default: return new[] { Vector3.zero };
            }
        }

        public static PieceView Create(Transform parent, Piece piece)
        {
            var view = new PieceView { Root = Make.Node("Piece", parent), Piece = piece };
            var offsets = Offsets(piece.Link);
            float lift = piece.IsLinked ? PlatterHeight : 0f;
            if (piece.IsLinked)
            {
                var platter = Make.Mesh("Platter", view.Root, Meshes.Slab(0.98f, 0.49f, 0.49f, PlatterHeight, 0.025f), Mats.Platter());
                platter.transform.localRotation = Quaternion.Euler(0, piece.Link == Link.Vertical ? 90f : 0f, 0);
                view.Platter = platter.transform;
                view.PlatterShadow = Make.Decal("PlatterShadow", platter.transform, Mats.Shadow(), 1f, 0.003f).transform;
                view.PlatterShadow.localScale = new Vector3(2.6f, 1f, 1.35f);
            }
            view.Plates = new PlateView[piece.Cakes.Length];
            for (int k = 0; k < piece.Cakes.Length; k++)
            {
                var plate = PlateView.Create(view.Root, "Plate" + k, !piece.IsLinked);
                plate.Root.localPosition = offsets[k] + Vector3.up * lift;
                plate.SetSlices(piece.Cakes[k].Slices);
                view.Plates[k] = plate;
            }
            return view;
        }

        public Vector3 FirstPlatePosition => Plates[0].Root.position;

        public void UpdateShadows(float groundY)
        {
            foreach (var plate in Plates) plate.UpdateShadow(groundY);
            if (PlatterShadow == null) return;
            var p = Platter.position;
            float lift = Mathf.Max(0f, p.y - groundY);
            PlatterShadow.position = new Vector3(p.x + lift * 0.12f, groundY + 0.004f, p.z - lift * 0.18f);
        }
    }

    /// <summary>Gift box, glass cloche, or slime sitting on a board cell.</summary>
    public class ObstacleView
    {
        public Transform Box, BoxLid, Cloche, Dome, Slime;

        public static ObstacleView Create(Transform cell)
        {
            var view = new ObstacleView();
            view.Box = Make.Node("GiftBox", cell);
            Make.Mesh("Body", view.Box, Meshes.Box(0.38f, 0.44f, 0.38f), Mats.GiftBox());
            view.BoxLid = Make.Node("Lid", view.Box, new Vector3(0, 0.42f, 0));
            Make.Mesh("LidMesh", view.BoxLid, Meshes.Box(0.41f, 0.13f, 0.41f), Mats.GiftBox());
            Make.Decal("Shadow", view.Box, Mats.Shadow(), 1.3f, 0.004f);
            view.Box.gameObject.SetActive(false);

            view.Cloche = Make.Node("Cloche", cell);
            Make.Mesh("Ring", view.Cloche, Meshes.GoldRing(), Mats.Gold());
            view.Dome = Make.Node("Dome", view.Cloche, new Vector3(0, 0.04f, 0));
            Make.Mesh("Glass", view.Dome, Meshes.Dome(), Mats.Glass());
            Make.Mesh("Knob", view.Dome, Meshes.Knob(), Mats.Gold()).transform.localPosition = new Vector3(0, 0.49f, 0);
            view.Cloche.gameObject.SetActive(false);

            view.Slime = Make.Decal("Slime", cell, Mats.Slime(), 0.95f, PlateView.PlateTop + 0.01f).transform;
            view.Slime.gameObject.SetActive(false);
            return view;
        }
    }
}
