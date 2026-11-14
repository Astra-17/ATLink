using System.Buffers.Binary;

namespace ATLink.Core;

public static class FifaModCacheSelection
{
    public static byte[] ReadBuild(string path)
    {
        using var input = File.OpenRead(path);
        Span<byte> header = stackalloc byte[12];
        input.ReadExactly(header);
        if (!header[..5].SequenceEqual("FETC\u0004"u8))
            throw new InvalidDataException(ATLink.Core.UiText.Get("Select an FC27 cache in FETC version 4 format."));
        return header[5..].ToArray();
    }

    public static string PreferLatestInstalled(string savedPath)
    {
        if (string.IsNullOrWhiteSpace(savedPath)) return savedPath;
        string full;
        try { full = Path.GetFullPath(savedPath); }
        catch (ArgumentException) { return savedPath; }
        catch (NotSupportedException) { return savedPath; }
        var folder = new DirectoryInfo(Path.GetDirectoryName(full)!);
        if (!folder.Name.StartsWith("FIFA Editor Tool v", StringComparison.OrdinalIgnoreCase) || folder.Parent is null)
            return savedPath;
        string best = savedPath;
        uint bestBuild = TryBuild(savedPath);
        try
        {
            foreach (var sibling in folder.Parent.EnumerateDirectories("FIFA Editor Tool v*"))
            {
                string candidate = Path.Combine(sibling.FullName, "fc27.cache");
                uint build = TryBuild(candidate);
                if (build > bestBuild)
                {
                    best = candidate;
                    bestBuild = build;
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return best;
    }

    static uint TryBuild(string path)
    {
        try { return BinaryPrimitives.ReadUInt32LittleEndian(ReadBuild(path).AsSpan(3)); }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
        catch (InvalidDataException) { return 0; }
    }
}
