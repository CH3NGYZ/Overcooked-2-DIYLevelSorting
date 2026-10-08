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
    private static bool ObserveScene(string __0) { selectedScene = __0; return false; }
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
        if (string.IsNullOrEmpty(output)) { enabled = false; return; }
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
        if (harmony != null) harmony.UnpatchSelf();
        string result = failure == null ? "PASS: " + assertions + " actual Unity assertions." : failure.ToString();
        File.WriteAllText(Path.Combine(output, "result.txt"), result);
        Logger.LogInfo(result);
        yield return new WaitForSecondsRealtime(2);
        Application.Quit();
    }

    private IEnumerator Run()
    {
        float deadline = Time.realtimeSinceStartup + 100;
        GameObject rootObject = null;
        while (Time.realtimeSinceStartup < deadline)
        {
            rootObject = GameObject.Find("/Frontend/FrontendParent/FrontendRootMenu");
            if (rootObject != null && rootObject.transform.Find("FixedAspectRootCanvas/ScreenSpaceCanvas/GameOptions") != null) break;
            yield return null;
        }
        Check(rootObject != null, "Frontend scene and original GameOptions template exist");
        if (!DIYLevelAssetBundleManager.IsInitialized) DIYLevelAssetBundleManager.Initialize();
        Type fast = AccessTools.TypeByName("DIYLevelFastInit.FastInitPlugin");
        FieldInfo loading = fast == null ? null : AccessTools.Field(fast, "Loading");
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
                if (count > 0 && count != observedLoaded)
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
                        T17StandaloneInputModule loadingInput = loadingMenu.CachedEventSystem.GetComponent<T17StandaloneInputModule>();
                        if (loadingInput != null) { loadingInput.allowMouseInput = false; loadingInput.forceModuleActive = true; }
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
        Check(setView.Toolbar.childCount == 2, "Right toolbar holds exactly two fixed controls");
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
        AccessTools.Field(typeof(BaseMenuBehaviour), "m_CurrentGamepadUser").SetValue(root, user);
        sets.Show(user, root, rootObject, false);
        yield return new WaitForSecondsRealtime(0.6f);
        Check(sets != null && setView != null && setView.Content != null, "Native menu and view survive activation");
        CheckNavigation(setView);
        Vector3 toolbarPosition = setView.Toolbar.position;
        setView.Scroll.verticalNormalizedPosition = 0;
        yield return null;
        Check(setView.Toolbar.position == toolbarPosition, "Right controls stay fixed while the list scrolls");
        setView.Scroll.verticalNormalizedPosition = 1;
        SortingDropdown dropdown = setView.Toolbar.Find("DIYSorting_Method").GetComponent<SortingDropdown>();
        T17EventSystem eventSystem = sets.CachedEventSystem;
        // Scripted native events must not race the user's physical mouse on the desktop.
        // Selection still runs through the real T17EventSystem and original UI handlers.
        T17StandaloneInputModule liveInput = eventSystem.GetComponent<T17StandaloneInputModule>();
        if (liveInput != null) { liveInput.allowMouseInput = false; liveInput.forceModuleActive = true; }
        yield return new WaitForSecondsRealtime(0.1f);
        GameObject firstRow = setView.Content.GetChild(0).gameObject;
        eventSystem.SetSelectedGameObject(firstRow);
        yield return new WaitForSecondsRealtime(0.1f);
        AxisEventData right = new AxisEventData(eventSystem);
        right.moveDir = MoveDirection.Right;
        ExecuteEvents.Execute(firstRow, right, ExecuteEvents.moveHandler);
        yield return new WaitForSecondsRealtime(0.1f);
        Logger.LogInfo("Right focus diagnostics: selected=" + (eventSystem.currentSelectedGameObject == null ? "null" : eventSystem.currentSelectedGameObject.name) + ", pending=" + (eventSystem.GetPendingSelectedGameObject() == null ? "null" : eventSystem.GetPendingSelectedGameObject().name));
        Check(eventSystem.currentSelectedGameObject == dropdown.gameObject, "Native right navigation reaches fixed toolbar");
        AxisEventData left = new AxisEventData(eventSystem);
        left.moveDir = MoveDirection.Left;
        ExecuteEvents.Execute(dropdown.gameObject, left, ExecuteEvents.moveHandler);
        yield return new WaitForSecondsRealtime(0.1f);
        Check(eventSystem.currentSelectedGameObject == firstRow, "Native left navigation returns to the selected list row");
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
        yield return new WaitForSecondsRealtime(0.3f);
        MenuView levelView = levels.GetComponent<MenuView>();
        Check(levelView.Content.childCount == chosen.levelInfos.Length, "Original package selection contains only its own levels");
        CheckNavigation(levelView);
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
        harmony = new Harmony("oc2.diylevel.sorting.unitytests.observe");
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
        }
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
