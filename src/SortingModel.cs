using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OC2DIYLevelSorting
{
    public enum SortMethod { Name, AddedTime }
    public enum SortDirection { Ascending, Descending }

    internal sealed class SortKey
    {
        internal string Name;
        internal long AddedTicks;
        internal string Identity;

        internal SortKey(string name, long ticks, string identity)
        {
            Name = name ?? string.Empty;
            AddedTicks = ticks;
            Identity = identity;
        }
    }

    internal sealed class SortKeyComparer : IComparer<SortKey>
    {
        private readonly SortMethod method;
        private readonly SortDirection direction;
        private readonly CompareInfo culture;

        internal SortKeyComparer(SortMethod method, SortDirection direction, CultureInfo culture)
        {
            this.method = method;
            this.direction = direction;
            this.culture = culture.CompareInfo;
        }

        public int Compare(SortKey a, SortKey b)
        {
            int result = method == SortMethod.AddedTime
                ? a.AddedTicks.CompareTo(b.AddedTicks)
                : culture.Compare(a.Name, b.Name, CompareOptions.IgnoreCase);
            if (result != 0)
                return direction == SortDirection.Descending ? -Math.Sign(result) : result;
            // The identity tie-break never depends on arrival order or direction.
            return string.CompareOrdinal(a.Identity, b.Identity);
        }
    }

    internal static class StableIdentity
    {
        internal static string Package(string baseUid, string uid, string relativeDirectory)
        {
            string key = !string.IsNullOrEmpty(baseUid) ? "base:" + baseUid
                : !string.IsNullOrEmpty(uid) ? "uid:" + uid
                : "path:" + (relativeDirectory ?? string.Empty).Replace('\\', '/').ToLowerInvariant();
            return Hash(key);
        }

        internal static string Level(string package, string scene, int occurrence)
        {
            return package + "." + Hash(scene ?? string.Empty) + "." + occurrence.ToString(CultureInfo.InvariantCulture);
        }

        private static string Hash(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                StringBuilder result = new StringBuilder(64);
                foreach (byte b in bytes) result.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }
    }

    internal sealed class ClickHistory
    {
        private readonly HashSet<string> levels = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> packages = new HashSet<string>(StringComparer.Ordinal);

        internal ClickHistory(string saved)
        {
            foreach (string item in (saved ?? string.Empty).Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                Add(item);
        }

        internal bool Add(string id)
        {
            if (string.IsNullOrEmpty(id) || !levels.Add(id)) return false;
            int separator = id.IndexOf('.');
            if (separator > 0) packages.Add(id.Substring(0, separator));
            return true;
        }

        internal bool Contains(string id) { return levels.Contains(id); }
        internal bool HasPackage(string id) { return packages.Contains(id); }
        internal string Save()
        {
            string[] sorted = new string[levels.Count];
            levels.CopyTo(sorted);
            Array.Sort(sorted, StringComparer.Ordinal);
            return string.Join(";", sorted);
        }
    }

    internal sealed class RefreshGate
    {
        private bool pending;
        private float due;
        internal void Request(float now)
        {
            if (!pending) { pending = true; due = now + 0.15f; }
        }
        internal bool Ready(float now) { return pending && now >= due; }
        internal void Complete() { pending = false; }
    }
}
