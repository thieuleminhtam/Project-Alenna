using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MoonlitParry
{
    // NOTE: Unity only saves a MonoBehaviour in a scene when it lives in a file with the SAME name as the class.
    /// <summary>A ground vertex of LevelLayout/Ground (selectable in the Scene view through its preview dot).</summary>
    [SelectionBase]
    public class LevelPoint : MonoBehaviour
    {
        [HideInInspector] public bool hasPreview;

#if UNITY_EDITOR
        public void BuildPreview()
        {
            var old = transform.Find("preview");
            if (old != null) DestroyImmediate(old.gameObject);
            var pv = new GameObject("preview");
            pv.tag = "EditorOnly";
            pv.transform.SetParent(transform, false);
            pv.transform.localScale = new Vector3(7f, 7f, 1f);
            var sr = pv.AddComponent<SpriteRenderer>();
            sr.sprite = Resources.Load<Sprite>("Sprites/FX/pixel");
            sr.color = new Color(1f, 0.9f, 0.3f, 0.95f);
            sr.sortingOrder = 60;
            hasPreview = true;
        }
#endif
    }
}
