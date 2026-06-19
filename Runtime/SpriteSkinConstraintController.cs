using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

namespace UnityEngine.U2D.Animation
{
    /// <summary>
    /// Applies primitive position, rotation, and scale constraints to SpriteSkin bone transforms at runtime.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(UpdateOrder.spriteSkinUpdateOrder - 1)]
    [AddComponentMenu("2D Animation/Sprite Skin Constraint Controller")]
    [DisallowMultipleComponent]
    public class SpriteSkinConstraintController : MonoBehaviour
    {
        [SerializeField, HideInInspector]
        SpriteSkin m_SpriteSkin;
        [SerializeField]
        SpriteSkinConstraintSet m_ConstraintSet;

        readonly List<RuntimeConstraint> m_RuntimeConstraints = new List<RuntimeConstraint>();
        readonly List<SpriteSkin> m_SpriteSkins = new List<SpriteSkin>();
        readonly Dictionary<string, Transform> m_BoneTransforms = new Dictionary<string, Transform>();

        public SpriteSkin spriteSkin
        {
            get => m_SpriteSkin;
            set
            {
                m_SpriteSkin = value;
                Rebind();
            }
        }

        public SpriteSkinConstraintSet constraintSet
        {
            get => m_ConstraintSet;
            set
            {
                m_ConstraintSet = value;
                Rebind();
            }
        }

        void Reset()
        {
            m_SpriteSkin = GetComponent<SpriteSkin>();
        }

        void OnEnable()
        {
            Rebind();
            ApplyConstraints();
        }

        void OnDisable()
        {
            RestorePose();
        }

        void LateUpdate()
        {
            ApplyConstraints();
        }

        void OnDidApplyAnimationProperties()
        {
            ApplyConstraints();
        }

        public void ApplyConstraints()
        {
            if (m_ConstraintSet == null)
                return;

            for (int i = 0; i < m_RuntimeConstraints.Count; ++i)
                m_RuntimeConstraints[i].Apply();
        }

        public void Rebind()
        {
            List<RuntimeConstraint> previousConstraints = new List<RuntimeConstraint>(m_RuntimeConstraints);
            m_RuntimeConstraints.Clear();
            m_SpriteSkins.Clear();
            m_BoneTransforms.Clear();

            if (m_ConstraintSet == null)
                return;

            CollectSpriteSkins(m_SpriteSkins);
            if (m_SpriteSkins.Count == 0)
                return;

            for (int i = 0; i < m_SpriteSkins.Count; ++i)
                AddBoneTransformsForSpriteSkin(m_SpriteSkins[i]);

            AddConstraints(previousConstraints);
        }

        public void UpdateConstraints()
        {
            Rebind();
            ApplyConstraints();
        }

        public void RestorePose()
        {
            for (int i = 0; i < m_RuntimeConstraints.Count; ++i)
                m_RuntimeConstraints[i].RestorePose();
        }

        void CollectSpriteSkins(List<SpriteSkin> spriteSkins)
        {
            if (m_SpriteSkin != null)
            {
                spriteSkins.Add(m_SpriteSkin);
                return;
            }

            GetComponentsInChildren(true, spriteSkins);
        }

        void AddBoneTransformsForSpriteSkin(SpriteSkin spriteSkin)
        {
            if (spriteSkin == null)
                return;

            Transform[] boneTransforms = spriteSkin.boneTransforms;
            if (boneTransforms == null)
                return;

            SpriteRenderer spriteRenderer = spriteSkin.GetComponent<SpriteRenderer>();
            Sprite sprite = spriteRenderer != null ? spriteRenderer.sprite : null;
            if (sprite == null)
                return;

            SpriteBone[] bones = sprite.GetBones();
            if (bones == null)
                return;

            for (int i = 0; i < bones.Length && i < boneTransforms.Length; ++i)
            {
                string guid = bones[i].guid;
                Transform boneTransform = boneTransforms[i];
                if (string.IsNullOrEmpty(guid) || boneTransform == null || m_BoneTransforms.ContainsKey(guid))
                    continue;

                m_BoneTransforms.Add(guid, boneTransform);
            }
        }

        void AddConstraints(List<RuntimeConstraint> previousConstraints)
        {
            foreach (SpriteBoneConstraint constraint in m_ConstraintSet.constraints)
            {
                Transform source = FindBoneTransform(constraint.sourceBoneGuid);
                Transform driven = FindDrivenTransform(constraint.drivenBoneGuid);
                if (source == null || driven == null || source == driven)
                    continue;

                if (ContainsRuntimeConstraint(constraint, source, driven))
                    continue;

                RuntimeConstraint previous = FindRuntimeConstraint(previousConstraints, constraint, source, driven);
                m_RuntimeConstraints.Add(new RuntimeConstraint(constraint, source, driven, previous));
            }
        }

        static RuntimeConstraint FindRuntimeConstraint(List<RuntimeConstraint> constraints, SpriteBoneConstraint data, Transform source, Transform driven)
        {
            for (int i = 0; i < constraints.Count; ++i)
            {
                if (constraints[i].Matches(data, source, driven))
                    return constraints[i];
            }

            return null;
        }

        bool ContainsRuntimeConstraint(SpriteBoneConstraint data, Transform source, Transform driven)
        {
            for (int i = 0; i < m_RuntimeConstraints.Count; ++i)
            {
                if (m_RuntimeConstraints[i].Matches(data, source, driven))
                    return true;
            }

            return false;
        }

        Transform FindBoneTransform(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return null;

            m_BoneTransforms.TryGetValue(guid, out Transform transform);
            return transform;
        }

        Transform FindDrivenTransform(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return null;

            if (m_BoneTransforms.TryGetValue(SpriteSkinConstraintParent.GetGuid(guid), out Transform parent))
                return parent;

            Transform driven = FindBoneTransform(guid);
            if (driven != null && IsConstraintParentTransform(driven.parent))
                return driven.parent;

            return null;
        }

        static bool IsConstraintParentTransform(Transform transform)
        {
            return transform != null && transform.name.EndsWith(" Constraint");
        }

        class RuntimeConstraint
        {
            readonly SpriteBoneConstraint m_Data;
            readonly Transform m_Source;
            readonly Transform m_Driven;
            readonly Vector3 m_SourcePosition;
            readonly Vector3 m_DrivenPosition;
            readonly Vector3 m_SourceScale;
            readonly Vector3 m_DrivenScale;
            readonly Quaternion m_SourceRotation;
            readonly Quaternion m_DrivenRotation;

            public RuntimeConstraint(SpriteBoneConstraint data, Transform source, Transform driven, RuntimeConstraint previous)
            {
                m_Data = data;
                m_Source = source;
                m_Driven = driven;
                m_SourcePosition = previous != null ? previous.m_SourcePosition : source.localPosition;
                m_DrivenPosition = previous != null ? previous.m_DrivenPosition : driven.localPosition;
                m_SourceScale = previous != null ? previous.m_SourceScale : source.localScale;
                m_DrivenScale = previous != null ? previous.m_DrivenScale : driven.localScale;
                m_SourceRotation = previous != null ? previous.m_SourceRotation : source.localRotation;
                m_DrivenRotation = previous != null ? previous.m_DrivenRotation : driven.localRotation;
            }

            public bool Matches(SpriteBoneConstraint data, Transform source, Transform driven)
            {
                return ReferenceEquals(m_Data, data) && m_Source == source && m_Driven == driven;
            }

            public void Apply()
            {
                float influence = Mathf.Clamp01(m_Data.influence);
                Vector3 multiplier = m_Data.multiplier;

                switch (m_Data.type)
                {
                    case SpriteBoneConstraintType.Position:
                        Vector3 positionDelta = Vector3.Scale(m_Source.localPosition - m_SourcePosition, multiplier) * influence;
                        Vector3 worldDelta = m_Source.parent != null ? m_Source.parent.TransformVector(positionDelta) : positionDelta;
                        Vector3 drivenLocalDelta = m_Driven.parent != null ? m_Driven.parent.InverseTransformVector(worldDelta) : worldDelta;
                        m_Driven.localPosition = m_DrivenPosition + drivenLocalDelta;
                        break;

                    case SpriteBoneConstraintType.Rotation:
                        Vector3 sourceEuler = m_SourceRotation.eulerAngles;
                        Vector3 currentEuler = m_Source.localRotation.eulerAngles;
                        Vector3 rotationDelta = new Vector3(
                            Mathf.DeltaAngle(sourceEuler.x, currentEuler.x) * multiplier.x,
                            Mathf.DeltaAngle(sourceEuler.y, currentEuler.y) * multiplier.y,
                            Mathf.DeltaAngle(sourceEuler.z, currentEuler.z) * multiplier.z) * influence;
                        m_Driven.localRotation = m_DrivenRotation * Quaternion.Euler(rotationDelta);
                        break;

                    case SpriteBoneConstraintType.Scale:
                        Vector3 scaleDelta = Vector3.Scale(m_Source.localScale - m_SourceScale, multiplier) * influence;
                        m_Driven.localScale = m_DrivenScale + scaleDelta;
                        break;
                }
            }

            public void RestorePose()
            {
                switch (m_Data.type)
                {
                    case SpriteBoneConstraintType.Position:
                        m_Driven.localPosition = m_DrivenPosition;
                        break;
                    case SpriteBoneConstraintType.Rotation:
                        m_Driven.localRotation = m_DrivenRotation;
                        break;
                    case SpriteBoneConstraintType.Scale:
                        m_Driven.localScale = m_DrivenScale;
                        break;
                }
            }
        }
    }

}
