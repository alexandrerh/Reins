using System.Collections.Generic;
using UnityEngine;

namespace Reins
{
    public class Obstacle
    {
        public Vector2 A, B;          // world XZ segment (A == B for a boulder)
        public float Radius;
        public bool IsLog;
        public bool Spent;
        public GameObject Visual;
        public float Z => (A.y + B.y) * 0.5f;
        public Vector2 Mid => (A + B) * 0.5f;
    }

    public class Pickup
    {
        public Vector3 Pos;
        public Transform Visual;
        public bool Taken;
    }

    public class Spook
    {
        public float Z;
        public float Side;            // -1 = left of the road, +1 = right
        public bool Fired;
        public Vector3 Perch;
        public GameObject Crows;
    }

    /// <summary>
    /// The ridge road. Everything about the course is a pure function of distance along it (z),
    /// so gameplay code can ask "where is the road here?" without touching the generated mesh.
    /// </summary>
    public class Track
    {
        public const float Length = 1400f;
        const float MeshStart = -70f;
        const float MeshEnd = Length + 110f;
        const float Step = 2f;

        public readonly List<Obstacle> Obstacles = new List<Obstacle>();
        public readonly List<Pickup> Pickups = new List<Pickup>();
        public readonly List<Spook> Spooks = new List<Spook>();

        System.Random rnd;

        // ---------------------------------------------------------------- course shape

        public float CenterX(float z)
        {
            float ramp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 170f, z));
            return ramp * (15f * Mathf.Sin(z / 58f) + 6f * Mathf.Sin(z / 24f + 1.3f));
        }

        public float Height(float z)
        {
            return 2.2f * Mathf.Sin(z / 47f) + 0.8f * Mathf.Sin(z / 21f + 2f);
        }

        /// <summary>Half the drivable width. The ridge pinches in a few times in the back half.</summary>
        public float HalfWidth(float z)
        {
            float pinch = Mathf.Max(0f, Mathf.Sin(z / 95f - 1.2f));
            float late = Mathf.InverseLerp(250f, 600f, z);
            return 5.2f - 1.5f * pinch * pinch * late;
        }

        /// <summary>Road direction at z as a heading in degrees (0 = +Z, positive = towards +X).</summary>
        public float TangentAngle(float z)
        {
            return Mathf.Atan2(CenterX(z + 0.5f) - CenterX(z - 0.5f), 1f) * Mathf.Rad2Deg;
        }

        public Vector3 Point(float z, float lateral)
        {
            return new Vector3(CenterX(z) + lateral, Height(z) + Drop(Mathf.Abs(lateral) - HalfWidth(z)), z);
        }

        /// <summary>How far the ground has fallen away, given distance past the road edge.</summary>
        public static float Drop(float beyondEdge)
        {
            if (beyondEdge <= 0f) return 0f;
            if (beyondEdge < 1.2f) return Mathf.Lerp(0f, -0.15f, beyondEdge / 1.2f);
            if (beyondEdge < 6f) return Mathf.Lerp(-0.15f, -3.5f, (beyondEdge - 1.2f) / 4.8f);
            return Mathf.Lerp(-3.5f, -38f, (beyondEdge - 6f) / 39f);
        }

        // ---------------------------------------------------------------- generation

        public void Generate(Transform root, int seed)
        {
            rnd = new System.Random(seed);
            BuildRoadMesh(root);
            BuildScenery(root);
            BuildCourse(root);
            BuildTown(root);
        }

        float R(float a, float b) { return a + (float)rnd.NextDouble() * (b - a); }

        void BuildRoadMesh(Transform root)
        {
            float[] yOff = { -38f, -3.5f, -0.15f, 0f, 0.06f, 0f, -0.15f, -3.5f, -38f };
            Color cliff = new Color(0.17f, 0.16f, 0.25f);
            Color slope = new Color(0.20f, 0.29f, 0.25f);
            Color verge = new Color(0.31f, 0.36f, 0.27f);
            Color rut = new Color(0.45f, 0.36f, 0.30f);
            Color crown = new Color(0.53f, 0.43f, 0.35f);
            Color[] cols = { cliff, slope, verge, rut, crown, rut, verge, slope, cliff };

            int rings = Mathf.CeilToInt((MeshEnd - MeshStart) / Step) + 1;
            var verts = new Vector3[rings * 9];
            var colors = new Color[rings * 9];
            var tris = new int[(rings - 1) * 8 * 6];

            for (int i = 0; i < rings; i++)
            {
                float z = MeshStart + i * Step;
                float w = HalfWidth(z);
                float cx = CenterX(z);
                float y = Height(z);
                float[] xOff = { -(w + 45f), -(w + 6f), -(w + 1.2f), -w, 0f, w, w + 1.2f, w + 6f, w + 45f };
                float shade = 0.92f + 0.16f * Mathf.PerlinNoise(z * 0.21f, 4.2f);
                for (int j = 0; j < 9; j++)
                {
                    verts[i * 9 + j] = new Vector3(cx + xOff[j], y + yOff[j], z);
                    colors[i * 9 + j] = cols[j] * shade;
                }
            }

            int t = 0;
            for (int i = 0; i < rings - 1; i++)
            {
                for (int j = 0; j < 8; j++)
                {
                    int a = i * 9 + j, b = a + 9, c = a + 1, d = b + 1;
                    tris[t++] = a; tris[t++] = b; tris[t++] = d;
                    tris[t++] = a; tris[t++] = d; tris[t++] = c;
                }
            }

            var mesh = new Mesh { name = "Ridge" };
            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Build.MeshObject(mesh, root, Vector3.zero, Vector3.one, Build.Lit(Color.white));
        }

        void BuildScenery(Transform root)
        {
            var scenery = Build.Empty("Scenery", root, Vector3.zero);
            Color trunk = new Color(0.22f, 0.16f, 0.14f);
            Color[] greens =
            {
                new Color(0.10f, 0.25f, 0.24f), new Color(0.13f, 0.30f, 0.24f), new Color(0.09f, 0.21f, 0.23f)
            };
            Color post = new Color(0.78f, 0.74f, 0.66f);
            Color stone = new Color(0.30f, 0.29f, 0.38f);

            for (float z = MeshStart + 10f; z < MeshEnd - 10f; z += 5f)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float w = HalfWidth(z);
                    double roll = rnd.NextDouble();
                    if (roll < 0.52)
                    {
                        float lateral = side * (w + R(2.6f, 12f));
                        Vector3 p = Point(z + R(-2f, 2f), lateral);
                        float s = R(0.8f, 1.7f);
                        Color g = greens[rnd.Next(greens.Length)];
                        Build.Cylinder(scenery, p + Vector3.up * 0.5f * s, new Vector3(0.3f, 0.7f, 0.3f) * s, trunk);
                        Build.Cone(scenery, p + Vector3.up * 0.8f * s, new Vector3(2.3f, 2.7f, 2.3f) * s, g);
                        Build.Cone(scenery, p + Vector3.up * 2.2f * s, new Vector3(1.6f, 2.3f, 1.6f) * s, g);
                    }
                    else if (roll < 0.60)
                    {
                        Vector3 p = Point(z, side * (w + R(1.8f, 8f)));
                        float s = R(0.5f, 1.3f);
                        Build.Rock(scenery, p + Vector3.up * 0.2f * s, new Vector3(s, s * 0.8f, s), stone, rnd.Next(4), R(0f, 360f));
                    }
                }

                // Whitewashed marker posts every 10 m so the edge reads at a gallop.
                if (Mathf.Abs(z % 10f) < 0.01f || Mathf.Abs(z % 10f - 10f) < 0.01f)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 p = Point(z, side * (HalfWidth(z) + 0.45f));
                        Build.Box(scenery, p + Vector3.up * 0.4f, new Vector3(0.14f, 0.9f, 0.14f), post);
                    }
                }
            }

            // Far peaks for a horizon line.
            Color peak = new Color(0.15f, 0.14f, 0.26f);
            for (int k = 0; k < 13; k++)
            {
                float z = -60f + k * 135f;
                float side = (k % 2 == 0) ? -1f : 1f;
                float size = R(150f, 240f);
                Build.Cone(scenery, new Vector3(CenterX(z) + side * R(130f, 210f), -45f, z + R(-30f, 30f)),
                    new Vector3(size, R(90f, 170f), size), peak);
            }
        }

        void BuildCourse(Transform root)
        {
            var course = Build.Empty("Course", root, Vector3.zero);
            float z = 125f;
            while (z < Length - 90f)
            {
                float progress = z / Length;
                float w = HalfWidth(z);
                int kind = rnd.Next(10);

                if (kind < 4)
                {
                    AddRock(course, z, R(-w + 1.3f, w - 1.3f), R(0.9f, 1.4f));
                }
                else if (kind < 6)
                {
                    // Two boulders with a coach-and-a-bit gap between them.
                    float gap = R(-w + 2.9f, w - 2.9f);
                    AddRock(course, z, gap - 2.9f, 1.0f);
                    AddRock(course, z + R(-1f, 1f), gap + 2.9f, 1.0f);
                }
                else if (kind < 8)
                {
                    AddLog(course, z, rnd.Next(2) == 0 ? -1f : 1f, w);
                }
                else
                {
                    AddSpook(course, z, rnd.Next(2) == 0 ? -1f : 1f, w);
                }

                // A short ribbon of letters on the far side of each hazard.
                float lat = R(-w + 1.4f, w - 1.4f);
                float drift = R(-0.9f, 0.9f);
                for (int i = 0; i < 3; i++)
                {
                    float lz = z + 8f + i * 3.2f;
                    float ll = Mathf.Clamp(lat + drift * i, -HalfWidth(lz) + 1.2f, HalfWidth(lz) - 1.2f);
                    AddLetter(course, lz, ll);
                }

                z += Mathf.Lerp(R(34f, 46f), R(22f, 30f), progress);
            }
        }

        void AddRock(Transform parent, float z, float lateral, float radius)
        {
            Vector3 p = Point(z, lateral);
            var go = Build.Rock(parent, p + Vector3.up * radius * 0.35f,
                new Vector3(radius * 1.15f, radius * 0.95f, radius * 1.15f),
                new Color(0.36f, 0.35f, 0.44f), rnd.Next(4), R(0f, 360f));
            Obstacles.Add(new Obstacle
            {
                A = new Vector2(p.x, p.z), B = new Vector2(p.x, p.z), Radius = radius, Visual = go
            });
        }

        void AddLog(Transform parent, float z, float side, float w)
        {
            Vector3 a = Point(z + R(-1.5f, 1.5f), side * (w - 0.2f));
            Vector3 b = Point(z + R(-1.5f, 1.5f), side * (w - 0.2f) - side * w * 1.1f);
            a.y = b.y = Height(z) + 0.38f;
            Vector3 dir = b - a;
            var go = Build.Cylinder(parent, (a + b) * 0.5f, new Vector3(0.8f, dir.magnitude * 0.5f, 0.8f),
                new Color(0.33f, 0.22f, 0.16f));
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            Obstacles.Add(new Obstacle
            {
                A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z), Radius = 0.42f, IsLog = true, Visual = go
            });
        }

        void AddSpook(Transform parent, float z, float side, float w)
        {
            // A dead tree at the roadside with crows roosting in it.
            Vector3 p = Point(z, side * (w + 0.9f));
            Color bark = new Color(0.13f, 0.11f, 0.13f);
            var tree = Build.Empty("CrowTree", parent, p);
            Build.Box(tree, new Vector3(0, 1.7f, 0), new Vector3(0.28f, 3.6f, 0.28f), bark, new Vector3(0, 0, side * 5f));
            Build.Box(tree, new Vector3(-side * 0.9f, 2.9f, 0), new Vector3(2.2f, 0.16f, 0.16f), bark, new Vector3(0, 0, -side * 12f));
            Build.Box(tree, new Vector3(side * 0.5f, 2.3f, 0), new Vector3(1.1f, 0.13f, 0.13f), bark, new Vector3(0, 0, side * 25f));

            var crows = Build.Empty("Crows", tree, Vector3.zero);
            Color feather = new Color(0.04f, 0.04f, 0.06f);
            for (int i = 0; i < 4; i++)
            {
                Build.Box(crows.transform, new Vector3(-side * (0.2f + i * 0.45f), 3.12f + i * 0.09f, 0),
                    new Vector3(0.18f, 0.2f, 0.36f), feather);
            }

            Spooks.Add(new Spook
            {
                Z = z, Side = side, Crows = crows.gameObject, Perch = p + Vector3.up * 3.1f
            });
        }

        void AddLetter(Transform parent, float z, float lateral)
        {
            Vector3 p = Point(z, lateral) + Vector3.up * 1.25f;
            var letter = Build.Empty("Letter", parent, p);
            Build.GlowBox(letter, Vector3.zero, new Vector3(0.62f, 0.42f, 0.05f), new Color(1f, 0.93f, 0.72f));
            Build.GlowBox(letter, new Vector3(0, 0, 0), new Vector3(0.14f, 0.14f, 0.08f), new Color(0.85f, 0.16f, 0.14f));
            Pickups.Add(new Pickup { Pos = p, Visual = letter });
        }

        void BuildTown(Transform root)
        {
            float z = Length;
            float w = HalfWidth(z);
            var town = Build.Empty("Town", root, new Vector3(CenterX(z), Height(z), z));
            town.localEulerAngles = new Vector3(0, TangentAngle(z), 0);

            Color wall = new Color(0.36f, 0.33f, 0.40f);
            Color roof = new Color(0.20f, 0.14f, 0.18f);
            Color window = new Color(1f, 0.78f, 0.36f);

            // Gatehouse.
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (w + 2.2f);
                Build.Box(town, new Vector3(x, 3.5f, 0), new Vector3(3.2f, 11f, 3.2f), wall);
                Build.Cone(town, new Vector3(x, 9f, 0), new Vector3(4.6f, 3.6f, 4.6f), roof);
                Build.GlowBox(town, new Vector3(x, 6f, -1.62f), new Vector3(0.7f, 1.1f, 0.06f), window);
                Build.GlowBox(town, new Vector3(x - side * 1.62f, 2.6f, -0.4f), new Vector3(0.06f, 0.8f, 0.5f), window);
            }
            Build.Box(town, new Vector3(0, 7.6f, 0), new Vector3(w * 2f + 2f, 1.8f, 2.2f), wall);
            Build.Box(town, new Vector3(0, 8.7f, 0), new Vector3(w * 2f + 2.6f, 0.4f, 2.8f), roof);
            Build.GlowBox(town, new Vector3(0, 7.6f, -1.12f), new Vector3(3.4f, 0.9f, 0.06f), new Color(0.95f, 0.88f, 0.70f));

            // A few houses beyond the gate.
            for (int i = 0; i < 9; i++)
            {
                float side = (i % 2 == 0) ? -1f : 1f;
                float hz = 12f + i * 9f + R(-2f, 2f);
                float hx = side * (w + R(5f, 13f));
                float hw = R(5f, 8f), hh = R(4f, 7f);
                Build.Box(town, new Vector3(hx, hh * 0.5f - 2.5f, hz), new Vector3(hw, hh + 5f, hw), wall * R(0.85f, 1.1f));
                Build.Cone(town, new Vector3(hx, hh, hz), new Vector3(hw * 1.5f, R(2.5f, 4f), hw * 1.5f), roof);
                Build.GlowBox(town, new Vector3(hx - side * hw * 0.505f, hh * 0.55f, hz + R(-1.5f, 1.5f)),
                    new Vector3(0.06f, 1f, 0.8f), window);
                Build.GlowBox(town, new Vector3(hx + R(-1.5f, 1.5f), hh * 0.5f, hz - hw * 0.505f),
                    new Vector3(0.8f, 1f, 0.06f), window);
            }
        }

        // ---------------------------------------------------------------- queries

        /// <summary>Distance in the XZ plane from a point to an obstacle's core segment.</summary>
        public static float Distance(Vector2 p, Obstacle o)
        {
            Vector2 ab = o.B - o.A;
            float len2 = ab.sqrMagnitude;
            float t = len2 < 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(p - o.A, ab) / len2);
            return Vector2.Distance(p, o.A + ab * t);
        }
    }
}
