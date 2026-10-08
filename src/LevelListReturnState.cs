using System;

namespace OC2DIYLevelSorting
{
    // Session-only bookmark: persisted click history must not reopen menus at boot.
    internal sealed class LevelListReturnState
    {
        internal string PackageIdentity;
        internal string LevelIdentity;
        internal string SceneName;
        internal float ScrollPosition;
        internal bool Pending;
        private bool entered;

        internal void Capture(string package, string level, string scene, float scroll)
        {
            PackageIdentity = package;
            LevelIdentity = level;
            SceneName = scene;
            ScrollPosition = scroll;
            entered = false;
            Pending = false;
        }

        internal void SceneEntered(string scene, bool customSession)
        {
            if (scene == "Loading" || scene == "StartScreen") return;
            entered = customSession && !string.IsNullOrEmpty(PackageIdentity)
                && string.Equals(scene, SceneName, StringComparison.OrdinalIgnoreCase);
            Pending = false;
        }

        internal void SceneLeaving(string nextScene, string currentScene, bool customSession, bool enabled)
        {
            Pending = enabled && customSession && entered && nextScene == "StartScreen"
                && string.Equals(currentScene, SceneName, StringComparison.OrdinalIgnoreCase);
        }

        internal void Complete() { Pending = false; entered = false; }
    }
}
