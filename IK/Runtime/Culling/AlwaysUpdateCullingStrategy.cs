#if UNITY_6000_4_OR_NEWER
using ObjectId = UnityEngine.EntityId;
#else
using ObjectId = System.Int32;
#endif

using System.Collections.Generic;

namespace UnityEngine.U2D.IK
{
    internal class AlwaysUpdateCullingStrategy : BaseCullingStrategy
    {
        public override bool AreBonesVisible(IList<ObjectId> transformIds) => true;
    }
}
