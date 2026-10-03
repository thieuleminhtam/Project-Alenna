using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace MoonlitParry.EditorTools
{
    /// <summary>
    /// Name and icon of the player build: "Lord of Alenna (Demo)" with the heroine portrait from MoonlitParry/Branding.
    /// Written into Player Settings when the editor loads (only if something differs) and again before every
    /// Moonlit Parry ▸ Build Windows, so Unity's own Build / Build And Run get the same name and icon.
    /// </summary>
    [InitializeOnLoad]
    public static class Branding
    {
        public const string ProductName = "Lord of Alenna (Demo)";
        public const string CompanyName = "Alenna";
        const string Dir = "Assets/MoonlitParry/Branding/";
        static readonly int[] Made = { 1024, 512, 256, 128, 64, 48, 32, 16 };

        static Branding()
        {
            EditorApplication.delayCall += () => Apply(false);
        }

        [MenuItem("Moonlit Parry/Apply Name + Icon", false, 102)]
        static void ApplyMenu() { Apply(true); }

        public static void Apply(bool force)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var big = Load(1024);
            var current = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            bool iconOk = big == null || (current != null && current.Length > 0 && current[0] == big);
            if (!force && iconOk && PlayerSettings.productName == ProductName && PlayerSettings.companyName == CompanyName) return;

            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = CompanyName;
            if (big != null)
            {
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { big }, IconKind.Any);
                // Standalone slots get hand-made small sizes so 16/32/48 px stay sharp instead of a blurry downscale.
                int[] sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any);
                if (sizes != null && sizes.Length > 0)
                {
                    var tex = new Texture2D[sizes.Length];
                    for (int i = 0; i < sizes.Length; i++) tex[i] = Load(Pick(sizes[i]));
                    PlayerSettings.SetIcons(NamedBuildTarget.Standalone, tex, IconKind.Any);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Moonlit Parry] Player Settings: \"" + ProductName + "\"" + (big != null ? " + icon" : " (icon file missing)"));
        }

        /// <summary>Smallest prepared icon that is at least <paramref name="size"/> px (the 1024 one above that).</summary>
        static int Pick(int size)
        {
            int best = Made[0];
            foreach (int m in Made) if (m >= size && m < best) best = m;
            return best;
        }

        static Texture2D Load(int size) { return AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "icon_" + size + ".png"); }
    }
}
