namespace Coclico.Security.Domain;

public enum SecurityIncident
{
    None,
    ManifestMissing,
    ManifestTooLarge,
    ManifestMalformed,
    FormatVersionUnsupported,
    ProductMismatch,
    KeyUnknown,
    KeyRevoked,
    SignatureInvalid,
    PathRejected,
    DuplicateEntry,
    FileModified,
    FileMissing,
    FileAdded,
    AccessError,
    PolicyRejected,
    JournalCompromised
}
