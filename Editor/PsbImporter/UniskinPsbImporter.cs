using UnityEditor.AssetImporters;
using UnityEditor.U2D.PSD;

namespace Fedorov.Uniskin
{
    // Override candidate only. Unity's PSDImporter remains the default for every PSB.
    // Inheriting its serialized fields and provider implementations preserves the
    // native Sprite/Skinning Editor data contract in both directions.
    [ScriptedImporter(1, new string[0], new[] { "psb" }, AllowCaching = true)]
    public sealed class UniskinPsbImporter : PSDImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            if (UniskinPsbImportCache.TryImport(this, context)) return;
            base.OnImportAsset(context);
            UniskinPsbImportCache.Store(this, context);
        }
    }
}
