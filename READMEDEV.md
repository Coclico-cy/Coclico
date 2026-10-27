<div align="center">

<img src="Coclico/Resources/icone/plage.png" alt="Coclico icon" width="72" />

# Coclico Developer Documentation

### Architecture, patterns, build pipeline and CI, explained module by module

[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2011-0078D4?logo=windows11&logoColor=white)](#1-tech-stack-and-projects)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE.txt)

[User guide (EN)](README.md) · [Guide utilisateur (FR)](README_FR.md) · **Developer (EN)** · [Développeur (FR)](READMEDEV_FR.md)

</div>

---

## Table of contents

1. [Tech stack and projects](#1-tech-stack-and-projects)
2. [Application startup](#2-application-startup)
3. [The CentralUnit](#3-the-centralunit)
4. [Code organization](#4-code-organization)
5. [The modules, one by one](#5-the-modules-one-by-one)
6. [Screenshot gallery](#6-screenshot-gallery)
7. [The AI assistant in detail](#7-the-ai-assistant-in-detail)
8. [Navigation and menu](#8-navigation-and-menu)
9. [Adding a module, step by step](#9-adding-a-module-step-by-step)
10. [Service patterns](#10-service-patterns)
11. [Localization and theming](#11-localization-and-theming)
12. [Settings and data](#12-settings-and-data)
13. [Tests](#13-tests)
14. [Build, format, CI and release](#14-build-format-ci-and-release)
15. [The installer pipeline](#15-the-installer-pipeline)
16. [Release integrity and signing](#16-release-integrity-and-signing)
17. [Security practices](#17-security-practices)
18. [Code scanning and P/Invoke rules](#18-code-scanning-and-pinvoke-rules)
19. [Publishing a release](#19-publishing-a-release)

---

## 1. Tech stack and projects

| Item | Detail |
| --- | --- |
| Target | `net10.0-windows10.0.22621.0`, WPF, `OutputType=WinExe` |
| Language | C# (`LangVersion latest`), `Nullable` and `ImplicitUsings` enabled |
| UI | [WPF-UI](https://github.com/lepoco/wpfui) 4.2.0 (Fluent controls: `FluentWindow`, `SymbolIcon`, `Snackbar`…) |
| MVVM | CommunityToolkit.Mvvm 8.4.0 (some ViewModels; many views stay code-behind) |
| DI | Microsoft.Extensions.DependencyInjection 10 + the in-house static `ServiceContainer` facade |
| Logging | Serilog (daily file in `%APPDATA%\Coclico\logs`, 14-day retention) |
| Local AI | LLamaSharp 0.26: CPU backend bundled; CUDA 12 backend only with `-p:IncludeCuda=true` |
| Misc | System.Management (WMI), System.Reactive, System.Text.Json, Microsoft.Extensions.Http / Caching.Memory |

Notable build settings in `Coclico/Coclico.csproj`:

- Tiered PGO + tiered compilation, `OptimizationPreference=Speed`, concurrent GC, no trimming.
- Portable PDBs in Release, kept on purpose: they power the application's error reports.
- `EnforceCodeStyleInBuild` + `AnalysisLevel=latest`: style violations fail the build.
- `SatelliteResourceLanguages=fr;en`: only the French and English satellite languages ship.
- `EnableWindowsTargeting`: CI can build the Windows target from a non-Windows agent.
- `InternalsVisibleTo("Coclico.Tests")`: the test project can reach `internal` members.

The solution (`Coclico.slnx`) contains **four projects**:

```text
Coclico/              WPF application (the main app)
Coclico.Security/     Release security: integrity manifests, signing tool, trust store
Coclico.InstallerAot/  Avalonia NativeAOT installer (see §15)
Coclico.Tests/        xunit tests (references Coclico through InternalsVisibleTo)
```

`Coclico.Security` (`OutputType=Exe`, `net10.0`, `IsAotCompatible=true`) hosts the security domain, the infrastructure and the signing tool (`Tool/Program.cs`: `keygen | sign | verify | check`). Both the application and the installer reference it; thanks to source-generated JSON (`SecurityJsonContext`), the installer compiles it cleanly in NativeAOT.

## 2. Application startup

`App.xaml.cs` (class `Coclico.App`), in order:

1. **Global error handling**: hooks on `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` and `DispatcherUnhandledException` → `LoggingService` + `IAppErrorManager`.
2. `ServiceContainer.Build(services => ...)`: builds the `IServiceProvider` (lazy, `ValidateOnBuild`). Serilog, the base services (`IAuditLog`, `ISecurityPolicy`, `IProcessExecutionService`, memory cache, `HttpClient`), then every module's services.
3. The application starts **as a standard user** (no UAC prompt at launch); elevation happens on demand. Check with `App.IsRunningAsAdministrator()`.
4. Settings load from `%APPDATA%\Coclico\settings.json`, then `LocalizationService.SetLanguage`, `ThemeService.ApplyCurrentSettings`, then `SplashWindow` → `MainWindow`.

```text
App.OnStartup
  ├── Global exception hooks (AppDomain / TaskScheduler / Dispatcher)
  ├── ServiceContainer.Build(...)          ← Serilog + base + module services
  ├── SettingsService: load settings.json
  ├── LocalizationService.SetLanguage(...)
  ├── ThemeService.ApplyCurrentSettings()
  ├── CentralUnitService.Current.Initialize()
  └── SplashWindow → MainWindow
```

`ServiceContainer` is a static facade: `ServiceContainer.GetRequired<T>()`, `GetOptional<T>()`. Views resolve their services this way.

## 3. The CentralUnit

`Common/Core/CentralUnit/` is the application's in-house kernel. A singleton, `CentralUnitService.Current`, orchestrates the app's stability. Nine building blocks:

| Block | Role |
| --- | --- |
| `CentralUnitService` | The orchestrator. Background loop, CPU/RAM policy, boost |
| `CentralUnitActivity` | Global activity level (startup, idle, active…) |
| `CentralUnitFlagStore` | Internal flag scheduler (`CentralFlags`, scheduled wakeups) |
| `CentralUnitVariables` | Shared variables bus (publish/subscribe) |
| `CentralUnitBus` | Inter-component event bus |
| `CentralUnitStigmergy` | Per-module usage scores (persisted statistics) |
| `CentralCircuitBreaker` | Circuit breaker: a subsystem failing 3 times opens for 30 s |
| `CentralErrorTriage` | Sorting and grouping of recurring errors |
| `CentralUnitWatchdog` | Watchdog of the background loop |

The heart: the **pressure loop**. It samples CPU and RAM every 20 s (45 s under pressure). If CPU crosses 85 % or RAM crosses 92 % for 3 samples, the app enters "external pressure" mode:

- memory priority and power throttling adjusted on its own processes;
- the AI model is unloaded after 30 s (`CentralFlags.AiModelUnload`);
- leaving pressure takes 2 samples below the thresholds (hysteresis: enter 85 % / exit 60 %).

Everything is guarded by a state `lock` and cancellable through a `CancellationTokenSource`. Every block has its unit tests (see §13).

## 4. Code organization

```text
Coclico/
  Common/
    Core/          SettingsService, CacheService, StartupService, ServiceContainer,
                   UpdateCheckService, UpdateManager, SimpleStemmer,
                   CentralUnit/ (9 files, see §3)
    Diagnostics/   LoggingService (Serilog), AppErrorManager, AuditLogService,
                   AsyncEventSafe (asynchronous event handlers that never crash the app)
    Execution/     IProcessExecutionService (process execution filtered by ISecurityPolicy),
                   ProcessExecutionService, ProcessWatcherService,
                   ResourceAllocatorService, ResourceGuardService
    UI/            ThemeService, LocalizationService, ToastService, DialogService,
                   NotificationService, KeyboardShortcutsService, TrayService.Win32
  Modules/         One folder per module (see §5)
  Resources/
    Lang/          fr.xaml + en.xaml (string dictionaries)
    docs/          coclico.md (the AI assistant's RAG knowledge base)
    icone/         application icons
  App.xaml         Global styles (brushes, CornerCard/CornerBtn, button styles)
  MainWindow.*     Main window: top bar, navigation drawer, panels
```

Anatomy of a module (`Modules/<Name>/`):

- `Services/`: business logic, kept **pure and testable** (no WPF dependency).
- `Views/`: `XxxView.xaml` + code-behind. Many modules add a `ViewModels/` folder.
- `Windows/`, `Models/`: as needed (modal windows, data models).

Repository root:

| Path | Role |
| --- | --- |
| `version.txt` | The single version number, read by the build and the installer script |
| `Directory.Build.props` | Shared build properties |
| `LICENSE.txt` / `SECURITY.md` / `CONTRIBUTORS.md` | License, security reporting, credits |
| `README.md` / `README_FR.md` | User guides (EN / FR) |
| `READMEDEV.md` / `READMEDEV_FR.md` | This developer documentation (EN / FR) |
| `docs/` | Screenshots used by the READMEs |
| `installer/build-installer.ps1` | The installer build script (§15) |
| `installer/Key/` | Local-only signing key (gitignored `key.keyfile` and remembered password); never committed |
| `Coclico/Security/trust-policy.json` | Anchored trusted signing keys and update policy (§16) |
| `.github/workflows/` | CI, release, repository automation (§14) |

## 5. The modules, one by one

`Modules/` contains twelve folders, one per feature. They all follow the shape of §4: logic in services, UI in views.

### Dashboard (`Modules/Dashboard`)

The customizable home page. `HomeCustomizationService` + `HomeCustomizationModel` remember which tiles are visible; `CustomShortcutsService` adds your own shortcuts; `HomeCustomizerWindow` is the customization dialog. Tiles go through the same public navigation path as the menu (§8). Three one-click modes (Zen, Gamer, Work) combine memory cleaning and temp file cleanup.

### AI assistant (`Modules/AI`)

The built-in, fully local assistant the chat panel opened with `Ctrl+Space`. Detailed in §7.

### PC Trash (`Modules/Cleaning`)

`CleaningService` (plus ViewModel and view) lists every target temporary files, caches… and shows what will be removed **before** anything happens. Deletions go to the **Recycle Bin**. The result reports the freed space and the skipped items. Nothing runs without an explicit confirmation.

### Repair & Diagnostics (`Modules/HealthDefense`)

- `ISecurityPolicy` / `SecurityPolicyService`: the central command policy. `IProcessExecutionService` consults it before running anything (§10). Forbidden commands are blocked in one single place, covered by `SecurityPolicyTests`.
- `SystemHealthService`: system status checks.
- `StartupHealthService`: reviews what runs at Windows startup.

### PC Diagnostics (`Modules/Diagnostics`)

The global PC diagnostics hub: it gathers and correlates hardware, system, network, storage and software information. **Every scan is read-only**, no system setting is ever changed, and each run is cancellable.

- `DiagnosticOrchestrator` (`IDiagnosticOrchestrator`): runs checks with limited concurrency (3 by default), a per-check timeout, per-check error isolation (one failure never fails the run), then merge, dedupe, severity/confidence sort and sanitization before persistence.
- Eleven `IDiagnosticCheck` implementations (one per domain): `WindowsHealthCheck`, `CpuCheck`, `GpuCheck`, `MemoryCheck`, `StorageCheck`, `NetworkCheck`, `PowerBatteryCheck`, `DeviceCheck`, `ProcessesCheck`, `SecurityCheck`, `MotherboardCheck`. **Quick** mode runs only cheap checks; **Full** adds the motherboard/BIOS check; **Targeted** runs only the selected categories. Checks reuse the existing services (`SystemHealthService`, `MemoryCleanerService`, `StartupHealthService`, `DriveInfo`, event logs, documented WMI/CIM) without duplicating their scans.
- `DiagnosticRuleEngine`: **deterministic, pure, versioned** rule engine (`RulesVersion`). Thresholds are explicit, documented constants; a missing value never produces a finding (neither "healthy" nor "failing"); severity and confidence are two separate axes; no opaque 0-100 global score.
- `DiagnosticActionCatalog`: **closed** catalog of typed actions (navigation to existing modules only). A finding never carries a free shell command; the local AI may explain results but cannot invent metrics, change severities or execute anything.
- `DiagnosticHistoryService` (`IDiagnosticHistoryService`): local JSON history under `%APPDATA%\Coclico\diagnostics\`, atomic writes, 30 runs / 30 days retention, resilient to corrupt files.
- `DiagnosticReportExporter`: versioned `.json` and human-readable `.txt` export, always through a user save dialog.
- `DiagnosticPrivacySanitizer`: masks profile paths, machine/user names and serial numbers before any persistence or export; sensitive metrics (MAC, serial) are masked entirely.
- `WmiHardwareTelemetryService` (`IHardwareTelemetryService`): MVP sensor provider (ACPI thermal zone through WMI). **LibreHardwareMonitorLib is not adopted** for now: the stable NuGet release is 0.9.6 (MPL-2.0) while visible 0.9.7 builds are frequent prereleases; a prerelease is not acceptable in a production package. The provider sits behind an interface for a documented later addition.
- `AppSettings`: `diagnosticEnableHardwareSensors`, `diagnosticEnableLiveMonitoring` (default false), `diagnosticHistoryRetentionCount/Days` (30/30), `diagnosticNetworkProbeTimeoutMs` (2000, bounded), `diagnosticMaxParallelChecks` (3), `diagnosticEnableExternalBandwidthTest` (default false).
- Navigation: tag `Diagnostics`, "PC Diagnostics" entry in the Maintenance group, AI action `open_diagnostics`.
- Tests in `Coclico.Tests/Modules/Diagnostics/`: rule engine (thresholds, missing data, sorting, dedupe, no free commands), orchestrator (fake checks: quick/full/targeted, error isolation, timeout, cancellation, progress), history (round-trip, retention, corrupt file), sanitizer, summary builder, and dynamic language-key validation (`DiagnosticResourceKeyTests`).

### Disk Analysis (`Modules/DiskAnalyzer`)

- `DiskAnalyzerService`: scans a drive, builds the folder tree, computes the zones breakdown and the largest files.
- `DuplicateFinderService`: the three-phase duplicate scan (size index → candidate groups → hash refinement) is fully static and unit-tested; NTFS hard links are deduplicated by file identity so a linked file is never reported twice.
- `RecycleBinHelper`: safe deletion through `SHFileOperation` with `FOF_ALLOWUNDO`.
- The view: segmented mode bar, drive cards with a used-space percentage, zones panel, duplicates list with per-file "open location" and delete-to-Recycle-Bin actions.

### RAM Management (`Modules/RamCleaner`)

- `MemoryCleanerService`: on-demand working-set cleanup, globally or per process.
- `SmartMemoryDaemonService` (+ `ISmartMemoryDaemonService`): the background daemon and its profiles. State protected by a `lock`, loop in a `Task.Run` with a cancellation token, `StatusChanged` events, settings persisted in `AppSettings`, clean stop when the view unloads.
- The view shows live RAM/CPU readouts and the top processes.

### Network Settings (`Modules/Network`)

- `NetworkMonitorService`: network adapters, live ping and throughput.
- `NetworkBenchmarkService` (`INetworkBenchmarkService`): before/after performance test.
- `NetworkOptimizerService` (`INetworkOptimizerService`): applies the settings. **Zero registry writes**: `netsh` for the TCP stack, official PowerShell (`Set-NetAdapterAdvancedProperty`, `Set-NetAdapterPowerManagement`) for the adapter. A missing setting is skipped and logged. `RestoreWindowsDefaultsAsync` puts back the real Windows defaults.
- `NetworkTuningCatalog`: the catalog of 9 Microsoft-documented parameters (5 TCP + 4 adapter), unit-tested.
- `NetworkSettingsGuard`: static validation of every value before application (autotuning levels, on/off states, forbidden characters in adapter names…).

### PC Power Management (`Modules/Power`)

- `PowerPlanCatalog`: plan definitions and the matching `powercfg` arguments (unit-tested).
- `CpuPowerService`: admin-gated wrapper around `powercfg`. Every setting is validated with `powercfg /q` before being applied; unsupported ones are skipped and counted. Coclico-created plans can always be deleted, back to the Balanced plan first. No direct registry writes.
- `CpuPowerBenchmark`: CPU load benchmark across the three plans (5 s each), restores your starting plan by its real GUID.
- `AdaptivePowerService`: the adaptive daemon it switches power plans based on CPU load, with thresholds, hysteresis, cooldown, and periodic re-sync with the actually active plan. The rule lives in the pure `AdaptivePowerRules`, tested without the daemon.

### Programs (`Modules/Programs`)

`InstalledProgramsService` enumerates installed programs (per-user and per-machine); `UninstallCommandParser` turns raw uninstall strings into a safe executable + arguments (unit-tested); the view lets you open, locate or uninstall each entry.

### Winget Installer (`Modules/Apps`)

- `AppCatalogService`: reads `Assets/apps.json` (70+ apps) and `Assets/apppacks.json` (17 packs). Consistency validated by `AppsCatalogTests`.
- `WingetEngineService`: Winget execution, automatic install of the Winget app bundle when missing.
- `GitHubToolService`: downloads from GitHub the software missing from Winget (DS4Windows, NVIDIA Profile Inspector…), unzips and locates the executable.
- `AppShortcutsService`: your software shortcuts on the home screen.

### Settings and Help (`Modules/Settings`, `Modules/Help`)

`SettingsView` (+ ViewModel), `ProfileService` (user profiles), `UserAccountService` (accounts and custom avatars), and the supporting windows: `SplashWindow`, `ProfileWindow` (with `AvatarCropWindow` to crop a custom avatar), `ConfirmationDialog`, `HelpView`.

## 6. Screenshot gallery

All screenshots live in `docs/`. They also illustrate the user guides.

<p align="center">
  <img src="docs/accueil.png" alt="Dashboard" width="400">
  <img src="docs/menu.png" alt="Menu" width="400">
  <br>
  <em>Dashboard (left) and navigation drawer (right) three collapsible groups.</em>
</p>

<p align="center">
  <img src="docs/Nettoyage-pc.png" alt="PC Trash" width="400">
  <img src="docs/santer-defence.png" alt="Repair & Diagnostics" width="400">
  <br>
  <em>PC Trash (left) and Repair & Diagnostics (right).</em>
</p>

<p align="center">
  <img src="docs/espace-disque.png" alt="Disk Analysis" width="400">
  <img src="docs/ram-clean.png" alt="RAM Management" width="400">
  <br>
  <em>Disk Analysis (left) and RAM Management (right).</em>
</p>

<p align="center">
  <img src="docs/Réglage réseau.png" alt="Network Settings" width="400">
  <img src="docs/Mangement-power.png" alt="PC Power Management" width="400">
  <br>
  <em>Network Settings (left) and PC Power Management (right) 9 documented network settings, 3 honest plans.</em>
</p>

<p align="center">
  <img src="docs/appplication.png" alt="Programs" width="400">
  <img src="docs/Installeur.png" alt="Winget Installer" width="400">
  <br>
  <em>Programs (left) and the Winget Installer catalog (right).</em>
</p>

<p align="center">
  <img src="docs/Pack.png" alt="Packs" width="400">
  <img src="docs/update.png" alt="Updates" width="400">
  <br>
  <em>Application packs (left) and software updates (right).</em>
</p>

<p align="center">
  <img src="docs/IA.png" alt="AI assistant" width="400">
  <img src="docs/Notification.png" alt="Notifications" width="400">
  <br>
  <em>Local AI assistant (left) and notifications panel (right).</em>
</p>

## 7. The AI assistant in detail

Everything runs **locally**. No cloud call, no API key anywhere in the code base. `AiLocalOnlyTests` guards this (no cloud provider may appear, Ollama only ever on loopback).

- **Providers.** `MultiProviderClient` (with `AiProviderContracts`) talks to the model backends: local GGUF files through LLamaSharp, or a local Ollama server. `AiHardwareService` detects the GPU/VRAM and decides the layer offload. Without the CUDA backend, LLamaSharp falls back to the bundled CPU backend automatically.
- **Chat.** `AiChatService` builds the compact system prompt, keeps the conversation memory with pruning, handles stop tokens. The assistant always knows where you are: navigation updates `IAiService.CurrentStatusContext` (§8).
- **Knowledge (RAG).** `RagService` runs BM25/TF-IDF retrieval over the bundled knowledge base (`Resources/docs/coclico.md`), which describes every module, action and safety rule. `SimpleStemmer` handles French and English.
- **Actions.** `AiActionParser` parses the model's structured output into actions (navigate to a module, clean RAM, tune the network…). `AiToolExecutionService` executes them with strict sanitization allowlisted navigation targets, validated package identifiers, numeric clamps. Nothing changes the system without explicit user confirmation. `AiToolExecutionServiceSanitizerTests` cover the sanitization.
- **Vision.** `AiVisionService` handles image understanding with the same local models.

## 8. Navigation and menu

- The main menu is the **hamburger drawer** in `MainWindow.xaml`. Home, then the collapsible groups **MAINTENANCE**, **PC SETTINGS** and **APPLICATIONS**, then Settings and Help, always visible. The expanded state is persisted in `AppSettings.MenuExpandedGroups`.
- Every card carries a `Tag` (the module id). The module registry lives in `MainWindow.Navigation.cs`:

```csharp
private static readonly (string Tag, Func<UIElement> Factory)[] ModuleRegistry =
[
    ("Home", static () => ServiceContainer.GetOptional<DashboardView>() ?? new DashboardView()),
    ("Programs", static () => new ProgramsView()),
];
```

- A click goes through `NavigateToTagAsync(tag)`: resolve or create the view (`_viewCache`), update the AI context, transition, active indicator.
- `TagForAiAction` maps AI actions (`open_dashboard`, `open_power`…) to those same tags. Dashboard tiles and AI actions go through this single public path. Never raise a `Click` event on a `RadioButton` manually.
- Asynchronous event handlers go through `AsyncEventSafe.Run(...)` (Common/Diagnostics): an exception inside an `async void` is caught and logged instead of crashing the app.
- Global shortcuts: `KeyboardShortcutsService`, plus `F11` (full screen) and `Ctrl+Space` (AI panel) handled in `MainWindow`.

## 9. Adding a module, step by step

Concrete example: the PC Power Management module (`Modules/Power`). Steps:

1. **Create the module**: `Modules/<Name>/Services/*.cs` (logic) and `Modules/<Name>/Views/<Name>View.xaml(.cs)`.
2. **Register the services** in `App.xaml.cs` (`services.AddSingleton<...>()`, or `AddTransient` for short-lived stateful services).
3. **Wire navigation** in `MainWindow.Navigation.cs`: an entry in `ModuleRegistry`, then the matching card in `MainWindow.xaml` (an element with `Tag="<Tag>"`, in the right group). Icons: only existing `Wpf.Ui.Controls.SymbolRegular` symbols.
4. **AI action**: a case in `TagForAiAction` if the assistant must be able to open the module.
5. **Language keys**: every key in `Resources/Lang/fr.xaml` **and** `en.xaml` (both dictionaries stay in sync).
6. **Tests**: pure logic in `Coclico.Tests`. For a simple view, an STA instantiation test avoids a `XamlParseException` at first click.
7. **Verify**: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` (new code must be clean).

## 10. Service patterns

**Pure, testable service.** Separate the algorithm from anything touching the system. Example `DuplicateFinderService`: the grouping steps (`IndexBySize`, `CandidateGroups`, `RefineByHash`) are static and testable; the full scan is just a `Task.Run` around them. Same approach in `PowerPlanCatalog` (data + powercfg argument building) and `AdaptivePowerRules.Evaluate` (pure rule, tested without the daemon).

**Process execution.**

- Standard path: `IProcessExecutionService.ExecuteAsync`, filtered by `ISecurityPolicy.IsCommandBlocked`, timeout, cancellation, configured encodings. `requiresElevation: true` sets `Verb = "runas"` → **one UAC prompt per call**.
- For an admin-gated tool run in series (e.g. `powercfg` in `CpuPowerService`), do **not** use per-command elevation: call the executable directly (the `ProcessStartInfo` pattern from `NetworkOptimizerService`) and **gate the UI** on `App.IsRunningAsAdministrator()`. The user elevates the app once; no UAC storm.

**Native interop.** Repository convention: classic `DllImport` (not `LibraryImport`, which would require `AllowUnsafeBlocks`). Examples: `GetSystemPowerStatus` (battery/AC, `CpuPowerService`), `GetSystemTimes` (CPU load, `AdaptivePowerService`), `SHFileOperation` (Recycle Bin, `RecycleBinHelper`). Always wrap in a small internal class with a failure-tolerant public method.

**Daemons.** Two references: `SmartMemoryDaemonService` (memory cleaning) and `AdaptivePowerService` (plan switching from CPU load). Common points: state protected by a `lock`, loop in a `Task.Run` with a `CancellationTokenSource`, `StatusChanged`-style events, persistence in `AppSettings`, clean stop on the views' `Unloaded`. Events raised on a background thread must be marshalled with `Dispatcher.CheckAccess()` / `Dispatcher.InvokeAsync`.

**System measurements.** Prefer Win32 APIs (`GetSystemTimes`) over performance counters: no dependency, no localization issue, trivial delta computation.

## 11. Localization and theming

- UI strings: `Resources/Lang/fr.xaml` and `en.xaml`, consumed as `{DynamicResource MyKey}`. `LocalizationService` swaps the merged dictionary on language change; `LocalizationService.Get(key)` is the code-behind fallback.
- **Absolute rule: every added key must exist in both files.**
- XAML resources: `XamlResourceValidationTests` statically verifies that every `{StaticResource}` (e.g. `CornerCard`, `CornerBtn`) and every merged dictionary resolves, otherwise the test build fails. Missing `{DynamicResource}` keys fail silently at load, hence the importance of checking keys.
- Global brushes in `App.xaml`: `BgCardBrush`, `BgSurfaceBrush`, `PrimaryBrush`, `SuccessBrush`, `DangerBrush`, `Text*Brush`… `ThemeService` applies accent and theme from settings.
- `FindResource` **throws** when absent; in code-behind, prefer `TryFindResource` or an `as Brush` cast with a fallback.

## 12. Settings and data

- `%APPDATA%\Coclico\settings.json` ↔ `AppSettings` (camelCase JSON through `[JsonPropertyName]`, defaults on the properties). Adding a property = declaring it with a default; loading, saving and gentle migration are handled by `SettingsService`.
- Other data: Serilog logs (`logs/`), audit trail (`IAuditLog`, configurable retention), notification history (`NotificationService`, 50 entries), user profiles (`ProfileService`).
- The CentralUnit persists its usage statistics through `CentralUnitStigmergy`.

## 13. Tests

`Coclico.Tests` (xunit): everything must pass before any merge. `InternalsVisibleTo("Coclico.Tests")` reaches `internal` members. The `Security/` folder holds the 61 release-security tests: keyfile encryption, integrity manifest building and RSA-PSS verification, trust policy, tamper-evident journal, path and archive guards, fuzzing. Current coverage:

| File | What it covers |
| --- | --- |
| `Common/Core/CentralCircuitBreakerTests.cs` | Circuit breaker: thresholds, states, cooldown |
| `Common/Core/CentralErrorTriageTests.cs` | Recurring error triage |
| `Common/Core/CentralUnitActivityTests.cs` | Activity levels |
| `Common/Core/CentralUnitBusTests.cs` | Event bus |
| `Common/Core/CentralUnitFlagStoreTests.cs` | Flag scheduler |
| `Common/Core/CentralUnitServiceTests.cs` | CPU/RAM pressure loop, boost |
| `Common/Core/CentralUnitStigmergyTests.cs` | Per-module usage scores |
| `Common/Core/CentralUnitVariablesTests.cs` | Shared variables |
| `Common/Core/CentralUnitWatchdogTests.cs` | Watchdog |
| `Common/Core/SettingsServiceTests.cs` | Settings loading / saving / migration |
| `Common/Core/UpdateVersionTests.cs` | Numeric version comparison, prereleases |
| `Common/Execution/ProcessExecutionServiceTests.cs` | Filtered process execution |
| `Modules/Ai/AiLocalOnlyTests.cs` | "Local-only AI" guards |
| `Modules/Ai/AiToolExecutionServiceSanitizerTests.cs` | AI action sanitization |
| `Modules/Ai/RagServiceTests.cs` | Knowledge base retrieval |
| `Modules/Apps/AppsCatalogTests.cs` | apps.json / apppacks.json catalogs |
| `Modules/Cleaning/CleaningServiceTests.cs` | Cleaning categories |
| `Modules/DiskAnalyzer/DiskAnalyzerTests.cs` | Tree scan, `FormatSize`, STA instantiation |
| `Modules/DiskAnalyzer/DuplicateFinderTests.cs` | 3-phase duplicates, thresholds, top-N |
| `Modules/HealthDefense/SecurityPolicyTests.cs` | Blocked commands |
| `Modules/Diagnostics/DiagnosticRuleEngineTests.cs` | Rules: thresholds, missing data, sorting, dedupe, no free commands |
| `Modules/Diagnostics/DiagnosticOrchestratorTests.cs` | Quick/full/targeted modes, error isolation, timeout, cancellation, progress |
| `Modules/Diagnostics/DiagnosticHistoryServiceTests.cs` | History: round-trip, retention, corrupt file, atomic write |
| `Modules/Diagnostics/DiagnosticPrivacySanitizerTests.cs` | Path, serial, machine name masking, sensitive metrics |
| `Modules/Diagnostics/DiagnosticSummaryBuilderTests.cs` | Honest counters, no global score |
| `Modules/Diagnostics/DiagnosticResourceKeyTests.cs` | Dynamic language keys present in fr.xaml and en.xaml |
| `Modules/Network/NetworkSettingsGuardTests.cs` | Network value validation |
| `Modules/Network/NetworkTuningCatalogTests.cs` | Catalog of the 9 settings |
| `Modules/Power/AdaptivePowerRulesTests.cs` | Adaptive rule: thresholds, hysteresis, cooldown |
| `Modules/Power/PowerPlanCatalogTests.cs` | Plan names, powercfg arguments, bounds |
| `Modules/Programs/UninstallCommandParserTests.cs` | Uninstall-string parsing |
| `Modules/RamCleaner/MemoryCleanerServiceTests.cs` | Memory purge profiles |
| `Modules/Settings/ProfileServiceTests.cs` | User profiles |
| `Ui/Xaml/XamlResourceValidationTests.cs` | `StaticResource` resolution, dictionaries, image paths |
| `Security/KeyfileProtectorTests.cs` | Keyfile encryption: KDF, AES-GCM round-trip, 128-character password rule |
| `Security/ManifestVerifierTests.cs` | Manifest building, canonicalization, RSA-PSS signature verification |
| `Security/UpdatePolicyTests.cs` | Trusted keys, activation/revocation, minimum versions and generations |
| `Security/SecurityJournalTests.cs` | Tamper-evident security journal |
| `Security/PathValidatorTests.cs` | Safe path validation |
| `Security/SecurityFuzzTests.cs` | Fuzzed inputs on manifest, journal and archive guards |

## 14. Build, format, CI and release

Local commands (identical to CI):

```powershell
dotnet restore Coclico.slnx
dotnet format Coclico.slnx --verify-no-changes --no-restore
dotnet build Coclico.slnx --configuration Release --no-restore
dotnet test Coclico.slnx --configuration Release --no-build --no-restore
```

> Format rule: all new code must pass `dotnet format --verify-no-changes`. If a local check fails, run `dotnet format Coclico.slnx` (without `--verify`) and commit the result.

### Zero-warning policy

All four projects (`Coclico`, `Coclico.Security`, `Coclico.Tests`, `Coclico.InstallerAot`) define `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, and `installer/build-installer.ps1` passes `-warnaserror` to every `dotnet publish`. Any warning therefore fails the build and the installer. For a full sweep (incremental builds do not re-emit existing warnings): `dotnet build Coclico.slnx -t:Rebuild -warnaserror`, in both Debug and Release.

**Documented exception — IL3053 in `Coclico.InstallerAot`**: this NativeAOT warning originates from `MicroCom.Runtime` (a third-party Avalonia dependency, COM interop that is not statically AOT-provable). Audit: the affected COM paths are not used at runtime by the installer; the warning is suppressed in a targeted way via `<NoWarn>IL3053</NoWarn>` in `Coclico.InstallerAot.csproj`. Everything else stays an error. The source code is intentionally comment-free; this rationale lives here, in the documentation.

**AOT guard**: `Coclico.Security` sets `<IsAotCompatible>true</IsAotCompatible>`. Because the installer compiles the security project NativeAOT, any reflection-based serialization or dynamic-code usage added to it fails the build immediately (IL2026/IL3050 become errors), on Windows and Linux alike.

Workflows in `.github/workflows/`:

- **`ci.yml`** on every push/PR to `main`: restore → format check → Release build → tests → on pushes to `main`, a win-x64 publish is uploaded as an artifact.
- **`release.yml`** triggered by a `vX.Y.Z` tag: tag ↔ `version.txt` consistency, restore, installer via `./installer/build-installer.ps1 -CI` (non-interactive, unsigned: no signing key exists on CI), portable ZIP + `SHA256SUMS.txt`, GitHub release.
- **`codeql.yml`** CodeQL security scanning.
- **`dependency-review.yml`** dependency review of package changes.
- **`scorecard.yml`** OpenSSF Scorecard.
- **`labeler.yml`, `greetings.yml`, `stale.yml`** repository automation.

## 15. The installer pipeline

What `installer/build-installer.ps1` does, step by step (interactive console, FR/EN):

1. **Version**: read from `version.txt` at the repository root (or passed with `-Version`).
2. **Tests first**: `dotnet test` runs before anything is packaged (skip with `-SkipTests`). The installer is never built on a red suite.
3. **Cleanup**: `publish/` and `artifacts/installer_build` are cleaned (skip with `-SkipClean`).
4. **Publish Coclico**: `dotnet publish` win-x64, **framework-dependent** (`--self-contained false`), no single file, no PDBs → `publish/`.
5. **Integrity signing** (§16): the script looks for `installer\Key\key.keyfile`. First run: it offers to generate an RSA-4096 signing key protected by a password of exactly 128 characters. Subsequent runs: the existing key is recognized, its password is requested once and can be remembered encrypted by Windows (DPAPI, current-user profile, gitignored `installer\Key\key.pwd.protected`) so later builds ask for nothing. With the key unlocked, every file of `publish/` is hashed into a signed `integrity.json`. Empty password or `-CI`: unsigned build, the rest of the pipeline is unchanged.
6. **Payload**: `publish/` is zipped into `payload.zip` inside `Coclico.InstallerAot/` (build artifact — never commit it, it is gitignored). Excluded: `.pdb` files and, unless `-IncludeCuda`, the CUDA runtime files. Without the CUDA DLLs, LLamaSharp cleanly uses the bundled CPU backend; NVIDIA users download the CUDA runtime during installation instead that is how the installer stays small. When signing is enabled, `integrity.json` travels inside the payload and is verified at install time.
7. **Final installer**: `Coclico.InstallerAot` is published as a native SingleFile with `payload.zip` embedded → `artifacts/installer/Coclico-<version>.exe` (`-Full-CUDA` variant), SHA-256 printed. `ValidateExecutableReferencesMatchSelfContained=false` lets the self-contained installer reference the `Coclico.Security` executable project.
8. `-SkipPublish` reuses the existing `publish/` folder. The release CI runs the full script with `-CI`: non-interactive and unsigned, since no signing key exists on CI.

At install time, the standalone `Coclico.Installer` project takes over (`Services/`):

- `SystemRequirementService` system requirements and the target drive derived from the chosen path.
- `GpuDetector` NVIDIA GPU detection (never throws).
- `DotnetRuntimeService` checks the .NET 10 Desktop Runtime; when missing, downloads the pinned Microsoft installer and runs it silently (`/install /passive /norestart /log`, with retries if Windows Installer is busy).
- `CudaRuntimeDownloader` + `AiModelDownloader` optional download of the CUDA runtime and of a local AI model (catalog in `ai-models.json`, resumable).
- `InstallService` installs for the current user (default `%LOCALAPPDATA%\Programs\Coclico`), creates shortcuts (`ShortcutHelper`), registers the uninstall entry, deploys the uninstaller, closes a running Coclico first, and preserves settings and data on uninstall.
- `InstallerLocalization` FR/EN installer strings.

## 16. Release integrity and signing

`Coclico.Security` (domain + infrastructure + signing tool) implements the end-to-end integrity of releases. Vulnerability reporting lives in `SECURITY.md`.

- **Signing key**: RSA 4096, generated into `installer\Key\key.keyfile`, never committed (gitignored together with every `.key`, `.pem` and the remembered password). The private key is sealed with AES-GCM under a key derived (HKDF-SHA256) from a password of exactly 128 characters; the public key lands in `installer\Key\key.pub`, and its keyId is the SHA-256 of the public key.
- **Trust anchor**: `Coclico/Security/trust-policy.json` is committed in the repository: trusted keys (keyId, base64 public key, `activeFromUtc`, optional `revokedUtc`) plus the update policy (minimum allowed version, last known good version, minimum security generation). The application verifies updates against it; the installer embeds it as a resource and verifies the payload before touching anything.
- **Integrity manifest**: `sign` hashes every payload file (SHA-256), canonicalizes the manifest and signs it (RSA-PSS-SHA256) into `integrity.json`. `verify` re-checks a payload against its manifest and the trust policy. The tool runs through `dotnet run --project Coclico.Security` (`keygen | sign | verify | check`).
- **Install time**: `PayloadGuard.VerifyStaging` verifies the extracted payload (manifest presence and format, per-file hash, signature, trusted keyId) against the embedded trust policy before `InstallService` copies anything. `ArchiveGuard` bounds entry count, entry size and total size, and blocks path traversal.
- **Runtime**: downloaded updates are verified twice (at download and again before launch) by `ReleaseAuthentication` and `IntegrityRuntimeService`; `NativeLibraryGuard` blocks native DLL planting.
- **AOT-safe by construction**: `IsAotCompatible=true` and source-generated JSON (`SecurityJsonContext`) keep the project NativeAOT-compatible; any reflection-based serialization added later fails the build immediately. Manifest JSON options are strict and fixed: depth limit 8, no comments, no trailing commas, indented writes, case-insensitive reads.

## 17. Security practices

- **Confirmations** before any destructive action; **Recycle Bin** (`SHFileOperation` + `FOF_ALLOWUNDO`) instead of permanent deletion for user files.
- **Network**: no registry writes `netsh` and PowerShell only; every value validated by `NetworkSettingsGuard`; one-click restore of the real Windows defaults.
- **Power**: only `powercfg`, every setting validated with `powercfg /q`, Coclico plans deletable at any time, back to the Balanced plan first.
- **Command filtering**: go through `IProcessExecutionService` whenever possible (`ISecurityPolicy` blocks forbidden commands, tested).
- **Local-only AI**: no cloud key vault; Ollama restricted to loopback; guards tested; actions sanitized and confirmed.
- **Updates**: HTTPS and GitHub domains only, mandatory SHA-256 verified at download **and** again before launch, file version checked without executing; releases ship `SHA256SUMS.txt`; payloads are additionally covered by a signed integrity manifest verified against the anchored trust policy (§16).
- **On-demand elevation**: the app starts as a standard user; affected modules show their state.
- **Logs**: never contain personal data; `LoggingService.LogException` in every `catch`; async errors routed by `AsyncEventSafe` and triaged by the CentralUnit.

## 18. Code scanning and P/Invoke rules

Coclico intentionally relies on Windows P/Invoke calls (memory trimming, GPU detection, recycle bin, working sets). The CodeQL rules `cs/call-to-unmanaged-code` and `cs/unmanaged-code` flag every such call by design, so they are excluded via `query-filters` in `.github/codeql/codeql-config.yml`. All other CodeQL rules are analyzed and must stay at zero alerts.

## 19. Publishing a release

1. Update `version.txt` at the repository root the single source of the version, picked up by the build and the installer script.
2. Adjust the version shown in the status bar of `MainWindow.xaml` if needed.
3. Merge to `main` (CI runs: format, build, tests).
4. Create the tag: `git tag vX.Y.Z && git push origin vX.Y.Z`. The release (installer + portable ZIP + SHA256SUMS.txt) is generated automatically, provided the tag matches the version. The CI-built installer is unsigned (no signing key on CI); a signed installer is built locally with `./installer/build-installer.ps1` once the key is anchored in `Coclico/Security/trust-policy.json`.
5. Check the GitHub release: the three expected files, and the SHA-256 printed by the local installer script matches `SHA256SUMS.txt`.

---

<p align="center"><i>Back to: <a href="README.md">user guide (EN)</a> · <a href="README_FR.md">guide utilisateur (FR)</a> · <a href="READMEDEV_FR.md">documentation développeur (FR)</a></i></p>
