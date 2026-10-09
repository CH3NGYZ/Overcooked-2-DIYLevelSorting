using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using LevelEditorStub;
using OC2DIYLevel;
using OC2DIYLevelSorting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Security.Cryptography;
using DIYUI = OC2DIYLevel.UIUtils;

// Test-only plugin, gated by the launcher environment; never included in the release DLL.
// Original OnLevelSelected is intercepted at its final entry to observe scene arguments
// without starting a save dialog/kitchen or changing player saves.
[BepInPlugin("oc2.diylevel.sorting.unitytests", "DIYLevel Sorting Unity Tests", "1.0.0")]
[BepInDependency(SortingPlugin.PluginGuid)]
public sealed class UnityTests : BaseUnityPlugin
{
    private string output;
    private int assertions;
    private static string selectedScene;
    private Harmony harmony;
    private static bool forceEnglish;
    private bool kitchenReturn;
    private bool roundEnd;
    private static bool allowSaveDialog;
    private static string saveRoot;
    private static readonly Dictionary<string, string> saveHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> sandboxSaves = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static readonly FieldInfo mouseDelta = AccessTools.Field(typeof(T17StandaloneInputModule), "m_CurrentMouseDelta");
    private static void DisablePhysicalMouse(T17EventSystem system)
    {
        T17StandaloneInputModule input = system == null ? null : system.GetComponent<T17StandaloneInputModule>();
        if (input == null) return;
        input.allowMouseInput = false;
        input.forceModuleActive = true;
        // The original Process calls UpdateMouseKeyboardFocus even with mouse
        // input off. A previous nonzero delta would otherwise keep deselecting UI.
        mouseDelta.SetValue(input, 0f);
    }
    private static void FrontendMouseDisabled(FrontendRootMenu __instance) { DisablePhysicalMouse(__instance.CachedEventSystem); }
    private static bool ObserveScene(string __0)
    {
        selectedScene = __0;
        return allowSaveDialog;
    }
    private static void ShortenResults(FlowroutineData __0)
    {
        AccessTools.Field(typeof(ScoreScreenFlowroutineData), "m_fTimeout").SetValue(__0, 1f);
    }
    private static bool OverrideLanguage(ref SupportedLanguages __result)
    {
        if (!forceEnglish) return true;
        __result = SupportedLanguages.English;
        return false;
    }

    private void Start()
    {
        output = Environment.GetEnvironmentVariable("OC2_SORTING_TEST_OUTPUT");
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-oc2SortingTestOutput") output = args[i + 1];
        kitchenReturn = Array.IndexOf(args, "-oc2SortingTestKitchenReturn") >= 0;
        roundEnd = Array.IndexOf(args, "-oc2SortingTestRoundEnd") >= 0;
        if (string.IsNullOrEmpty(output)) { enabled = false; return; }
        harmony = new Harmony("oc2.diylevel.sorting.unitytests.observe");
        saveRoot = Path.Combine(output, "sandbox-saves");
        Directory.CreateDirectory(saveRoot);
        HarmonyMethod isolateSaves = new HarmonyMethod(AccessTools.Method(typeof(UnityTests), "RedirectSaveAddress"));
        isolateSaves.priority = Priority.Last;
        harmony.Patch(AccessTools.Method(typeof(PCSaveManager), "GetFileAddress"), null, isolateSaves);
        harmony.Patch(AccessTools.Method(typeof(DIYLevelSaveManager), "GetFileAddress"), null, isolateSaves);
        harmony.Patch(AccessTools.Method(typeof(FrontendRootMenu), "Show"), null,
            new HarmonyMethod(AccessTools.Method(typeof(UnityTests), "FrontendMouseDisabled")));
        if (roundEnd) harmony.Patch(AccessTools.Method(typeof(ScoreScreenOutroFlowroutine), "Setup"), null,
            new HarmonyMethod(AccessTools.Method(typeof(UnityTests), "ShortenResults")));
        StartCoroutine(RunProtected());
    }

    private void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        assertions++;
        Logger.LogInfo("PASS: " + message);
    }

    private IEnumerator RunProtected()
    {
        IEnumerator work = Run();
        Exception failure = null;
        while (true)
        {
            bool next = false;
            try { next = work.MoveNext(); }
            catch (Exception e) { failure = e; }
            if (failure != null || !next) break;
            yield return work.Current;
        }
        string result = failure == null ? "PASS: " + assertions + " actual Unity assertions." : failure.ToString();
        File.WriteAllText(Path.Combine(output, "result.txt"), result);
        Logger.LogInfo(result);
        if (failure != null)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "failure.png"));
        }
        yield return new WaitForSecondsRealtime(2);
        Application.Quit();
    }

    private IEnumerator Run()
    {
        float deadline = Time.realtimeSinceStartup + 100;
        GameObject rootObject = null;
        bool startedEngagement = false;
        while (Time.realtimeSinceStartup < deadline)
        {
            AcknowledgeUpdateCheckError();
            StartScreenFlow start = StartScreenFlow.Instance;
            PlayerManager player = GameUtils.RequestManager<PlayerManager>();
            if (!startedEngagement && start != null && player != null && !player.HasPlayer()
                && !LoadingScreenFlow.IsLoading && SteamPlayerManager.Initialized
                && (bool)AccessTools.Field(typeof(StartScreenFlow), "m_bCheckingForEngagement").GetValue(null))
            {
                // Use the native keyboard engagement/profile flow; saves already use the sandbox.
                startedEngagement = true;
                AccessTools.Field(typeof(StartScreenFlow), "m_bCheckingForEngagement").SetValue(null, false);
                player.StartGameownerEngagement(ControlPadInput.PadNum.One, null, delegate(GamepadUser gamer)
                {
                    AccessTools.Method(typeof(StartScreenFlow), "OnEngagementFinished").Invoke(start, new object[] { gamer });
                });
            }
            rootObject = GameObject.Find("/Frontend/FrontendParent/FrontendRootMenu");
            if (rootObject != null && rootObject.transform.Find("FixedAspectRootCanvas/ScreenSpaceCanvas/GameOptions") != null) break;
            yield return null;
        }
        Check(rootObject != null, "Frontend scene and original GameOptions template exist");
        Check(!SortingPlugin.Instance.Return.State.Pending, "Normal game startup does not reopen a persisted click-history menu");
        if (!DIYLevelAssetBundleManager.IsInitialized) DIYLevelAssetBundleManager.Initialize();
        Type fast = AccessTools.TypeByName("DIYLevelFastInit.FastInitPlugin");
        FieldInfo loading = fast == null ? null : AccessTools.Field(fast, "Loading");
        if (roundEnd)
        {
            while (loading != null && (bool)loading.GetValue(null) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSecondsRealtime(2f);
            AcknowledgeUpdateCheckError();
            DIYLevelEntryUI.AddUI();
            GamepadUser roundUser = GameUtils.RequireManager<PlayerManager>().GetUser(EngagementSlot.One);
            Check(roundUser != null, "Round-end test has a real engaged player");
            FrontendRootMenu roundRoot = rootObject.GetComponent<FrontendRootMenu>();
            T17EventSystem roundSystem = T17EventSystemsManager.Instance.GetEventSystemForGamepadUser(roundUser);
            while ((roundSystem.IsDisabled() || T17DialogBoxManager.HasAnyOpenDialogs()) && Time.realtimeSinceStartup < deadline)
            {
                AcknowledgeUpdateCheckError();
                yield return null;
            }
            AccessTools.Field(typeof(BaseMenuBehaviour), "m_CurrentGamepadUser").SetValue(roundRoot, roundUser);
            SortingPlugin.Instance.Config.Bind<bool>("Navigation", "ReturnToLevelList", true, "test").Value = true;
            harmony.Patch(AccessTools.Method(typeof(DIYLevelEntryUI), "OnLevelSelected"),
                new HarmonyMethod(AccessTools.Method(typeof(UnityTests), "ObserveScene")));
            LevelSetInfoSO roundSet = DIYLevelAssetBundleManager.levelSetInfos.Find(delegate(KeyValuePair<string, LevelSetInfoSO> item)
            {
                return string.Equals(new DirectoryInfo(item.Key).Name, "littleHa", StringComparison.OrdinalIgnoreCase);
            }).Value ?? DIYLevelAssetBundleManager.levelSetInfos[0].Value;
            IEnumerator roundWork = VerifyRoundEnd(roundSet);
            while (roundWork.MoveNext()) yield return roundWork.Current;
            yield break;
        }
        int observedLoaded = -1;
        bool switchedWhileLoading = false;
        int stableLoadingFrames = 0;
        int visibleLoadingFrames = 0;
        int stableLoadingAppends = 0;
        SortMethod loadingMethod = SortingPlugin.Instance.Method;
        SortDirection loadingDirection = SortingPlugin.Instance.Direction;
        SortingDropdown stableMethodControl = null;
        SortingDropdown stableDirectionControl = null;
        while (Time.realtimeSinceStartup < deadline && loading != null && (bool)loading.GetValue(null))
        {
            if (DIYLevelAssetBundleManager.IsInitialized)
            {
                if (GameAccess.SetMenu.GetValue(null) as FrontendOptionsMenu == null) DIYLevelEntryUI.AddUI();
                int count = DIYLevelAssetBundleManager.levelSetInfos.Count;
                if (count != observedLoaded)
                {
                    observedLoaded = count;
                    if (!switchedWhileLoading)
                    {
                        FrontendOptionsMenu loadingMenu = (FrontendOptionsMenu)GameAccess.SetMenu.GetValue(null);
                        GamepadUser loadingUser = GameUtils.RequireManager<PlayerManager>().GetUser(EngagementSlot.One);
                        Check(loadingUser != null, "Primary user exists while FastInit is still loading");
                        FrontendRootMenu loadingRoot = rootObject.GetComponent<FrontendRootMenu>();
                        AccessTools.Field(typeof(BaseMenuBehaviour), "m_CurrentGamepadUser").SetValue(loadingRoot, loadingUser);
                        loadingMenu.Show(loadingUser, loadingRoot, rootObject, false);
                        DisablePhysicalMouse(loadingMenu.CachedEventSystem);
                        // Exercise a real change even if the saved config already
                        // contains AddedTime/Descending from a previous manual run.
                        SortingPlugin.Instance.SetSort(1 - (int)loadingMethod, false);
                        SortingPlugin.Instance.SetSort(1 - (int)loadingDirection, true);
                        loadingMethod = SortingPlugin.Instance.Method;
                        loadingDirection = SortingPlugin.Instance.Direction;
                        MenuView loadingView = ((FrontendOptionsMenu)GameAccess.SetMenu.GetValue(null)).GetComponent<MenuView>();
                        stableMethodControl = loadingView.Toolbar.GetChild(0).GetComponent<SortingDropdown>();
                        stableDirectionControl = loadingView.Toolbar.GetChild(1).GetComponent<SortingDropdown>();
                        switchedWhileLoading = true;
                    }
                    else stableLoadingAppends++;
                }
                if (switchedWhileLoading)
                {
                    MenuView loadingView = ((FrontendOptionsMenu)GameAccess.SetMenu.GetValue(null)).GetComponent<MenuView>();
                    AssertStableControls(loadingView, stableMethodControl, stableDirectionControl, loadingMethod, loadingDirection);
                    stableLoadingFrames++;
                    if (loadingView.Menu.isActiveAndEnabled) visibleLoadingFrames++;
                }
            }
            yield return null;
        }
        if (fast != null)
        {
            // The last append and Loading=false can happen in the same frame.
            // Observe the completion frame too, rather than losing that append
            // when a small installation finishes between coroutine resumes.
            if (switchedWhileLoading)
            {
                MenuView completedView = ((FrontendOptionsMenu)GameAccess.SetMenu.GetValue(null)).GetComponent<MenuView>();
                AssertStableControls(completedView, stableMethodControl, stableDirectionControl, loadingMethod, loadingDirection);
                stableLoadingAppends += DIYLevelAssetBundleManager.levelSetInfos.Count - observedLoaded;
                stableLoadingFrames++;
                if (completedView.Menu.isActiveAndEnabled) visibleLoadingFrames++;
            }
            Check(switchedWhileLoading, "Sorting changed once during the real FastInit loading coroutine");
            Check(stableLoadingFrames > 0 && stableLoadingAppends > 0, "Both dropdown identities, captions and saved selections stay stable across " + stableLoadingFrames + " frames and " + stableLoadingAppends + " further appends");
            Check(visibleLoadingFrames > 0, "Sorting toolbar stays stable on the visible menu during " + visibleLoadingFrames + " loading frames");
            ((FrontendOptionsMenu)GameAccess.SetMenu.GetValue(null)).Hide(false, false);
        }
        Check(DIYLevelAssetBundleManager.IsInitialized, "DIYLevel initialized through the installed manager");
        Logger.LogInfo("FastInit=" + (fast != null));
        // Let the native frontend finish its initial activation before retaining menu objects.
        yield return new WaitForSecondsRealtime(2);
        rootObject = GameObject.Find("/Frontend/FrontendParent/FrontendRootMenu");
        DIYLevelEntryUI.AddUI();
        FrontendOptionsMenu sets = (FrontendOptionsMenu)GameAccess.SetMenu.GetValue(null);
        FrontendOptionsMenu levels = (FrontendOptionsMenu)GameAccess.LevelMenu.GetValue(null);
        Check(sets != null && levels != null, "Both real DIYLevel menus exist");
        MenuView setView = sets.GetComponent<MenuView>();
        Check(setView != null && levels.GetComponent<MenuView>() != null, "Both menus attach sorting views");
        Check(sets.GetComponentsInChildren<SortingDropdown>(true).Length == 2, "Two dropdown controls in package menu");
        Check(levels.GetComponentsInChildren<SortingDropdown>(true).Length == 2, "Two dropdown controls in level menu");
        Check(setView.Toolbar.parent == sets.transform.Find("SettingsBody") && !setView.Toolbar.IsChildOf(setView.Content), "Sorting toolbar is outside the scrolling content");
        Check(setView.Toolbar.childCount == 2, "Left toolbar holds exactly two fixed controls");
        SortingDropdown originalMethod = setView.Toolbar.GetChild(0).GetComponent<SortingDropdown>();
        DIYLevelEntryUI.AddUI();
        Check(sets.GetComponentsInChildren<SortingDropdown>(true).Length == 2, "Repeated AddUI has no duplicate dropdowns");
        List<KeyValuePair<string, LevelSetInfoSO>> business = DIYLevelAssetBundleManager.levelSetInfos;
        List<KeyValuePair<string, LevelSetInfoSO>> snapshot = new List<KeyValuePair<string, LevelSetInfoSO>>(business);
        Check(snapshot.Count > 0, "Installed metadata packages are available");
        foreach (KeyValuePair<string, LevelSetInfoSO> pair in snapshot)
        {
            FileInfo[] info = new DirectoryInfo(pair.Key).GetFiles("info*");
            Check(SortingPlugin.Instance.Metadata.Get(pair.Value).AddedTicks == info[0].LastWriteTimeUtc.Ticks,
                "Added time equals actual info* LastWriteTimeUtc: " + new DirectoryInfo(pair.Key).Name);
            Check(SortingPlugin.Instance.Metadata.Get(pair.Value).LevelTimes.Count == 0,
                "Package listing does not pre-read level file times: " + new DirectoryInfo(pair.Key).Name);
        }
        for (int method = 0; method < 2; method++) for (int direction = 0; direction < 2; direction++)
        {
            SortingPlugin.Instance.SetSort(method, false);
            SortingPlugin.Instance.SetSort(direction, true);
            CheckOrder(setView, snapshot);
            CheckBusinessUnchanged(business, snapshot);
        }
        FrontendRootMenu root = rootObject.GetComponent<FrontendRootMenu>();
        GamepadUser user = GameUtils.RequireManager<PlayerManager>().GetUser(EngagementSlot.One);
        Check(user != null, "Real primary GamepadUser exists for native Show/focus tests");
        T17EventSystem startupSystem = T17EventSystemsManager.Instance.GetEventSystemForGamepadUser(user);
        deadline = Time.realtimeSinceStartup + 30f;
        while (startupSystem != null && (startupSystem.IsDisabled() || T17DialogBoxManager.HasAnyOpenDialogs()) && Time.realtimeSinceStartup < deadline)
        {
            AcknowledgeUpdateCheckError();
            yield return null;
        }
        Check(startupSystem != null && !startupSystem.IsDisabled() && !T17DialogBoxManager.HasAnyOpenDialogs(),
            "Native frontend transition releases its input suppression before scripted menu navigation");
        AccessTools.Field(typeof(BaseMenuBehaviour), "m_CurrentGamepadUser").SetValue(root, user);
        sets.Show(user, root, rootObject, false);
        yield return new WaitForSecondsRealtime(0.6f);
        Check(sets != null && setView != null && setView.Content != null, "Native menu and view survive activation");
        CheckNavigation(setView);
        Vector3 toolbarPosition = setView.Toolbar.position;
        setView.Scroll.verticalNormalizedPosition = 0;
        yield return null;
        Check(setView.Toolbar.position == toolbarPosition, "Left controls stay fixed while the list scrolls");
        setView.Scroll.verticalNormalizedPosition = 1;
        SortingDropdown dropdown = setView.Toolbar.Find("DIYSorting_Method").GetComponent<SortingDropdown>();
        T17EventSystem eventSystem = sets.CachedEventSystem;
        // Scripted native events must not race the user's physical mouse on the desktop.
        // Selection still runs through the real T17EventSystem and original UI handlers.
        DisablePhysicalMouse(eventSystem);
        yield return new WaitForSecondsRealtime(0.1f);
        CheckLeftToolbar(setView);
        GameObject firstRow = setView.Content.Find("LevelSet_" + snapshot[0].Value.levelSetName).gameObject;
        eventSystem.SetSelectedGameObject(firstRow);
        yield return new WaitForSecondsRealtime(0.1f);
        AxisEventData right = new AxisEventData(eventSystem);
        right.moveDir = MoveDirection.Right;
        ExecuteEvents.Execute(firstRow, right, ExecuteEvents.moveHandler);
        yield return new WaitForSecondsRealtime(0.1f);
        GameObject pinFocus = firstRow.transform.Find("DIYSorting_Pin").gameObject;
        Check(eventSystem.currentSelectedGameObject == pinFocus, "Native right navigation reaches the row pin button");
        AxisEventData left = new AxisEventData(eventSystem);
        left.moveDir = MoveDirection.Left;
        ExecuteEvents.Execute(pinFocus, left, ExecuteEvents.moveHandler);
        yield return new WaitForSecondsRealtime(0.1f);
        Check(eventSystem.currentSelectedGameObject == firstRow, "Native left navigation returns to the selected list row");
        ExecuteEvents.Execute(firstRow, left, ExecuteEvents.moveHandler);
        yield return new WaitForSecondsRealtime(0.1f);
        Check(eventSystem.currentSelectedGameObject == dropdown.gameObject, "Native left navigation reaches the left sorting toolbar");
        ExecuteEvents.Execute(dropdown.gameObject, right, ExecuteEvents.moveHandler);
        yield return new WaitForSecondsRealtime(0.1f);
        Check(eventSystem.currentSelectedGameObject == firstRow, "Native right navigation returns from the left toolbar to the selected row");
        GameObject directionFocus = setView.Toolbar.Find("DIYSorting_Direction").gameObject;
        eventSystem.SetSelectedGameObject(directionFocus);
        yield return new WaitForSecondsRealtime(0.1f);
        ExecuteEvents.Execute(directionFocus, right, ExecuteEvents.moveHandler);
        yield return new WaitForSecondsRealtime(0.1f);
        Check(eventSystem.currentSelectedGameObject == firstRow, "Direction control also returns right to the selected row");
        T17Button trigger = dropdown.GetComponent<T17Button>();
        Logger.LogInfo("Dropdown diagnostics: active=" + trigger.gameObject.activeInHierarchy + ", enabled=" + trigger.enabled + ", interactable=" + trigger.interactable + ", IsInteractable=" + trigger.IsInteractable() + ", menu=" + sets.gameObject.activeInHierarchy);
        foreach (Component component in trigger.GetComponents<Component>()) Logger.LogInfo("Trigger component: " + component.GetType().FullName);
        ExecuteEvents.Execute(dropdown.gameObject, new BaseEventData(sets.CachedEventSystem), ExecuteEvents.submitHandler);
        yield return null;
        Transform popup = sets.transform.Find("SettingsBody/DIYSorting_Method_Options");
        Logger.LogInfo("Popup diagnostics: active=" + popup.gameObject.activeSelf + ", hierarchy=" + popup.gameObject.activeInHierarchy + ", children=" + popup.childCount);
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "before-dropdown-check.png"));
        yield return new WaitForSecondsRealtime(0.2f);
        Check(popup.gameObject.activeSelf && popup.childCount == 2, "Submit opens a real two-option dropdown list");
        CheckRaycast(eventSystem, popup.GetChild(0).GetComponent<T17Button>(), "Popup option is reachable through the real UI raycaster");
        foreach (Transform option in popup)
        {
            RectTransform textRect = option.Find("Title") as RectTransform;
            Vector3[] textCorners = new Vector3[4];
            textRect.GetWorldCorners(textCorners);
            RectTransform optionRect = option as RectTransform;
            foreach (Vector3 corner in textCorners)
            {
                Vector2 local = optionRect.InverseTransformPoint(corner);
                Check(local.x >= optionRect.rect.xMin - 0.1f && local.x <= optionRect.rect.xMax + 0.1f, "Option label stays inside its narrow dropdown row");
            }
        }
        Check(sets.CachedEventSystem.currentSelectedGameObject != null && sets.CachedEventSystem.currentSelectedGameObject.transform.parent == popup, "Native focus enters dropdown choices");
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "dropdown.png"));
        yield return new WaitForSecondsRealtime(0.3f);
        sets.InvokeNavigateOnUICancel();
        yield return null;
        Check(!popup.gameObject.activeSelf && sets.gameObject.activeSelf, "Cancel closes dropdown and retains menu");
        Check(sets.CachedEventSystem.currentSelectedGameObject == dropdown.gameObject, "Cancel restores trigger focus");
        dropdown.Toggle();
        T17Button choice = popup.GetChild(1).GetComponent<T17Button>();
        ExecuteEvents.Execute(choice.gameObject, new BaseEventData(sets.CachedEventSystem), ExecuteEvents.submitHandler);
        yield return null;
        Check(SortingPlugin.Instance.Method == SortMethod.AddedTime && !popup.gameObject.activeSelf, "Dropdown selection persists and closes");
        SortingDropdown directionDropdown = setView.Toolbar.Find("DIYSorting_Direction").GetComponent<SortingDropdown>();
        directionDropdown.Toggle();
        Transform directionPopup = sets.transform.Find("SettingsBody/DIYSorting_Direction_Options");
        PointerEventData click = new PointerEventData(eventSystem);
        click.button = PointerEventData.InputButton.Left;
        ExecuteEvents.Execute(directionPopup.GetChild(0).gameObject, click, ExecuteEvents.pointerClickHandler);
        yield return null;
        Check(SortingPlugin.Instance.Direction == SortDirection.Ascending && !directionPopup.gameObject.activeSelf, "Mouse click selects direction in the fixed dropdown");

        // Follow the original package callback into the real level menu.
        KeyValuePair<string, LevelSetInfoSO> chosenPair = snapshot.Find(delegate(KeyValuePair<string, LevelSetInfoSO> pair)
        {
            return string.Equals(new DirectoryInfo(pair.Key).Name, "littleHa", StringComparison.OrdinalIgnoreCase);
        });
        LevelSetInfoSO chosen = chosenPair.Value ?? snapshot[0].Value;
        if (chosenPair.Value != null) Check(chosen.levelInfos.Length == 8, "littleHa exposes the eight installed levels reported by the player");
        AccessTools.Method(typeof(DIYLevelEntryUI), "OnLevelSetSelected").Invoke(null, new object[] { chosen });
        MenuView levelView = levels.GetComponent<MenuView>();
        IEnumerator transition = WaitForMenuToSettle(levelView);
        while (transition.MoveNext()) yield return transition.Current;
        Check(levelView.Content.childCount == chosen.levelInfos.Length, "Original package selection contains only its own levels");
        CheckNavigation(levelView);
        CheckLeftToolbar(levelView);
        HashSet<long> chosenTimes = new HashSet<long>();
        foreach (LevelInfoSO level in chosen.levelInfos)
        {
            long expectedTime = ExpectedLevelTime(chosen, level);
            chosenTimes.Add(expectedTime);
            Check(SortingPlugin.Instance.Metadata.GetLevelAddedTicks(chosen, level) == expectedTime,
                "Level time comes from its own scene file: " + level.sceneName);
        }
        if (chosenPair.Value != null) Check(chosenTimes.Count == 8, "littleHa's eight distinct resource modification times remain distinct");
        string[] ascendingRows = null;
        for (int method = 0; method < 2; method++) for (int direction = 0; direction < 2; direction++)
        {
            SortingPlugin.Instance.SetSort(method, false);
            SortingPlugin.Instance.SetSort(direction, true);
            CheckLevelOrder(levelView, chosen);
            if (method == (int)SortMethod.AddedTime && chosenTimes.Count == chosen.levelInfos.Length)
            {
                if (direction == (int)SortDirection.Ascending)
                {
                    ascendingRows = new string[levelView.Content.childCount];
                    for (int i = 0; i < ascendingRows.Length; i++) ascendingRows[i] = levelView.Content.GetChild(i).name;
                }
                else for (int i = 0; i < ascendingRows.Length; i++)
                    Check(levelView.Content.GetChild(i).name == ascendingRows[ascendingRows.Length - i - 1],
                        "Reversing added-time direction reverses the real level row " + i);
                List<string> ordered = new List<string>();
                foreach (Transform row in levelView.Content) ordered.Add(row.name);
                File.AppendAllText(Path.Combine(output, "level-added-time-order.txt"), chosen.levelSetName + "/" + (SortDirection)direction + ": " + string.Join(", ", ordered.ToArray()) + Environment.NewLine);
                ScreenCapture.CaptureScreenshot(Path.Combine(output, "level-time-" + (SortDirection)direction + ".png"));
                yield return new WaitForSecondsRealtime(0.2f);
            }
        }
        harmony.Patch(AccessTools.Method(typeof(DIYLevelEntryUI), "OnLevelSelected"),
            new HarmonyMethod(AccessTools.Method(typeof(UnityTests), "ObserveScene")));
        foreach (LevelInfoSO level in chosen.levelInfos)
        {
            selectedScene = null;
            levelView.Content.Find("Level_" + level.levelName).GetComponent<T17Button>().OnSubmit(new BaseEventData(levels.CachedEventSystem));
            Check(selectedScene == level.sceneName, "Each time-sorted row retains its original scene callback: " + level.sceneName);
        }
        LevelInfoSO clicked = chosen.levelInfos[0];
        T17Button levelButton = levelView.Content.Find("Level_" + clicked.levelName).GetComponent<T17Button>();
        bool otherListener = false;
        levelButton.onClick.AddListener(delegate { otherListener = true; });
        selectedScene = null;
        ExecuteEvents.Execute(levelButton.gameObject, new BaseEventData(levels.CachedEventSystem), ExecuteEvents.submitHandler);
        Check(selectedScene == clicked.sceneName, "Sorted original level callback passes the unchanged scene name");
        Check(otherListener, "An additional mod listener remains intact");
        string clickedId = SortingPlugin.Instance.Metadata.Get(chosen).LevelIds[clicked];
        Check(SortingPlugin.Instance.History.Contains(clickedId), "Original level click records its stable identity");
        ConfigFile reloaded = new ConfigFile(SortingPlugin.Instance.Config.ConfigFilePath, false);
        Check(new ClickHistory(reloaded.Bind<string>("History", "ClickedLevels", "", "test").Value).Contains(clickedId), "Click history persisted on disk");
        AccessTools.Method(typeof(DIYLevelEntryUI), "OnLevelSetSelected").Invoke(null, new object[] { chosen });
        yield return null;
        levelButton = levelView.Content.Find("Level_" + clicked.levelName).GetComponent<T17Button>();
        Check(levelButton.transform.Find("DIYSorting_Clicked").gameObject.activeSelf, "Rebuilt level row restores its circle badge");
        setView.Flush();
        CheckSingleBadge(levelView, levelButton.transform, "Latest real level after rebuild");
        CheckSingleBadge(setView, setView.Content.Find("LevelSet_" + chosen.levelSetName), "Latest real package after refresh");
        Check(levels.GetComponentsInChildren<SortingDropdown>(true).Length == 2, "Level clear/rebuild restores exactly two dropdowns");

        IEnumerator pinChecks = VerifyPins(levelView, LevelPinRows(levelView, chosen));
        while (pinChecks.MoveNext()) yield return pinChecks.Current;
        levels.Hide(false, false);
        sets.Show(user, root, rootObject, false);
        yield return new WaitForSecondsRealtime(0.2f);
        // Append synthetic packages through the exact installed private button entry.
        LevelSetInfoSO duplicateA = Fixture("Same Name", "shared_scene", "fixture-a");
        LevelSetInfoSO duplicateB = Fixture("Same Name", "shared_scene", "fixture-b");
        LevelInfoSO second = ScriptableObject.CreateInstance<LevelInfoSO>();
        second.levelName = second.levelNameZH = "Other Level";
        second.sceneName = "OTHER_SCENE";
        duplicateB.levelInfos = new LevelInfoSO[] { duplicateB.levelInfos[0], second };
        string timeFixtureDirectory = Path.Combine(output, "time-fixture");
        Directory.CreateDirectory(timeFixtureDirectory);
        string fixtureInfo = Path.Combine(timeFixtureDirectory, "info_fixture");
        string fixtureScene = Path.Combine(timeFixtureDirectory, "shared_scene");
        string fixtureOtherScene = Path.Combine(timeFixtureDirectory, "other_scene");
        File.WriteAllText(fixtureInfo, "metadata timestamp fixture; no AssetBundle load");
        File.WriteAllText(fixtureScene, "scene timestamp fixture; no AssetBundle load");
        File.WriteAllText(fixtureOtherScene, "scene timestamp fixture; no AssetBundle load");
        DateTime oldTime = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime newTime = oldTime.AddDays(20);
        File.SetLastWriteTimeUtc(fixtureInfo, oldTime.AddDays(1));
        File.SetLastWriteTimeUtc(fixtureScene, oldTime);
        File.SetLastWriteTimeUtc(fixtureOtherScene, oldTime.AddDays(10));
        business.Add(new KeyValuePair<string, LevelSetInfoSO>(snapshot[0].Key, duplicateA));
        business.Add(new KeyValuePair<string, LevelSetInfoSO>(timeFixtureDirectory, duplicateB));
        MethodInfo addSet = AccessTools.Method(typeof(DIYLevelEntryUI), "AddLevelSetButton");
        T17Button aButton = (T17Button)addSet.Invoke(null, new object[] { duplicateA });
        T17Button bButton = (T17Button)addSet.Invoke(null, new object[] { duplicateB });
        SortingPlugin.Instance.SetSort(0, false);
        yield return new WaitForSecondsRealtime(0.25f);
        Check(aButton.transform.parent == setView.Content && bButton.transform.parent == setView.Content, "Incremental entry path keeps both same-name buttons");
        string aId = SortingPlugin.Instance.Metadata.Get(duplicateA).LevelIds[duplicateA.levelInfos[0]];
        string bId = SortingPlugin.Instance.Metadata.Get(duplicateB).LevelIds[duplicateB.levelInfos[0]];
        Check(aId != bId && !SortingPlugin.Instance.History.Contains(bId), "Same name/scene in different packages never shares history");
        sets.Show(user, root, rootObject, false);
        CheckNavigation(setView);
        List<KeyValuePair<T17Button, string>> packagePins = new List<KeyValuePair<T17Button, string>>();
        foreach (KeyValuePair<string, LevelSetInfoSO> pair in snapshot)
            packagePins.Add(new KeyValuePair<T17Button, string>(setView.Content.Find("LevelSet_" + pair.Value.levelSetName).GetComponent<T17Button>(), SortingPlugin.Instance.Metadata.Get(pair.Value).Identity));
        packagePins.Add(new KeyValuePair<T17Button, string>(bButton, SortingPlugin.Instance.Metadata.Get(duplicateB).Identity));
        pinChecks = VerifyPins(setView, packagePins);
        while (pinChecks.MoveNext()) yield return pinChecks.Current;
        // Pin checks rebuild content; retrieve both duplicate-name rows by their
        // restored stable pin identity rather than an ambiguous transform name.
        List<Row> rebuiltRows = AccessTools.Field(typeof(MenuView), "rows").GetValue(setView) as List<Row>;
        foreach (Row row in rebuiltRows)
        {
            if (row.Set == duplicateA) aButton = row.Button;
            if (row.Set == duplicateB) bButton = row.Button;
        }
        // Delete an entry; observer schedules a refresh without rebuilding the others.
        UnityEngine.Object.DestroyImmediate(aButton.gameObject);
        business.RemoveAll(delegate(KeyValuePair<string, LevelSetInfoSO> item) { return item.Value == duplicateA; });
        yield return new WaitForSecondsRealtime(0.25f);
        Check(!GameAccess.Items(setView.Scroll).Exists(delegate(RectTransform item) { return item == null; }), "Deleted row removed from navigation cache");
        Check(bButton != null, "Incremental deletion preserves surviving button objects");
        // Failure in the original onClick event cannot prevent the requested click history.
        AccessTools.Method(typeof(DIYLevelEntryUI), "OnLevelSetSelected").Invoke(null, new object[] { duplicateB });
        yield return null;
        Check(SortingPlugin.Instance.Metadata.GetLevelAddedTicks(duplicateB, duplicateB.levelInfos[0]) == oldTime.Ticks,
            "A same-named scene reads the selected package's own file");
        Check(SortingPlugin.Instance.Metadata.GetLevelAddedTicks(duplicateB, second) == oldTime.AddDays(10).Ticks,
            "Mixed-case scene names resolve the installed loader's lowercase resource file");
        File.SetLastWriteTimeUtc(fixtureScene, newTime);
        Check(SortingPlugin.Instance.Metadata.GetLevelAddedTicks(duplicateB, duplicateB.levelInfos[0]) == oldTime.Ticks,
            "File times are cached within a level menu, not reread on every sort");
        AccessTools.Method(typeof(DIYLevelEntryUI), "OnLevelSetSelected").Invoke(null, new object[] { duplicateB });
        Check(SortingPlugin.Instance.Metadata.GetLevelAddedTicks(duplicateB, duplicateB.levelInfos[0]) == newTime.Ticks,
            "Reopening a level menu refreshes a changed scene file timestamp");
        SortingPlugin.Instance.SetSort((int)SortMethod.AddedTime, false);
        SortingPlugin.Instance.SetSort((int)SortDirection.Ascending, true);
        CheckLevelOrder(levelView, duplicateB);
        Check(levelView.Content.GetChild(0).name == "Level_Other Level", "Rebuilt rows sort by the refreshed file times");
        File.Delete(fixtureOtherScene);
        AccessTools.Method(typeof(DIYLevelEntryUI), "OnLevelSetSelected").Invoke(null, new object[] { duplicateB });
        Check(SortingPlugin.Instance.Metadata.GetLevelAddedTicks(duplicateB, second) == 0,
            "Missing scene files use minimum time rather than the package time");
        CheckLevelOrder(levelView, duplicateB);
        File.WriteAllText(fixtureOtherScene, "restored fixture");
        File.SetLastWriteTimeUtc(fixtureOtherScene, newTime);
        AccessTools.Method(typeof(DIYLevelEntryUI), "OnLevelSetSelected").Invoke(null, new object[] { duplicateB });
        string tiedFirst = levelView.Content.GetChild(0).name;
        SortingPlugin.Instance.SetSort((int)SortDirection.Descending, true);
        Check(levelView.Content.GetChild(0).name == tiedFirst, "Equal file times keep stable identity order across directions");
        CheckLevelOrder(levelView, duplicateB);
        T17Button failing = levels.GetComponent<MenuView>().Content.Find("Level_Same Name").GetComponent<T17Button>();
        // This fixture deliberately removes the appended history listener: only the
        // production Press prefix can record a click before the first handler throws.
        failing.onClick = new Button.ButtonClickedEvent();
        failing.onClick.AddListener(delegate { throw new InvalidOperationException("Expected test listener failure"); });
        try { failing.OnSubmit(new BaseEventData(levels.CachedEventSystem)); }
        catch (InvalidOperationException) { }
        Check(SortingPlugin.Instance.History.Contains(bId), "A failing original listener still leaves a recorded click");
        Check(!SortingPlugin.Instance.History.Contains(clickedId) && !SortingPlugin.Instance.History.HasPackage(SortingPlugin.Instance.Metadata.Get(chosen).Identity),
            "Clicking a different package removes the previous level and package from recent state");
        levelView.Flush();
        setView.Flush();
        CheckSingleBadge(levelView, failing.transform, "Only the latest level remains after switching packages");
        CheckSingleBadge(setView, bButton.transform, "Only the latest package remains after switching packages");
        T17Button secondButton = levelView.Content.Find("Level_" + second.levelName).GetComponent<T17Button>();
        secondButton.OnSubmit(new BaseEventData(levels.CachedEventSystem));
        string secondId = SortingPlugin.Instance.Metadata.Get(duplicateB).LevelIds[second];
        Check(SortingPlugin.Instance.History.Contains(secondId) && !SortingPlugin.Instance.History.Contains(bId), "Clicking another level in the same package replaces its green dot");
        levelView.Flush();
        setView.Flush();
        CheckSingleBadge(levelView, secondButton.transform, "Same-package level change has one green dot");
        CheckSingleBadge(setView, bButton.transform, "Same-package level change keeps one package dot");
        ConfigFile recentReloaded = new ConfigFile(SortingPlugin.Instance.Config.ConfigFilePath, false);
        Check(recentReloaded.Bind<string>("History", "ClickedLevels", "", "test").Value == secondId, "Disk persistence contains only the latest clicked level");
        SortingPlugin.Instance.Config.Reload();
        yield return null;
        Check(SortingPlugin.Instance.History.Contains(secondId) && !SortingPlugin.Instance.History.Contains(bId), "Actual plugin config reload restores only the latest level");
        CheckSingleBadge(levelView, secondButton.transform, "Config reload keeps one level marker");
        CheckSingleBadge(setView, bButton.transform, "Config reload keeps one package marker");
        try { failing.OnSubmit(new BaseEventData(levels.CachedEventSystem)); }
        catch (InvalidOperationException) { }
        Check(SortingPlugin.Instance.History.Contains(bId) && !SortingPlugin.Instance.History.Contains(secondId), "Revisiting a previously clicked level replaces the more recent marker");
        levelView.Flush();
        CheckSingleBadge(levelView, failing.transform, "Revisited level has the unique green dot");
        LevelSetInfoSO updated = Fixture("Updated Name", "shared_scene", "fixture-b-v2");
        GameAccess.BaseUid.SetValue(updated, "fixture-b");
        int updateIndex = business.FindIndex(delegate(KeyValuePair<string, LevelSetInfoSO> item) { return item.Value == duplicateB; });
        File.SetLastWriteTimeUtc(fixtureInfo, newTime);
        business[updateIndex] = new KeyValuePair<string, LevelSetInfoSO>(timeFixtureDirectory, updated);
        if (fast != null)
        {
            AccessTools.Field(fast, "_builtCount").SetValue(null, -1);
            AccessTools.Method(fast, "HealLevelSetButtons").Invoke(null, null);
        }
        else
        {
            DIYUI.ClearAllMenuContent(sets);
            foreach (KeyValuePair<string, LevelSetInfoSO> pair in business) addSet.Invoke(null, new object[] { pair.Value });
        }
        setView.Flush();
        Check(SortingPlugin.Instance.Metadata.Get(updated).LevelIds[updated.levelInfos[0]] == bId,
            "Metadata replacement at the same business index preserves baseUID click identity");
        Check(SortingPlugin.Instance.Metadata.Get(updated).AddedTicks == newTime.Ticks,
            "Package refresh rereads the updated info* file modification time");
        AccessTools.Method(typeof(DIYLevelEntryUI), "OnLevelSetSelected").Invoke(null, new object[] { updated });
        yield return null;
        Check(levels.GetComponent<MenuView>().Content.childCount == 1, "Single-level replacement remains a single visible row");
        Check(levels.GetComponent<MenuView>().Content.GetChild(0).Find("DIYSorting_Clicked").gameObject.activeSelf,
            "Updated package restores the clicked circle without using the changed display name");
        CheckSingleBadge(levels.GetComponent<MenuView>(), levels.GetComponent<MenuView>().Content.GetChild(0), "Updated level preserves one green dot");
        CheckSingleBadge(setView, setView.Content.Find("LevelSet_" + updated.levelSetName), "Updated package preserves one green dot");
        business.RemoveAll(delegate(KeyValuePair<string, LevelSetInfoSO> item) { return item.Value == updated; });
        business.RemoveAll(delegate(KeyValuePair<string, LevelSetInfoSO> item) { return item.Value == duplicateB; });

        // Exercise FastInit's actual repair, reload-button state and hot-sync entry when present.
        if (fast != null)
        {
            levels.Hide(false, false);
            sets.Show(user, root, rootObject, false);
            yield return null;
            AccessTools.Method(fast, "HealLevelSetButtons").Invoke(null, null);
            setView.Flush();
            Transform reload = setView.Content.Find("FastInit_Reload");
            Check(reload != null && reload.GetSiblingIndex() == 0, "Real FastInit reload button remains first");
            bool savedLoading = (bool)loading.GetValue(null);
            loading.SetValue(null, true);
            AccessTools.Method(fast, "EnsureReloadButton").Invoke(null, new object[] { sets });
            setView.Flush();
            Check(!reload.GetComponent<T17Button>().interactable, "Sorting preserves FastInit Loading disable state");
            loading.SetValue(null, savedLoading);
            AccessTools.Method(fast, "EnsureReloadButton").Invoke(null, new object[] { sets });
            SortMethod reloadMethod = SortingPlugin.Instance.Method;
            SortDirection reloadDirection = SortingPlugin.Instance.Direction;
            SortingDropdown reloadDirectionControl = setView.Toolbar.GetChild(1).GetComponent<SortingDropdown>();
            AccessTools.Method(fast, "TrySyncReload").Invoke(null, null);
            int reloadFrames = 0;
            float reloadObserveUntil = Time.realtimeSinceStartup + 1;
            while (((bool)loading.GetValue(null) || Time.realtimeSinceStartup < reloadObserveUntil) && Time.realtimeSinceStartup < deadline)
            {
                AssertStableControls(setView, originalMethod, reloadDirectionControl, reloadMethod, reloadDirection);
                reloadFrames++;
                yield return null;
            }
            setView.Flush();
            Check(sets.GetComponentsInChildren<SortingDropdown>(true).Length == 2, "Actual FastInit hot-sync retains controls");
            Check(reloadFrames > 0, "Both visible dropdowns retain their objects, captions and selections during " + reloadFrames + " hot-sync frames");
        }
        else Check(setView.Content.Find("FastInit_Reload") == null, "Sorting operates when FastInit is absent");
        DIYUI.ClearAllMenuContent(sets);
        Check(setView.Content.childCount == 0 && setView.Toolbar.childCount == 2, "Empty package list retains two fixed controls outside content");
        Check(setView.Toolbar.GetChild(0).GetComponent<SortingDropdown>() == originalMethod, "ClearAllMenuContent preserves the original fixed control object");
        levels.Hide(false, false);
        // Native Hide can restore the direction control selected before entering a
        // package. Preserve valid toolbar focus, then separately test stale row focus.
        sets.CachedEventSystem.SetSelectedGameObject(directionDropdown.gameObject);
        yield return null;
        sets.Show(user, root, rootObject, false);
        yield return new WaitForSecondsRealtime(0.2f);
        Check(sets.CachedEventSystem.currentSelectedGameObject == directionDropdown.gameObject, "Empty list preserves existing direction-control focus");
        sets.CachedEventSystem.SetSelectedGameObject(levels.GetComponent<MenuView>().Content.GetChild(0).gameObject);
        sets.Show(user, root, rootObject, false);
        yield return new WaitForSecondsRealtime(0.2f);
        Check(sets.CachedEventSystem.currentSelectedGameObject == originalMethod.gameObject, "Empty list can focus the fixed sorting controls");
        foreach (KeyValuePair<string, LevelSetInfoSO> pair in snapshot) addSet.Invoke(null, new object[] { pair.Value });
        setView.Flush();
        Check(sets.GetComponentsInChildren<SortingDropdown>(true).Length == 2, "Full clear/rebuild preserves exactly two controls");
        Check(!Array.Exists(setView.Content.GetComponentsInChildren<Image>(true), delegate(Image badge) { return badge.name == "DIYSorting_Clicked" && badge.gameObject.activeSelf; }),
            "Removing the latest package does not revive markers on older packages");
        CheckBusinessUnchanged(business, snapshot);
        ConfigFile persisted = new ConfigFile(SortingPlugin.Instance.Config.ConfigFilePath, false);
        Check(persisted.Bind<SortMethod>("Sorting", "Method", SortMethod.AddedTime, "test").Value == SortingPlugin.Instance.Method,
            "Current sorting setting survives config reload");
        harmony.Patch(AccessTools.Method(typeof(Localization), "GetLanguage"), new HarmonyMethod(AccessTools.Method(typeof(UnityTests), "OverrideLanguage")));
        forceEnglish = true;
        SortingPlugin.Instance.AttachMenus();
        setView.RefreshLabels();
        Check(setView.Toolbar.Find("DIYSorting_Method/Title").GetComponent<T17Text>().text.Contains("Sort by"), "English dropdown caption comes from the language entry");
        forceEnglish = false;
        setView.RefreshLabels();
        levels.Hide(false, false);
        sets.Hide(false, false);
        UnityEngine.Object.DestroyImmediate(levels.gameObject);
        UnityEngine.Object.DestroyImmediate(sets.gameObject);
        // Reconstruct actual menu objects from the original native template.
        DIYLevelEntryUI.AddUI();
        sets = (FrontendOptionsMenu)GameAccess.SetMenu.GetValue(null);
        levels = (FrontendOptionsMenu)GameAccess.LevelMenu.GetValue(null);
        setView = sets.GetComponent<MenuView>();
        Check(sets.GetComponentsInChildren<SortingDropdown>(true).Length == 2 && levels.GetComponentsInChildren<SortingDropdown>(true).Length == 2,
            "Destroyed and reconstructed frontend menus restore exactly two fixed dropdowns each");
        sets.Show(user, root, rootObject, false);
        yield return new WaitForSecondsRealtime(0.5f);
        ScreenCapture.CaptureScreenshot(Path.Combine(output, "menus.png"));
        yield return new WaitForSecondsRealtime(0.3f);
        Check(!SortingPlugin.Instance.Return.State.Pending, "Selecting levels without actually loading a kitchen does not arm return navigation");
        levels.Hide(false, false);
        sets.Hide(false, false);
        SortingPlugin.Instance.Config.Bind<bool>("Navigation", "ReturnToLevelList", true, "test").Value = true;
        Check(SortingPlugin.Instance.ReturnToLevelList, "Removed-package regression explicitly enables automatic return");
        LevelListReturnState returnState = SortingPlugin.Instance.Return.State;
        returnState.Capture("removed-package", "removed-level", "test-kitchen", 1f);
        returnState.SceneEntered("test-kitchen", true);
        returnState.SceneLeaving("StartScreen", "test-kitchen", true, true);
        SortingPlugin.Instance.Return.FrontendShown(root);
        deadline = Time.realtimeSinceStartup + 10f;
        while (returnState.Pending && Time.realtimeSinceStartup < deadline) yield return null;
        Check(!returnState.Pending && sets.gameObject.activeInHierarchy && !levels.gameObject.activeInHierarchy,
            "A removed package falls back to the real package list and consumes the pending return (simulated return request)");
        sets.Hide(false, false);
        if (kitchenReturn)
        {
            IEnumerator work = VerifyKitchenReturn(chosen);
            while (work.MoveNext()) yield return work.Current;
        }
        if (roundEnd)
        {
            IEnumerator work = VerifyRoundEnd(chosen);
            while (work.MoveNext()) yield return work.Current;
        }
    }

    private static string FileHash(string path)
    {
        if (!File.Exists(path)) return null;
        using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
    }

    private static List<KeyValuePair<T17Button, string>> LevelPinRows(MenuView view, LevelSetInfoSO set)
    {
        List<KeyValuePair<T17Button, string>> result = new List<KeyValuePair<T17Button, string>>();
        foreach (LevelInfoSO level in set.levelInfos)
            result.Add(new KeyValuePair<T17Button, string>(view.Content.Find("Level_" + level.levelName).GetComponent<T17Button>(), SortingPlugin.Instance.Metadata.Get(set).LevelIds[level]));
        return result;
    }

    private IEnumerator VerifyPins(MenuView view, List<KeyValuePair<T17Button, string>> items)
    {
        Check(items.Count >= 2, "Pin regression has two real business rows");
        string history = SortingPlugin.Instance.History.Save();
        string scene = selectedScene;
        foreach (KeyValuePair<T17Button, string> item in items)
        {
            Transform pin = item.Key.transform.Find("DIYSorting_Pin");
            Check(pin != null && item.Key.GetComponentsInChildren<RowPinControl>(true).Length == 1, "Each real row owns exactly one pin control: " + item.Key.name);
            Check(pin.parent == item.Key.transform && pin.localScale == Vector3.one, "Pin inherits its row's menu scaling: " + item.Key.name);
            Check(pin.GetComponent<RowPinControl>().Owner == item.Key, "Pin selection maps to the original scroll row");
            if (SortingPlugin.Instance.Pins.IndexOf(item.Value) >= 0) SortingPlugin.Instance.TogglePin(item.Value);
        }
        T17Button first = items[items.Count - 1].Key;
        T17Button second = items[0].Key;
        string firstName = first.name;
        string secondName = second.name;
        string firstId = items[items.Count - 1].Value;
        string secondId = items[0].Value;
        T17Button firstPin = first.transform.Find("DIYSorting_Pin").GetComponent<T17Button>();
        T17Button secondPin = second.transform.Find("DIYSorting_Pin").GetComponent<T17Button>();
        view.Menu.CachedEventSystem.SetSelectedGameObject(firstPin.gameObject);
        yield return new WaitForSecondsRealtime(0.1f);
        CheckRaycast(view.Menu.CachedEventSystem, firstPin, "Pin is reachable by the actual menu raycaster");
        PointerEventData click = new PointerEventData(view.Menu.CachedEventSystem);
        click.button = PointerEventData.InputButton.Left;
        ExecuteEvents.Execute(firstPin.gameObject, click, ExecuteEvents.pointerClickHandler);
        ExecuteEvents.Execute(secondPin.gameObject, new BaseEventData(view.Menu.CachedEventSystem), ExecuteEvents.submitHandler);
        Check(SortingPlugin.Instance.Pins.IndexOf(firstId) >= 0 && SortingPlugin.Instance.Pins.IndexOf(secondId) > SortingPlugin.Instance.Pins.IndexOf(firstId), "Mouse and native submit preserve pinning order");
        int start = view.Content.Find("FastInit_Reload") == null ? 0 : 1;
        for (int method = 0; method < 2; method++) for (int direction = 0; direction < 2; direction++)
        {
            SortingPlugin.Instance.SetSort(method, false);
            SortingPlugin.Instance.SetSort(direction, true);
            Check(view.Content.GetChild(start) == first.transform && view.Content.GetChild(start + 1) == second.transform, "Pinned rows remain immediately after refresh and ignore " + (SortMethod)method + "/" + (SortDirection)direction);
            CheckNavigation(view);
        }
        Check(firstPin.transform.Find("Title").GetComponent<T17Text>().text == Language.Unpin, "Pinned button shows the localized unpin action");
        Check(SortingPlugin.Instance.History.Save() == history && selectedScene == scene, "Pin clicks preserve the green dot and original level callback");
        ConfigFile disk = new ConfigFile(SortingPlugin.Instance.Config.ConfigFilePath, false);
        Check(new PinOrder(disk.Bind<string>("Pinning", "PinnedEntries", "", "test").Value).IndexOf(secondId) > new PinOrder(disk.Bind<string>("Pinning", "PinnedEntries", "", "test").Value).IndexOf(firstId), "Pin order is saved to disk");
        SortingPlugin.Instance.Config.Reload();
        yield return null;
        Check(view.Content.GetChild(start) == first.transform && view.Content.GetChild(start + 1) == second.transform, "Config reload preserves the real pinned row order");
        RectTransform rowRect = first.GetComponent<RectTransform>();
        Vector3 savedScale = rowRect.localScale;
        Vector3[] before = new Vector3[4];
        Vector3[] after = new Vector3[4];
        firstPin.GetComponent<RectTransform>().GetWorldCorners(before);
        rowRect.localScale = savedScale * 0.8f;
        firstPin.GetComponent<RectTransform>().GetWorldCorners(after);
        Check(Mathf.Abs(Vector3.Distance(after[0], after[3]) / Vector3.Distance(before[0], before[3]) - 0.8f) < 0.001f, "Pin's visible width follows the real row scaling");
        rowRect.localScale = savedScale;
        ExecuteEvents.Execute(firstPin.gameObject, new BaseEventData(view.Menu.CachedEventSystem), ExecuteEvents.submitHandler);
        Check(SortingPlugin.Instance.Pins.IndexOf(firstId) < 0 && view.Content.GetChild(start) == second.transform, "Unpinning restores ordinary sorting and retains the other pin");
        ExecuteEvents.Execute(firstPin.gameObject, click, ExecuteEvents.pointerClickHandler);
        Check(view.Content.GetChild(start) == second.transform && view.Content.GetChild(start + 1) == first.transform, "Repinning appends after earlier pinned rows");
        ScreenCapture.CaptureScreenshot(Path.Combine(output, view.Content.GetChild(0).name.StartsWith("Level_") ? "level-pins.png" : "package-pins.png"));
        yield return new WaitForSecondsRealtime(0.2f);
        // Rebuild the actual native content while pins remain saved.
        FrontendOptionsMenu sets = (FrontendOptionsMenu)GameAccess.SetMenu.GetValue(null);
        if (view.Menu == sets)
        {
            DIYUI.ClearAllMenuContent(sets);
            MethodInfo addSet = AccessTools.Method(typeof(DIYLevelEntryUI), "AddLevelSetButton");
            foreach (KeyValuePair<string, LevelSetInfoSO> item in DIYLevelAssetBundleManager.levelSetInfos)
                addSet.Invoke(null, new object[] { item.Value });
        }
        else AccessTools.Method(typeof(DIYLevelEntryUI), "OnLevelSetSelected").Invoke(null, new object[] { SortingPlugin.Instance.SelectedSet });
        view.Flush();
        Check(SortingPlugin.Instance.Pins.IndexOf(firstId) >= 0 && SortingPlugin.Instance.Pins.IndexOf(secondId) >= 0, "Content rebuild preserves both saved pin identities");
        start = view.Content.Find("FastInit_Reload") == null ? 0 : 1;
        Check(view.Content.GetChild(start).name == secondName && view.Content.GetChild(start + 1).name == firstName, "Native clear/rebuild restores the pinned display order");
        SortingPlugin.Instance.TogglePin(firstId);
        SortingPlugin.Instance.TogglePin(secondId);
    }

    private void AcknowledgeUpdateCheckError()
    {
        // This machine's optional HostUtilities updater can block startup with an
        // informational release-mirror error. Acknowledge only that exact notice,
        // through its original callback; never accept an installation or data prompt.
        foreach (T17DialogBox dialog in Resources.FindObjectsOfTypeAll<T17DialogBox>())
        {
            if (!dialog.IsActive || !dialog.gameObject.activeInHierarchy || dialog.m_Title == null || dialog.m_Message == null) continue;
            bool knownTitle = dialog.m_Title.text == "检测更新时出错" || dialog.m_Title.text == "Update Check Failed";
            bool knownMessage = dialog.m_Message.text.Contains("资源 Release 尚未发布完整")
                || dialog.m_Message.text.Contains("The resource release on this platform is incomplete or temporarily unavailable.");
            if (!knownTitle || !knownMessage || dialog.m_ConfirmButton == null || !dialog.m_ConfirmButton.gameObject.activeSelf
                || (dialog.m_DeclineButton != null && dialog.m_DeclineButton.gameObject.activeSelf)
                || (dialog.m_CancelButton != null && dialog.m_CancelButton.gameObject.activeSelf)) continue;
            Logger.LogInfo("Acknowledging the known informational HostUtilities update-check error for scripted testing.");
            dialog.Confirm();
        }
    }

    private static void RedirectSaveAddress(ref string __result)
    {
        if (string.IsNullOrEmpty(__result)) return;
        string sandbox;
        if (!sandboxSaves.TryGetValue(__result, out sandbox))
        {
            sandbox = Path.Combine(saveRoot, StableIdentity.Package(null, null, __result) + ".save").Replace('\\', '/');
            saveHashes[__result] = FileHash(__result);
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(saveRoot), "save-manifest.tsv"),
                (saveHashes[__result] == null ? "-" : saveHashes[__result].Replace("-", string.Empty)) + "\t" + __result + Environment.NewLine);
            if (File.Exists(__result)) File.Copy(__result, sandbox);
            sandboxSaves[__result] = sandbox;
        }
        __result = sandbox;
    }

    private IEnumerator VerifyRoundEnd(LevelSetInfoSO set)
    {
        LevelInfoSO level = Array.Find(set.levelInfos, delegate(LevelInfoSO item) { return item.sceneName == "s_ha_test_0"; }) ?? set.levelInfos[0];
        FrontendRootMenu root = GameObject.Find("/Frontend/FrontendParent/FrontendRootMenu").GetComponent<FrontendRootMenu>();
        FrontendDLCMenu dlcMenu = root.GetComponentInChildren<FrontendDLCMenu>(true);
        Check(dlcMenu != null, "The native DLC menu exists before selecting the first kitchen");
        dlcMenu.Show(root.CurrentGamepadUser, root, root.gameObject, false);
        FrontendOptionsMenu sets = (FrontendOptionsMenu)GameAccess.SetMenu.GetValue(null);
        sets.Show(root.CurrentGamepadUser, root, root.gameObject, false);
        AccessTools.Method(typeof(DIYLevelEntryUI), "OnLevelSetSelected").Invoke(null, new object[] { set });
        FrontendOptionsMenu levels = (FrontendOptionsMenu)GameAccess.LevelMenu.GetValue(null);
        T17Button button = levels.GetComponent<MenuView>().Content.Find("Level_" + level.levelName).GetComponent<T17Button>();
        levels.CachedEventSystem.SetSelectedGameObject(button.gameObject);
        yield return new WaitForSecondsRealtime(0.2f);
        button.OnSubmit(new BaseEventData(levels.CachedEventSystem));
        DIYLevelAssetBundleManager.LoadLevelServer(level.sceneName);
        float deadline = Time.realtimeSinceStartup + 65f;
        ServerCampaignFlowController server = null;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (SceneManager.GetActiveScene().name == level.sceneName && !LoadingScreenFlow.IsLoading)
                server = Array.Find(Resources.FindObjectsOfTypeAll<ServerCampaignFlowController>(), delegate(ServerCampaignFlowController item) { return item.gameObject.scene.IsValid() && item.InRound; });
            if (server != null) break;
            yield return null;
        }
        Check(server != null, "Round-end test reaches an actual running custom kitchen");
        ServerRoundTimer timer = AccessTools.Field(typeof(ServerKitchenFlowControllerBase), "m_roundTimer").GetValue(server) as ServerRoundTimer;
        Check(timer != null && !timer.TimeExpired(), "The original kitchen timer is running before expiry");
        // Shorten only the test kitchen's timer; its normal Update/HasFinished/outro
        // and frontend-loading chain must run without a simulated return or quit.
        AccessTools.Field(typeof(ServerRoundTimer), "m_timeLimit").SetValue(timer, timer.TimeElapsed + 1f);
        deadline = Time.realtimeSinceStartup + 65f;
        bool expired = false;
        while (Time.realtimeSinceStartup < deadline)
        {
            expired |= timer.TimeExpired();
            levels = GameAccess.LevelMenu.GetValue(null) as FrontendOptionsMenu;
            if (SceneManager.GetActiveScene().name == "StartScreen" && !LoadingScreenFlow.IsLoading && levels != null
                && levels.gameObject.activeInHierarchy && !SortingPlugin.Instance.Return.State.Pending) break;
            yield return null;
        }
        Check(expired, "The native countdown actually reaches TimeExpired");
        Check(SceneManager.GetActiveScene().name == "StartScreen" && !LoadingScreenFlow.IsLoading && levels != null && levels.gameObject.activeInHierarchy,
            "The native results flow returns to the previous DIY level list");
        yield return new WaitForSecondsRealtime(0.8f);
        GameObject focus = levels.CachedEventSystem.currentSelectedGameObject;
        Check(focus != null && focus.GetComponent<ClickTracker>() != null
            && focus.GetComponent<ClickTracker>().Identity == SortingPlugin.Instance.Metadata.Get(SortingPlugin.Instance.SelectedSet).LevelIds[level],
            "Countdown return retains the clicked level focus after native DLC initialization");
        allowSaveDialog = true;
        selectedScene = null;
        button = levels.GetComponent<MenuView>().Content.Find("Level_" + level.levelName).GetComponent<T17Button>();
        button.OnSubmit(new BaseEventData(levels.CachedEventSystem));
        yield return new WaitForSecondsRealtime(0.5f);
        Check(selectedScene == level.sceneName, "A real level button still dispatches its original callback after timer expiry");
        SelectSaveDialog dialog = T17FrontendFlow.Instance.gameObject.GetComponentInChildren<SelectSaveDialog>(true);
        Check(dialog != null && dialog.gameObject.activeInHierarchy && dialog.CurrentGamepadUser != null,
            "After timer expiry a real level click opens the native save-selection dialog with an engaged user");
        dialog.InvokeNavigateOnUICancel();
        yield return new WaitForSecondsRealtime(0.3f);
        LevelInfoSO nextLevel = Array.Find(set.levelInfos, delegate(LevelInfoSO item) { return item.sceneName != level.sceneName; });
        Check(nextLevel != null, "Round-end regression has a different custom kitchen to select next");
        selectedScene = null;
        button = levels.GetComponent<MenuView>().Content.Find("Level_" + nextLevel.levelName).GetComponent<T17Button>();
        button.OnSubmit(new BaseEventData(levels.CachedEventSystem));
        deadline = Time.realtimeSinceStartup + 20f;
        while (Time.realtimeSinceStartup < deadline && (!dialog.gameObject.activeInHierarchy
            || AccessTools.Field(typeof(SelectSaveDialog), "m_slotUpdate").GetValue(dialog) != null)) yield return null;
        Check(selectedScene == nextLevel.sceneName && dialog.gameObject.activeInHierarchy, "Selecting a different level opens its original save dialog");
        SaveSlotElement[] slots = AccessTools.Field(typeof(SelectSaveDialog), "m_saveElements").GetValue(dialog) as SaveSlotElement[];
        Check(slots != null && slots.Length > 0 && slots[0].gameObject.activeInHierarchy, "Native save-slot loading completes after the returned-list click");
        slots[0].OnSlotClicked();
        deadline = Time.realtimeSinceStartup + 65f;
        while (Time.realtimeSinceStartup < deadline && (SceneManager.GetActiveScene().name != nextLevel.sceneName || LoadingScreenFlow.IsLoading)) yield return null;
        Check(SceneManager.GetActiveScene().name == nextLevel.sceneName && !LoadingScreenFlow.IsLoading, "The original save-slot callback loads a different real kitchen after countdown expiry");
        allowSaveDialog = false;
        foreach (KeyValuePair<string, string> item in saveHashes)
            Check(FileHash(item.Key) == item.Value, "A real round end leaves the original save unchanged");
        File.WriteAllText(Path.Combine(output, "round-end-result.txt"), "Native countdown expired; results returned to the DIY list; real clicks opened save selection; the original save-slot callback loaded a different kitchen. Original save hashes unchanged.");
    }

    private IEnumerator VerifyKitchenReturn(LevelSetInfoSO set)
    {
        Check(AccessTools.Method(typeof(DIYLevelSaveManager), "GetFileAddress").Invoke(null, new object[] { -1 }).ToString().StartsWith(saveRoot.Replace('\\', '/')),
            "Kitchen test redirects DIY save addresses to the isolated validation directory");
        LevelInfoSO level = Array.Find(set.levelInfos, delegate(LevelInfoSO item) { return item.sceneName == "s_ha_test_0"; }) ?? set.levelInfos[0];
        string identity = SortingPlugin.Instance.Metadata.Get(set).LevelIds[level];
        SortingPlugin.Instance.SetSort((int)SortMethod.AddedTime, false);
        SortingPlugin.Instance.SetSort((int)SortDirection.Descending, true);
        ConfigEntry<bool> autoReturn = SortingPlugin.Instance.Config.Bind<bool>("Navigation", "ReturnToLevelList", true, "test");
        autoReturn.Value = true;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            FrontendOptionsMenu sets = (FrontendOptionsMenu)GameAccess.SetMenu.GetValue(null);
            FrontendRootMenu root = GameObject.Find("/Frontend/FrontendParent/FrontendRootMenu").GetComponent<FrontendRootMenu>();
            sets.Show(root.CurrentGamepadUser, root, root.gameObject, false);
            AccessTools.Method(typeof(DIYLevelEntryUI), "OnLevelSetSelected").Invoke(null, new object[] { set });
            FrontendOptionsMenu levels = (FrontendOptionsMenu)GameAccess.LevelMenu.GetValue(null);
            MenuView view = levels.GetComponent<MenuView>();
            DisablePhysicalMouse(levels.CachedEventSystem);
            levels.CachedEventSystem.SetSelectedGameObject(view.Content.Find("Level_" + level.levelName).gameObject);
            yield return new WaitForSecondsRealtime(0.1f);
            view.Content.Find("Level_" + level.levelName).GetComponent<T17Button>().OnSubmit(new BaseEventData(levels.CachedEventSystem));
            Check(SortingPlugin.Instance.Return.State.LevelIdentity == identity, "Real selected-level callback captures the return bookmark");
            DIYLevelAssetBundleManager.LoadLevelServer(level.sceneName);
            float deadline = Time.realtimeSinceStartup + 65f;
            while (Time.realtimeSinceStartup < deadline && (SceneManager.GetActiveScene().name != level.sceneName || LoadingScreenFlow.IsLoading || GameUtils.RequestManager<FlowControllerBase>() == null)) yield return null;
            Check(SceneManager.GetActiveScene().name == level.sceneName && !LoadingScreenFlow.IsLoading && GameUtils.RequestManager<FlowControllerBase>() != null,
                "Actual custom kitchen loaded and finished its loading transition, pass " + attempt);
            Check(GameUtils.GetGameSession() != null && GameUtils.GetGameSession().DLC == DIYLevelAssetBundleManager.diyLevelDLCId,
                "Actual loaded kitchen belongs to the DIY session");
            InGamePauseMenu pause = Array.Find(Resources.FindObjectsOfTypeAll<InGamePauseMenu>(), delegate(InGamePauseMenu menu) { return menu.gameObject.scene.IsValid(); });
            Check(pause != null, "Original in-game pause menu exists for the quit callback");
            if (attempt == 1) autoReturn.Value = false;
            AccessTools.Method(typeof(InGamePauseMenu), "OnQuitConfirmed").Invoke(pause, null);
            deadline = Time.realtimeSinceStartup + 45f;
            while (Time.realtimeSinceStartup < deadline)
            {
                levels = GameAccess.LevelMenu.GetValue(null) as FrontendOptionsMenu;
                if (SceneManager.GetActiveScene().name == "StartScreen" && !LoadingScreenFlow.IsLoading
                    && (attempt == 1 || (levels != null && levels.gameObject.activeInHierarchy && !SortingPlugin.Instance.Return.State.Pending))) break;
                yield return null;
            }
            Check(SceneManager.GetActiveScene().name == "StartScreen" && !LoadingScreenFlow.IsLoading, "Original kitchen quit completed its frontend scene reload");
            yield return new WaitForSecondsRealtime(0.6f);
            levels = GameAccess.LevelMenu.GetValue(null) as FrontendOptionsMenu;
            if (attempt == 0)
            {
                Check(levels != null && levels.gameObject.activeInHierarchy && !SortingPlugin.Instance.Return.State.Pending,
                    "Exiting a real kitchen automatically opens the prior level list exactly once");
                view = levels.GetComponent<MenuView>();
                Check(SortingPlugin.Instance.SelectedSet != null && SortingPlugin.Instance.Metadata.Get(SortingPlugin.Instance.SelectedSet).Identity == SortingPlugin.Instance.Metadata.Get(set).Identity,
                    "Real kitchen return restores the original package");
                Check(levels.CachedEventSystem.currentSelectedGameObject != null && levels.CachedEventSystem.currentSelectedGameObject.GetComponent<ClickTracker>() != null
                    && levels.CachedEventSystem.currentSelectedGameObject.GetComponent<ClickTracker>().Identity == identity,
                    "Real kitchen return restores focus to the clicked level; actual=" + (levels.CachedEventSystem.currentSelectedGameObject == null ? "null" : levels.CachedEventSystem.currentSelectedGameObject.name));
                Check(SortingPlugin.Instance.Method == SortMethod.AddedTime && SortingPlugin.Instance.Direction == SortDirection.Descending,
                    "Real kitchen return retains the saved sort method and direction");
                CheckNavigation(view);
                CheckLevelOrder(view, SortingPlugin.Instance.SelectedSet);
                CheckSingleBadge(view, view.Content.Find("Level_" + level.levelName), "Actual kitchen return retains one recent green dot");
                ScreenCapture.CaptureScreenshot(Path.Combine(output, "actual-kitchen-return.png"));
                yield return new WaitForSecondsRealtime(0.2f);
                levels.InvokeNavigateOnUICancel();
                yield return new WaitForSecondsRealtime(0.2f);
                sets = (FrontendOptionsMenu)GameAccess.SetMenu.GetValue(null);
                Check(!levels.gameObject.activeInHierarchy && sets.gameObject.activeInHierarchy && !SortingPlugin.Instance.Return.State.Pending,
                    "Cancel leaves the restored list for its package menu without reopening it");
                Check(sets.CachedEventSystem.currentSelectedGameObject == sets.GetComponent<MenuView>().Content.Find("LevelSet_" + set.levelSetName).gameObject,
                    "Cancel from the restored level list returns focus to its original package");
                sets.InvokeNavigateOnUICancel();
                yield return new WaitForSecondsRealtime(0.2f);
                Check(!sets.gameObject.activeInHierarchy, "Cancel from the restored package menu returns to the native frontend");
            }
            else Check((levels == null || !levels.gameObject.activeInHierarchy) && !SortingPlugin.Instance.Return.State.Pending,
                "Disabling the option keeps the original frontend after a real kitchen quit");
            // The original frontend may not have built the DIY menus when disabled.
            DIYLevelEntryUI.AddUI();
        }
        autoReturn.Value = true;
        foreach (KeyValuePair<string, string> item in saveHashes)
            Check(FileHash(item.Key) == item.Value, "An isolated kitchen test leaves the original save file unchanged");
        File.WriteAllText(Path.Combine(output, "kitchen-return-result.txt"), "Real kitchen loaded and exited twice; enabled and disabled return verified. Original save hashes unchanged.");
    }

    private void CheckSingleBadge(MenuView view, Transform expectedRow, string message)
    {
        int count = 0;
        foreach (Image badge in view.Content.GetComponentsInChildren<Image>(true))
        {
            if (badge.name != "DIYSorting_Clicked" || !badge.gameObject.activeSelf) continue;
            count++;
            Check(badge.transform.parent == expectedRow, message + ": marker belongs to the latest row");
        }
        Check(count == 1, message + ": exactly one marker in the list");
    }

    private static void AssertStableControls(MenuView view, SortingDropdown methodControl, SortingDropdown directionControl, SortMethod method, SortDirection direction)
    {
        if (view.Toolbar.GetChild(0).GetComponent<SortingDropdown>() != methodControl || view.Toolbar.GetChild(1).GetComponent<SortingDropdown>() != directionControl)
            throw new Exception("Dropdown control was recreated during FastInit refresh.");
        if (SortingPlugin.Instance.Method != method || SortingPlugin.Instance.Direction != direction)
            throw new Exception("Sorting configuration reset during FastInit refresh.");
        if (methodControl.transform.Find("Title").GetComponent<T17Text>().text != Language.Caption(Language.MethodLabel, Language.Methods[(int)method]) ||
            directionControl.transform.Find("Title").GetComponent<T17Text>().text != Language.Caption(Language.DirectionLabel, Language.Directions[(int)direction]))
            throw new Exception("Sorting caption reset during FastInit refresh.");
    }

    private static LevelSetInfoSO Fixture(string name, string scene, string uid)
    {
        LevelSetInfoSO set = ScriptableObject.CreateInstance<LevelSetInfoSO>();
        set.levelSetName = set.levelSetNameZH = name;
        set.uid = uid;
        GameAccess.BaseUid.SetValue(set, uid);
        LevelInfoSO level = ScriptableObject.CreateInstance<LevelInfoSO>();
        level.levelName = level.levelNameZH = name;
        level.sceneName = scene;
        set.levelInfos = new LevelInfoSO[] { level };
        return set;
    }

    private void CheckBusinessUnchanged(List<KeyValuePair<string, LevelSetInfoSO>> current, List<KeyValuePair<string, LevelSetInfoSO>> snapshot)
    {
        Check(current.Count == snapshot.Count, "Business collection count unchanged");
        for (int i = 0; i < snapshot.Count; i++) Check(current[i].Value == snapshot[i].Value, "Business index " + i + " unchanged");
    }

    private void CheckOrder(MenuView view, List<KeyValuePair<string, LevelSetInfoSO>> data)
    {
        Dictionary<string, SortKey> keys = new Dictionary<string, SortKey>();
        foreach (KeyValuePair<string, LevelSetInfoSO> pair in data)
            keys["LevelSet_" + pair.Value.levelSetName] = new SortKey(DIYUI.GetLocalizedText(pair.Value.levelSetName, pair.Value.levelSetNameZH),
                SortingPlugin.Instance.Metadata.Get(pair.Value).AddedTicks, SortingPlugin.Instance.Metadata.Get(pair.Value).Identity);
        SortKeyComparer compare = new SortKeyComparer(SortingPlugin.Instance.Method, SortingPlugin.Instance.Direction,
            System.Globalization.CultureInfo.GetCultureInfo(Localization.GetLanguage() == SupportedLanguages.Chinese ? "zh-CN" : "en-US"));
        SortKey previous = null;
        for (int i = 0; i < view.Content.childCount; i++)
        {
            SortKey current;
            if (!keys.TryGetValue(view.Content.GetChild(i).name, out current)) continue;
            Check(previous == null || compare.Compare(previous, current) <= 0, "Actual sibling order matches " + SortingPlugin.Instance.Method + "/" + SortingPlugin.Instance.Direction);
            previous = current;
        }
    }

    private void CheckNavigation(MenuView view)
    {
        List<RectTransform> cached = GameAccess.Items(view.Scroll);
        Check(cached.Count == view.Content.childCount, "Native navigation cache covers every visible row");
        for (int i = 0; i < cached.Count; i++)
        {
            Check(cached[i] == view.Content.GetChild(i), "Cache index matches display " + i);
            if (i + 1 < cached.Count) Check(cached[i].GetComponent<Selectable>().navigation.selectOnDown == cached[i + 1].GetComponent<Selectable>(), "Down navigation matches next row " + i);
            Check(cached[i].GetComponent<Selectable>().navigation.selectOnLeft == view.Toolbar.GetChild(0).GetComponent<Selectable>(),
                "Left navigation matches the left sorting toolbar " + i);
        }
    }

    private IEnumerator WaitForMenuToSettle(MenuView view)
    {
        // The native slide-in can still put controls off screen after a fixed
        // 0.3 seconds. Observe the actual pose before testing pointer input.
        float deadline = Time.realtimeSinceStartup + 5f;
        float stableSince = Time.realtimeSinceStartup;
        Vector3 previous = view.Toolbar.position;
        Canvas canvas = view.Toolbar.GetComponentInParent<Canvas>();
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector3[] corners = new Vector3[4];
        bool settled = false;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            Vector3 position = view.Toolbar.position;
            bool visible = true;
            view.Toolbar.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corner);
                visible &= point.x >= 0 && point.x <= Screen.width && point.y >= 0 && point.y <= Screen.height;
            }
            if (!visible || (position - previous).sqrMagnitude > 0.01f) stableSince = Time.realtimeSinceStartup;
            previous = position;
            if (visible && Time.realtimeSinceStartup - stableSince >= 0.2f && !view.Menu.CachedEventSystem.IsDisabled())
            {
                settled = true;
                break;
            }
        }
        Check(settled, "Native menu transition settles with both controls on screen before pointer checks");
    }

    private void CheckLeftToolbar(MenuView view)
    {
        RectTransform body = view.Menu.transform.Find("SettingsBody") as RectTransform;
        Vector3[] listCorners = new Vector3[4];
        Vector3[] toolbarCorners = new Vector3[4];
        view.Scroll.GetComponent<RectTransform>().GetWorldCorners(listCorners);
        view.Toolbar.GetWorldCorners(toolbarCorners);
        Vector3 listLeft = body.InverseTransformPoint(listCorners[1]);
        Vector3 toolbarLeft = body.InverseTransformPoint(toolbarCorners[1]);
        Vector3 toolbarRight = body.InverseTransformPoint(toolbarCorners[2]);
        Check(Mathf.Abs(listLeft.x - toolbarRight.x - 24f) < 0.1f,
            "Left toolbar has the mirrored gap and does not overlap the list");
        Check(Mathf.Abs(listLeft.y - toolbarRight.y) < 0.1f,
            "Left toolbar stays aligned with the list's top edge");
        Check(toolbarLeft.x >= body.rect.xMin + 29.9f && Mathf.Abs(view.Toolbar.rect.width - 290f) < 0.1f,
            "Left toolbar retains its width and fits inside the menu with a margin");
        T17Button method = view.Toolbar.GetChild(0).GetComponent<T17Button>();
        T17Button direction = view.Toolbar.GetChild(1).GetComponent<T17Button>();
        CheckRaycast(view.Menu.CachedEventSystem, method, "Left method control is reachable by the real UI raycaster");
        CheckRaycast(view.Menu.CachedEventSystem, direction, "Left direction control is reachable by the real UI raycaster");
        float width = Vector3.Distance(toolbarCorners[0], toolbarCorners[3]);
        float gap = Vector3.Distance(toolbarCorners[2], listCorners[1]);
        Vector3 savedScale = body.localScale;
        try
        {
            body.localScale = savedScale * 0.8f;
            Canvas.ForceUpdateCanvases();
            view.Toolbar.GetWorldCorners(toolbarCorners);
            view.Scroll.GetComponent<RectTransform>().GetWorldCorners(listCorners);
            Check(Mathf.Abs(Vector3.Distance(toolbarCorners[0], toolbarCorners[3]) / width - 0.8f) < 0.001f
                && Mathf.Abs(Vector3.Distance(toolbarCorners[2], listCorners[1]) / gap - 0.8f) < 0.001f,
                "Left toolbar width and spacing follow the menu scaling");
            CheckRaycast(view.Menu.CachedEventSystem, method, "Scaled left method control remains clickable");
            CheckRaycast(view.Menu.CachedEventSystem, direction, "Scaled left direction control remains clickable");
        }
        finally { body.localScale = savedScale; Canvas.ForceUpdateCanvases(); }
    }

    private void CheckLevelOrder(MenuView view, LevelSetInfoSO set)
    {
        PackageMetadata metadata = SortingPlugin.Instance.Metadata.Get(set);
        Dictionary<string, SortKey> keys = new Dictionary<string, SortKey>();
        foreach (LevelInfoSO level in set.levelInfos)
            keys["Level_" + level.levelName] = new SortKey(DIYUI.GetLocalizedText(level.levelName, level.levelNameZH), ExpectedLevelTime(set, level), metadata.LevelIds[level]);
        SortKeyComparer comparer = new SortKeyComparer(SortingPlugin.Instance.Method, SortingPlugin.Instance.Direction,
            System.Globalization.CultureInfo.GetCultureInfo(Localization.GetLanguage() == SupportedLanguages.Chinese ? "zh-CN" : "en-US"));
        SortKey previous = null;
        for (int i = 0; i < view.Content.childCount; i++)
        {
            SortKey current = keys[view.Content.GetChild(i).name];
            Check(previous == null || comparer.Compare(previous, current) <= 0, "Actual level sibling order matches " + SortingPlugin.Instance.Method + "/" + SortingPlugin.Instance.Direction);
            previous = current;
        }
    }

    private static long ExpectedLevelTime(LevelSetInfoSO set, LevelInfoSO level)
    {
        KeyValuePair<string, LevelSetInfoSO> pair = DIYLevelAssetBundleManager.levelSetInfos.Find(delegate(KeyValuePair<string, LevelSetInfoSO> item) { return item.Value == set; });
        if (string.IsNullOrEmpty(pair.Key) || string.IsNullOrEmpty(level.sceneName)) return 0;
        FileInfo file = new FileInfo(Path.Combine(pair.Key, level.sceneName.ToLowerInvariant()));
        return file.Exists ? file.LastWriteTimeUtc.Ticks : 0;
    }

    private void CheckRaycast(T17EventSystem system, T17Button button, string message)
    {
        Canvas canvas = button.GetComponentInParent<Canvas>();
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        RectTransform rect = button.GetComponent<RectTransform>();
        PointerEventData pointer = new PointerEventData(system);
        pointer.position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
        List<RaycastResult> hits = new List<RaycastResult>();
        system.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<T17Button>() == button, message);
    }
}
