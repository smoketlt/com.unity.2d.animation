using UnityEngine;

namespace Fedorov.Uniskin
{
    // Library-only archive root. Never registered as a PSB subasset.
    internal sealed class UniskinPsbCachedAssets : ScriptableObject
    {
        public string key;
        public Object[] assets;
        public string[] identifiers;
        public Object main;
    }
}
