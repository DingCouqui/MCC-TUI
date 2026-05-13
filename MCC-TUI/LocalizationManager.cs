using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MccTui;

public static class LocalizationManager
{
    private static readonly Dictionary<string, string> _strings = new();
    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public static string CurrentLanguage { get; private set; } = "zh_cn";

    public static void Initialize()
    {
        var exeDir = GetExeDirectory();

        var configPath = Path.Combine(exeDir, "MCC-TUI.yml");
        if (File.Exists(configPath))
        {
            try
            {
                var yaml = File.ReadAllText(configPath);
                var config = _deserializer.Deserialize<ConfigData>(yaml);
                if (config?.Language is "zh_cn" or "en_us")
                    CurrentLanguage = config.Language;
            }
            catch
            {
                CurrentLanguage = "zh_cn";
            }
        }

        var langPath = Path.Combine(exeDir, "lang", $"{CurrentLanguage}.yml");
        if (File.Exists(langPath))
        {
            try
            {
                var yaml = File.ReadAllText(langPath);
                var dict = _deserializer.Deserialize<Dictionary<string, string>>(yaml);
                if (dict != null)
                {
                    _strings.Clear();
                    foreach (var kv in dict)
                        _strings[kv.Key] = kv.Value;
                }
            }
            catch
            {
                // keep defaults
            }
        }
    }

    public static string Get(string key) =>
        _strings.TryGetValue(key, out var value) ? value : key;

    private static string GetExeDirectory()
    {
        var path = Environment.ProcessPath;
        if (path != null)
            return Path.GetDirectoryName(path)!;
        return AppContext.BaseDirectory;
    }

    private class ConfigData
    {
        public string? Language { get; set; }
    }
}
