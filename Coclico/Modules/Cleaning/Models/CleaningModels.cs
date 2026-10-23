namespace Coclico.Modules.Cleaning.Models;

public sealed class CleaningCategory
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "🗑️";
    public string Description { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public int PresetLevel { get; set; } = 2;

    public long BytesFound { get; set; }
    public int FileCount { get; set; }
    public long BytesFreed { get; set; }
    public int FilesDeleted { get; set; }

    public bool IsSelected { get; set; } = true;

    public bool IsSizeMeasurable { get; set; } = true;
}

public sealed class CleaningResult
{
    public long TotalBytesFreed { get; set; }
    public int FilesDeleted { get; set; }
    public int DirectoriesCleaned { get; set; }
    public List<CleaningCategory> Categories { get; set; } = [];
    public TimeSpan ElapsedTime { get; set; }
    public List<string> Errors { get; set; } = [];
}
