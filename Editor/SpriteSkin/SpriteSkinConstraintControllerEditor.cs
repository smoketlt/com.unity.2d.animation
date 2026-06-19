using UnityEngine;
using UnityEngine.U2D.Animation;

namespace UnityEditor.U2D.Animation
{
    [CustomEditor(typeof(SpriteSkinConstraintController))]
    [CanEditMultipleObjects]
    [InitializeOnLoad]
    internal class SpriteSkinConstraintControllerEditor : Editor
    {
        static SpriteSkinConstraintControllerEditor()
        {
            Undo.undoRedoPerformed += ApplyConstraintsAfterUndoRedo;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (GUILayout.Button("Update"))
            {
                foreach (Object targetObject in targets)
                {
                    SpriteSkinConstraintController controller = targetObject as SpriteSkinConstraintController;
                    if (controller == null)
                        continue;

                    controller.UpdateConstraints();
                    EditorUtility.SetDirty(controller);
                }
            }
        }

        static void ApplyConstraintsAfterUndoRedo()
        {
            SpriteSkinConstraintController[] controllers = Resources.FindObjectsOfTypeAll<SpriteSkinConstraintController>();
            foreach (SpriteSkinConstraintController controller in controllers)
            {
                if (controller == null || !controller.isActiveAndEnabled || !controller.gameObject.scene.IsValid())
                    continue;

                controller.ApplyConstraints();
                EditorUtility.SetDirty(controller);
            }

            SceneView.RepaintAll();
        }
    }
}
