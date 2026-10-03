using System.Collections.Generic;
using UnityEngine;

namespace MoonlitParry
{
    /// <summary>
    /// Cream-white crescent left by a blade while it swings: samples the blade base/tip every frame and draws a
    /// Catmull-Rom smoothed strip that fades with age (texture Resources/HD/FX/trail).
    /// </summary>
    [DefaultExecutionOrder(70)]
    public class SwordTrail : MonoBehaviour
    {
        struct Sample { public Vector3 b, t; public float time; }

        Transform blade;
        Vector2 baseLocal, tipLocal;
        readonly List<Sample> samples = new List<Sample>();
        Mesh mesh;
        MeshRenderer mr;
        bool emitting;
        public float Life = 0.17f;
        public Color Color = new Color(1f, 0.97f, 0.9f, 1f);
        const int Sub = 4;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color> cols = new List<Color>();
        readonly List<int> tris = new List<int>();

        public static SwordTrail Create(Transform blade, Vector2 baseLocal, Vector2 tipLocal, int order)
        {
            var owner = blade;
            while (owner.parent != null && owner.GetComponent<Rigidbody2D>() == null) owner = owner.parent;
            var go = new GameObject("SwordTrail");
            go.transform.SetParent(owner, false);
            var tr = go.AddComponent<SwordTrail>();
            tr.blade = blade;
            tr.baseLocal = baseLocal;
            tr.tipLocal = tipLocal;
            tr.mesh = new Mesh { name = "trail" };
            tr.mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = tr.mesh;
            tr.mr = go.AddComponent<MeshRenderer>();
            var tex = Resources.Load<Texture2D>("HD/FX/trail");
            tr.mr.sharedMaterial = tex != null ? Gfx.HDTexMat(tex) : Gfx.HDMat;
            tr.mr.sortingOrder = order;
            tr.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.mr.receiveShadows = false;
            return tr;
        }

        public void Emit(bool on) { emitting = on; }

        public void Clear() { samples.Clear(); }

        void LateUpdate()
        {
            if (blade == null) { Destroy(gameObject); return; }
            float now = Time.time;
            if (emitting)
            {
                var s = new Sample { b = blade.TransformPoint(baseLocal), t = blade.TransformPoint(tipLocal), time = now };
                if (samples.Count == 0 || (samples[samples.Count - 1].t - s.t).sqrMagnitude > 1e-6f || now > samples[samples.Count - 1].time) samples.Add(s);
            }
            while (samples.Count > 0 && now - samples[0].time > Life) samples.RemoveAt(0);
            Build(now);
        }

        static Vector3 CR(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }

        void Build(float now)
        {
            verts.Clear(); uvs.Clear(); cols.Clear(); tris.Clear();
            int n = samples.Count;
            if (n >= 2)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    var s0 = samples[Mathf.Max(0, i - 1)];
                    var s1 = samples[i];
                    var s2 = samples[i + 1];
                    var s3 = samples[Mathf.Min(n - 1, i + 2)];
                    int steps = i == n - 2 ? Sub + 1 : Sub;
                    for (int k = 0; k < steps; k++)
                    {
                        float u = k / (float)Sub;
                        Vector3 b = CR(s0.b, s1.b, s2.b, s3.b, u);
                        Vector3 t = CR(s0.t, s1.t, s2.t, s3.t, u);
                        float time = Mathf.Lerp(s1.time, s2.time, u);
                        float age = Mathf.Clamp01((now - time) / Life);
                        // the crescent thins toward its tail: pull the inner edge toward the outer edge with age
                        Vector3 inner = Vector3.Lerp(b, t, 0.1f + 0.75f * age * age);
                        verts.Add(transform.InverseTransformPoint(inner));
                        verts.Add(transform.InverseTransformPoint(t));
                        uvs.Add(new Vector2(age, 0f));
                        uvs.Add(new Vector2(age, 1f));
                        var c = Color;
                        c.a *= 1f - age;
                        cols.Add(c); cols.Add(c);
                    }
                }
                for (int i = 0; i < verts.Count / 2 - 1; i++)
                {
                    int a = i * 2;
                    tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
                }
            }
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
    }
}
