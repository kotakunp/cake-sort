// Synthesized sound effects and a soft music-box loop, so the project ships no audio files.
using System.Collections.Generic;
using UnityEngine;

namespace CakeSort.Game
{
    public static class AudioTones
    {
        const int Rate = 44100;
        static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();

        public static AudioClip Pop() => Make("pop", 0.12f, t =>
            Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(900f, 380f, t / 0.12f) * t) * Mathf.Exp(-t * 32f) * 0.55f);

        public static AudioClip Click() => Make("click", 0.05f, t =>
            Mathf.Sin(2f * Mathf.PI * 1400f * t) * Mathf.Exp(-t * 90f) * 0.4f);

        public static AudioClip Whoosh()
        {
            var rng = new System.Random(7);
            float lp = 0f;
            return Make("whoosh", 0.22f, t =>
            {
                float noise = (float)rng.NextDouble() * 2f - 1f;
                lp += (noise - lp) * Mathf.Lerp(0.05f, 0.35f, Mathf.Sin(t / 0.22f * Mathf.PI));
                return lp * Mathf.Sin(t / 0.22f * Mathf.PI) * 0.5f;
            });
        }

        public static AudioClip Chime() => Make("chime", 0.6f, t =>
            (Bell(1046.5f, t) + Bell(1318.5f, t - 0.06f) * 0.8f + Bell(1568f, t - 0.12f) * 0.7f) * 0.28f);

        public static AudioClip Thud() => Make("thud", 0.25f, t =>
            (Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(160f, 60f, t / 0.25f) * t) * Mathf.Exp(-t * 14f)) * 0.7f);

        public static AudioClip Boom()
        {
            var rng = new System.Random(3);
            return Make("boom", 0.6f, t =>
                (((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 9f) * 0.5f
                 + Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(120f, 40f, t / 0.6f) * t) * Mathf.Exp(-t * 6f) * 0.6f));
        }

        public static AudioClip Error() => Make("error", 0.2f, t =>
            Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 140f * t)) * Mathf.Exp(-t * 16f) * 0.18f);

        public static AudioClip Win() => Make("win", 1.4f, t =>
        {
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            float sum = 0f;
            for (int i = 0; i < notes.Length; i++) sum += Bell(notes[i], t - i * 0.13f) * (i == 3 ? 1.2f : 0.8f);
            return sum * 0.25f;
        });

        public static AudioClip Lose() => Make("lose", 1.0f, t =>
        {
            float[] notes = { 392f, 349.23f, 311.13f, 261.63f };
            float sum = 0f;
            for (int i = 0; i < notes.Length; i++) sum += Bell(notes[i], t - i * 0.18f);
            return sum * 0.25f;
        });

        /// <summary>Gentle 16-bar pentatonic music-box loop.</summary>
        public static AudioClip Music()
        {
            if (Cache.TryGetValue("music", out var cached)) return cached;
            float[] scale = { 523.25f, 587.33f, 659.25f, 783.99f, 880f, 1046.5f };
            int[] melody = { 0, 2, 4, 2, 3, 1, 2, -1, 0, 2, 4, 5, 4, 2, 3, -1, 2, 3, 4, 3, 2, 1, 0, -1, 1, 2, 3, 1, 0, 0, -1, -1 };
            int[] bass = { 0, 0, 3, 3, 1, 1, 4, 4 };
            const float beat = 0.42f;
            float length = melody.Length * beat;
            int count = (int)(length * Rate);
            var data = new float[count];
            for (int n = 0; n < melody.Length; n++)
            {
                int start = (int)(n * beat * Rate);
                if (melody[n] >= 0) AddNote(data, start, scale[melody[n]], 0.11f, 1.2f);
                if (n % 4 == 0) AddNote(data, start, scale[bass[(n / 4) % bass.Length]] / 4f, 0.07f, 1.6f);
            }
            var clip = AudioClip.Create("music", count, 1, Rate, false);
            clip.SetData(data, 0);
            Cache["music"] = clip;
            return clip;
        }

        static void AddNote(float[] data, int start, float frequency, float volume, float seconds)
        {
            int length = Mathf.Min((int)(seconds * Rate), data.Length - start);
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)Rate;
                data[start + i] += Bell(frequency, t) * volume;
            }
        }

        static float Bell(float frequency, float t)
        {
            if (t < 0f) return 0f;
            float env = Mathf.Exp(-t * 5.5f) * Mathf.Clamp01(t * 400f);
            return (Mathf.Sin(2f * Mathf.PI * frequency * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * frequency * t)
                    + 0.12f * Mathf.Sin(6.01f * Mathf.PI * frequency * t)) * env;
        }

        static AudioClip Make(string name, float seconds, System.Func<float, float> sample)
        {
            if (Cache.TryGetValue(name, out var cached)) return cached;
            int count = (int)(seconds * Rate);
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float fade = Mathf.Clamp01((count - i) / (Rate * 0.01f));
                data[i] = Mathf.Clamp(sample(i / (float)Rate), -1f, 1f) * fade;
            }
            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            Cache[name] = clip;
            return clip;
        }
    }
}
