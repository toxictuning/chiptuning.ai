using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChiptuningAi.Dashboard.Services;

public sealed class TraceFile
{
    public string Version { get; set; } = "1.0";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string OriginalFilePath { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public List<TraceParent> Parents { get; set; } = [];
    public List<TraceSelectedPatch> SelectedPatches { get; set; } = [];
    public bool BypassIntegrity { get; set; }
    public string? BypassReason { get; set; }

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string GetTracePath(string filePath)
        => Path.Combine(
            Path.GetDirectoryName(filePath) ?? "",
            Path.GetFileNameWithoutExtension(filePath) + ".ctatrace");

    public void Save(string filePath)
        => File.WriteAllText(GetTracePath(filePath), JsonSerializer.Serialize(this, _opts));

    public static TraceFile? Load(string tracePath)
    {
        if (!File.Exists(tracePath)) return null;
        try { return JsonSerializer.Deserialize<TraceFile>(File.ReadAllText(tracePath), _opts); }
        catch { return null; }
    }
}

public sealed class TraceParent
{
    public Guid ParentFileId { get; set; }
    public string FileName { get; set; } = "";
    public double JaccardScore { get; set; }
    public string MatchPercentage { get; set; } = "";
}

public sealed class TraceSelectedPatch
{
    public Guid PatchId { get; set; }
    public Guid ParentFileId { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public double ParentJaccardScore { get; set; }
}
