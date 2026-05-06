using UnityEngine;
using UnityEngine.SceneManagement;

namespace MastersGame.Gameplay
{
    public static class DayNightCycleDiagnostics
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LogLoadedDayNightCycles()
        {
            var activeScene = SceneManager.GetActiveScene();
            var cycles = Object.FindObjectsByType<DayNightCycle>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            Debug.Log($"[DayNightCycleDiagnostics] Active scene: {activeScene.name}, loaded cycles: {cycles.Length}");

            foreach (var cycle in cycles)
            {
                var gameObject = cycle.gameObject;
                Debug.Log($"[DayNightCycleDiagnostics] Found cycle on '{GetHierarchyPath(gameObject)}'. activeInHierarchy: {gameObject.activeInHierarchy}, component enabled: {cycle.enabled}");
            }
        }

        private static string GetHierarchyPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return "<null>";
            }

            var path = gameObject.name;
            var parent = gameObject.transform.parent;
            while (parent != null)
            {
                path = $"{parent.name}/{path}";
                parent = parent.parent;
            }

            return path;
        }
    }
}
