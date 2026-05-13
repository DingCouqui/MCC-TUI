using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MccTui;

public static class LocalizationManager
{
    private const string DefaultConfigYml = "language: zh_cn\n";

    private const string DefaultZhCnYml =
@"title: ""MCC TUI 启动器""
manage_label: ""[管理]""
add_button: ""[+]""
file_list_title: ""配置文件列表""
error_config_not_found: ""未找到 config 目录:""
error_no_ini_files: ""config 目录中没有 .ini 文件""
error_launch_failed: ""无法启动 MCC:""
ok: ""确定""
hint: ""提示""
error: ""错误""
";

    private const string DefaultEnUsYml =
@"title: ""MCC TUI Launcher""
manage_label: ""[Manage]""
add_button: ""[+]""
file_list_title: ""Config File List""
error_config_not_found: ""Config directory not found:""
error_no_ini_files: ""No .ini files in config directory""
error_launch_failed: ""Failed to launch MCC:""
ok: ""OK""
hint: ""Hint""
error: ""Error""
";

    private static readonly Dictionary<string, string> _strings = new();
    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public static string CurrentLanguage { get; private set; } = "zh_cn";

    public static void Initialize()
    {
        var exeDir = GetExeDirectory();

        EnsureDirectoriesAndFiles(exeDir);

        var configPath = Path.Combine(exeDir, "MCC-TUI-config", "MCC-TUI.yml");
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

        var langPath = Path.Combine(exeDir, "MCC-TUI-config", "lang", $"{CurrentLanguage}.yml");
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

    private static void EnsureDirectoriesAndFiles(string exeDir)
    {
        var configDir = Path.Combine(exeDir, "MCC-TUI-config");
        var langDir = Path.Combine(configDir, "lang");
        var mccConfigDir = Path.Combine(exeDir, "config");

        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(langDir);
        Directory.CreateDirectory(mccConfigDir);

        var configPath = Path.Combine(configDir, "MCC-TUI.yml");
        if (!File.Exists(configPath))
            File.WriteAllText(configPath, DefaultConfigYml, Encoding.UTF8);

        var zhPath = Path.Combine(langDir, "zh_cn.yml");
        if (!File.Exists(zhPath))
            File.WriteAllText(zhPath, DefaultZhCnYml, Encoding.UTF8);

        var enPath = Path.Combine(langDir, "en_us.yml");
        if (!File.Exists(enPath))
            File.WriteAllText(enPath, DefaultEnUsYml, Encoding.UTF8);
    }

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
