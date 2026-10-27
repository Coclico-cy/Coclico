namespace Coclico.Security.Domain;

public static class SecurityLimits
{
    public const int SupportedFormatVersion = 1;

    public const int MaxManifestBytes = 2 * 1024 * 1024;

    public const int MaxManifestEntries = 5000;

    public const int MaxEntryPathChars = 255;

    public const int MaxJournalEntryBytes = 4096;

    public const long MaxJournalFileBytes = 4 * 1024 * 1024;

    public const int KeyFileIterations = 600000;

    public const int MinSigningPasswordLength = 128;

    public const int MaxSigningPasswordLength = 128;

    public const string KeyFileMagic = "CCLIK3";

    public const string SignatureAlgorithm = "RSA-PSS-SHA256";
}
