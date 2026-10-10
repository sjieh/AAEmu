using System.Linq;
using System.Text.RegularExpressions;
using AAEmu.Commons.Utils.AAPak;

namespace AAEmu.Game.IO;

public class ClientSource
{
    public ClientSourceType SourceType { get; set; }
    public string PathName { get; set; } // Directory Name or game_pak File Name
    public AAPak GamePak { get; set; }
    private string[] _gamePakFileNames = [];

    public bool Open()
    {
        switch (SourceType)
        {
            case ClientSourceType.Directory when Directory.Exists(PathName):
                return true;
            case ClientSourceType.GamePak:
                GamePak = new AAPak(PathName);
                // GamePak.GenerateFolderList();
                _gamePakFileNames = GamePak.isOpen ? GamePak.pakFiles.Keys.ToArray() : [];
                return GamePak.isOpen;
            default:
                return false;
        }
    }

    public void Close()
    {
        if (SourceType == ClientSourceType.GamePak && GamePak != null)
        {
            GamePak.ClosePak();
            _gamePakFileNames = [];
        }
    }

    /// <summary>
    /// Checks if target file exists for this source
    /// </summary>
    /// <param name="fileName">filename to check</param>
    /// <returns></returns>
    public bool FileExists(string fileName)
    {
        switch (SourceType)
        {
            case ClientSourceType.Directory:
                {
                    var fn = Path.Combine(PathName, fileName);
                    return File.Exists(fn);
                }
            case ClientSourceType.GamePak:
                return GamePak.FileExists(fileName);
            default:
                return false;
        }
    }

    public static string WildcardToRegex(string pattern)
    {
        return "^" + Regex.Escape(pattern).
            Replace("\\*", ".*").
            Replace("\\?", ".") + "$";
    }

    /// <summary>
    /// Create a list of filenames found in target directory
    /// </summary>
    /// <param name="directory">Directory to search in</param>
    /// <param name="searchPattern">search pattern to use</param>
    /// <param name="includeSubDirectories"></param>
    /// <returns>List of filenames</returns>
    public List<string> GetFilesInDirectory(string directory, string searchPattern, bool includeSubDirectories)
    {
        List<string> list = [];
        switch (SourceType)
        {
            case ClientSourceType.Directory:
                {
                    var rootDir = directory.Replace('/', Path.DirectorySeparatorChar);
                    if (Directory.Exists(Path.Combine(PathName, rootDir)))
                    {
                        var files = Directory.GetFiles(Path.Combine(PathName, rootDir), searchPattern,
                            includeSubDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                        list.AddRange(files);
                    }
                    break;
                }
            case ClientSourceType.GamePak:
                {
                    var rootDir = directory
                        .Replace('/', Path.DirectorySeparatorChar)
                        .Replace(Path.DirectorySeparatorChar, '/');
                    var hasSearchPattern = !string.IsNullOrWhiteSpace(searchPattern);
                    var hasWildcard = hasSearchPattern && searchPattern.IndexOfAny(['*', '?']) >= 0;
                    if (includeSubDirectories)
                    {
                        var wildCardRegex = hasWildcard
                            ? new Regex(
                                WildcardToRegex("*" + searchPattern + "*"),
                                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                            : null;

                        // These are immutable paths in a read-only client pak. Reuse the snapshot
                        // captured by Open() so every directory query avoids another full key copy.
                        // Ordinal comparisons also avoid culture-sensitive path matching.
                        var fileNames = _gamePakFileNames.Length == 0
                            ? GamePak.pakFiles.Keys.ToArray()
                            : _gamePakFileNames;
                        foreach (var pfiName in fileNames)
                        {
                            if (pfiName.StartsWith(rootDir, System.StringComparison.OrdinalIgnoreCase))
                            {
                                if (!hasSearchPattern ||
                                    (hasWildcard
                                        ? wildCardRegex!.IsMatch(pfiName)
                                        : pfiName.Contains(searchPattern, System.StringComparison.OrdinalIgnoreCase)))
                                    list.Add(pfiName);
                            }
                        }
                    }
                    else
                    {
                        var wildCardPattern = WildcardToRegex("*" + searchPattern.ToLower() + "*");
                        var files = GamePak.GetFilesInDirectory(rootDir);
                        foreach (var pfi in files)
                            if (!hasSearchPattern || Regex.Match(pfi.name, wildCardPattern).Success)
                                list.Add(pfi.name);
                    }
                    break;
                }
        }

        return list;
    }

    /// <summary>
    /// Grabs the target fileName as a Stream
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public Stream GetFileStream(string fileName)
    {
        switch (SourceType)
        {
            case ClientSourceType.Directory:
                {
                    var fn = Path.Combine(PathName, fileName);
                    var fStream = new FileStream(fn, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return fStream;
                }
            case ClientSourceType.GamePak:
                return GamePak.ExportFileAsStreamCloned(fileName);
            default:
                return null;
        }
    }

    public string GetFileAsString(string fileName)
    {
        try
        {
            using (var stream = GetFileStream(fileName))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Returns a string that can be compared to against
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public string GetBaseName(string fileName)
    {
        if (SourceType == ClientSourceType.GamePak)
        {
            return fileName.Replace('/', Path.DirectorySeparatorChar);
        }

        if (SourceType == ClientSourceType.Directory)
        {
            if (fileName.StartsWith(PathName))
            {
                var res = fileName.Substring(PathName.Length);
                res = res.TrimStart(Path.DirectorySeparatorChar);
                return res;
            }
        }

        // Failed ?
        return fileName;
    }
}
