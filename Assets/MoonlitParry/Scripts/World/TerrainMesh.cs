using System.Collections.Generic;
using UnityEngine;

namespace MoonlitParry
{
    /// <summary>
    /// HD terrain: the ground body is a black mesh under the profile and a tiled grass-edge strip runs along the surface
    /// (textures in Resources/HD/Terrain, written by Tools/ArtGenerator/hd/terrain_flat.py).
    /// </summary>
    public static class TerrainMesh
    {
        public const float EdgeAbove = 0.56f, EdgeBelow = 0.44f;     // must match GRASS_ABOVE / GRASS_BELOW (px / 100)
        public const float EdgeRepeat = 10.24f;                       // grass_edge.png width / 100 px per unit

        public static readonly Color Ground = new Color(0.0015f, 0.0021f, 0.003f, 1f);   // linear value of the strip's #05070a (vertex colours are not gamma-converted)

        public static bool Available
        {
            get { return Resources.Load<Texture2D>("HD/Terrain/grass_edge") != null; }
        }

        public static void Build(Transform parent, List<Vector2> profile, float bottom)
        {
            var edgeTex = Resources.Load<Texture2D>("HD/Terrain/grass_edge");
            if (edgeTex != null) { edgeTex.wrapModeU = TextureWrapMode.Repeat; edgeTex.wrapModeV = TextureWrapMode.Clamp; }   // no bleeding of blade tips into the bottom edge

            // ---------------- body: one quad column per segment (vertical drops add nothing)
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            for (int i = 0; i < profile.Count - 1; i++)
            {
                Vector2 a = profile[i], b = profile[i + 1];
                if (b.x - a.x < 1e-4f) continue;
                int s = v.Count;
                v.Add(new Vector3(a.x, a.y)); v.Add(new Vector3(b.x, b.y)); v.Add(new Vector3(b.x, bottom)); v.Add(new Vector3(a.x, bottom));
                for (int k = s; k < s + 4; k++) uv.Add(new Vector2(v[k].x * 0.25f, v[k].y * 0.25f));
                tri.Add(s); tri.Add(s + 1); tri.Add(s + 2);
                tri.Add(s); tri.Add(s + 2); tri.Add(s + 3);
            }
            // the ground body is plain black (Hollow-Knight style): no texture, vertex colour only
            MakeObject(parent, "TerrainBody", v, uv, tri, null, Order.Tiles - 1, Ground);

            // ---------------- grass edge strip along the surface (vertical offset, so blades stay upright on slopes)
            v = new List<Vector3>();
            uv = new List<Vector2>();
            tri = new List<int>();
            for (int i = 0; i < profile.Count - 1; i++)
            {
                Vector2 a = profile[i], b = profile[i + 1];
                if (b.x - a.x < 1e-4f) continue;
                int s = v.Count;
                v.Add(new Vector3(a.x, a.y + EdgeAbove)); v.Add(new Vector3(b.x, b.y + EdgeAbove));
                v.Add(new Vector3(b.x, b.y - EdgeBelow)); v.Add(new Vector3(a.x, a.y - EdgeBelow));
                uv.Add(new Vector2(a.x / EdgeRepeat, 1f)); uv.Add(new Vector2(b.x / EdgeRepeat, 1f));
                uv.Add(new Vector2(b.x / EdgeRepeat, 0f)); uv.Add(new Vector2(a.x / EdgeRepeat, 0f));
                tri.Add(s); tri.Add(s + 1); tri.Add(s + 2);
                tri.Add(s); tri.Add(s + 2); tri.Add(s + 3);
            }
            MakeObject(parent, "TerrainEdge", v, uv, tri, edgeTex, Order.Tiles, Color.white);
        }

        static GameObject MakeObject(Transform parent, string name, List<Vector3> v, List<Vector2> uv, List<int> tri, Texture tex, int order, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { name = name };
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            var cols = new Color[v.Count];
            for (int i = 0; i < cols.Length; i++) cols[i] = c;
            mesh.colors = cols;
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = tex != null ? Gfx.HDTexMat(tex) : Gfx.HDMat;
            mr.sortingOrder = order;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }
    }
}
