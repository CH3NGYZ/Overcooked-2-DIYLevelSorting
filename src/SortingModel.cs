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
        private string lastLevel = string.Empty;
        private string lastPackage = string.Empty;

        internal ClickHistory(string saved)
        {
            string candidate = null;
            foreach (string item in (saved ?? string.Empty).Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // Older versions saved an identity-sorted set, not click order.
                // A single distinct legacy identity can be restored; several
                // identities cannot tell us which click was most recent.
                if (candidate != null && !string.Equals(candidate, item, StringComparison.Ordinal)) return;
                candidate = item;
            }
            Add(candidate);
        }

        internal bool Add(string id)
        {
            if (string.IsNullOrEmpty(id) || string.Equals(lastLevel, id, StringComparison.Ordinal)) return false;
            lastLevel = id;
            int separator = id.IndexOf('.');
            lastPackage = separator > 0 ? id.Substring(0, separator) : string.Empty;
            return true;
        }

        internal bool Contains(string id) { return lastLevel.Length != 0 && string.Equals(lastLevel, id, StringComparison.Ordinal); }
        internal bool HasPackage(string id) { return lastPackage.Length != 0 && string.Equals(lastPackage, id, StringComparison.Ordinal); }
        internal string Save() { return lastLevel; }
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
