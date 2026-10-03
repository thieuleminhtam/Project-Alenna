using System.Collections.Generic;
using UnityEngine;

namespace MoonlitParry
{
    public class LevelData
    {
        public Vector3 spawn;
        public float levelMinX, levelMaxX;
        public readonly List<Bonfire> bonfires = new List<Bonfire>();
        public readonly List<float> bonfireX = new List<float>();
        public readonly List<float> bonfireY = new List<float>();
        public Zombie firstZombie;
        public readonly List<EnemyBase> enemies = new List<EnemyBase>();

        public float moundMinX, moundMaxX, moundMinY;
        public Gate arenaIn, arenaOut;
        public float arenaMinX, arenaMaxX, arenaTriggerX;
        public Gate bossDoor;
        public float bossGateX, bossTriggerX, bossArenaMin, bossArenaMax;
        public Boss boss;
        public Transform root;
    }

    /// <summary>
    /// Builds the stage from LayoutData: mossy earth tiles with 1:2 slopes and cliffs, underground details,
    /// one-way ledges, decor, bonfires, the ruins trap gates and the colosseum gate. 1 unit = 16 px = 1 tile.
    /// </summary>
    public static class LevelBuilder
    {
        const int Bottom = -8;
        static int MinX, MaxX;
        static float[] HL, HR;   // surface height approaching integer x from the left / from the right
        static List<Vector2> profile;
        static bool underground = true;

        static int Ix(int x) { return x - MinX; }
        public static float LeftH(int x) { return HL == null || x < MinX || x > MaxX ? 12f : HL[Ix(x)]; }
        public static float RightH(int x) { return HR == null || x < MinX || x > MaxX ? 12f : HR[Ix(x)]; }
        static List<Vector3Int> plats;
        /// <summary>Visual lift so the moss line of the tiles sits exactly on the collider (no floating feet).</summary>
        const float TileLift = 3f / 16f, PlatLift = 2f / 16f;

        /// <summary>Height to stand something at: lands on the ledge or ground under the placed point (ground if none was placed).</summary>
        public static float StandAt(float x, float y)
        {
            return LayoutData.Land(profile, plats, x, y);
        }

        public static float SurfaceAt(float x)
        {
            int c = Mathf.FloorToInt(x);
            float hl = RightH(c), hr = LeftH(c + 1);
            return Mathf.Lerp(hl, hr, x - c);
        }

        static void BuildHeights(List<Vector2> g)
        {
            profile = g;
            MinX = Mathf.RoundToInt(g[0].x);
            MaxX = Mathf.RoundToInt(g[g.Count - 1].x);
            int n = MaxX - MinX + 1;
            HL = new float[n]; HR = new float[n];
            for (int i = 0; i < n; i++) { HL[i] = g[0].y; HR[i] = g[0].y; }
            for (int i = 0; i < g.Count - 1; i++)
            {
                float ax = g[i].x, ay = g[i].y, bx = g[i + 1].x, by = g[i + 1].y;
                if (Mathf.Approximately(ax, bx)) continue;
                for (int x = Mathf.RoundToInt(ax); x <= Mathf.RoundToInt(bx); x++)
                {
                    float h = ay + (by - ay) * (x - ax) / (bx - ax);
                    if (x > ax) HL[Ix(x)] = h;
                    if (x < bx) HR[Ix(x)] = h;
                }
            }
        }

        public static LevelData Build(Transform root, LayoutData L)
        {
            var data = new LevelData { root = root };
            BuildHeights(L.ground);
            plats = L.platforms;

            var terrain = new GameObject("Terrain").transform;
            terrain.SetParent(root, false);
            underground = L.undergroundDetails;
            if (TerrainMesh.Available) TerrainMesh.Build(terrain, profile, Bottom);     // HD: black body + grass edge strip
            else BuildTiles(terrain);
            BuildCollider(terrain);
            BuildPlatforms(terrain, L);

            var decor = new GameObject("Decor").transform;
            decor.SetParent(root, false);
            BuildDecor(decor, L);

            data.spawn = new Vector3(L.spawnX, StandAt(L.spawnX, L.spawnY) + 0.02f, 0f);
            data.levelMinX = FirstWallRight();
            data.levelMaxX = L.bossArenaEnd;
            data.moundMinX = L.moundX0; data.moundMaxX = L.moundX1;
            data.moundMinY = SurfaceAt((L.moundX0 + L.moundX1) * 0.5f) - 0.4f;

            // bonfires
            for (int i = 0; i < L.bonfires.Count; i++)
            {
                float x = L.bonfires[i], y = StandAt(x, LayoutData.YAt(L.bonfireYs, i));
                data.bonfireX.Add(x);
                data.bonfireY.Add(y);
                bool lit = i < Progress.BonfireLit.Length && Progress.BonfireLit[i];
                data.bonfires.Add(Bonfire.Create(root, new Vector3(x, y, 0f), i, lit));
            }

            // ruins trap
            data.arenaMinX = L.arenaIn + 0.7f;
            data.arenaMaxX = L.arenaOut - 0.7f;
            data.arenaTriggerX = (L.arenaIn + L.arenaOut) * 0.5f;
            float ainY = StandAt(L.arenaIn, L.arenaInY), aoutY = StandAt(L.arenaOut, L.arenaOutY);
            Gfx.Sprite("ruins_arch", decor, new Vector3(L.arenaIn, ainY - 0.06f, 0f), SpriteBank.One("Decor/ruins_arch"), Order.DecorBack);
            Gfx.Sprite("ruins_arch", decor, new Vector3(L.arenaOut, aoutY - 0.06f, 0f), SpriteBank.One("Decor/ruins_arch"), Order.DecorBack);
            data.arenaIn = Gate.Create(root, "portcullis", L.arenaIn, ainY, 0.9f, false);
            data.arenaOut = Gate.Create(root, "portcullis", L.arenaOut, aoutY, 0.9f, false);

            // colosseum gate + braziers + door
            float bg = L.bossGate, by = StandAt(bg, L.bossGateY);
            data.bossGateX = bg;
            data.bossArenaMin = bg + 1.7f;
            data.bossTriggerX = bg + 4.1f;
            data.bossArenaMax = L.bossArenaEnd;
            Gfx.Sprite("boss_arch", decor, new Vector3(bg, by - 0.06f, 0f), SpriteBank.One("Decor/boss_arch"), Order.DecorBack);
            foreach (float off in new[] { -52f / 16f, 52f / 16f })
            {
                var pos = new Vector3(bg + off + 0.06f, by + 131f / 16f + 0.55f, 0f);
                var fl = Gfx.Sprite("brazier", decor, pos, null, Order.DecorBack + 1);
                fl.gameObject.AddComponent<FlameAnim>();
                var gl = Gfx.Sprite("brazier_glow", decor, pos, Fx.Glow, Order.DecorBack);
                gl.color = new Color(0.5f, 1f, 0.9f, 0.3f);
                gl.transform.localScale = new Vector3(3f, 3f, 1f);
                gl.gameObject.AddComponent<GlowFlicker>();
            }
            data.bossDoor = Gate.Create(root, "boss_door", bg, by, 1.1f, false);      // narrow side-on grate

            // enemies
            var enemies = new GameObject("Enemies").transform;
            enemies.SetParent(root, false);
            if (!float.IsNaN(L.firstZombie) && !Progress.FirstZombieDead)
            {
                data.firstZombie = SpawnZombie(enemies, L.firstZombie, false, L.firstZombieY);
                data.firstZombie.Died += e => Progress.FirstZombieDead = true;
                data.enemies.Add(data.firstZombie);
            }
            for (int i = 0; i < L.zombies.Count; i++) data.enemies.Add(SpawnZombie(enemies, L.zombies[i], false, LayoutData.YAt(L.zombieYs, i)));

            var boss = new GameObject("Boss").AddComponent<Boss>();
            boss.transform.SetParent(enemies, false);
            boss.Init(new Vector3(L.boss, StandAt(L.boss, L.bossY) + 0.02f, 0f), data.bossArenaMin, data.bossArenaMax);
            data.boss = boss;
            data.enemies.Add(boss);
            return data;
        }

        static float FirstWallRight()
        {
            // left camera bound: the first cliff that drops from a high wall
            for (int i = 0; i < profile.Count - 1; i++)
                if (Mathf.Approximately(profile[i].x, profile[i + 1].x) && profile[i].y > profile[i + 1].y + 3f) return profile[i].x;
            return profile[0].x;
        }

        public static Zombie SpawnZombie(Transform parent, float x, bool emerge, float y = float.NaN)
        {
            var z = new GameObject("Zombie").AddComponent<Zombie>();
            z.transform.SetParent(parent, false);
            z.Init(new Vector3(x, StandAt(x, y) + 0.02f, 0f), emerge);
            return z;
        }

        // ------------------------------------------------------------------ tiles
        static void Tile(Transform parent, int x, int y, string name, float depth)
        {
            var sr = Gfx.Sprite("t", parent, new Vector3(x, y + TileLift, 0f), SpriteBank.One("Tiles/" + name), Order.Tiles);
            float c = 1f - Mathf.Min(0.62f, depth * 0.12f);
            sr.color = new Color(c, c * 0.97f, Mathf.Min(1f, c * 1.05f), 1f);
        }

        static void BuildTiles(Transform parent)
        {
            var rng = new System.Random(5);
            for (int c = MinX; c < MaxX; c++)
            {
                float hl = RightH(c), hr = LeftH(c + 1);
                int top;
                bool flat = Mathf.Approximately(hl, hr);
                if (flat)
                {
                    int h = Mathf.RoundToInt(hl);
                    bool l = LeftH(c) < h - 0.01f, r = RightH(c + 1) < h - 0.01f;
                    Tile(parent, c, h - 1, l ? "top_l" : r ? "top_r" : "top_" + rng.Next(3), 0);
                    top = h - 1;
                }
                else
                {
                    float lo = Mathf.Min(hl, hr);
                    int b = Mathf.FloorToInt(lo + 0.001f);
                    bool half = lo - b > 0.25f;
                    bool rising = hr > hl;
                    // slope pieces are 2 tiles tall and sit one row lower, so the moss runs on without a seam
                    Tile(parent, c, b - 1, (half ? "slope_hi_" : "slope_lo_") + (rising ? "u" : "d"), 0);
                    top = b - 1;
                }
                for (int y = top - 1; y >= Bottom; y--)
                {
                    int depth = top - y;
                    bool l = LeftH(c) <= y + 0.01f, r = RightH(c + 1) <= y + 0.01f;
                    string n = l ? "fill_l" : r ? "fill_r" : (depth >= 4 ? "deep_" : "fill_") + rng.Next(3);
                    Tile(parent, c, y, n, Mathf.Max(0.5f, SurfaceAt(c + 0.5f) - 1f - y));
                }
                if (flat && top - Bottom > 4 && underground)   // underground details behind the grass silhouettes
                {
                    double roll = rng.NextDouble();
                    if (roll < 0.13)
                        Gfx.Sprite("u", parent, new Vector3(c, top - 1 + TileLift, 0f), SpriteBank.One("Under/roots_" + rng.Next(2)), Order.Under);
                    else if (roll < 0.24)
                    {
                        string[] pick = { "boulder", "bones", "skull", "brick", "fossil", "boulder" };
                        int y = top - 2 - rng.Next(3);
                        var u = Gfx.Sprite("u", parent, new Vector3(c, y + TileLift, 0f), SpriteBank.One("Under/" + pick[rng.Next(pick.Length)]), Order.Under);
                        float k = 1f - Mathf.Min(0.5f, (top - y) * 0.1f);
                        u.color = new Color(k, k, k, 1f);
                    }
                }
            }
        }

        static void BuildCollider(Transform parent)
        {
            var go = new GameObject("GroundEdge");
            go.layer = Layers.Ground;
            go.transform.SetParent(parent, false);
            var e = go.AddComponent<EdgeCollider2D>();
            e.points = profile.ToArray();
        }

        static void BuildPlatforms(Transform parent, LayoutData L)
        {
            var rng = new System.Random(9);
            foreach (var pl in L.platforms)
            {
                int x0 = pl.x, x1 = Mathf.Max(pl.x, pl.y), s = pl.z;
                for (int x = x0; x <= x1; x++)
                {
                    string n = x == x0 ? "plat_l" : x == x1 ? "plat_r" : "plat_m";
                    Gfx.Sprite("p", parent, new Vector3(x, s - 1 + PlatLift, 0f), SpriteBank.One("Tiles/" + n), Order.Tiles + 1);
                    if (rng.NextDouble() < 0.5)
                        Gfx.Sprite("moss", parent, new Vector3(x + 0.5f, s - 1.25f + PlatLift, 0f), SpriteBank.One("Decor/hangmoss_" + rng.Next(2)), Order.Tiles + 2);   // hangs under the ledge
                }
                var go = new GameObject("ledge_" + x0);
                go.layer = Layers.Ground;
                go.transform.SetParent(parent, false);
                float w = x1 - x0 + 1;
                go.transform.position = new Vector3(x0 + w * 0.5f, s - 0.25f, 0f);
                var bc = go.AddComponent<BoxCollider2D>();
                bc.size = new Vector2(w, 0.375f);
                bc.offset = new Vector2(0f, 0.0625f);
                bc.usedByEffector = true;
                var eff = go.AddComponent<PlatformEffector2D>();
                eff.useOneWay = true;
                eff.surfaceArc = 160f;
            }
        }

        static bool Flat(int x) { return Mathf.Approximately(RightH(x), LeftH(x + 1)) && Mathf.Approximately(LeftH(x), RightH(x)); }

        static float DecorY(LayoutData.Item t) { return t.keepY && !float.IsNaN(t.y) ? t.y : StandAt(t.x, t.y); }

        /// <summary>
        /// Ground height for a prop whose footprint is +-halfFoot wide: on a slope it sits at the lower edge of its
        /// footprint so no corner hovers in the air (the uphill side just sinks into the ground). Ledge edges / drops
        /// (a big height difference) keep the height under the centre.
        /// </summary>
        static float FootY(LayoutData.Item t, float halfFoot)
        {
            float y = DecorY(t);
            if ((t.keepY && !float.IsNaN(t.y)) || halfFoot <= 0.01f) return y;
            float ry = float.IsNaN(t.y) ? t.y : y;
            float lo = Mathf.Min(StandAt(t.x - halfFoot, ry), StandAt(t.x + halfFoot, ry));
            return lo < y && y - lo < Mathf.Max(0.2f, halfFoot * 0.8f) ? lo : y;      // up to ~40° slopes; cliffs keep the centre
        }

        static void Put(Transform parent, string name, LayoutData.Item t, string sprite, int order, float yOff = -0.06f, float foot = 0.55f)
        {
            var s = SpriteBank.One(sprite);
            float half = s != null ? s.bounds.extents.x * foot : 0f;
            Gfx.Sprite(name, parent, new Vector3(t.x, FootY(t, half) + yOff, 0f), s, t.order == int.MinValue ? order : t.order);
        }

        static void BuildDecor(Transform parent, LayoutData L)
        {
            var rng = new System.Random(11);
            foreach (var t in L.trees) Put(parent, "tree", t, "Decor/tree_" + Mathf.Clamp(t.v, 0, 1), Order.Trees, -0.1f, 0.12f);   // only the trunk base
            foreach (var t in L.rocks) Put(parent, "rock", t, "Decor/rock_" + Mathf.Clamp(t.v, 0, 2), Order.Decor);
            foreach (var t in L.bushes) Put(parent, "bush", t, "Decor/bush_" + Mathf.Clamp(t.v, 0, 1), Order.DecorBack + 2);
            foreach (var t in L.ruinWalls) Put(parent, "ruinwall", t, "Decor/ruinwall_" + Mathf.Clamp(t.v, 0, 1), Order.DecorBack + 1);
            foreach (var t in L.ruinPillars) Put(parent, "ruinpillar", t, "Decor/ruinpillar_" + Mathf.Clamp(t.v, 0, 1), Order.DecorBack + 1);
            foreach (var t in L.ferns) Put(parent, "fern", t, "Decor/fern_" + Mathf.Clamp(t.v, 0, 1), Order.GrassFront);
            // mushrooms removed from the art direction (markers of that kind are ignored)
            // random grass & flowers on flat ground
            if (L.randomGrass)
            for (int x = MinX + 1; x < MaxX - 1; x++)
            {
                if (!Flat(x) || RightH(x) > 8f) continue;
                float h = RightH(x);
                double r = rng.NextDouble();
                if (r < 0.2)
                    Gfx.Sprite("grass", parent, new Vector3(x + 0.5f, h - 0.06f, 0f), SpriteBank.One("Decor/grass_" + rng.Next(3)), Order.GrassFront);
                else if (r < 0.28)
                    Gfx.Sprite("flower", parent, new Vector3(x + 0.3f + (float)rng.NextDouble() * 0.4f, h - 0.06f, 0f), SpriteBank.One("Decor/flower_" + rng.Next(2)), Order.Decor);
            }
        }
    }

    /// <summary>Loops the brazier flame frames.</summary>
    public class FlameAnim : MonoBehaviour
    {
        SpriteAnim anim;
        void Start()
        {
            anim = new SpriteAnim(GetComponent<SpriteRenderer>(), SpriteBank.Set("FX"));
            anim.Play("brazier", 10f, true, true);
        }
        void Update() { if (anim != null) anim.Tick(Time.deltaTime); }
    }
}
