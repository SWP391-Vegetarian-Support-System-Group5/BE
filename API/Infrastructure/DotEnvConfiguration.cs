using Microsoft.Extensions.Configuration;

namespace API.Infrastructure;

public static class DotEnvConfiguration
{
    public static void AddFile(IConfigurationBuilder configuration, string path)
    {
        if (!File.Exists(path)) return;

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            var value = line.Trim();
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith('#')) continue;
            if (value.StartsWith("export ", StringComparison.OrdinalIgnoreCase)) value = value[7..].TrimStart();

            var separator = value.IndexOf('=');
            if (separator <= 0) continue;

            var key = value[..separator].Trim().Replace("__", ":");
            var setting = value[(separator + 1)..].Trim();
            if (setting.Length >= 2 && setting[0] == setting[^1] && (setting[0] == '\'' || setting[0] == '"')) setting = setting[1..^1];
            values[key] = setting;
        }

        configuration.AddInMemoryCollection(values);
    }
}
