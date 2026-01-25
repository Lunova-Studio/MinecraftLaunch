namespace MinecraftLaunch.Extensions;

public static class StringExtension
{
    public static IEnumerable<string> GroupArguments(this IEnumerable<string> parameters)
    {
        List<string> group = [];
        Queue<string> queue = new(parameters);

        while (queue.Count > 0)
        {
            var next = queue.Dequeue();

            if (group.Count == 0)
            {
                group.Add(next);
            }
            else
            {
                if (group.First().StartsWith('-') && next.StartsWith('-'))
                {
                    yield return string.Join(group.First().EndsWith('=') ? "" : " ", group);
                    group.Clear();
                }

                group.Add(next);
            }
        }

        if (group.Count > 0)
        {
            yield return string.Join(group.First().EndsWith('=') ? "" : " ", group);
        }
    }

    public static string ReplaceFromDictionary(this string text, Dictionary<string, string> keyValuePairs)
    {
        string replacedText = text;

        foreach (var item in keyValuePairs)
            replacedText = replacedText.Replace(item.Key, item.Value);

        return replacedText;
    }

    public static IEnumerable<string> FormatLibraryName(this string Name)
    {
        var extension = Name.Contains('@') ? Name.Split('@') : Array.Empty<string>();
        var subString = extension.Any()
            ? Name.Replace($"@{extension[1]}", string.Empty).Split(':')
            : Name.Split(':');

        foreach (string item in subString[0].Split('.'))
            yield return item;

        yield return subString[1];
        yield return subString[2];

        if (!extension.Any())
            yield return $"{subString[1]}-{subString[2]}{(subString.Length > 3 ? $"-{subString[3]}" : string.Empty)}.jar";
        else yield return $"{subString[1]}-{subString[2]}{(subString.Length > 3 ? $"-{subString[3]}" : string.Empty)}.jar".Replace("jar", extension[1]);
    }

    public static string FormatLibraryNameToRelativePath(this string name)
    {
        string path = string.Empty;

        foreach (var subPath in name.FormatLibraryName())
            path = Path.Combine(path, subPath);

        return path;
    }
}