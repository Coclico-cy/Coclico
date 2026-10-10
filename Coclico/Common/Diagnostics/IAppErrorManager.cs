namespace Coclico.Services;

public interface IAppErrorManager
{
    event EventHandler<AppBlockError>? BlockError;

    event EventHandler<string>? BlockRecovered;

    event EventHandler<AppBlockError>? FatalError;

    void Report(AppErrorCode code, Exception? ex = null, string? detail = null);

    Task ReportAsync(AppErrorCode code, Exception? ex = null, string? detail = null);

    bool IsBlockDegraded(AppBlock block);

    void MarkRecovered(AppBlock block);

    IReadOnlyDictionary<AppBlock, BlockStatus> GetBlockStatuses();
}

public enum AppBlock
{
    AI,
    AIRag,
    Cleaning,
    HealthDefense,
    RamCleaner,
    Power,
    DiskAnalyzer,
    Dashboard,
    Apps,
    Installer,
    Security,
    Settings,
    UI,
    System,
    Network,
    Updates,
}

public enum AppErrorCode
{
    AI_ModelNotFound = 10001,
    AI_ModelLoadFailed = 10002,
    AI_InferenceFailed = 10003,
    AI_DownloadFailed = 10004,
    AI_ContextDisposed = 10005,
    AI_EngineStartFailed = 10006,

    RAG_IndexFailed = 11001,
    RAG_SearchFailed = 11002,

    CLN_ScanFailed = 20001,
    CLN_DeleteFailed = 20002,
    CLN_PermissionDenied = 20003,
    CLN_PathInvalid = 20004,

    HLT_ScanFailed = 30001,
    HLT_RepairFailed = 30002,
    HLT_DefenderError = 30003,
    HLT_ComponentStoreError = 30004,

    RAM_CleanFailed = 40001,
    RAM_PrivilegeRequired = 40002,
    RAM_MetricsUnavailable = 40003,

    APP_ScanFailed = 50001,
    APP_LaunchFailed = 50002,
    APP_UninstallFailed = 50003,
    APP_IconLoadFailed = 50004,

    INS_WingetNotFound = 60001,
    INS_InstallFailed = 60002,
    INS_UpgradeFailed = 60003,

    SEC_PolicyLoadFailed = 70001,
    SEC_CommandBlocked = 70002,
    SEC_PathTraversalBlocked = 70003,

    PWR_PlanApplyFailed = 80001,
    PWR_BenchmarkFailed = 80002,
    PWR_AdaptiveToggleFailed = 80003,

    CFG_LoadFailed = 90001,
    CFG_SaveFailed = 90002,
    CFG_RegistryError = 90003,

    UI_NavigationFailed = 100001,
    UI_RenderFailed = 100002,
    UI_ThemeApplyFailed = 100003,

    SYS_ContainerBuildFailed = 110001,
    SYS_StartupFailed = 110002,
    SYS_ShutdownError = 110003,

    NET_MonitorFailed = 120001,
    NET_PingFailed = 120002,

    UPD_CheckFailed = 130001,
    UPD_DownloadFailed = 130002,
    UPD_VerificationFailed = 130003,

    DSK_ScanFailed = 140001,
    DSK_DuplicateScanFailed = 140002,

    DSB_WidgetRefreshFailed = 150001,
    DSB_ShortcutFailed = 150002,
}

public enum BlockStatus { Healthy, Degraded, Failed }

public sealed record AppBlockError(
    AppBlock Block,
    AppErrorCode Code,
    string Message,
    Exception? Exception,
    DateTime Timestamp,
    int FailureCount
);