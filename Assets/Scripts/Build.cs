using System.Collections.Generic;
using UnityEngine;

namespace Reins
{
    /// <summary>
    /// Small helper layer for assembling the low-poly world from primitives at runtime.
    /// Materials are cached per colour so the whole game runs on a handful of them.
    /// </summary>
    public static class Build
    {
        static Shader litShader, glowShader;
        static readonly Dictionary<int, Material> litCache = new Dictionary<int, Material>();
        static readonly Dictionary<int, Material> glowCache = new Dictionary<int, Material>();
        static Mesh coneMesh;
        static Mesh[] rockMeshes;

        /// <summary>Runtime-created assets do not survive a scene reload, so drop the caches.</summary>
        public static void Reset()
        {
            litCache.Clear();
            glowCache.Clear();
            coneMesh = null;
            rockMeshes = null;
            litShader = Resources.Load<Shader>("ReinsLit");
            glowShader = Resources.Load<Shader>("ReinsGlow");
        }

        static int Key(Color c)
        {
            Color32 k = c;
            return (k.r << 24) | (k.g << 16) | (k.b << 8) | k.a;
        }

        public static Material Lit(Color c)
        {
            int key = Key(c);
            if (!litCache.TryGetValue(key, out var m) || m == null)
            {
                m = new Material(litShader) { color = c, enableInstancing = true };
                litCache[key] = m;
            }
            return m;
        }

        public static Material Glow(Color c)
        {
            int key = Key(c);
            if (!glowCache.TryGetValue(key, out var m) || m == null)
            {
                m = new Material(glowShader) { color = c };
                glowCache[key] = m;
            }
            return m;
        }

        /// <summary>Lit material with an albedo map, for imported models.</summary>
        public static Material Textured(Texture texture, Color tint)
        {
            return new Material(litShader) { color = tint, mainTexture = texture };
        }

        public static Transform Empty(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        public static GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            Place(go, parent, pos, scale, euler);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        public static GameObject Box(Transform parent, Vector3 pos, Vector3 scale, Color color, Vector3 euler = default)
        {
            return Prim(PrimitiveType.Cube, parent, pos, scale, Lit(color), euler);
        }

        public static GameObject GlowBox(Transform parent, Vector3 pos, Vector3 scale, Color color, Vector3 euler = default)
        {
            var go = Prim(PrimitiveType.Cube, parent, pos, scale, Glow(color), euler);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        public static GameObject Cylinder(Transform parent, Vector3 pos, Vector3 scale, Color color, Vector3 euler = default)
        {
            return Prim(PrimitiveType.Cylinder, parent, pos, scale, Lit(color), euler);
        }

        public static GameObject Sphere(Transform parent, Vector3 pos, Vector3 scale, Color color)
        {
            return Prim(PrimitiveType.Sphere, parent, pos, scale, Lit(color));
        }

        public static GameObject MeshObject(Mesh mesh, Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default)
        {
            var go = new GameObject(mesh.name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            Place(go, parent, pos, scale, euler);
            return go;
        }

        public static GameObject Cone(Transform parent, Vector3 pos, Vector3 scale, Color color)
        {
            return MeshObject(ConeMesh(), parent, pos, scale, Lit(color));
        }

        public static GameObject Rock(Transform parent, Vector3 pos, Vector3 scale, Color color, int variant, float yaw)
        {
            var meshes = RockMeshes();
            return MeshObject(meshes[Mathf.Abs(variant) % meshes.Length], parent, pos, scale, Lit(color), new Vector3(0, yaw, 0));
        }

        static void Place(GameObject go, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler)
        {
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localScale = scale;
            t.localEulerAngles = euler;
        }

        /// <summary>Seven-sided faceted cone: base radius 0.5 on y = 0, apex at y = 1.</summary>
        static Mesh ConeMesh()
        {
            if (coneMesh != null) return coneMesh;
            const int sides = 7;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides;
                float a1 = (i + 1) * Mathf.PI * 2f / sides;
                verts.Add(new Vector3(Mathf.Sin(a0) * 0.5f, 0, Mathf.Cos(a0) * 0.5f));
                verts.Add(new Vector3(0, 1, 0));
                verts.Add(new Vector3(Mathf.Sin(a1) * 0.5f, 0, Mathf.Cos(a1) * 0.5f));
                tris.Add(i * 3); tris.Add(i * 3 + 1); tris.Add(i * 3 + 2);
            }
            coneMesh = new Mesh { name = "Cone" };
            coneMesh.SetVertices(verts);
            coneMesh.SetTriangles(tris, 0);
            coneMesh.RecalculateNormals();
            coneMesh.RecalculateBounds();
            return coneMesh;
        }

        /// <summary>A few jittered, flat-shaded icosahedra to use as boulders.</summary>
        static Mesh[] RockMeshes()
        {
            if (rockMeshes != null && rockMeshes[0] != null) return rockMeshes;

            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            Vector3[] ico =
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };

            rockMeshes = new Mesh[4];
            for (int m = 0; m < rockMeshes.Length; m++)
            {
                var rnd = new System.Random(977 + m * 31);
                var jittered = new Vector3[ico.Length];
                for (int i = 0; i < ico.Length; i++)
                {
                    float r = 0.78f + (float)rnd.NextDouble() * 0.44f;
                    jittered[i] = ico[i].normalized * r;
                }
                var verts = new Vector3[faces.Length];
                var tris = new int[faces.Length];
                for (int i = 0; i < faces.Length; i++)
                {
                    verts[i] = jittered[faces[i]];
                    tris[i] = i;
                }
                var mesh = new Mesh { name = "Rock" + m };
                mesh.vertices = verts;
                mesh.triangles = tris;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                rockMeshes[m] = mesh;
            }
            return rockMeshes;
        }
    }
}
