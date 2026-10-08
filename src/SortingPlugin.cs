using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using LevelEditorStub;
using UnityEngine.SceneManagement;

namespace OC2DIYLevelSorting
{
    [BepInPlugin(PluginGuid, "DIYLevel Sorting", "1.2.0")]
    [BepInDependency("dev.gua.overcooked.diylevel")]
    [BepInDependency("oc2.diylevel.fastinit", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInProcess("Overcooked2.exe")]
    public sealed class SortingPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.ch3ngyz.plugin.diylevel.sorting";
        internal static SortingPlugin Instance;
        internal readonly MetadataResolver Metadata = new MetadataResolver();
        internal ClickHistory History;
        internal PinOrder Pins;
        internal LevelSetInfoSO SelectedSet;
        internal LevelListReturn Return;
        private ConfigEntry<SortMethod> method;
        private ConfigEntry<SortDirection> direction;
        private ConfigEntry<string> history;
        private ConfigEntry<string> pinnedEntries;
        private ConfigEntry<bool> returnToLevelList;
        private readonly Dictionary<FrontendOptionsMenu, MenuView> views = new Dictionary<FrontendOptionsMenu, MenuView>();
        private Harmony harmony;
        private bool configReloaded;

        internal SortMethod Method { get { return method.Value; } }
        internal SortDirection Direction { get { return direction.Value; } }
        internal bool ReturnToLevelList { get { return returnToLevelList.Value; } }

        private void Awake()
        {
            Instance = this;
            try
            {
                Config.SaveOnConfigSet = false;
                method = Config.Bind<SortMethod>("Sorting", "Method", SortMethod.Name, Language.MethodDescription);
                direction = Config.Bind<SortDirection>("Sorting", "Direction", SortDirection.Ascending, Language.DirectionDescription);
                history = Config.Bind<string>("History", "ClickedLevels", string.Empty, Language.HistoryDescription);
                pinnedEntries = Config.Bind<string>("Pinning", "PinnedEntries", string.Empty, Language.PinDescription);
                returnToLevelList = Config.Bind<bool>("Navigation", "ReturnToLevelList", true, Language.ReturnDescription);
                NormalizeSettings();
                History = new ClickHistory(history.Value);
                Pins = new PinOrder(pinnedEntries.Value);
                Return = new LevelListReturn(this);
                Config.ConfigReloaded += OnConfigReloaded;
                harmony = new Harmony(PluginGuid);
                Patches.Install(harmony);
                SceneManager.sceneLoaded += OnSceneLoaded;
                Logger.LogInfo("Sorting enabled; metadata uses info*/scene file LastWriteTimeUtc. DIYLevel=" + typeof(OC2DIYLevel.DIYLevelEntryUI).Assembly.GetName().Version);
            }
            catch (Exception e)
            {
                if (harmony != null) harmony.UnpatchSelf();
                Logger.LogError("SortingPlugin.Awake initialization failed: " + e);
                enabled = false;
            }
        }

        private void NormalizeSettings()
        {
            bool changed = false;
            if (!Enum.IsDefined(typeof(SortMethod), method.Value)) { method.Value = SortMethod.Name; changed = true; }
            if (!Enum.IsDefined(typeof(SortDirection), direction.Value)) { direction.Value = SortDirection.Ascending; changed = true; }
            if (changed) Config.Save();
        }

        private void OnConfigReloaded(object sender, EventArgs e) { configReloaded = true; }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            try { Return.SceneEntered(scene.name); }
            catch (Exception e) { Report("SceneManager.sceneLoaded", "Event", e); }
        }

        private void LateUpdate()
        {
            try
            {
                if (configReloaded)
                {
                    configReloaded = false;
                    NormalizeSettings();
                    History = new ClickHistory(history.Value);
                    Pins = new PinOrder(pinnedEntries.Value);
                    foreach (MenuView view in views.Values) if (view != null) view.RefreshLabels();
                }
                // Two dirty gates only: idle frames never traverse entries or UI hierarchies.
                foreach (MenuView view in views.Values) if (view != null) view.Tick();
            }
            catch (Exception e) { Report("SortingPlugin.LateUpdate", "Update", e); }
        }

        internal MenuView GetView(FrontendOptionsMenu menu)
        {
            if (menu == null) return null;
            MenuView view;
            if (views.TryGetValue(menu, out view) && view != null) return view;
            view = menu.gameObject.AddComponent<MenuView>();
            view.Initialize(menu);
            views[menu] = view;
            return view;
        }

        internal void Forget(FrontendOptionsMenu menu) { views.Remove(menu); }

        internal void AttachMenus()
        {
            Attach(GameAccess.SetMenu);
            Attach(GameAccess.LevelMenu);
        }

        private void Attach(FieldInfo field)
        {
            MenuView view = GetView(field.GetValue(null) as FrontendOptionsMenu);
            if (view != null) view.Flush();
        }

        internal void SetSort(int selected, bool isDirection)
        {
            if (isDirection)
            {
                if ((int)direction.Value == selected) return;
                direction.Value = (SortDirection)selected;
            }
            else
            {
                if ((int)method.Value == selected) return;
                method.Value = (SortMethod)selected;
            }
            Config.Save();
            foreach (MenuView view in views.Values) if (view != null) { view.RefreshLabels(); view.Flush(); }
        }

        internal void TogglePin(string id)
        {
            if (!Pins.Toggle(id)) return;
            pinnedEntries.Value = Pins.Save();
            Config.Save();
            foreach (MenuView view in views.Values) if (view != null) { view.RefreshLabels(); view.Flush(); }
        }

        internal void Record(string id)
        {
            if (!History.Add(id)) return;
            history.Value = History.Save();
            Config.Save();
            // Badges intentionally update at the next refresh/show, as requested.
        }

        internal void Warn(string text) { Logger.LogWarning(text); }
        internal void Report(string target, string patchType, Exception error)
        {
            Logger.LogError(target + " [" + patchType + "] " + error);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Config.ConfigReloaded -= OnConfigReloaded;
            if (harmony != null) harmony.UnpatchSelf();
            Instance = null;
        }
    }
}
