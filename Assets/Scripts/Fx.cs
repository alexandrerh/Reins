using UnityEngine;

namespace Reins
{
    /// <summary>
    /// A fixed pool of little cubes used for every particle-ish effect in the game:
    /// hoof dust, splinters, scattered letters and startled crows.
    /// </summary>
    public class Fx : MonoBehaviour
    {
        const int Count = 160;

        struct Bit
        {
            public Transform T;
            public MeshRenderer R;
            public Vector3 Velocity;
            public Vector3 Spin;
            public float Life, MaxLife, Size, Gravity;
        }

        Bit[] bits;
        int next;

        public static Fx Create(Transform parent)
        {
            var go = new GameObject("Fx");
            go.transform.SetParent(parent, false);
            return go.AddComponent<Fx>();
        }

        void Awake()
        {
            bits = new Bit[Count];
            for (int i = 0; i < Count; i++)
            {
                var go = Build.Box(transform, Vector3.zero, Vector3.one, Color.white);
                go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.SetActive(false);
                bits[i].T = go.transform;
                bits[i].R = go.GetComponent<MeshRenderer>();
            }
        }

        public void Burst(Vector3 pos, Color color, int count, float speed, float life, float gravity, float size,
            Vector3 bias = default, bool glow = false)
        {
            var mat = glow ? Build.Glow(color) : Build.Lit(color);
            for (int i = 0; i < count; i++)
            {
                int k = next;
                next = (next + 1) % Count;
                bits[k].T.gameObject.SetActive(true);
                bits[k].T.position = pos + Random.insideUnitSphere * 0.3f;
                bits[k].T.rotation = Random.rotation;
                bits[k].R.sharedMaterial = mat;
                bits[k].Velocity = Random.insideUnitSphere * speed + bias;
                bits[k].Spin = Random.insideUnitSphere * 360f;
                bits[k].Life = bits[k].MaxLife = life * Random.Range(0.7f, 1.2f);
                bits[k].Size = size * Random.Range(0.6f, 1.3f);
                bits[k].Gravity = gravity;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < Count; i++)
            {
                if (bits[i].Life <= 0f) continue;
                bits[i].Life -= dt;
                if (bits[i].Life <= 0f)
                {
                    bits[i].T.gameObject.SetActive(false);
                    continue;
                }
                bits[i].Velocity += Vector3.down * bits[i].Gravity * dt;
                bits[i].T.position += bits[i].Velocity * dt;
                bits[i].T.Rotate(bits[i].Spin * dt);
                bits[i].T.localScale = Vector3.one * bits[i].Size * Mathf.Clamp01(bits[i].Life / bits[i].MaxLife * 1.6f);
            }
        }
    }
}
