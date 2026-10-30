using UnityEditor;
using UnityEditor.SceneManagement;

namespace SkeletonPose.EditorTools
{
    [InitializeOnLoad]
    public static class PoseStageLoader
    {
        const string ScenePath = "Assets/Scenes/PoseStage.unity";

        static PoseStageLoader()
        {
            EditorApplication.delayCall += OpenOnce;
        }

        static void OpenOnce()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                return;
            if (SessionState.GetBool("SkeletonPose.Opened", false))
                return;
            SessionState.SetBool("SkeletonPose.Opened", true);

            var scene = EditorSceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(scene.path) || scene.isDirty)
                return;
            if (System.IO.File.Exists(ScenePath))
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Skeleton Pose/Open Pose Stage")]
        public static void Open()
        {
            if (System.IO.File.Exists(ScenePath))
                EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
