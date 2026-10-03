using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MoonlitParry.EditorTools
{
    /// <summary>
    /// Keeps the drag-and-drop LevelLayout intuitive in the editor:
    /// • clicking a picture selects its marker (not the picture),
    /// • dragging / deleting the picture moves / deletes the marker,
    /// • when the mouse is released, markers drop onto the ledge or ground below them (same rule as the game),
    /// • a banner in the Scene view warns that edits made during Play are thrown away.
    /// </summary>
    [InitializeOnLoad]
    static class LayoutGuard
    {
        static double next;
#if UNITY_2019_1_OR_NEWER
        static GUIStyle banner;
#endif

        static LayoutGuard()
        {
            EditorApplication.update += Tick;
            Selection.selectionChanged += RedirectSelection;
#if UNITY_2019_1_OR_NEWER
            SceneView.duringSceneGui += SceneBanner;
#endif
        }

        static bool Lands(LevelMarker m)
        {
            switch (m.kind)
            {
                case MarkerKind.Platform: case MarkerKind.BossArenaEnd: case MarkerKind.MoundLookout: return false;
            }
            return !(m.IsDecor && m.layer == DrawLayer.BehindGround);   // "behind the ground" decor may be half-buried on purpose
        }

        static Transform Owner(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
            {
                if (p.GetComponent<LevelMarker>() != null || p.GetComponent<LevelPoint>() != null) return p;
                if (p.GetComponent<LevelLayout>() != null) return null;
            }
            return null;
        }

        static void RedirectSelection()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var sel = Selection.gameObjects;
            if (sel == null || sel.Length == 0) return;
            bool changed = false;
            var res = new List<Object>();
            foreach (var go in sel)
            {
                var o = Owner(go.transform);
                if (o != null && o.gameObject != go) { changed = true; if (!res.Contains(o.gameObject)) res.Add(o.gameObject); }
                else if (!res.Contains(go)) res.Add(go);
            }
            if (changed) Selection.objects = res.ToArray();
        }

        static void Tick()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 0.2;

#if UNITY_2023_1_OR_NEWER
            var layouts = Object.FindObjectsByType<LevelLayout>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var layouts = Object.FindObjectsOfType<LevelLayout>(true);
#endif
            if (layouts.Length == 0) RepairBroken();
            foreach (var lay in layouts) Heal(lay);
        }

        /// <summary>Safety net: a "LevelLayout" object whose scripts went missing gets its components back (positions kept).</summary>
        static void RepairBroken()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != "LevelLayout" || root.GetComponent<LevelLayout>() != null) continue;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                root.AddComponent<LevelLayout>();
                var ground = root.transform.Find("Ground");
                for (int gi = 0; gi < root.transform.childCount; gi++)
                {
                    var grp = root.transform.GetChild(gi);
                    for (int i = 0; i < grp.childCount; i++)
                    {
                        var c = grp.GetChild(i);
                        if (grp == ground) { c.gameObject.AddComponent<LevelPoint>(); continue; }
                        string n = c.name;
                        int cut = n.IndexOf(' ');
                        if (cut > 0) n = n.Substring(0, cut);
                        MarkerKind k;
                        if (!System.Enum.IsDefined(typeof(MarkerKind), n)) continue;
                        k = (MarkerKind)System.Enum.Parse(typeof(MarkerKind), n);
                        c.gameObject.AddComponent<LevelMarker>().kind = k;   // old picture is rebuilt by Heal()
                    }
                }
                EditorSceneManager.MarkSceneDirty(scene);
                Debug.LogWarning("[Moonlit Parry] LevelLayout bị mất script, đã tự gắn lại. Kiểm tra lại 'variant'/'width' nếu cần rồi Ctrl+S.");
            }
        }

        static void Heal(LevelLayout lay)
        {
            bool dirty = false;

            // ---- ground points
            var g = lay.Ground;
            if (g != null)
            {
                for (int i = g.childCount - 1; i >= 0; i--)
                {
                    var c = g.GetChild(i);
                    var lp = c.GetComponent<LevelPoint>();
                    if (lp == null) { lp = c.gameObject.AddComponent<LevelPoint>(); dirty = true; }
                    var pv = c.Find("preview");
                    if (pv == null)
                    {
                        if (lp.hasPreview) { Undo.DestroyObjectImmediate(c.gameObject); dirty = true; continue; }   // user deleted the dot
                        lp.BuildPreview(); dirty = true;
                    }
                    else if (pv.localPosition.sqrMagnitude > 1e-6f)          // user dragged the dot itself
                    {
                        Undo.RecordObjects(new Object[] { c, pv }, "Move ground point");
                        c.position = pv.position;
                        pv.localPosition = Vector3.zero;
                        dirty = true;
                    }
                }
            }

            // ---- what things stand on (same rule as in game)
            var raw = new List<Vector2>();
            if (g != null) for (int i = 0; i < g.childCount; i++) raw.Add(g.GetChild(i).position);
            var groundLine = LayoutData.Normalize(raw);
            if (groundLine.Count < 2) groundLine = LayoutData.Default().ground;
            var plats = new List<Vector3Int>();
            foreach (var m in lay.GetComponentsInChildren<LevelMarker>(true))
                if (m != null && m.kind == MarkerKind.Platform)
                {
                    int x0 = Mathf.RoundToInt(m.transform.position.x);
                    plats.Add(new Vector3Int(x0, x0 + Mathf.Max(1, m.width) - 1, Mathf.RoundToInt(m.transform.position.y)));
                }
            bool dragging = GUIUtility.hotControl != 0;

            // ---- markers
            foreach (var m in lay.GetComponentsInChildren<LevelMarker>(true))
            {
                if (m == null) continue;
                var pv = m.transform.Find("preview");
                if (pv == null)
                {
                    if (m.hasPreview) { Undo.DestroyObjectImmediate(m.gameObject); dirty = true; continue; }   // user deleted the picture
                    m.BuildPreview(); dirty = true;
                    continue;
                }
                if (m.previewKey != m.PreviewKey || !m.hasPreview) { m.BuildPreview(); dirty = true; continue; }   // variant / width changed

                var t = m.transform;
                if ((pv.localPosition - m.previewLocal).sqrMagnitude > 1e-6f)   // user dragged the picture itself
                {
                    Undo.RecordObjects(new Object[] { t, pv }, "Move marker");
                    t.position += pv.position - t.TransformPoint(m.previewLocal);
                    pv.localPosition = m.previewLocal;
                    dirty = true;
                }

                if (Mathf.Abs(t.position.z) > 1e-4f) { var q = t.position; q.z = 0f; t.position = q; dirty = true; }

                // once the mouse is released, drop it onto the ledge / ground below — exactly where the game will put it
                if (!dragging && Lands(m))
                {
                    var q = t.position;
                    float y = m.kind == MarkerKind.ArenaGate || m.kind == MarkerKind.BossGate
                        ? LayoutData.SurfaceAt(groundLine, q.x)
                        : LayoutData.Land(groundLine, plats, q.x, q.y);
                    if (Mathf.Abs(y - q.y) > 1e-3f) { q.y = y; t.position = q; dirty = true; }
                }
            }

            if (dirty && lay.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(lay.gameObject.scene);
        }

#if UNITY_2019_1_OR_NEWER
        static void SceneBanner(SceneView sv)
        {
            if (!EditorApplication.isPlaying) return;
            if (banner == null)
            {
                banner = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, wordWrap = true };
                banner.normal.textColor = new Color(1f, 0.85f, 0.3f);
            }
            Handles.BeginGUI();
            var r = new Rect(10, 10, 430, 46);
            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.75f));
            GUI.Label(new Rect(18, 14, 418, 40),
                "ĐANG PLAY: mọi thứ trong 'World' do code tạo lại, kéo/xóa ở đây sẽ MẤT khi Stop.\n" +
                "Stop → sửa trong LevelLayout → Ctrl+S → Play.", banner);
            Handles.EndGUI();
        }
#endif
    }
}
