using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MoonlitParry
{
    // NOTE: Unity only saves a MonoBehaviour in a scene when it lives in a file with the SAME name as the class.
    /// <summary>A draggable placement in the scene (created by Moonlit Parry ▸ Bake Level Layout).</summary>
    [SelectionBase]
    public class LevelMarker : MonoBehaviour
    {
        public MarkerKind kind;
        [Tooltip("Biến thể hình (đá 0-2, cây 0-1, ...)")] public int variant;
        [Tooltip("Bục: số ô chiều ngang. Chõm đất: bề rộng vùng kích hoạt.")] public int width = 4;
        [Tooltip("Lớp hiển thị (chỉ áp dụng cho đồ trang trí): đưa vật ra trước hoặc ra sau vật khác")] public DrawLayer layer;
        [Tooltip("Tinh chỉnh trong cùng lớp: số lớn hơn = nằm trước, nhỏ hơn = nằm sau (ví dụ -1, +1)")] public int orderOffset;
        [HideInInspector] public bool hasPreview;
        [HideInInspector] public int previewKey = -1;
        [HideInInspector] public Vector3 previewLocal;

        public int PreviewKey
        {
            get { unchecked { return ((((((int)kind * 397) ^ variant) * 397 ^ width) * 397 ^ (int)layer) * 397 ^ orderOffset) ^ 0x35A1; } }   // last constant = preview art version
        }

        public bool IsDecor
        {
            get
            {
                return kind == MarkerKind.Tree || kind == MarkerKind.Rock || kind == MarkerKind.Bush || kind == MarkerKind.RuinWall
                    || kind == MarkerKind.RuinPillar || kind == MarkerKind.Fern || kind == MarkerKind.Mushroom;
            }
        }

        /// <summary>Draw order the decor gets in game when left on "Mặc định".</summary>
        public static int DefaultOrder(MarkerKind k)
        {
            switch (k)
            {
                case MarkerKind.Tree: return Order.Trees;
                case MarkerKind.Bush: return Order.DecorBack + 2;
                case MarkerKind.RuinWall:
                case MarkerKind.RuinPillar: return Order.DecorBack + 1;
                case MarkerKind.Fern: return Order.GrassFront;
                default: return Order.Decor;
            }
        }

        public static int LayerOrder(DrawLayer l, MarkerKind k)
        {
            switch (l)
            {
                case DrawLayer.BehindAll: return Order.Trees - 5;
                case DrawLayer.Back: return Order.DecorBack;
                case DrawLayer.BehindGround: return Order.Tiles - 2;
                case DrawLayer.FrontOfGround: return Order.Decor;
                case DrawLayer.FrontAll: return Order.GrassFront + 1;
                default: return DefaultOrder(k);
            }
        }

        public int DrawOrder { get { return LayerOrder(layer, kind) + orderOffset; } }

        public static Vector3 PreviewOffset(MarkerKind k)
        {
            return k == MarkerKind.Bonfire ? new Vector3(0f, 1.06f, 0f) : Vector3.zero;
        }

#if UNITY_EDITOR
        public void BuildPreview()
        {
            var old = transform.Find("preview");
            if (old != null) DestroyImmediate(old.gameObject);
            var mat = Resources.Load<Material>("MoonlitSpriteMat");
            var pv = new GameObject("preview");
            pv.tag = "EditorOnly";
            pv.transform.SetParent(transform, false);
            pv.transform.localPosition = PreviewOffset(kind);

            if (kind == MarkerKind.Platform)
            {
                int w = Mathf.Max(1, width);
                for (int i = 0; i < w; i++)
                {
                    var t = new GameObject("tile");
                    t.tag = "EditorOnly";
                    t.transform.SetParent(pv.transform, false);
                    t.transform.localPosition = new Vector3(i, -1f + 2f / 16f, 0f);
                    var tsr = t.AddComponent<SpriteRenderer>();
                    tsr.sprite = LoadPreview("Tiles/" + (i == 0 ? "plat_l" : i == w - 1 ? "plat_r" : "plat_m"));
                    tsr.sortingOrder = -9;
                    if (mat != null) tsr.sharedMaterial = mat;
                }
            }
            else
            {
                var sr = pv.AddComponent<SpriteRenderer>();
                string path = PreviewSprite(kind, variant);
                bool enemy = kind == MarkerKind.Zombie || kind == MarkerKind.FirstZombie || kind == MarkerKind.Boss;
                if (enemy) pv.transform.localScale = new Vector3(-1f, 1f, 1f);
                if (path != null)
                {
                    sr.sprite = LoadPreview(path);
                    sr.sortingOrder = IsDecor ? DrawOrder : kind == MarkerKind.ArenaGate || kind == MarkerKind.BossGate ? -20 : enemy ? 10 : -5;
                    if (mat != null) sr.sharedMaterial = mat;
                }
                else
                {
                    // zones without art: a translucent block so they can be clicked in the Scene view
                    sr.sprite = Resources.Load<Sprite>("Sprites/FX/pixel");
                    sr.sortingOrder = 55;
                    if (kind == MarkerKind.MoundLookout)
                    {
                        pv.transform.localPosition = new Vector3(0f, 1f, 0f);
                        pv.transform.localScale = new Vector3(Mathf.Max(1, width) * 16f, 32f, 1f);
                        sr.color = new Color(0.4f, 1f, 1f, 0.22f);
                    }
                    else
                    {
                        pv.transform.localPosition = new Vector3(0f, 4f, 0f);
                        pv.transform.localScale = new Vector3(5f, 192f, 1f);
                        sr.color = new Color(1f, 0.35f, 0.35f, 0.5f);
                    }
                }
            }
            hasPreview = true;
            previewKey = PreviewKey;
            previewLocal = pv.transform.localPosition;
        }
#endif

        /// <summary>HD art first (Resources/HD), else the pixel sprite (Resources/Sprites).</summary>
        public static Sprite LoadPreview(string path)
        {
            var s = SpriteBank.HDAllowed(path) ? Resources.Load<Sprite>("HD/" + path) : null;
            return s != null ? s : Resources.Load<Sprite>("Sprites/" + path);
        }

        public static string PreviewSprite(MarkerKind k, int v)
        {
            switch (k)
            {
                case MarkerKind.PlayerSpawn: return "Player/idle_00";
                case MarkerKind.Bonfire: return "FX/bonfire_00";
                case MarkerKind.FirstZombie:
                case MarkerKind.Zombie: return "Zombie/idle_00";
                case MarkerKind.Boss: return "Boss/idle_00";
                case MarkerKind.Tree: return "Decor/tree_" + Mathf.Clamp(v, 0, 1);
                case MarkerKind.Rock: return "Decor/rock_" + Mathf.Clamp(v, 0, 2);
                case MarkerKind.Bush: return "Decor/bush_" + Mathf.Clamp(v, 0, 1);
                case MarkerKind.RuinWall: return "Decor/ruinwall_" + Mathf.Clamp(v, 0, 1);
                case MarkerKind.RuinPillar: return "Decor/ruinpillar_" + Mathf.Clamp(v, 0, 1);
                case MarkerKind.Fern: return "Decor/fern_" + Mathf.Clamp(v, 0, 1);
                case MarkerKind.Mushroom: return "Decor/shroom_" + Mathf.Clamp(v, 0, 1);
                case MarkerKind.ArenaGate: return "Decor/ruins_arch";
                case MarkerKind.BossGate: return "Decor/boss_arch";
            }
            return null;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            var p = transform.position;
            Color c = kind == MarkerKind.Boss ? new Color(1f, 0.3f, 0.3f) : kind == MarkerKind.Bonfire ? new Color(1f, 0.6f, 0.2f)
                    : kind == MarkerKind.Zombie || kind == MarkerKind.FirstZombie ? new Color(0.9f, 0.5f, 0.3f)
                    : kind == MarkerKind.PlayerSpawn ? Color.cyan : new Color(0.7f, 0.9f, 0.7f);
            Gizmos.color = c;
            if (kind == MarkerKind.Platform)
            {
                float x0 = Mathf.Round(p.x), y = Mathf.Round(p.y);
                Gizmos.DrawWireCube(new Vector3(x0 + width * 0.5f, y - 0.2f, 0f), new Vector3(width, 0.4f, 0f));
                Gizmos.color = new Color(c.r, c.g, c.b, 0.25f);
                Gizmos.DrawCube(new Vector3(x0 + width * 0.5f, y - 0.2f, 0f), new Vector3(width, 0.4f, 0f));
            }
            else if (kind == MarkerKind.MoundLookout)
            {
                Gizmos.DrawWireCube(p + new Vector3(0f, 1f, 0f), new Vector3(width, 2f, 0f));
            }
            else if (kind == MarkerKind.BossArenaEnd)
            {
                Gizmos.DrawLine(p + Vector3.down * 2f, p + Vector3.up * 10f);
            }
            else Gizmos.DrawWireSphere(p, 0.25f);
            Handles.color = c;
            Handles.Label(p + new Vector3(0f, kind == MarkerKind.Boss ? 6f : 3.2f, 0f), kind.ToString());
        }
#endif
    }
}
