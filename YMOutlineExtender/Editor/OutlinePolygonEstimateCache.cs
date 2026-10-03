using System.Collections.Generic;
using System.Linq;

namespace YoridoriModifiers.OutlineExtender
{
    // Only numeric results survive Inspector disposal. No meshes, materials or
    // generated textures are retained. Domain reload clears the cache naturally.
    internal static class OutlinePolygonEstimateCache
    {
        private const int Capacity = 32;
        private sealed class Entry
        {
            internal (int before, int after) Counts;
            internal long LastUsed;
        }
        private static readonly Dictionary<string, Entry> Entries = new();
        private static long access;

        internal static bool TryGet(string key, out (int before, int after) counts)
        {
            if (Entries.TryGetValue(key, out var entry))
            {
                entry.LastUsed = ++access;
                counts = entry.Counts;
                return true;
            }
            counts = default;
            return false;
        }

        internal static void Store(string key, (int before, int after) counts)
        {
            Entries[key] = new Entry { Counts = counts, LastUsed = ++access };
            if (Entries.Count > Capacity)
                Entries.Remove(Entries.OrderBy(pair => pair.Value.LastUsed).First().Key);
        }
    }
}
