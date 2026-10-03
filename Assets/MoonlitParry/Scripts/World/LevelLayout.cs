using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MoonlitParry
{
    // NOTE: Unity only saves a MonoBehaviour in a scene when it lives in a file with the SAME name as the class.
    /// <summary>
    /// Scene-editable level. Children: "Ground" (points, in order, left → right) and LevelMarker objects.
    /// Drag them in the Scene view (outside Play mode), save the scene, press Play.
    /// </summary>
    public class LevelLayout : MonoBehaviour
    {
        [Tooltip("Cỏ và hoa rải ngẫu nhiên trên mặt đất phẳng")] public bool randomGrass = true;
        [Tooltip("Rễ cây, xương, đá... ngẫu nhiên trong lòng đất")] public bool undergroundDetails = true;

        public Transform Ground { get { return transform.Find("Ground"); } }

        public LayoutData Read()
        {
            var d = new LayoutData();
            d.bonfires.Clear(); d.zombies.Clear(); d.platforms.Clear();
            var fires = new List<Vector2>();
            var gates = new List<Vector2>();
            var g = Ground;
            var raw = new List<Vector2>();
            if (g != null)
                for (int i = 0; i < g.childCount; i++)
                {
                    var c = g.GetChild(i);
#if UNITY_EDITOR
                    // editor only: its dot was deleted = point deleted (builds strip the EditorOnly previews)
                    var lp = c.GetComponent<LevelPoint>();
                    if (lp != null && lp.hasPreview && c.Find("preview") == null) continue;
#endif
                    raw.Add(new Vector2(c.position.x, c.position.y));
                }
            d.ground = LayoutData.Normalize(raw);
            if (d.ground.Count < 2) d.ground = LayoutData.Default().ground;
            d.randomGrass = randomGrass;
            d.undergroundDetails = undergroundDetails;

            bool hasBossEnd = false;
            foreach (var m in GetComponentsInChildren<LevelMarker>(true))
            {
#if UNITY_EDITOR
                // editor only: its picture was deleted = object deleted (builds strip the EditorOnly previews, which
                // used to drop every monster, bonfire and prop from the built game)
                if (m.hasPreview && m.transform.Find("preview") == null) continue;
#endif
                float x = m.transform.position.x, y = m.transform.position.y;
                switch (m.kind)
                {
                    case MarkerKind.PlayerSpawn: d.spawnX = x; d.spawnY = y; break;
                    case MarkerKind.Bonfire: fires.Add(new Vector2(x, y)); break;
                    case MarkerKind.FirstZombie: d.firstZombie = x; d.firstZombieY = y; break;
                    case MarkerKind.Zombie: d.zombies.Add(x); d.zombieYs.Add(y); break;
                    case MarkerKind.Boss: d.boss = x; d.bossY = y; break;
                    case MarkerKind.Platform:
                        int x0 = Mathf.RoundToInt(m.transform.position.x);
                        d.platforms.Add(new Vector3Int(x0, x0 + Mathf.Max(1, m.width) - 1, Mathf.RoundToInt(m.transform.position.y)));
                        break;
                    case MarkerKind.Tree: d.trees.Add(new LayoutData.Item(x, y, m.variant, m.DrawOrder, m.layer == DrawLayer.BehindGround)); break;
                    case MarkerKind.Rock: d.rocks.Add(new LayoutData.Item(x, y, m.variant, m.DrawOrder, m.layer == DrawLayer.BehindGround)); break;
                    case MarkerKind.Bush: d.bushes.Add(new LayoutData.Item(x, y, m.variant, m.DrawOrder, m.layer == DrawLayer.BehindGround)); break;
                    case MarkerKind.RuinWall: d.ruinWalls.Add(new LayoutData.Item(x, y, m.variant, m.DrawOrder, m.layer == DrawLayer.BehindGround)); break;
                    case MarkerKind.RuinPillar: d.ruinPillars.Add(new LayoutData.Item(x, y, m.variant, m.DrawOrder, m.layer == DrawLayer.BehindGround)); break;
                    case MarkerKind.Fern: d.ferns.Add(new LayoutData.Item(x, y, m.variant, m.DrawOrder, m.layer == DrawLayer.BehindGround)); break;
                    case MarkerKind.Mushroom: d.shrooms.Add(new LayoutData.Item(x, y, m.variant, m.DrawOrder, m.layer == DrawLayer.BehindGround)); break;
                    case MarkerKind.ArenaGate: gates.Add(new Vector2(x, y)); break;
                    case MarkerKind.BossGate: d.bossGate = x; d.bossGateY = y; break;
                    case MarkerKind.BossArenaEnd: d.bossArenaEnd = x; hasBossEnd = true; break;
                    case MarkerKind.MoundLookout: d.moundX0 = x - m.width * 0.5f; d.moundX1 = x + m.width * 0.5f; break;
                }
            }
            fires.Sort((a, b) => a.x.CompareTo(b.x));
            foreach (var f in fires) { d.bonfires.Add(f.x); d.bonfireYs.Add(f.y); }
            if (gates.Count >= 2)
            {
                gates.Sort((a, b) => a.x.CompareTo(b.x));
                d.arenaIn = gates[0].x; d.arenaInY = gates[0].y;
                d.arenaOut = gates[gates.Count - 1].x; d.arenaOutY = gates[gates.Count - 1].y;
            }
            if (!hasBossEnd) d.bossArenaEnd = d.ground[d.ground.Count - 1].x - 6f;
            return d;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            var g = Ground;
            if (g == null) return;
            var raw = new List<Vector2>();
            for (int i = 0; i < g.childCount; i++)
            {
                var c = g.GetChild(i);
                raw.Add(c.position);
                Gizmos.color = new Color(1f, 1f, 0.4f);
                Gizmos.DrawWireCube(c.position, Vector3.one * 0.35f);
            }
            var n = LayoutData.Normalize(raw);
            Gizmos.color = new Color(0.4f, 1f, 0.5f);
            for (int i = 0; i < n.Count - 1; i++)
            {
                Gizmos.DrawLine(n[i], n[i + 1]);
                Gizmos.DrawLine(n[i] + Vector2.down * 0.05f, n[i + 1] + Vector2.down * 0.05f);
            }
            Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.15f);
            for (int i = 0; i < n.Count - 1; i++)
                if (n[i + 1].x > n[i].x)
                    Gizmos.DrawCube(new Vector3((n[i].x + n[i + 1].x) * 0.5f, (Mathf.Min(n[i].y, n[i + 1].y) - 8f) * 0.5f, 0f),
                                    new Vector3(n[i + 1].x - n[i].x, Mathf.Min(n[i].y, n[i + 1].y) + 8f, 0f));
        }
#endif
    }
}
