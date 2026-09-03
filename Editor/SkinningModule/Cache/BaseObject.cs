#if UNITY_6000_4_OR_NEWER
using ObjectId = UnityEngine.EntityId;
#else
using ObjectId = System.Int32;
#endif

using System.Collections.Generic;
using UnityEngine.U2D.Animation;
using System;
using UnityEngine;

namespace UnityEditor.U2D.Animation
{
#if CODE_COVERAGE
    internal class BaseObject
    {
        public static T CreateInstance<T>()
        {
            return Activator.CreateInstance<T>();
        }

        public static void DestroyImmediate(object o)
        {
            if (o is BaseObject)
            {
                var obj = o as BaseObject;
                obj.OnDestroy();
                s_Objects.Remove(obj.GetObjectId());
            }
            else if (o is UnityEngine.Object)
            {
                var obj = o as UnityEngine.Object;
                Undo.ClearUndo(obj);
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        public static BaseObject InstanceIDToObject(ObjectId instanceID)
        {
            var obj = default(BaseObject);
            s_Objects.TryGetValue(instanceID, out obj);
            return obj;
        }

        private static Dictionary<ObjectId, BaseObject> s_Objects = new Dictionary<ObjectId, BaseObject>();
        private static int s_InstanceID = 0;
        private int m_InstanceID;

        public string name { get; set; }
        public HideFlags hideFlags = HideFlags.None;

        public BaseObject()
        {
            m_InstanceID = ++s_InstanceID;
            s_Objects.Add(GetObjectId(), this);
        }

        internal virtual void OnEnable() { }
        internal virtual void OnDestroy() { }

        public ObjectId GetObjectId()
        {
#if UNITY_6000_4_OR_NEWER
            // Synthetic IDs are used only by the non-Unity code coverage cache.
            return EntityId.FromULong((ulong)m_InstanceID);
#else
            return m_InstanceID;
#endif
        }

        public override bool Equals(object other)
        {
            if ((other == null))
                return false;

            return object.ReferenceEquals(this, other);
        }

        public override int GetHashCode()
        {
            return m_InstanceID.GetHashCode();
        }

        public static bool operator ==(BaseObject t1, BaseObject t2)
        {
            if (object.ReferenceEquals(t1, null))
                return object.ReferenceEquals(t2, null);

            return object.ReferenceEquals(t1, t2);
        }

        public static bool operator !=(BaseObject t1, BaseObject t2)
        {
            return !(t1 == t2);
        }
    }
#else
    internal class BaseObject : ScriptableObject
    {
        public static void DestroyImmediate(object o)
        {
            if (o is UnityEngine.Object)
            {
                UnityEngine.Object obj = o as UnityEngine.Object;
                Undo.ClearUndo(obj);
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        public static BaseObject InstanceIDToObject(ObjectId instanceID)
        {
            return UnityEditorObjectCompatibility.FindObject(instanceID) as BaseObject;
        }

        internal virtual void OnEnable() { }
        internal virtual void OnDestroy() { }
    }
#endif
}
