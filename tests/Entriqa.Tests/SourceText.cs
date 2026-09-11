namespace Entriqa.Tests;

/// <summary>
/// Cutting a region out of a source file, for the guards that have to read code no test host runs.
/// The two slicing helpers fail loudly rather than returning an empty string: a guard that silently reads nothing
/// passes on everything, which is the failure mode that makes a guard worse than no guard at all.
/// </summary>
internal static class SourceText
{
    /// <summary>
    /// A directory of the repository, resolved upwards from the test binary. The tests run in
    /// bin/Debug/net10.0, so a path relative to the repository root does not exist - and three guards
    /// had grown their own copy of this walk.
    /// </summary>
    public static string RepoDirectory(params string[] relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine([dir.FullName, .. relative]);
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException($"{Path.Combine(relative)} not found above {AppContext.BaseDirectory}");
    }

    /// <summary>The text between an opening marker and the first closing one after it.</summary>
    public static string Between(string source, string from, string to, string hint)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException(hint);
        var end = source.IndexOf(to, start, StringComparison.Ordinal);
        if (end <= start) throw new InvalidOperationException(hint);
        return source[start..end];
    }

    /// <summary>
    /// The braced block that follows <paramref name="marker"/> - an <c>@if</c> body, a class body, a
    /// method body, an object initialiser - cut at its own closing brace rather than at whatever text
    /// happens to come next. Ending such a slice at the next heading or the next declaration lets the
    /// very thing being guarded move out of the block while the guard keeps passing.
    /// </summary>
    public static string Block(string source, string marker, string hint)
    {
        var at = source.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) throw new InvalidOperationException(hint);
        var open = source.IndexOf('{', at);
        if (open < 0) throw new InvalidOperationException(hint);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[(open + 1)..i];
        }
        throw new InvalidOperationException(hint);
    }
}
