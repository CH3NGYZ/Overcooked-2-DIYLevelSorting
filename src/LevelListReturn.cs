using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LevelEditorStub;
using UnityEngine;
using UnityEngine.SceneManagement;
using DIY = OC2DIYLevel.DIYLevelAssetBundleManager;

namespace OC2DIYLevelSorting
{
    internal sealed class LevelListReturn
    {
        internal readonly LevelListReturnState State = new LevelListReturnState();
        private readonly SortingPlugin plugin;
        private bool restoring;
        private FrontendDLCMenu preparedDlcMenu;
        private static readonly MethodInfo selectSet = AccessTools.Method(typeof(OC2DIYLevel.DIYLevelEntryUI), "OnLevelSetSelected");
        private static readonly Type fastType = AccessTools.TypeByName("DIYLevelFastInit.FastInitPlugin");
        private static readonly FieldInfo fastLoading = fastType == null ? null : AccessTools.Field(fastType, "Loading");

        internal LevelListReturn(SortingPlugin plugin) { this.plugin = plugin; }

        internal void Capture(string scene)
        {
            LevelSetInfoSO set = plugin.SelectedSet;
            if (set == null || set.levelInfos == null) return;
            PackageMetadata metadata = plugin.Metadata.Get(set);
            string identity = null;
            foreach (LevelInfoSO level in set.levelInfos)
            {
                if (level == null || !string.Equals(level.sceneName, scene, StringComparison.OrdinalIgnoreCase)) continue;
                string candidate = metadata.LevelIds[level];
                if (identity == null) identity = candidate;
                if (plugin.History.Contains(candidate)) { identity = candidate; break; }
            }
            if (identity == null) return;
            FrontendOptionsMenu menu = GameAccess.LevelMenu.GetValue(null) as FrontendOptionsMenu;
            MenuView view = menu == null ? null : menu.GetComponent<MenuView>();
            State.Capture(metadata.Identity, identity, scene, view == null ? 1f : view.Scroll.verticalNormalizedPosition);
        }

        private static bool IsCustomSession()
        {
            GameSession session = GameUtils.GetGameSession();
            return session != null && session.DLC == DIY.diyLevelDLCId;
        }

        internal void SceneEntered(string scene)
        {
            bool custom = IsCustomSession();
            if (scene != "Loading" && scene != "StartScreen" && custom
                && !string.Equals(State.SceneName, scene, StringComparison.OrdinalIgnoreCase))
            {
                // Clients may enter a host-selected scene without a local click.
                // Follow the original loader's first matching package in that case.
                List<KeyValuePair<string, LevelSetInfoSO>> sets = DIY.levelSetInfos;
                if (sets != null) foreach (KeyValuePair<string, LevelSetInfoSO> item in sets)
                {
                    if (item.Value == null || item.Value.levelInfos == null) continue;
                    foreach (LevelInfoSO level in item.Value.levelInfos)
                    {
                        if (level == null || !string.Equals(level.sceneName, scene, StringComparison.OrdinalIgnoreCase)) continue;
                        PackageMetadata metadata = plugin.Metadata.Get(item.Value);
                        State.Capture(metadata.Identity, metadata.LevelIds[level], scene, 1f);
                        State.SceneEntered(scene, true);
                        return;
                    }
                }
            }
            State.SceneEntered(scene, custom);
        }

        internal void SceneLeaving(string nextScene)
        {
            State.SceneLeaving(nextScene, SceneManager.GetActiveScene().name, IsCustomSession(), plugin.ReturnToLevelList);
        }

        internal void FrontendShown(FrontendRootMenu root)
        {
            if (!State.Pending || restoring || !plugin.ReturnToLevelList || root == null) return;
            restoring = true;
            plugin.StartCoroutine(Restore(root));
        }

        private IEnumerator Restore(FrontendRootMenu root)
        {
            // Let the native Show and loading transition finish before opening children.
            yield return null;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (State.Pending && plugin.ReturnToLevelList && root != null && Time.realtimeSinceStartup < deadline)
            {
                if (TryRestore(root)) break;
                yield return new WaitForSecondsRealtime(0.1f);
            }
            if (State.Pending) plugin.Warn("Returning to the DIY level list timed out or was cancelled; keeping the original frontend.");
            State.Complete();
            preparedDlcMenu = null;
            restoring = false;
        }

        private bool TryRestore(FrontendRootMenu root)
        {
            try
            {
                if (SceneManager.GetActiveScene().name != "StartScreen" || LoadingScreenFlow.IsLoading
                    || !root.isActiveAndEnabled || root.CurrentGamepadUser == null || T17FrontendFlow.Instance == null
                    || root.CachedEventSystem == null || root.CachedEventSystem.IsDisabled()
                    || T17DialogBoxManager.HasAnyOpenDialogs() || !DIY.IsInitialized
                    || (fastLoading != null && (bool)fastLoading.GetValue(null))) return false;
                OC2DIYLevel.DIYLevelEntryUI.AddUI();
                FrontendOptionsMenu sets = GameAccess.SetMenu.GetValue(null) as FrontendOptionsMenu;
                FrontendOptionsMenu levels = GameAccess.LevelMenu.GetValue(null) as FrontendOptionsMenu;
                if (sets == null || levels == null) return false;
                FrontendDLCMenu dlcMenu = root.SearchAllForMenuOfType<FrontendDLCMenu>();
                if (dlcMenu == null) return false;
                if (preparedDlcMenu != dlcMenu)
                {
                    T17FrontendFlow.Instance.FocusOnMainMenu();
                    root.HideMenuStack();
                    root.ExpandCurrentTab();
                    // The original callback needs the card's handler and player.
                    // Let the carousel's first Start/selection finish before
                    // restoring DIY focus, so it cannot select a postcard later.
                    if (!dlcMenu.Show(root.CurrentGamepadUser, root, root.gameObject, false)) return false;
                    preparedDlcMenu = dlcMenu;
                    return false;
                }
                LevelSetInfoSO target = null;
                foreach (KeyValuePair<string, LevelSetInfoSO> item in DIY.levelSetInfos)
                    if (item.Value != null && plugin.Metadata.Get(item.Value).Identity == State.PackageIdentity) { target = item.Value; break; }
                sets.Show(root.CurrentGamepadUser, root, root.gameObject, false);
                if (target != null)
                {
                    MenuView setView = sets.GetComponent<MenuView>();
                    GameObject packageFocus = setView == null ? null : setView.RestoreFocus(State.PackageIdentity, 1f);
                    // Native child menus remember the current selection for Cancel.
                    // Apply the parent selection before opening the child: the
                    // one-argument T17 setter defers it until LateUpdate.
                    if (packageFocus != null) sets.CachedEventSystem.SetSelectedGameObject(packageFocus, null);
                    selectSet.Invoke(null, new object[] { target });
                    MenuView view = levels.GetComponent<MenuView>();
                    if (view != null) view.RestoreFocus(State.LevelIdentity, State.ScrollPosition);
                }
                else plugin.Warn("The previous DIY package was removed; returning to the package list.");
                State.Complete();
                return true;
            }
            catch (Exception e)
            {
                plugin.Report("LevelListReturn.Restore", "Coroutine", e);
                State.Complete();
                return true;
            }
        }
    }
}
