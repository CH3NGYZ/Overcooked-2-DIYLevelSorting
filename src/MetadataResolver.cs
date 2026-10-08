using System;
using System.Collections.Generic;
using System.IO;
using LevelEditorStub;

namespace OC2DIYLevelSorting
{
    internal sealed class PackageMetadata
    {
        internal string Identity;
        internal long AddedTicks;
        internal readonly Dictionary<LevelInfoSO, string> LevelIds = new Dictionary<LevelInfoSO, string>();
    }

    internal sealed class MetadataResolver
    {
        private readonly Dictionary<LevelSetInfoSO, string> paths = new Dictionary<LevelSetInfoSO, string>();
        private readonly Dictionary<LevelSetInfoSO, PackageMetadata> cache = new Dictionary<LevelSetInfoSO, PackageMetadata>();
        private List<KeyValuePair<string, LevelSetInfoSO>> indexedList;
        private int indexedCount;

        internal void Invalidate()
        {
            paths.Clear();
            cache.Clear();
            indexedCount = 0;
            indexedList = null;
        }

        private void Index()
        {
            List<KeyValuePair<string, LevelSetInfoSO>> list = OC2DIYLevel.DIYLevelAssetBundleManager.levelSetInfos;
            if (list == null) return;
            if (!object.ReferenceEquals(list, indexedList) || list.Count < indexedCount)
            {
                Invalidate();
                indexedList = list;
            }
            for (; indexedCount < list.Count; indexedCount++)
                if (list[indexedCount].Value != null) paths[list[indexedCount].Value] = list[indexedCount].Key;
        }

        internal PackageMetadata Get(LevelSetInfoSO set)
        {
            Index();
            PackageMetadata result;
            if (cache.TryGetValue(set, out result)) return result;
            string path;
            if (!paths.TryGetValue(set, out path))
            {
                // A reload can replace an object without changing the list length.
                Invalidate();
                Index();
                paths.TryGetValue(set, out path);
            }
            string relativePath = string.IsNullOrEmpty(path) ? set.uid : new DirectoryInfo(path).Name;
            string baseUid = GameAccess.BaseUid == null ? null : GameAccess.BaseUid.GetValue(set) as string;
            if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(baseUid) && string.IsNullOrEmpty(set.uid))
                throw new InvalidOperationException("A package without a path or UID cannot be assigned a persistent identity.");
            result = new PackageMetadata();
            result.Identity = StableIdentity.Package(baseUid, set.uid, relativePath);
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    // Match DIYLevel's info* metadata bundle selection; no bundles are loaded here.
                    FileInfo[] files = new DirectoryInfo(path).GetFiles("info*");
                    if (files.Length != 0) result.AddedTicks = files[0].CreationTimeUtc.Ticks;
                    else SortingPlugin.Instance.Warn("No info* file; using minimum added time: " + relativePath);
                }
                catch (IOException e) { SortingPlugin.Instance.Warn("Reading creation time failed: " + e.Message); }
                catch (UnauthorizedAccessException e) { SortingPlugin.Instance.Warn("Reading creation time failed: " + e.Message); }
            }
            Dictionary<string, int> occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            if (set.levelInfos != null)
            {
                foreach (LevelInfoSO level in set.levelInfos)
                {
                    if (level == null) continue;
                    string scene = level.sceneName ?? string.Empty;
                    int occurrence;
                    occurrences.TryGetValue(scene, out occurrence);
                    result.LevelIds[level] = StableIdentity.Level(result.Identity, scene, occurrence);
                    occurrences[scene] = occurrence + 1;
                }
            }
            cache[set] = result;
            return result;
        }
    }
}
