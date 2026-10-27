namespace Coclico.Security.Domain;

public sealed class VerificationResult
{
    public IntegrityStatus Status { get; init; } = IntegrityStatus.Unknown;

    public SecurityIncident Incident { get; init; } = SecurityIncident.None;

    public string Reason { get; init; } = string.Empty;

    public DateTime CheckedAtUtc { get; init; } = DateTime.UtcNow;

    public int CheckedFiles { get; init; }

    public IReadOnlyList<string> ModifiedFiles { get; init; } = [];

    public IReadOnlyList<string> MissingFiles { get; init; } = [];

    public IReadOnlyList<string> AddedFiles { get; init; } = [];

    public static VerificationResult Valid(int checkedFiles, string reason = "")
    {
        return new VerificationResult
        {
            Status = IntegrityStatus.Valid,
            Incident = SecurityIncident.None,
            Reason = reason,
            CheckedFiles = checkedFiles
        };
    }

    public static VerificationResult Unknown(SecurityIncident incident, string reason, int checkedFiles = 0)
    {
        return new VerificationResult
        {
            Status = IntegrityStatus.Unknown,
            Incident = incident,
            Reason = reason,
            CheckedFiles = checkedFiles
        };
    }

    public static VerificationResult Altered(SecurityIncident incident, string reason,
        int checkedFiles,
        IReadOnlyList<string>? modified = null,
        IReadOnlyList<string>? missing = null,
        IReadOnlyList<string>? added = null)
    {
        return new VerificationResult
        {
            Status = IntegrityStatus.Altered,
            Incident = incident,
            Reason = reason,
            CheckedFiles = checkedFiles,
            ModifiedFiles = modified ?? [],
            MissingFiles = missing ?? [],
            AddedFiles = added ?? []
        };
    }

    public static VerificationResult RecoveryRequired(SecurityIncident incident, string reason)
    {
        return new VerificationResult
        {
            Status = IntegrityStatus.RecoveryRequired,
            Incident = incident,
            Reason = reason
        };
    }

    public string Describe()
    {
        string counts = $"checked={CheckedFiles} modified={ModifiedFiles.Count} missing={MissingFiles.Count} added={AddedFiles.Count}";
        return string.IsNullOrWhiteSpace(Reason)
            ? $"{Status} {Incident} {counts}"
            : $"{Status} {Incident} {counts} {Reason}";
    }
}
