using System;
using UnityEngine.Rendering;
using UnityEngine.U2D;

namespace UnityEngine.U2D.Animation
{
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(SpriteRenderer))]
    [AddComponentMenu("2D Animation/Uniskin Clipping Mask")]
    [DefaultExecutionOrder(UpdateOrder.spriteSkinUpdateOrder + 1)]
    public sealed class UniskinClippingMask : MonoBehaviour
    {
        [HideInInspector] public UniskinClippingMaskData contour;
        [HideInInspector] public UniskinRigBinding character;
        [HideInInspector] public SpriteRenderer contourRenderer;
        [SerializeField, HideInInspector] Material m_PreviousMaterial;
        [SerializeField, HideInInspector] int m_PreviousOrder, m_PreviousLayer;
        [SerializeField, HideInInspector] SpriteMaskInteraction m_PreviousInteraction;
        [SerializeField, HideInInspector] bool m_OwnsRenderer, m_PreviousSpriteMaskEnabled;
        [SerializeField, HideInInspector] bool m_SortingFromSprite;
        [SerializeField, HideInInspector] UniskinSpriteMask m_SpriteMask;

        SpriteRenderer m_Target;
        // Serialized references are remapped by Instantiate before lifecycle callbacks.
        // The helpers themselves remain DontSave, so scene/prefab files do not own them.
        [SerializeField, HideInInspector] SpriteRenderer m_Clear;
        [SerializeField, HideInInspector] bool m_RuntimeContour;
        [SerializeField, HideInInspector] SortingGroup m_Group;
        int m_Revision = -1, m_CharacterRevision = -1;
        bool m_Editing;
        SpriteRenderer m_EditingPreview;
        MaterialPropertyBlock m_Properties;
        static Sprite s_ClearSprite;
        static Material s_WriteMaterial, s_ClearMaterial, s_LitMaterial, s_UnlitMaterial;

        public void SetEditing(bool editing)
        {
            if (m_EditingPreview != null) m_EditingPreview.enabled = false;
            m_EditingPreview = null;
            m_Editing = editing;
            if (editing) Restore(); else Refresh();
        }

        public void SetEditingPreview(SpriteRenderer renderer)
        {
            if (renderer == null) { SetEditing(true); return; }
            if (m_EditingPreview == renderer && !m_Editing) { UpdateClearBounds(); return; }
            m_EditingPreview = renderer; m_Editing = false; Refresh();
        }

        void OnEnable() => Refresh();
        void OnDisable() => Restore(!gameObject.activeInHierarchy);
        void OnValidate() { m_Revision = -1; }

        void LateUpdate()
        {
            if (m_Editing || contour == null || (!contour.hasGeometry && m_EditingPreview == null)) { if (m_OwnsRenderer) Restore(); return; }
            if (character == null) character = GetComponentInParent<UniskinRigBinding>();
            if (character == null) return;
            if (m_OwnsRenderer && m_Target != null && (m_Target.sortingOrder != m_PreviousOrder || m_Target.sortingLayerID != m_PreviousLayer)) SyncSorting();
            if (contourRenderer == null || m_Revision != contour.revision || m_CharacterRevision != character.appliedRevision || !m_OwnsRenderer) Refresh();
            UpdateClearBounds();
        }

        public void Refresh()
        {
            if (character == null) character = GetComponentInParent<UniskinRigBinding>();
            if (!isActiveAndEnabled || m_Editing || contour == null || (!contour.hasGeometry && m_EditingPreview == null) || contour.sprite == null || character == null)
            {
                if (m_OwnsRenderer) Restore();
                return;
            }
            if (character.rig != contour.rig) { if (m_OwnsRenderer) Restore(); return; }
            if (m_Target == null) m_Target = GetComponent<SpriteRenderer>();
            if (!m_SortingFromSprite)
            {
                if (m_OwnsRenderer && m_Target.sortingOrder == 0) m_Target.sortingOrder = m_PreviousOrder;
                m_SortingFromSprite = true;
            }
            var existingGroup = m_Group == null ? GetComponent<SortingGroup>() : null;
            if (m_Group == null && existingGroup != null)
            {
                // Instantiate may copy this component after OnDisable restored
                // ownership, but still copy its native DontSave SortingGroup.
                if ((existingGroup.hideFlags & HideFlags.HideAndDontSave) == HideFlags.HideAndDontSave && m_PreviousMaterial != null)
                {
                    m_Group = existingGroup; m_OwnsRenderer = true;
                }
                else throw new InvalidOperationException("Place Uniskin Clipping Mask on a Sprite layer without its own SortingGroup.");
            }
            if (contourRenderer == null)
            {
                var go = new GameObject("Uniskin Clipping Contour") { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(transform, false);
                var relative = transform.worldToLocalMatrix * character.transform.localToWorldMatrix * contour.rigToContour.inverse;
                go.transform.localPosition = relative.GetColumn(3); go.transform.localRotation = relative.rotation; go.transform.localScale = relative.lossyScale;
                contourRenderer = go.AddComponent<SpriteRenderer>(); m_RuntimeContour = true;
            }
            if (m_Properties == null) m_Properties = new MaterialPropertyBlock();
            if (!m_OwnsRenderer)
            {
                // A texture mask follows the body surface. Suspend it while the
                // independently weighted contour owns this renderer.
                m_SpriteMask = GetComponent<UniskinSpriteMask>();
                if (m_SpriteMask != null) { m_PreviousSpriteMaskEnabled = m_SpriteMask.enabled; m_SpriteMask.enabled = false; }
                m_PreviousMaterial = m_Target.sharedMaterial;
                m_PreviousOrder = m_Target.sortingOrder; m_PreviousLayer = m_Target.sortingLayerID;
                m_PreviousInteraction = m_Target.maskInteraction;
                m_OwnsRenderer = true;
            }
            if (m_Group == null)
            {
                m_Group = gameObject.AddComponent<SortingGroup>();
                m_Group.hideFlags = HideFlags.HideAndDontSave | HideFlags.HideInInspector;
                m_Group.sortingLayerID = m_PreviousLayer; m_Group.sortingOrder = m_PreviousOrder;
            }
            m_Group.enabled = true;
            m_Group.sortingLayerID = m_PreviousLayer; m_Group.sortingOrder = m_PreviousOrder;
            bool unlit = m_PreviousMaterial != null && m_PreviousMaterial.shader != null &&
                (m_PreviousMaterial.shader.name.Contains("Unlit") || m_PreviousMaterial.shader.name == "Sprites/Default" ||
                (m_PreviousMaterial.shader.name == "Uniskin/UniskinMask" && m_PreviousMaterial.GetFloat("_UniskinUseLighting") < .5f));
            m_Target.sharedMaterial = Load(ref s_LitMaterial, ref s_UnlitMaterial, unlit);
            m_Target.maskInteraction = SpriteMaskInteraction.None;
            m_Target.GetPropertyBlock(m_Properties);
            m_Properties.SetFloat("_UniskinMaskEnabled", 0);
            m_Target.SetPropertyBlock(m_Properties);
            if (s_WriteMaterial == null) s_WriteMaterial = Resources.Load<Material>("UniskinClipWrite");
            if (s_ClearMaterial == null) s_ClearMaterial = Resources.Load<Material>("UniskinClipClear");
            var writer = m_EditingPreview != null ? m_EditingPreview : contourRenderer;
            writer.sharedMaterial = s_WriteMaterial;
            writer.maskInteraction = SpriteMaskInteraction.None;
            writer.enabled = true;
            if (m_EditingPreview == null)
            {
                contourRenderer.sprite = contour.sprite;
                var bones = contour.sprite.GetBones();
                var transforms = new Transform[bones.Length];
                for (var i = 0; i < transforms.Length; ++i)
                    transforms[i] = Array.Find(character.bones, link => link.guid == bones[i].guid)?.transform;
                var skin = contourRenderer.GetComponent<SpriteSkin>();
                if (skin == null && bones.Length > 0) skin = contourRenderer.gameObject.AddComponent<SpriteSkin>();
                if (skin != null && Array.TrueForAll(transforms, item => item != null))
                {
                    skin.SetRootBone(character.transform); skin.SetBoneTransforms(transforms); skin.ResetSprite();
                }
            }
            if (m_Clear == null)
            {
                for (var i = 0; i < transform.childCount; ++i)
                {
                    var child = transform.GetChild(i);
                    if (child.name == "Uniskin Clip Clear" && child.gameObject.hideFlags == HideFlags.HideAndDontSave)
                        m_Clear = child.GetComponent<SpriteRenderer>();
                }
            }
            if (m_Clear == null)
            {
                var go = new GameObject("Uniskin Clip Clear") { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(transform, false);
                m_Clear = go.AddComponent<SpriteRenderer>();
                if (s_ClearSprite == null)
                {
                    s_ClearSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height), Vector2.one * .5f, Texture2D.whiteTexture.width);
                    s_ClearSprite.hideFlags = HideFlags.HideAndDontSave;
                }
                m_Clear.sprite = s_ClearSprite;
            }
            m_Clear.enabled = true;
            m_Clear.sharedMaterial = s_ClearMaterial;
            SyncSorting();
            m_Clear.gameObject.layer = gameObject.layer;
            contourRenderer.gameObject.layer = gameObject.layer;
            writer.gameObject.layer = gameObject.layer;
            m_Revision = contour.revision; m_CharacterRevision = character.appliedRevision;
            UpdateClearBounds();
        }

        static Material Load(ref Material lit, ref Material unlit, bool useUnlit)
        {
            if (useUnlit) return unlit != null ? unlit : unlit = Resources.Load<Material>("UniskinClipBodyUnlit");
            return lit != null ? lit : lit = Resources.Load<Material>("UniskinClipBody");
        }

        void SyncSorting()
        {
            m_PreviousLayer = m_Target.sortingLayerID; m_PreviousOrder = m_Target.sortingOrder;
            if (m_Group != null) { m_Group.sortingLayerID = m_PreviousLayer; m_Group.sortingOrder = m_PreviousOrder; }
            var writer = m_EditingPreview != null ? m_EditingPreview : contourRenderer;
            if (writer != null) { writer.sortingLayerID = m_PreviousLayer; writer.sortingOrder = m_PreviousOrder - 1; }
            if (m_Clear != null) { m_Clear.sortingLayerID = m_PreviousLayer; m_Clear.sortingOrder = m_PreviousOrder + 1; }
        }

        void UpdateClearBounds()
        {
            var writer = m_EditingPreview != null ? m_EditingPreview : contourRenderer;
            if (m_Clear == null || writer == null) return;
            var bounds = writer.bounds;
            var inverse = transform.worldToLocalMatrix;
            var a = inverse.MultiplyPoint3x4(new Vector3(bounds.min.x, bounds.min.y, bounds.center.z));
            var b = inverse.MultiplyPoint3x4(new Vector3(bounds.max.x, bounds.min.y, bounds.center.z));
            var c = inverse.MultiplyPoint3x4(new Vector3(bounds.min.x, bounds.max.y, bounds.center.z));
            var d = inverse.MultiplyPoint3x4(new Vector3(bounds.max.x, bounds.max.y, bounds.center.z));
            var min = Vector3.Min(Vector3.Min(a, b), Vector3.Min(c, d));
            var max = Vector3.Max(Vector3.Max(a, b), Vector3.Max(c, d));
            m_Clear.transform.localPosition = (min + max) * .5f;
            m_Clear.transform.localScale = new Vector3(max.x - min.x + .01f, max.y - min.y + .01f, 1);
        }

        void Restore(bool deferDestruction = false)
        {
            // Unity can omit DontSave references when cloning/reloading an object,
            // although the cloned helper components are already present.
            if (m_OwnsRenderer && m_Group == null)
            {
                var group = GetComponent<SortingGroup>();
                if (group != null && (group.hideFlags & HideFlags.HideAndDontSave) == HideFlags.HideAndDontSave) m_Group = group;
            }
            if (m_OwnsRenderer && m_Clear == null)
                for (var i = 0; i < transform.childCount; ++i)
                {
                    var child = transform.GetChild(i);
                    if (child.name == "Uniskin Clip Clear" && child.gameObject.hideFlags == HideFlags.HideAndDontSave)
                        m_Clear = child.GetComponent<SpriteRenderer>();
                }
            if (m_Target == null) m_Target = GetComponent<SpriteRenderer>();
            if (m_OwnsRenderer && m_Target != null)
            {
                m_Target.sharedMaterial = m_PreviousMaterial;
                if (!m_SortingFromSprite) { m_Target.sortingLayerID = m_PreviousLayer; m_Target.sortingOrder = m_PreviousOrder; }
                m_Target.maskInteraction = m_PreviousInteraction;
                if (m_SpriteMask != null) m_SpriteMask.enabled = m_PreviousSpriteMaskEnabled;
            }
            m_OwnsRenderer = false;
            if (m_EditingPreview != null) m_EditingPreview.enabled = false;
            if (contourRenderer != null) contourRenderer.enabled = false;
            if (m_RuntimeContour && contourRenderer != null) { DestroyHelper(contourRenderer.gameObject, deferDestruction); contourRenderer = null; m_RuntimeContour = false; }
            if (m_Clear != null) { m_Clear.enabled = false; DestroyHelper(m_Clear.gameObject, deferDestruction); }
            if (m_Group != null) { m_Group.enabled = false; DestroyHelper(m_Group, deferDestruction); }
            m_Clear = null; m_Group = null;
        }

        void DestroyHelper(UnityEngine.Object obj, bool deferDestruction)
        {
            if (Application.isPlaying) { Destroy(obj); return; }
#if UNITY_EDITOR
            if (deferDestruction)
            {
                // Undo can invoke OnDisable inside GameObject activation. Unity
                // forbids immediate component destruction until it finishes.
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (obj == null) return;
                    // Rapid reactivation may have reclaimed the native helpers.
                    if (this != null && (obj == m_Group ||
                        (m_Clear != null && obj == m_Clear.gameObject) ||
                        (contourRenderer != null && obj == contourRenderer.gameObject))) return;
                    DestroyImmediate(obj);
                };
                return;
            }
#endif
            DestroyImmediate(obj);
        }
    }
}
