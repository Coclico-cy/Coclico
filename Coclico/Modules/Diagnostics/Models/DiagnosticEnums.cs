namespace Coclico.Modules.Diagnostics.Models;

public enum DiagnosticMode
{
    Quick,
    Full,
    Targeted
}

public enum DiagnosticRunStatus
{
    Running,
    Completed,
    Partial,
    Cancelled,
    Failed
}

public enum DiagnosticSeverity
{
    Success = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

public enum DiagnosticConfidence
{
    Low = 0,
    Medium = 1,
    High = 2
}

public enum DiagnosticCheckStatus
{
    Passed,
    Findings,
    Skipped,
    Unsupported,
    TimedOut,
    Failed,
    Cancelled
}

public enum DiagnosticCategory
{
    Windows,
    Cpu,
    Gpu,
    Memory,
    Storage,
    Network,
    Power,
    Devices,
    Processes,
    Security,
    Motherboard
}

public enum DiagnosticValueState
{
    Measured,

    NotExposed,

    AccessDenied,

    SourceError,

    Stale,

    NotApplicable
}
