using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.U2D.Animation
{
    internal static class BoneIconUtility
    {
        internal const string Default = "0000_Default";
        const string Prefix = "Uniskin.BoneIcons.v1:";
        internal static readonly string[] Names = { Default, "0001_Power", "0002_Compas", "0003_Tools", "0004_QuestionMark", "0005_PaperClip", "0006_CircleArrow", "0007_Angry", "0008_Direction", "0009_Key", "0010_Arrow", "0011_Shield", "0012_Recycle", "0013_Light", "0014_Aim", "0015_Airplane", "0016_Close", "0017_Plus", "0018_Person", "0019_Info", "0020_Note", "0021_Star", "0022_Lightning", "0023_Heart", "0024_Happy", "0025_Lock", "0026_Settings", "0027_Warning", "0028_Message" };
        internal static readonly string[] Choices = Names.SelectMany(name => name == "0008_Direction" || name == "0010_Arrow"
            ? Enumerable.Range(0, 4).Select(index => index == 0 ? name : name + "@" + index * 90) : new[] { name }).ToArray();
        static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        [Serializable] internal sealed class Entry { public string guid; public string icon; public float size; }
        internal const float DefaultSize = 24f;
        internal static float NormalizeSize(float size) => float.IsFinite(size) && size > 0 ? Mathf.Clamp(size, 4f, 128f) : DefaultSize;
        [Serializable] sealed class Document { public Entry[] bones = Array.Empty<Entry>(); }

        internal static string Normalize(string id)
        {
            if (Choices.Contains(id)) return id;
            var parts = (id ?? string.Empty).Split('@');
            if (parts.Length == 2 && (parts[0] == "0008_Direction" || parts[0] == "0010_Arrow") &&
                int.TryParse(parts[1], out var angle) && angle >= 0 && angle < 360 && angle % 45 == 0)
            {
                var cardinal = ((angle + 45) / 90 * 90) % 360;
                return cardinal == 0 ? parts[0] : parts[0] + "@" + cardinal;
            }
            return Default;
        }
        internal static float Angle(string id)
        {
            var parts = Normalize(id).Split('@');
            return parts.Length == 2 ? int.Parse(parts[1]) : 0;
        }
        internal static Texture2D Texture(string id)
        {
            var name = Normalize(id).Split('@')[0];
            if (!Textures.TryGetValue(name, out var texture) || texture == null)
                Textures[name] = texture = ResourceLoader.Load<Texture2D>("SkinningModule/BoneIcons/" + name + ".png");
            return texture;
        }
        internal static void SetImage(Image image, string id)
        {
            image.image = Texture(id);
            image.style.rotate = new Rotate(new Angle(Angle(id), AngleUnit.Degree));
        }
        internal static Entry[] Read(string userData)
        {
            var line = (userData ?? string.Empty).Split('\n').FirstOrDefault(item => item.StartsWith(Prefix, StringComparison.Ordinal));
            if (line == null) return Array.Empty<Entry>();
            try { return JsonUtility.FromJson<Document>(line.Substring(Prefix.Length))?.bones ?? Array.Empty<Entry>(); }
            catch (ArgumentException) { return Array.Empty<Entry>(); }
        }
        internal static string Find(Entry[] entries, string guid) => Normalize(entries.FirstOrDefault(entry => entry != null && entry.guid == guid)?.icon);
        internal static float FindSize(Entry[] entries, string guid) => NormalizeSize(entries.FirstOrDefault(entry => entry != null && entry.guid == guid)?.size ?? 0);
        internal static Entry[] Capture(IEnumerable<BoneCache> bones) => bones.Where(bone => !string.IsNullOrEmpty(bone.guid) && (bone.iconId != Default || bone.iconSize != DefaultSize))
            .GroupBy(bone => bone.guid).Select(group => new Entry { guid = group.Key, icon = group.First().iconId, size = group.First().iconSize }).OrderBy(entry => entry.guid).ToArray();
        internal static string Write(string userData, IEnumerable<BoneCache> bones)
        {
            var lines = (userData ?? string.Empty).Split('\n').Where(line => !line.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
            var entries = Capture(bones);
            if (entries.Length > 0) lines.Add(Prefix + JsonUtility.ToJson(new Document { bones = entries }));
            return string.Join("\n", lines);
        }
        internal static IEnumerable<BoneCache> Bones(SkinningCache cache) => cache.hasCharacter ? cache.character.skeleton.bones : cache.GetSprites().SelectMany(sprite => sprite.GetSkeleton().bones);
        internal static void Load(SkinningCache cache, ISpriteEditorDataProvider provider)
        {
            var importer = provider.targetObject as AssetImporter;
            var entries = Read(SceneMeshBindingPose.UserData(provider, importer));
            foreach (var bone in Bones(cache).Concat(cache.GetSprites().SelectMany(sprite => sprite.GetSkeleton().bones)))
            {
                bone.iconId = Find(entries, bone.guid);
                bone.iconSize = FindSize(entries, bone.guid);
            }
        }
        internal static void Save(SkinningCache cache, ISpriteEditorDataProvider provider)
        {
            if (provider is UniskinRigDataProvider rig) rig.data.userData = Write(rig.data.userData, Bones(cache));
            else if (provider.targetObject is AssetImporter importer) importer.userData = Write(importer.userData, Bones(cache));
        }
        internal static void Draw(Vector3 position, string id, Color color, bool outline = false, float iconSize = DefaultSize)
        {
            if (Event.current.type != EventType.Repaint) return;
            var texture = Texture(id);
            if (texture == null) return;
            var center = HandleUtility.WorldToGUIPoint(position);
            var size = NormalizeSize(iconSize) * SkinningModuleSettings.boneDisplayScale;
            Handles.BeginGUI();
            var matrix = GUI.matrix;
            var oldColor = GUI.color;
            try
            {
                GUI.color = color;
                GUIUtility.RotateAroundPivot(Angle(id), center);
                if (outline)
                    foreach (var offset in new[] { Vector2.left, Vector2.right, Vector2.up, Vector2.down })
                        GUI.DrawTexture(new Rect(center - Vector2.one * size * .5f + offset * 2f, Vector2.one * size), texture);
                else GUI.DrawTexture(new Rect(center - Vector2.one * size * .5f, Vector2.one * size), texture);
            }
            finally { GUI.matrix = matrix; GUI.color = oldColor; Handles.EndGUI(); }
        }
    }
}
