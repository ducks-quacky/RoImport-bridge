using System.Text.Json;

namespace RoImportBridge;

internal sealed class UploadLogStore
{
    private readonly string logPath;
    private readonly object syncRoot = new();
    private readonly List<UploadLogEntry> entries;

    public UploadLogStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoImportBridge");
        Directory.CreateDirectory(directory);
        logPath = Path.Combine(directory, "uploads.json");
        entries = LoadEntries();
    }

    public IReadOnlyList<UploadLogEntry> GetEntries()
    {
        lock (syncRoot)
        {
            return entries.OrderByDescending(entry => entry.UploadedAt).ToArray();
        }
    }

    public void Add(UploadLogEntry entry)
    {
        lock (syncRoot)
        {
            entries.Add(entry);
            SaveEntries();
        }
    }

    private List<UploadLogEntry> LoadEntries()
    {
        if (!File.Exists(logPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(logPath);
            return JsonSerializer.Deserialize<List<UploadLogEntry>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SaveEntries()
    {
        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(logPath, json);
    }
}
