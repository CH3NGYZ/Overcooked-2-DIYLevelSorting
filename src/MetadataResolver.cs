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
        internal string DirectoryPath;
        internal readonly Dictionary<LevelInfoSO, string> LevelIds = new Dictionary<LevelInfoSO, string>();
        internal readonly Dictionary<string, long> LevelTimes = new Dictionary<string, long>(StringComparer.Ordinal);
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

        internal void InvalidateLevelTimes(LevelSetInfoSO set)
        {
            PackageMetadata metadata;
            if (set != null && cache.TryGetValue(set, out metadata)) metadata.LevelTimes.Clear();
        }

        internal long GetLevelAddedTicks(LevelSetInfoSO set, LevelInfoSO level)
        {
            PackageMetadata metadata = Get(set);
            // Match the installed loader's asset name, not the localized display name.
            string scene = (level.sceneName ?? string.Empty).ToLowerInvariant();
            long ticks;
            if (metadata.LevelTimes.TryGetValue(scene, out ticks)) return ticks;
            ticks = string.IsNullOrEmpty(metadata.DirectoryPath) ? 0
                : ReadModifiedTime(Path.Combine(metadata.DirectoryPath, scene));
            metadata.LevelTimes[scene] = ticks;
            return ticks;
        }

        private static long ReadModifiedTime(string path)
        {
            try
            {
                FileInfo file = new FileInfo(path);
                if (file.Exists) return file.LastWriteTimeUtc.Ticks;
                SortingPlugin.Instance.Warn("No resource file; using minimum added time: " + path);
            }
            catch (IOException e) { SortingPlugin.Instance.Warn("Reading modification time failed: " + e.Message); }
            catch (UnauthorizedAccessException e) { SortingPlugin.Instance.Warn("Reading modification time failed: " + e.Message); }
            return 0;
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
            result.DirectoryPath = path;
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    // Match DIYLevel's info* metadata bundle selection; no bundles are loaded here.
                    FileInfo[] files = new DirectoryInfo(path).GetFiles("info*");
                    if (files.Length != 0) result.AddedTicks = ReadModifiedTime(files[0].FullName);
                    else SortingPlugin.Instance.Warn("No info* file; using minimum added time: " + relativePath);
                }
                catch (IOException e) { SortingPlugin.Instance.Warn("Reading modification time failed: " + e.Message); }
                catch (UnauthorizedAccessException e) { SortingPlugin.Instance.Warn("Reading modification time failed: " + e.Message); }
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
