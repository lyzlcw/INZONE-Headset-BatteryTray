using System.Text.Json;

namespace MixTray;

public class Config
{
    public int SchemeIndex { get; set; } = 0;
    public int RefreshIntervalIndex { get; set; } = 2; // 0=5s, 1=15s, 2=25s, 3=50s

    private static readonly string ConfigPath =
        Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "mix_config.json");

    public static int[] IntervalValues = [5000, 15000, 25000, 50000];
    public static string[] IntervalLabels = ["5 秒", "15 秒", "25 秒", "50 秒"];

    public static Config Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<Config>(json) ?? new Config();
            }
        }
        catch { }
        return new Config();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this);
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
}
