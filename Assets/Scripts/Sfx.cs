using UnityEngine;

namespace Reins
{
    /// <summary>
    /// All audio is synthesised at startup; there are no audio assets in the project.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        const int Rate = 44100;

        AudioSource oneShots, hooves, wind;
        AudioClip hoof, chime, crash, snap, caw, horn;
        System.Random rnd = new System.Random(5);

        public static Sfx Create(Transform parent)
        {
            var go = new GameObject("Sfx");
            go.transform.SetParent(parent, false);
            return go.AddComponent<Sfx>();
        }

        void Awake()
        {
            oneShots = gameObject.AddComponent<AudioSource>();
            hooves = gameObject.AddComponent<AudioSource>();
            wind = gameObject.AddComponent<AudioSource>();
            foreach (var s in new[] { oneShots, hooves, wind })
            {
                s.playOnAwake = false;
                s.spatialBlend = 0f;
            }

            hoof = Make("Hoof", 0.10f, t =>
                Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(150f, 55f, t / 0.10f) * t) * Mathf.Exp(-t * 42f) +
                Noise() * Mathf.Exp(-t * 95f) * 0.5f);

            chime = Make("Chime", 0.55f, t =>
                (Mathf.Sin(2f * Mathf.PI * 880f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 1320f * t) +
                 0.25f * Mathf.Sin(2f * Mathf.PI * 1760f * t)) * Mathf.Exp(-t * 7f) * 0.4f);

            float lp = 0f;
            crash = Make("Crash", 0.6f, t =>
            {
                lp = Mathf.Lerp(lp, Noise(), 0.22f);
                return lp * Mathf.Exp(-t * 7f) * 2.2f + Mathf.Sin(2f * Mathf.PI * 52f * t) * Mathf.Exp(-t * 10f) * 0.8f;
            });

            snap = Make("Snap", 0.22f, t =>
                Noise() * Mathf.Exp(-t * 60f) * 0.9f +
                Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(1100f, 180f, t / 0.22f) * t) * Mathf.Exp(-t * 16f) * 0.6f);

            caw = Make("Caw", 0.5f, t =>
            {
                float u = t % 0.25f;
                float f = Mathf.Lerp(620f, 430f, u / 0.25f);
                float saw = 2f * ((f * t) % 1f) - 1f;
                return (saw * 0.6f + Noise() * 0.35f) * Mathf.Sin(Mathf.PI * u / 0.25f) * 0.45f;
            });

            horn = Make("Horn", 1.4f, t =>
            {
                float f = t < 0.45f ? 392f : 523.25f;
                float env = Mathf.Clamp01(t * 20f) * Mathf.Clamp01((1.4f - t) * 4f);
                return (Mathf.Sin(2f * Mathf.PI * f * t) + 0.4f * Mathf.Sin(4f * Mathf.PI * f * t) +
                        0.2f * Mathf.Sin(6f * Mathf.PI * f * t)) * env * 0.3f;
            });

            // Looping wind: brown noise, crossfaded at the seam.
            int n = Rate * 3;
            var data = new float[n];
            float b = 0f;
            for (int i = 0; i < n; i++)
            {
                b = Mathf.Clamp(b + Noise() * 0.04f, -1f, 1f) * 0.998f;
                data[i] = b;
            }
            int fade = Rate / 4;
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                data[n - fade + i] = Mathf.Lerp(data[n - fade + i], data[i], k);
            }
            var windClip = AudioClip.Create("Wind", n, 1, Rate, false);
            windClip.SetData(data, 0);
            wind.clip = windClip;
            wind.loop = true;
            wind.volume = 0f;
            wind.Play();
        }

        float Noise() { return (float)rnd.NextDouble() * 2f - 1f; }

        AudioClip Make(string clipName, float seconds, System.Func<float, float> wave)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(wave(i / (float)Rate), -1f, 1f);
            var clip = AudioClip.Create(clipName, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public void Hoof(float speed01)
        {
            hooves.pitch = 0.85f + (float)rnd.NextDouble() * 0.3f;
            hooves.PlayOneShot(hoof, 0.25f + 0.35f * speed01);
        }

        public void Chime(int streak)
        {
            oneShots.pitch = 1f;
            // Walk up a pentatonic scale as the streak builds.
            float[] steps = { 0f, 2f, 4f, 7f, 9f, 12f, 14f, 16f };
            float semis = steps[Mathf.Min(streak, steps.Length - 1)];
            var src = hooves; // reuse a source so the pitch change does not bend other one-shots
            src.pitch = Mathf.Pow(2f, semis / 12f);
            src.PlayOneShot(chime, 0.5f);
        }

        public void Crash() { oneShots.pitch = 1f; oneShots.PlayOneShot(crash, 0.9f); }
        public void Snap() { oneShots.pitch = 1f; oneShots.PlayOneShot(snap, 0.9f); }
        public void Caw() { oneShots.pitch = 1f; oneShots.PlayOneShot(caw, 0.6f); }
        public void Horn() { oneShots.pitch = 1f; oneShots.PlayOneShot(horn, 0.7f); }

        public void SetWind(float speed01)
        {
            wind.volume = Mathf.Lerp(wind.volume, 0.08f + 0.5f * speed01 * speed01, 0.1f);
            wind.pitch = 0.7f + 0.6f * speed01;
        }
    }
}
