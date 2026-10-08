using System;
using System.Collections.Generic;
using System.Globalization;
using OC2DIYLevelSorting;
using SortKey = OC2DIYLevelSorting.SortKey;

internal static class BehaviorTests
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }

    private static void Order(SortMethod method, SortDirection direction, string expected)
    {
        List<SortKey> entries = new List<SortKey>();
        entries.Add(new SortKey("Zulu", 10, "a"));
        entries.Add(new SortKey("alpha", 30, "b"));
        entries.Add(new SortKey("Bravo", 20, "c"));
        entries.Sort(new SortKeyComparer(method, direction, CultureInfo.GetCultureInfo("en-US")));
        string actual = entries[0].Identity + entries[1].Identity + entries[2].Identity;
        Check(actual == expected, method + "/" + direction + " expected " + expected + ", got " + actual);
    }

    private static int Main()
    {
        try
        {
            Order(SortMethod.Name, SortDirection.Ascending, "bca");
            Order(SortMethod.Name, SortDirection.Descending, "acb");
            Order(SortMethod.AddedTime, SortDirection.Ascending, "acb");
            Order(SortMethod.AddedTime, SortDirection.Descending, "bca");
            ReturnToList();
            foreach (SortMethod method in Enum.GetValues(typeof(SortMethod)))
            foreach (SortDirection direction in Enum.GetValues(typeof(SortDirection)))
            {
                SortKeyComparer compare = new SortKeyComparer(method, direction, CultureInfo.GetCultureInfo("zh-CN"));
                SortKey a = new SortKey("同名", 7, "a");
                SortKey b = new SortKey("同名", 7, "b");
                Check(compare.Compare(a, b) < 0, "Equal primary values retain stable identity order.");
                Check(compare.Compare(b, a) > 0, "Comparator antisymmetry.");
                Check(compare.Compare(a, a) == 0, "Comparator reflexivity.");
                List<SortKey> empty = new List<SortKey>();
                empty.Sort(compare);
                Check(empty.Count == 0, "Empty list.");
                empty.Add(a);
                empty.Sort(compare);
                Check(empty[0] == a, "Single entry.");
            }
            SortKeyComparer nameCompare = new SortKeyComparer(SortMethod.Name, SortDirection.Ascending, CultureInfo.GetCultureInfo("en-US"));
            Check(nameCompare.Compare(new SortKey(null, 0, "a"), new SortKey("B", 0, "b")) < 0, "Null name becomes empty.");
            Check(nameCompare.Compare(new SortKey("alpha", 0, "a"), new SortKey("ALPHA", 0, "b")) < 0, "Case-insensitive tie uses identity.");
            string package = StableIdentity.Package("original", "version1", "old-folder");
            Check(package == StableIdentity.Package("original", "version2", "renamed-folder"), "baseUID survives updates and directory rename.");
            Check(package != StableIdentity.Package("other", "version1", "old-folder"), "Same display names in different packages stay distinct.");
            Check(StableIdentity.Package(null, "version1", "x") != StableIdentity.Package(null, "version2", "x"), "uid-only update boundary is explicit.");
            Check(StableIdentity.Package(null, null, "A\\B") == StableIdentity.Package(null, null, "a/b"), "Windows path fallback normalization.");
            string levelA = StableIdentity.Level(package, "scene", 0);
            string levelB = StableIdentity.Level(package, "scene", 1);
            string levelC = StableIdentity.Level(package, "other-scene", 0);
            Check(levelA != levelB && levelA != levelC, "Duplicate scene occurrences and different scenes stay distinct.");
            Check(levelA != StableIdentity.Level(StableIdentity.Package("other", null, null), "scene", 0), "Same scene across packages stays distinct.");
            ClickHistory history = new ClickHistory(null);
            Check(history.Add(levelA), "First click changes history.");
            Check(!history.Add(levelA), "Repeat click does not request another save.");
            Check(history.HasPackage(package) && history.Contains(levelA) && !history.Contains(levelB), "History tracks only the clicked occurrence.");
            Check(history.Add(levelB), "A different level replaces the latest click.");
            Check(!history.Contains(levelA) && history.Contains(levelB) && history.HasPackage(package), "Only the latest level in a package remains marked.");
            string saved = history.Save();
            Check(saved == levelB, "Persistence contains exactly one level identity.");
            ClickHistory restored = new ClickHistory(saved + ";" + saved + ";");
            Check(restored.Save() == saved && restored.Contains(levelB), "Persistence round-trip and duplicates.");
            string otherPackage = StableIdentity.Package("other", null, null);
            string otherLevel = StableIdentity.Level(otherPackage, "scene", 0);
            Check(history.Add(otherLevel), "Clicking another package replaces the previous click.");
            Check(history.HasPackage(otherPackage) && !history.HasPackage(package) && history.Contains(otherLevel) && !history.Contains(levelB), "The previous package and its level both lose their markers.");
            Check(history.Add(levelA) && history.Contains(levelA) && !history.Contains(otherLevel) && !history.HasPackage(otherPackage), "Revisiting an older level makes it the unique latest click.");
            Check(!history.Add(levelA), "Prefix and listener recording the same click only request one save.");
            Check(!history.Add(null) && !history.Add(string.Empty) && history.Contains(levelA), "Empty click identities cannot replace the latest level.");
            ClickHistory ambiguous = new ClickHistory(levelA + ";" + otherLevel);
            Check(ambiguous.Save() == string.Empty && !ambiguous.Contains(levelA) && !ambiguous.HasPackage(otherPackage), "Unordered legacy history cannot invent the latest click or display multiple markers.");
            Check(ambiguous.Add(levelB) && ambiguous.Save() == levelB, "The next click replaces ambiguous legacy history with a unique latest identity.");
            ClickHistory emptyHistory = new ClickHistory(";;");
            Check(!emptyHistory.Contains(null) && !emptyHistory.Contains(string.Empty) && !emptyHistory.HasPackage(string.Empty), "Empty saved state has no marked level or package.");
            RefreshGate gate = new RefreshGate();
            Check(!gate.Ready(99), "Idle gate stays idle.");
            gate.Request(1);
            gate.Request(1.1f);
            Check(!gate.Ready(1.14f) && gate.Ready(1.151f), "Continuous arrivals cannot indefinitely delay refresh.");
            gate.Complete();
            Check(!gate.Ready(2), "Refresh consumes pending work.");
            gate.Request(3);
            Check(!gate.Ready(3.1f) && gate.Ready(3.151f), "New batch gets its own delay.");
            // Arrival order never affects results, including while a sort setting changes.
            List<SortKey> growing = new List<SortKey>();
            for (int i = 499; i >= 0; i--)
            {
                growing.Add(new SortKey("same", i % 5, i.ToString("D3")));
                if (i % 23 == 0) growing.Sort(new SortKeyComparer(SortMethod.AddedTime, SortDirection.Descending, CultureInfo.InvariantCulture));
            }
            SortKeyComparer final = new SortKeyComparer(SortMethod.Name, SortDirection.Ascending, CultureInfo.InvariantCulture);
            growing.Sort(final);
            for (int i = 1; i < growing.Count; i++) Check(final.Compare(growing[i - 1], growing[i]) < 0, "Incremental appends followed by a changed sort setting.");
            Console.WriteLine("PASS: " + assertions + " assertions (production sorting, identity, history and refresh gate).");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static void ReturnToList()
    {
        LevelListReturnState state = new LevelListReturnState();
        state.SceneLeaving("StartScreen", "kitchen", true, true);
        Check(!state.Pending, "Boot without a session bookmark cannot reopen a level list.");
        state.Capture("package", "level", "kitchen", 0.4f);
        state.SceneLeaving("StartScreen", "StartScreen", true, true);
        Check(!state.Pending, "Cancelling a save selection is not an exited kitchen.");
        state.SceneEntered("KITCHEN", true);
        state.SceneLeaving("StartScreen", "kitchen", true, true);
        Check(state.Pending && state.PackageIdentity == "package" && state.LevelIdentity == "level" && state.ScrollPosition == 0.4f,
            "An actual custom kitchen return preserves its package, level and viewport bookmark.");
        state.Complete();
        state.SceneLeaving("StartScreen", "kitchen", true, true);
        Check(!state.Pending, "A completed return cannot reopen the menu a second time.");
        state.SceneEntered("kitchen", true);
        state.SceneLeaving("kitchen", "kitchen", true, true);
        Check(!state.Pending, "Restarting the current kitchen does not open a level list.");
        state.SceneEntered("Loading", true);
        state.SceneEntered("kitchen", true);
        state.SceneLeaving("StartScreen", "kitchen", true, false);
        Check(!state.Pending, "Disabling automatic return preserves the original destination.");
        state.SceneLeaving("StartScreen", "kitchen", false, true);
        Check(!state.Pending, "A non-custom session cannot use the custom bookmark.");
        state.SceneEntered("official", false);
        state.SceneLeaving("StartScreen", "official", false, true);
        Check(!state.Pending, "Leaving official levels never opens the old DIY list.");
        state.Capture("other-package", "other-level", "other-kitchen", 1f);
        state.SceneEntered("kitchen", true);
        state.SceneLeaving("StartScreen", "kitchen", true, true);
        Check(!state.Pending, "A bookmark for another kitchen cannot hijack its return.");
        state.SceneEntered("other-kitchen", true);
        state.SceneLeaving("StartScreen", "other-kitchen", true, true);
        state.SceneEntered("Loading", true);
        state.SceneEntered("StartScreen", false);
        Check(state.Pending && state.PackageIdentity == "other-package", "Loading and frontend scenes preserve a pending return.");
        state.Complete();
        Check(!state.Pending, "An explicit cancellation consumes pending restoration.");
    }
}
