using System;
using System.Reflection;
using HarmonyLib;
using LevelEditorStub;
using UnityEngine;
using UnityEngine.UI;

namespace OC2DIYLevelSorting
{
    internal static class Patches
    {
        internal static void Install(Harmony harmony)
        {
            Patch(harmony, typeof(OC2DIYLevel.DIYLevelEntryUI), "AddUI", null, "MenusAdded");
            Patch(harmony, typeof(OC2DIYLevel.DIYLevelEntryUI), "AddLevelSetButton", null, "SetAdded");
            Patch(harmony, typeof(OC2DIYLevel.DIYLevelEntryUI), "AddLevelButton", null, "LevelAdded");
            Patch(harmony, typeof(OC2DIYLevel.DIYLevelEntryUI), "OnLevelSetSelected", "SelectSet", "SetSelected");
            Patch(harmony, typeof(OC2DIYLevel.UIUtils), "ClearAllMenuContent", "ClearPrefix", "ClearPostfix");
            Patch(harmony, typeof(OC2DIYLevel.UIUtils), "AddButton", null, "ButtonAdded");
            Patch(harmony, typeof(FrontendOptionsMenu), "Show", "ShowPrefix", "ShowPostfix");
            Patch(harmony, typeof(FrontendOptionsMenu), "Hide", "HidePrefix", null);
            Patch(harmony, typeof(T17ScrollView), "OnElementSelected", "ElementSelected", null);
            Patch(harmony, typeof(BaseMenuBehaviour), "InvokeNavigateOnUICancel", "CancelPrefix", null);
            Patch(harmony, typeof(Button), "Press", "ButtonPress", null);
            Patch(harmony, typeof(OC2DIYLevel.DIYLevelEntryUI), "OnLevelSelected", "LevelSelected", null);
            Patch(harmony, typeof(LoadingScreenFlow), "LoadScene", "SceneLeaving", null);
            Patch(harmony, typeof(GameUtils), "LoadScene", "SceneLeaving", null);
            Patch(harmony, typeof(FrontendRootMenu), "Show", null, "FrontendShown");
        }

        private static void Patch(Harmony harmony, Type type, string method, string prefix, string postfix)
        {
            MethodInfo target = AccessTools.Method(type, method);
            if (target == null) throw new MissingMethodException(type.FullName, method);
            harmony.Patch(target, Handler(prefix), Handler(postfix));
        }

        private static HarmonyMethod Handler(string name)
        {
            if (name == null) return null;
            HarmonyMethod result = new HarmonyMethod(AccessTools.Method(typeof(Patches), name));
            result.priority = Priority.Last;
            result.after = new string[] { "oc2.diylevel.fastinit" };
            return result;
        }

        private static void Error(string target, string kind, Exception e)
        {
            if (SortingPlugin.Instance != null) SortingPlugin.Instance.Report(target, kind, e);
        }

        private static void MenusAdded()
        {
            try { SortingPlugin.Instance.AttachMenus(); }
            catch (Exception e) { Error("DIYLevelEntryUI.AddUI", "Postfix", e); }
        }

        private static void LevelSelected(string __0)
        {
            try { SortingPlugin.Instance.Return.Capture(__0); }
            catch (Exception e) { Error("DIYLevelEntryUI.OnLevelSelected", "Prefix", e); }
        }

        private static void SceneLeaving(string __0)
        {
            try { SortingPlugin.Instance.Return.SceneLeaving(__0); }
            catch (Exception e) { Error("LoadingScreenFlow/GameUtils.LoadScene", "Prefix", e); }
        }

        private static void FrontendShown(FrontendRootMenu __instance)
        {
            try { SortingPlugin.Instance.Return.FrontendShown(__instance); }
            catch (Exception e) { Error("FrontendRootMenu.Show", "Postfix", e); }
        }

        private static void SetAdded(LevelSetInfoSO __0, T17Button __result)
        {
            try
            {
                if (__result == null) return;
                MenuView view = SortingPlugin.Instance.GetView(GameAccess.SetMenu.GetValue(null) as FrontendOptionsMenu);
                if (view != null) view.Add(__result, __0, null);
            }
            catch (Exception e) { Error("DIYLevelEntryUI.AddLevelSetButton", "Postfix", e); }
        }

        private static void LevelAdded(LevelInfoSO __0, T17Button __result)
        {
            try
            {
                if (__result == null || SortingPlugin.Instance.SelectedSet == null) return;
                MenuView view = SortingPlugin.Instance.GetView(GameAccess.LevelMenu.GetValue(null) as FrontendOptionsMenu);
                if (view != null) view.Add(__result, SortingPlugin.Instance.SelectedSet, __0);
            }
            catch (Exception e) { Error("DIYLevelEntryUI.AddLevelButton", "Postfix", e); }
        }

        private static void SelectSet(LevelSetInfoSO __0)
        {
            try
            {
                SortingPlugin.Instance.SelectedSet = __0;
                SortingPlugin.Instance.Metadata.InvalidateLevelTimes(__0);
            }
            catch (Exception e) { Error("DIYLevelEntryUI.OnLevelSetSelected", "Prefix", e); }
        }

        private static void SetSelected()
        {
            try { SortingPlugin.Instance.AttachMenus(); }
            catch (Exception e) { Error("DIYLevelEntryUI.OnLevelSetSelected", "Postfix", e); }
        }

        private static void ClearPrefix(FrontendOptionsMenu __0)
        {
            try
            {
                MenuView view = __0 == null ? null : __0.GetComponent<MenuView>();
                if (view != null)
                {
                    view.BeforeClear();
                    if (__0 == GameAccess.SetMenu.GetValue(null) as FrontendOptionsMenu) SortingPlugin.Instance.Metadata.Invalidate();
                    else SortingPlugin.Instance.Metadata.InvalidateLevelTimes(SortingPlugin.Instance.SelectedSet);
                }
            }
            catch (Exception e) { Error("UIUtils.ClearAllMenuContent", "Prefix", e); }
        }

        private static void ClearPostfix(FrontendOptionsMenu __0)
        {
            try
            {
                MenuView view = __0 == null ? null : __0.GetComponent<MenuView>();
                if (view != null) view.AfterClear();
            }
            catch (Exception e) { Error("UIUtils.ClearAllMenuContent", "Postfix", e); }
        }

        private static void ButtonAdded(FrontendOptionsMenu __0, string __1)
        {
            try
            {
                if (__1 != "FastInit_Reload" || __0 == null) return;
                MenuView view = __0.GetComponent<MenuView>();
                if (view != null) view.Request();
            }
            catch (Exception e) { Error("UIUtils.AddButton", "Postfix", e); }
        }

        private static void ShowPrefix(FrontendOptionsMenu __instance)
        {
            try
            {
                MenuView view = __instance.GetComponent<MenuView>();
                if (view != null) { view.RefreshLabels(); view.Flush(); }
            }
            catch (Exception e) { Error("FrontendOptionsMenu.Show", "Prefix", e); }
        }

        private static void ShowPostfix(FrontendOptionsMenu __instance)
        {
            try
            {
                MenuView view = __instance.GetComponent<MenuView>();
                if (view != null) view.SynchronizeNavigation();
            }
            catch (Exception e) { Error("FrontendOptionsMenu.Show", "Postfix", e); }
        }

        private static void HidePrefix(FrontendOptionsMenu __instance)
        {
            try
            {
                MenuView view = __instance.GetComponent<MenuView>();
                if (view != null) view.CloseDropdowns(false);
            }
            catch (Exception e) { Error("FrontendOptionsMenu.Hide", "Prefix", e); }
        }

        private static bool ElementSelected(T17ScrollView __instance, ref Selectable __0, ref int __1)
        {
            try
            {
                RowPinControl pin = __0 == null ? null : __0.GetComponent<RowPinControl>();
                if (pin != null && pin.Owner != null) __0 = pin.Owner;
                MenuView view = __instance.GetComponentInParent<MenuView>();
                if (view != null && __0 != null) return view.AcceptSelection(__0, ref __1);
            }
            catch (Exception e) { Error("T17ScrollView.OnElementSelected", "Prefix", e); }
            return true;
        }

        private static bool CancelPrefix(BaseMenuBehaviour __instance, ref bool __result)
        {
            try
            {
                MenuView view = __instance.GetComponentInParent<MenuView>();
                if (view != null && view.CloseDropdowns(true)) { __result = true; return false; }
            }
            catch (Exception e) { Error("BaseMenuBehaviour.InvokeNavigateOnUICancel", "Prefix", e); }
            return true;
        }

        private static void ButtonPress(Button __instance)
        {
            try
            {
                // Record before dispatch so a failing original listener does not erase a click.
                if (!__instance.IsActive() || !__instance.IsInteractable()) return;
                ClickTracker tracker = __instance.GetComponent<ClickTracker>();
                if (tracker != null) SortingPlugin.Instance.Record(tracker.Identity);
            }
            catch (Exception e) { Error("UnityEngine.UI.Button.Press", "Prefix", e); }
        }
    }
}
