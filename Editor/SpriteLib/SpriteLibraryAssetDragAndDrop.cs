#if UNITY_6000_4_OR_NEWER
using ObjectId = UnityEngine.EntityId;
#else
using ObjectId = System.Int32;
#endif

using UnityEngine;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    [InitializeOnLoad]
    internal static class SpriteLibraryAssetDragAndDrop
    {
        const string k_UndoableCreate = "Create new Sprite Library Object";
        const string k_UndoableAdd = "Add Sprite Library";

        static SpriteLibraryAssetDragAndDrop()
        {
#if UNITY_6000_4_OR_NEWER
            DragAndDrop.AddDropHandlerV2(HandleDropInspector);
            DragAndDrop.AddDropHandlerV2(HandleDropHierarchy);
            DragAndDrop.AddDropHandlerV2(HandleDropScene);
#else
            DragAndDrop.AddDropHandler(HandleDropInspector);
            DragAndDrop.AddDropHandler(HandleDropHierarchy);
            DragAndDrop.AddDropHandler(HandleDropScene);
#endif
        }

        static DragAndDropVisualMode HandleDropInspector(Object[] targets, bool perform)
        {
            return HandleDropInspectorInternal(DragAndDrop.objectReferences, targets, perform);
        }

        static DragAndDropVisualMode HandleDropHierarchy(ObjectId dropTargetInstanceID, HierarchyDropFlags dropMode, Transform parentForDraggedObjects, bool perform)
        {
            return HandleDropHierarchyInternal(DragAndDrop.objectReferences, dropTargetInstanceID, dropMode, perform);
        }

        static DragAndDropVisualMode HandleDropScene(Object dropUpon, Vector3 worldPosition, Vector2 viewportPosition, Transform parentForDraggedObjects, bool perform)
        {
            return HandleDropSceneInternal(DragAndDrop.objectReferences, dropUpon, worldPosition, perform);
        }

        internal static DragAndDropVisualMode HandleDropInspectorInternal(Object[] draggedObjects, Object[] targets, bool perform)
        {
            SpriteLibraryAsset spriteLibraryAsset = GetSpriteLibraryAsset(draggedObjects);
            if (spriteLibraryAsset == null)
                return DragAndDropVisualMode.None;

            DragAndDrop.AcceptDrag();
            if (perform)
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    if (targets[i] is GameObject targetGo)
                        AddSpriteLibraryToObject(targetGo, spriteLibraryAsset);
                }
            }

            return DragAndDropVisualMode.Copy;
        }

        internal static DragAndDropVisualMode HandleDropHierarchyInternal(Object[] draggedObjects, ObjectId dropTargetInstanceID, HierarchyDropFlags dropMode, bool perform)
        {
            SpriteLibraryAsset spriteLibraryAsset = GetSpriteLibraryAsset(draggedObjects);
            if (spriteLibraryAsset == null)
                return DragAndDropVisualMode.None;

            Object dropUpon = UnityEditorObjectCompatibility.FindObject(dropTargetInstanceID);
            if (dropUpon == null || dropMode == HierarchyDropFlags.DropBetween)
            {
                DragAndDrop.AcceptDrag();
                if (perform)
                    CreateSpriteLibraryObject(spriteLibraryAsset, Vector3.zero);

                return DragAndDropVisualMode.Copy;
            }

            if (dropUpon is GameObject targetGo)
            {
                DragAndDrop.AcceptDrag();
                if (perform)
                    AddSpriteLibraryToObject(targetGo, spriteLibraryAsset);

                return DragAndDropVisualMode.Link;
            }

            return DragAndDropVisualMode.None;
        }

        internal static DragAndDropVisualMode HandleDropSceneInternal(Object[] draggedObjects, Object dropUpon, Vector3 worldPosition, bool perform)
        {
            SpriteLibraryAsset spriteLibraryAsset = GetSpriteLibraryAsset(draggedObjects);
            if (spriteLibraryAsset == null)
                return DragAndDropVisualMode.None;

            DragAndDrop.AcceptDrag();

            if (dropUpon is GameObject targetGo)
            {
                if (perform)
                    AddSpriteLibraryToObject(targetGo, spriteLibraryAsset);

                return DragAndDropVisualMode.Link;
            }

            if (perform)
                CreateSpriteLibraryObject(spriteLibraryAsset, worldPosition);

            return DragAndDropVisualMode.Copy;
        }

        internal static SpriteLibraryAsset GetSpriteLibraryAsset(Object[] objectReferences)
        {
            for (int i = 0; i < objectReferences.Length; i++)
            {
                Object draggedObject = objectReferences[i];
                if (draggedObject is SpriteLibraryAsset spriteLibraryAsset)
                    return spriteLibraryAsset;
            }

            return null;
        }


        internal static void AddSpriteLibraryToObject(GameObject targetGo, SpriteLibraryAsset spriteLibraryAsset)
        {
            Undo.RegisterFullObjectHierarchyUndo(targetGo, k_UndoableAdd);
            SpriteLibrary spriteLibraryComponent = targetGo.GetComponent<SpriteLibrary>();
            if (spriteLibraryComponent == null)
                spriteLibraryComponent = targetGo.AddComponent<SpriteLibrary>();
            spriteLibraryComponent.spriteLibraryAsset = spriteLibraryAsset;

            Selection.objects = new Object[] { targetGo };
        }

        internal static void CreateSpriteLibraryObject(SpriteLibraryAsset spriteLibraryAsset, Vector3 position)
        {
            GameObject newSpriteLibraryGameObject = new GameObject(spriteLibraryAsset.name);
            Transform transform = newSpriteLibraryGameObject.transform;
            transform.position = position;
            SpriteLibrary spriteLibraryComponent = newSpriteLibraryGameObject.AddComponent<SpriteLibrary>();
            spriteLibraryComponent.spriteLibraryAsset = spriteLibraryAsset;
            Undo.RegisterCreatedObjectUndo(newSpriteLibraryGameObject, k_UndoableCreate);

            Selection.objects = new Object[] { newSpriteLibraryGameObject };
        }
    }
}
