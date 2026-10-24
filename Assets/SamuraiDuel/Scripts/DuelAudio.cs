using UnityEngine;

namespace SamuraiDuel
{
    /// <summary>
    /// Tiny procedural sound bank so the duel needs no audio assets. Clips are synthesised once
    /// (a few KB each, mono 22 kHz) and played through a small pool of 3D AudioSources.
    /// </summary>
    public class DuelAudio : MonoBehaviour
    {
        public enum Sound
        {
            Clang,   // blade on blade / shield
            Flesh,   // blade on body
            Whoosh,  // samurai strike release
            Cue,     // telegraph "shing" before a strike
            Gong     // duel start / end
        }

        private const int SampleRate = 22050;
        private const int PoolSize = 8;

        private static DuelAudio instance;
        private AudioClip[] clips;
        private AudioSource[] pool;
        private int next;

        public static void Play(Sound sound, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            if (instance == null)
            {
                if (!Application.isPlaying)
                    return;
                instance = new GameObject("DuelAudio").AddComponent<DuelAudio>();
            }
            instance.PlayInternal(sound, position, volume, pitch);
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            Build();
        }

        private void Build()
        {
            clips = new[]
            {
                Synth("Clang", 0.45f, t => Metallic(t)),
                Synth("Flesh", 0.18f, t => Thud(t)),
                Synth("Whoosh", 0.3f, t => Whoosh(t)),
                Synth("Cue", 0.35f, t => Shing(t)),
                Synth("Gong", 1.6f, t => Gong(t))
            };

            pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var source = new GameObject("SFX " + i).AddComponent<AudioSource>();
                source.transform.SetParent(transform, false);
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 1f;
                source.maxDistance = 25f;
                source.dopplerLevel = 0f;
                pool[i] = source;
            }
        }

        private void PlayInternal(Sound sound, Vector3 position, float volume, float pitch)
        {
            AudioSource source = pool[next];
            next = (next + 1) % PoolSize;
            source.transform.position = position;
            source.pitch = pitch * Random.Range(0.94f, 1.06f);
            source.PlayOneShot(clips[(int)sound], Mathf.Clamp01(volume));
        }

        // ---- synthesis -------------------------------------------------------------------------

        private static uint noiseState = 0x12345678;

        private static float Noise()
        {
            noiseState ^= noiseState << 13;
            noiseState ^= noiseState >> 17;
            noiseState ^= noiseState << 5;
            return (noiseState / (float)uint.MaxValue) * 2f - 1f;
        }

        private static AudioClip Synth(string clipName, float seconds, System.Func<float, float> generator)
        {
            int length = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[length];
            float peak = 0.0001f;
            for (int i = 0; i < length; i++)
            {
                data[i] = generator(i / (float)SampleRate);
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            float gain = 0.9f / peak;
            for (int i = 0; i < length; i++)
                data[i] *= gain;

            AudioClip clip = AudioClip.Create(clipName, length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Sine(float freq, float t) => Mathf.Sin(2f * Mathf.PI * freq * t);

        private static float Metallic(float t)
        {
            float ring = Sine(1180f, t) * 0.5f + Sine(2630f, t) * 0.35f + Sine(3970f, t) * 0.25f + Sine(5210f, t) * 0.15f;
            float transient = Noise() * Mathf.Exp(-t * 90f);
            return ring * Mathf.Exp(-t * 9f) + transient * 0.8f;
        }

        private static float lowpass;

        private static float Thud(float t)
        {
            lowpass = Mathf.Lerp(lowpass, Noise(), 0.12f);
            return (Sine(95f * (1f - t), t) * 0.8f + lowpass * 2.2f) * Mathf.Exp(-t * 24f);
        }

        private static float Whoosh(float t)
        {
            float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.3f));
            lowpass = Mathf.Lerp(lowpass, Noise(), 0.08f + 0.25f * t);
            return lowpass * env * env;
        }

        private static float Shing(float t)
        {
            float sweep = 2400f + 2200f * t;
            float env = Mathf.Clamp01(t * 30f) * Mathf.Exp(-t * 7f);
            return (Sine(sweep, t) * 0.4f + Noise() * 0.25f) * env;
        }

        private static float Gong(float t)
        {
            float tone = Sine(110f, t) * 0.6f + Sine(171f, t) * 0.35f + Sine(287f, t) * 0.2f + Sine(442f, t) * 0.1f;
            return tone * Mathf.Exp(-t * 2.2f) * Mathf.Clamp01(t * 200f);
        }
    }
}
