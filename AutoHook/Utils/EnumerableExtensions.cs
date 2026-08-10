namespace AutoHook.Utils;

// porting-note(api13): upstream gets IEnumerable<T>.ForEach from the ECommons revision it builds
// against; the revision pinned for api13 does not expose it. Same semantics, defined locally.
public static class EnumerableExtensions
{
    public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
    {
        foreach (var item in source)
            action(item);
    }
}
