using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace MoonlitParry.EditorTools
{
    /// <summary>Forces pixel-art import settings on every sprite inside MoonlitParry/Resources/Sprites.</summary>
    public class MoonlitImporter : AssetPostprocessor
    {
        const string Root = "MoonlitParry/Resources/Sprites/";
        const string HD = "MoonlitParry/Resources/HD/";

        /// <summary>HD (smooth vector) art: bilinear, mipmapped, high-quality compression, PPU per folder.</summary>
        void ImportHD(string path)
        {
            var ti = (TextureImporter)assetImporter;
            string rel = path.Substring(path.IndexOf(HD) + HD.Length);
            bool rig = rel.StartsWith("Rigs/"), terrain = rel.StartsWith("Terrain/"), bg = rel.StartsWith("BG/");
            ti.textureType = rig || terrain ? TextureImporterType.Default : TextureImporterType.Sprite;
            if (!rig && !terrain) ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.isReadable = false;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 4096;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.mipmapEnabled = !bg;
            ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            ti.mipMapBias = -0.4f;
            ti.filterMode = bg ? FilterMode.Bilinear : FilterMode.Trilinear;
            ti.wrapMode = terrain ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            if (rig || terrain) return;
            ti.spritePixelsPerUnit = bg ? 50 : rel.StartsWith("FX/slash_") ? 40 : 100;   // big boss slashes are drawn at 40 px/unit
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteMeshType = SpriteMeshType.FullRect;
            s.spriteExtrude = 0;
            if (rel.StartsWith("Tiles/") || rel.StartsWith("Under/"))
                s.spriteAlignment = (int)SpriteAlignment.BottomLeft;
            else if (rel.StartsWith("Player/") || rel.StartsWith("Zombie/") || rel.StartsWith("Boss/") || rel.StartsWith("Decor/"))
                s.spriteAlignment = (int)SpriteAlignment.BottomCenter;
            else
                s.spriteAlignment = (int)SpriteAlignment.Center;
            ti.SetTextureSettings(s);
        }

        void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (path.Contains(HD)) { ImportHD(path); return; }
            if (path.Contains("MoonlitParry/Branding/"))           // game icon: exact pixels, no compression or mips
            {
                var bi = (TextureImporter)assetImporter;
                bi.textureType = TextureImporterType.Default;
                bi.npotScale = TextureImporterNPOTScale.None;
                bi.mipmapEnabled = false;
                bi.filterMode = FilterMode.Point;
                bi.textureCompression = TextureImporterCompression.Uncompressed;
                bi.alphaIsTransparency = true;
                bi.isReadable = true;
                bi.maxTextureSize = 2048;
                bi.wrapMode = TextureWrapMode.Clamp;
                return;
            }
            if (!path.Contains(Root)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = path.Contains(Root + "Player/") || path.Contains(Root + "Boss/") ? 24 : 16;   // heroine + boss: 1.5x pixel density
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.isReadable = true;        // white hit-flash silhouettes are generated at runtime
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.npotScale = TextureImporterNPOTScale.None;

            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteMeshType = SpriteMeshType.FullRect;
            s.spriteExtrude = 0;
            if (path.Contains(Root + "Tiles/") || path.Contains(Root + "Under/"))
                s.spriteAlignment = (int)SpriteAlignment.BottomLeft;
            else if (path.Contains(Root + "Player/"))
            {
                // heroine frames (player5.py) are 192 x 132 with 24 empty rows under the feet line: pivot on that line
                s.spriteAlignment = (int)SpriteAlignment.Custom;
                s.spritePivot = new Vector2(0.5f, 24f / 132f);
            }
            else if (path.Contains(Root + "Zombie/") || path.Contains(Root + "Boss/") || path.Contains(Root + "Decor/"))
                s.spriteAlignment = (int)SpriteAlignment.BottomCenter;
            else
                s.spriteAlignment = (int)SpriteAlignment.Center;
            ti.SetTextureSettings(s);
        }
    }

    public static class MoonlitMenu
    {
        const string ScenePath = "Assets/MoonlitParry/Scenes/MoonlitParry.unity";
        const string MatPath = "Assets/MoonlitParry/Resources/MoonlitSpriteMat.mat";

        [MenuItem("Moonlit Parry/1. Setup Project (Create Scene)", false, 0)]
        public static void Setup()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Moonlit Parry", "Hãy bấm Stop (thoát Play mode) trước khi chạy Setup.", "OK");
                return;
            }
            // 1) re-import sprites so the pixel-art settings are applied even if they were imported before the scripts compiled
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/MoonlitParry/Resources/Sprites" });
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < guids.Length; i++)
                {
                    var p = AssetDatabase.GUIDToAssetPath(guids[i]);
                    AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            // 2) unlit sprite material (works for Built-in and URP; URP 2D "Lit" sprites would be black without 2D lights)
            Shader sh = null;
            if (GraphicsSettings.currentRenderPipeline != null)
                sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh != null)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
                if (mat == null)
                {
                    mat = new Material(sh);
                    AssetDatabase.CreateAsset(mat, MatPath);
                }
                else mat.shader = sh;
                EditorUtility.SetDirty(mat);
            }

            // 3) scene with the GameManager
            Directory.CreateDirectory("Assets/MoonlitParry/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var gm = new GameObject("GameManager");
            gm.AddComponent<GameManager>();
            EditorSceneManager.SaveScene(scene, ScenePath);

            // 4) put it first in Build Settings
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(x => x.path == ScenePath);
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();

            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Moonlit Parry",
                "Đã thiết lập xong!\n\n• " + guids.Length + " sprite pixel-art (16 PPU, Point filter)\n• Scene: " + ScenePath +
                "\n\nBấm Play để chơi. Đặt cửa sổ Game ở tỉ lệ 16:9 để khung hình đẹp nhất.", "OK");
        }

        [MenuItem("Moonlit Parry/2. Open Scene", false, 1)]
        public static void OpenScene()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Moonlit Parry", "Hãy bấm Stop (thoát Play mode) trước, rồi mở scene.", "OK");
                return;
            }
            if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
            else Setup();
        }

        // ------------------------------------------------------------------ drag-and-drop level layout
        [MenuItem("Moonlit Parry/3. Bake Level Layout (kéo thả)", false, 20)]
        public static void BakeLayout()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Moonlit Parry", "Hãy bấm Stop (thoát Play mode) trước.", "OK");
                return;
            }
            var existing = FindLayout();
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("Moonlit Parry", "Scene đã có LevelLayout. Xóa và tạo lại từ bản đồ mặc định?", "Tạo lại", "Hủy")) return;
                Undo.DestroyObjectImmediate(existing.gameObject);
            }
            var d = LayoutData.Default();
            var root = new GameObject("LevelLayout");
            root.AddComponent<LevelLayout>();
            Undo.RegisterCreatedObjectUndo(root, "Bake Level Layout");

            var ground = new GameObject("Ground").transform;
            ground.SetParent(root.transform, false);
            for (int i = 0; i < d.ground.Count; i++)
            {
                var pt = new GameObject("P" + i.ToString("00")).transform;
                pt.SetParent(ground, false);
                pt.position = d.ground[i];
                pt.gameObject.AddComponent<LevelPoint>().BuildPreview();
            }

            var gChars = Group(root, "Nhân vật & quái");
            var gFire = Group(root, "Lửa trại");
            var gPlat = Group(root, "Bục nhảy");
            var gDecor = Group(root, "Trang trí");
            var gZone = Group(root, "Cổng & vùng kích hoạt");

            Mk(gChars, MarkerKind.PlayerSpawn, d.spawnX, d, 0, 0);
            if (!float.IsNaN(d.firstZombie)) Mk(gChars, MarkerKind.FirstZombie, d.firstZombie, d, 0, 0);
            foreach (var x in d.zombies) Mk(gChars, MarkerKind.Zombie, x, d, 0, 0);
            Mk(gChars, MarkerKind.Boss, d.boss, d, 0, 0);
            foreach (var x in d.bonfires) Mk(gFire, MarkerKind.Bonfire, x, d, 0, 0);
            foreach (var pl in d.platforms)
            {
                var m = Mk(gPlat, MarkerKind.Platform, pl.x, d, 0, pl.y - pl.x + 1);
                m.transform.position = new Vector3(pl.x, pl.z, 0f);
            }
            foreach (var t in d.trees) Mk(gDecor, MarkerKind.Tree, t.x, d, t.v, 0);
            foreach (var t in d.rocks) Mk(gDecor, MarkerKind.Rock, t.x, d, t.v, 0);
            foreach (var t in d.bushes) Mk(gDecor, MarkerKind.Bush, t.x, d, t.v, 0);
            foreach (var t in d.ruinWalls) Mk(gDecor, MarkerKind.RuinWall, t.x, d, t.v, 0);
            foreach (var t in d.ruinPillars) Mk(gDecor, MarkerKind.RuinPillar, t.x, d, t.v, 0);
            foreach (var t in d.ferns) Mk(gDecor, MarkerKind.Fern, t.x, d, t.v, 0);
            foreach (var t in d.shrooms) Mk(gDecor, MarkerKind.Mushroom, t.x, d, t.v, 0);
            Mk(gZone, MarkerKind.ArenaGate, d.arenaIn, d, 0, 0);
            Mk(gZone, MarkerKind.ArenaGate, d.arenaOut, d, 0, 0);
            Mk(gZone, MarkerKind.BossGate, d.bossGate, d, 0, 0);
            Mk(gZone, MarkerKind.BossArenaEnd, d.bossArenaEnd, d, 0, 0);
            Mk(gZone, MarkerKind.MoundLookout, (d.moundX0 + d.moundX1) * 0.5f, d, 0, Mathf.RoundToInt(d.moundX1 - d.moundX0));

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorUtility.DisplayDialog("Moonlit Parry",
                "Đã tạo LevelLayout trong scene.\n\n• Kéo các điểm vàng trong LevelLayout/Ground để đổi địa hình (tự làm tròn theo ô, dốc 1:2 tự sinh).\n" +
                "• Kéo quái, lửa trại, bục, cây, đá, cổng... trong các nhóm con. Nhân bản (Ctrl+D) để thêm, Delete để xóa.\n" +
                "• Bục: chỉnh 'width' trong Inspector. Đồ trang trí: chỉnh 'variant'.\n\nLưu scene (Ctrl+S) rồi bấm Play.", "OK");
        }

        [MenuItem("Moonlit Parry/4. Xóa Level Layout (dùng bản đồ mặc định)", false, 21)]
        public static void RemoveLayout()
        {
            var l = FindLayout();
            if (l != null) { Undo.DestroyObjectImmediate(l.gameObject); EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); }
        }

        static LevelLayout FindLayout()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<LevelLayout>(FindObjectsInactive.Include);
#else
            return Object.FindObjectOfType<LevelLayout>(true);
#endif
        }

        static Transform Group(GameObject root, string name)
        {
            var g = new GameObject(name).transform;
            g.SetParent(root.transform, false);
            return g;
        }

        static float Surface(LayoutData d, float x)
        {
            var g = d.ground;
            for (int i = 0; i < g.Count - 1; i++)
            {
                if (Mathf.Approximately(g[i].x, g[i + 1].x)) continue;
                if (x >= g[i].x && x <= g[i + 1].x) return Mathf.Lerp(g[i].y, g[i + 1].y, (x - g[i].x) / (g[i + 1].x - g[i].x));
            }
            return 0f;
        }

        static LevelMarker Mk(Transform parent, MarkerKind kind, float x, LayoutData d, int variant, int width)
        {
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, Surface(d, x), 0f);
            var m = go.AddComponent<LevelMarker>();
            m.kind = kind;
            m.variant = variant;
            if (width > 0) m.width = width;
            m.BuildPreview();
            return m;
        }
    }
}
