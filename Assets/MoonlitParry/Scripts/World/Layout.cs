using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MoonlitParry
{
    /// <summary>All placement data for the stage (from the scene's LevelLayout, or the built-in default).</summary>
    public class LayoutData
    {
        public struct Item
        {
            public float x, y; public int v; public int order;   // order == int.MinValue: default draw order for that kind
            public bool keepY;                                    // true: stays exactly at y (e.g. half-buried behind the ground)
            public Item(float x, int v) { this.x = x; this.v = v; y = float.NaN; order = int.MinValue; keepY = false; }
            public Item(float x, float y, int v, int order, bool keepY = false) { this.x = x; this.y = y; this.v = v; this.order = order; this.keepY = keepY; }
        }

        public List<Vector2> ground = new List<Vector2>();   // surface vertices (normalised: int x, 1:2 slopes, cliffs)
        // y values: NaN = stand on the ground at x (built-in map); otherwise the exact height placed in LevelLayout
        public float spawnX = 2.5f, spawnY = float.NaN;
        public List<float> bonfires = new List<float>(), bonfireYs = new List<float>();
        public float firstZombie = float.NaN, firstZombieY = float.NaN;
        public List<float> zombies = new List<float>(), zombieYs = new List<float>();
        public float boss = 206.5f, bossY = float.NaN;
        public List<Vector3Int> platforms = new List<Vector3Int>();  // x0, x1, surface height
        public List<Item> trees = new List<Item>(), rocks = new List<Item>(), bushes = new List<Item>(),
            ruinWalls = new List<Item>(), ruinPillars = new List<Item>(), ferns = new List<Item>(), shrooms = new List<Item>();
        public float arenaIn = 82.5f, arenaOut = 106.5f, arenaInY = float.NaN, arenaOutY = float.NaN;
        public float bossGate = 179.5f, bossGateY = float.NaN, bossArenaEnd = 222f;

        public static float YAt(List<float> ys, int i) { return ys != null && i < ys.Count ? ys[i] : float.NaN; }
        public float moundX0 = 36.3f, moundX1 = 39.7f;
        public bool randomGrass = true, undergroundDetails = true;

        /// <summary>Things snap down onto whatever they would stand on: a ledge or the ground under them.
        /// Dropped up to 0.5 tile below a surface still counts as on it; dropped deeper below the ground = put on the ground.</summary>
        public static float Land(List<Vector2> ground, List<Vector3Int> plats, float x, float y)
        {
            float s = SurfaceAt(ground, x);
            if (float.IsNaN(y)) return s;
            float best = float.NegativeInfinity;
            if (s <= y + 0.5f) best = s;
            if (plats != null)
                foreach (var p in plats)
                    if (x >= p.x - 0.1f && x <= p.y + 1.1f && p.z <= y + 0.5f && p.z > best) best = p.z;
            return float.IsNegativeInfinity(best) ? s : best;
        }

        /// <summary>Height of a normalised ground profile at x.</summary>
        public static float SurfaceAt(List<Vector2> g, float x)
        {
            float best = 0f;
            for (int i = 0; i < g.Count - 1; i++)
            {
                if (Mathf.Approximately(g[i].x, g[i + 1].x)) continue;
                if (x >= g[i].x && x <= g[i + 1].x) return Mathf.Lerp(g[i].y, g[i + 1].y, (x - g[i].x) / (g[i + 1].x - g[i].x));
            }
            if (g.Count > 0) best = x < g[0].x ? g[0].y : g[g.Count - 1].y;
            return best;
        }

        public static LayoutData Default()
        {
            var d = new LayoutData();
            float[,] g =
            {
                { -14, 9 }, { -8, 9 }, { -8, 0 }, { 10, 0 }, { 12, 1 }, { 17, 1 }, { 19, 0 },
                { 30, 0 }, { 36, 3 }, { 40, 3 }, { 46, 0 }, { 110, 0 }, { 114, 2 }, { 124, 2 }, { 124, 0 },
                { 140, 0 }, { 146, 3 }, { 156, 3 }, { 162, 0 }, { 222, 0 }, { 222, 12 }, { 228, 12 },
            };
            for (int i = 0; i < g.GetLength(0); i++) d.ground.Add(new Vector2(g[i, 0], g[i, 1]));
            d.bonfires.AddRange(new[] { 68.5f, 172.5f });
            d.firstZombie = 56.5f;
            d.zombies.AddRange(new[] { 118.5f, 132.5f, 150.5f, 165.5f });
            d.platforms.AddRange(new[] { new Vector3Int(92, 96, 3), new Vector3Int(128, 131, 3), new Vector3Int(134, 137, 5),
                                         new Vector3Int(188, 191, 4), new Vector3Int(211, 214, 4) });
            int[] trees = { -4, 22, 50, 74, 116, 143, 168 };
            for (int i = 0; i < trees.Length; i++) d.trees.Add(new Item(trees[i] + 0.5f, i % 2));
            int[] rocks = { 6, 26, 61, 100, 130, 158, 200, 216 };
            for (int i = 0; i < rocks.Length; i++) d.rocks.Add(new Item(rocks[i] + 0.5f, i % 3));
            int[] bushes = { 3, 20, 48, 71, 120, 165 };
            for (int i = 0; i < bushes.Length; i++) d.bushes.Add(new Item(bushes[i] + 0.5f, i % 2));
            d.ruinWalls.AddRange(new[] { new Item(88.5f, 0), new Item(101.5f, 1), new Item(127.5f, 1) });
            d.ruinPillars.AddRange(new[] { new Item(85.5f, 1), new Item(103.5f, 1), new Item(150.5f, 0), new Item(197.5f, 1), new Item(218.5f, 0) });
            int[] ferns = { 8, 15, 34, 52, 64, 78, 98, 112, 133, 147, 160, 170, 186, 205, 214 };
            for (int i = 0; i < ferns.Length; i++) d.ferns.Add(new Item(ferns[i] + 0.5f, i % 2));
            int[] shrooms = { 5, 24, 44, 58, 90, 104, 122, 138, 152, 175, 193, 209 };
            for (int i = 0; i < shrooms.Length; i++) d.shrooms.Add(new Item(shrooms[i] + 0.5f, i % 2));
            return d;
        }

        /// <summary>
        /// Turn freely dragged points into buildable terrain: x snapped to whole tiles, heights to whole tiles,
        /// rises/drops become 1:2 slopes when there is room, otherwise a cliff.
        /// </summary>
        public static List<Vector2> Normalize(List<Vector2> raw)
        {
            var res = new List<Vector2>();
            if (raw.Count == 0) return res;
            var p = new Vector2(Mathf.Round(raw[0].x), Mathf.Round(raw[0].y));
            res.Add(p);
            for (int i = 1; i < raw.Count; i++)
            {
                var q = new Vector2(Mathf.Round(raw[i].x), Mathf.Round(raw[i].y));
                p = res[res.Count - 1];
                if (q.x < p.x) q.x = p.x;
                if (Mathf.Approximately(q.x, p.x))
                {
                    if (!Mathf.Approximately(q.y, p.y)) res.Add(q);   // cliff
                    continue;
                }
                float dy = q.y - p.y, dx = q.x - p.x;
                if (Mathf.Approximately(dy, 0f)) { res.Add(q); continue; }
                float need = Mathf.Abs(dy) * 2f;
                if (need <= dx)
                {
                    res.Add(new Vector2(p.x + need, q.y));
                    if (p.x + need < q.x) res.Add(q);
                }
                else
                {
                    res.Add(new Vector2(q.x, p.y));   // flat, then cliff
                    res.Add(q);
                }
            }
            // merge collinear flats
            for (int i = res.Count - 2; i >= 1; i--)
            {
                var a = res[i - 1]; var b = res[i]; var c = res[i + 1];
                if (Mathf.Approximately(a.y, b.y) && Mathf.Approximately(b.y, c.y) && a.x < b.x && b.x < c.x) res.RemoveAt(i);
            }
            return res;
        }
    }

    public enum MarkerKind
    {
        PlayerSpawn, Bonfire, FirstZombie, Zombie, Boss, Platform, Tree, Rock, Bush, RuinWall, RuinPillar, Fern, Mushroom,
        ArenaGate, BossGate, BossArenaEnd, MoundLookout
    }

    /// <summary>Which depth layer a decor marker is drawn on (front/back relative to other things).</summary>
    public enum DrawLayer
    {
        [InspectorName("Mặc định")] Auto,
        [InspectorName("Sau cùng (sau cả cây)")] BehindAll,
        [InspectorName("Sau (ngang tường đổ, sau cây bụi)")] Back,
        [InspectorName("Sau mặt đất (bị đất che bớt)")] BehindGround,
        [InspectorName("Trước mặt đất, sau nhân vật")] FrontOfGround,
        [InspectorName("Trước cả nhân vật")] FrontAll,
    }

}
