using System.Text;

namespace SowBroadcast.Core.Export;

/// <summary>
/// Writes overlay files. Files are only rewritten when their content changes, and every write goes
/// through a temp file + move so an overlay never reads a half-written file.
/// </summary>
public static class FileWriter
{
    public static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Returns true when the file was written.</summary>
    public static bool WriteTextIfChanged(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            try
            {
                if (File.ReadAllText(path, Utf8NoBom) == content) return false;
            }
            catch (IOException) { /* fall through and rewrite */ }
        }
        WriteBytesAtomic(path, Utf8NoBom.GetBytes(content));
        return true;
    }

    public static bool WriteBytesIfChanged(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            try
            {
                if (File.ReadAllBytes(path).AsSpan().SequenceEqual(content)) return false;
            }
            catch (IOException) { /* fall through and rewrite */ }
        }
        WriteBytesAtomic(path, content);
        return true;
    }

    public static bool DeleteIfExists(string path)
    {
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private static void WriteBytesAtomic(string path, byte[] content)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, content);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Legacy text files use Windows line endings (the Python app wrote them in text mode on Windows).</summary>
    public static string Crlf(string text) => text.Replace("\r\n", "\n").Replace("\n", "\r\n");
}
