<div align="center">

<img src="Coclico/Resources/icone/plage.png" alt="Coclico icon" width="72" />

# Coclico Developer Documentation

### Architecture, patterns, build pipeline and CI, explained module by module

[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%2011-0078D4?logo=windows11&logoColor=white)](#requirements)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE.txt)

[User guide (EN)](README.md) · [Guide utilisateur (FR)](README_FR.md) · **Developer (EN)** · [Développeur (FR)](READMEDEV_FR.md)

</div>

---

## Table of contents

1. [Tech stack and projects](#1-tech-stack-and-projects)
2. [Application startup](#2-application-startup)
3. [Code organization](#3-code-organization)
4. [The modules, one by one](#4-the-modules-one-by-one)
5. [The AI assistant in detail](#5-the-ai-assistant-in-detail)
6. [Navigation and menu](#6-navigation-and-menu)
7. [Adding a module, step by step](#7-adding-a-module-step-by-step)
8. [Service patterns](#8-service-patterns)
9. [Localization and theming](#9-localization-and-theming)
10. [Settings and data](#10-settings-and-data)
11. [Tests](#11-tests)
12. [Build, format, CI and release](#12-build-format-ci-and-release)
13. [The installer pipeline](#13-the-installer-pipeline)
14. [Security practices](#14-security-practices)
15. [Publishing a release](#15-publishing-a-release)

---

## 1. Tech stack and projects

| Item | Detail |
| --- | --- |
| Target | `net10.0-windows10.0.22621.0`, WPF, `OutputType=WinExe` |
| Language | C# (`LangVersion latest`), `Nullable` and `ImplicitUsings` enabled |
| UI | [WPF-UI](https://github.com/lepoco/wpfui) 4.2.0 (Fluent controls: `FluentWindow`, `SymbolIcon`, `Snackbar`…) |
| MVVM | CommunityToolkit.Mvvm 8.4.0 (used for some ViewModels; many views stay code-behind) |
| DI / hosting | Microsoft.Extensions.DependencyInjection 10 + the in-house static `ServiceContainer` facade |
| Logging | Serilog (daily file in `%APPDATA%\Coclico\logs`, 14-day retention) |
| Local AI | LLamaSharp 0.26: CPU backend bundled; CUDA 12 backend (`LLamaSharp.Backend.Cuda12` + NVIDIA runtime packages) only with `-p:IncludeCuda=true` |
| Misc | System.Management (WMI), System.ServiceProcess.ServiceController, System.Reactive, System.Text.Json, Microsoft.Extensions.Http / Caching.Memory |

Notable build settings in `Coclico/Coclico.csproj`:

- Tiered PGO + tiered compilation, `OptimizationPreference=Speed`, concurrent GC (server GC off), no trimming.
- Portable PDBs in Release, kept on purpose: they power the application's error reports.
- `EnforceCodeStyleInBuild` + `AnalysisLevel=latest`: style violations fail the build.
- `SatelliteResourceLanguages=fr;en`: only the French and English satellite languages ship.
- `EnableWindowsTargeting`: CI can build the Windows target from a non-Windows agent.
- `InternalsVisibleTo("Coclico.Tests")`: the test project can reach `internal` members.
- The application icon is `Resources/icone/plage.ico`; the version lives in `version.txt` at the repository root and flows into the build (see §13 and §15).

The solution (`Coclico.slnx`, also mirrored as `Coclico.sln`) contains **two projects**:

```text
Coclico/         WPF application (the main app)
Coclico.Tests/   xunit tests (references Coclico through InternalsVisibleTo)
```

`Coclico.Installer/` is **not** part of the solution: it is a standalone WPF project compiled only by `installer/build-installer.ps1` (see §13).

## 2. Application startup

`App.xaml.cs` (class `Coclico.App`):

1. **Global error handling**: hooks on `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` and `DispatcherUnhandledException` → `LoggingService` + `IAppErrorManager`.
2. `ServiceContainer.Build(services => ...)`: builds the `IServiceProvider` (lazy, `ValidateOnBuild`). Serilog is configured here, along with the base services (`IAuditLog`, `ISecurityPolicy`, `IProcessExecutionService`, memory cache, `HttpClient`), then every module's services.
3. The application starts **as a standard user** (no UAC prompt at launch); elevation happens on demand through `App.RestartAsAdmin()`. Check the state with `App.IsRunningAsAdministrator()`.
4. Settings load from `%APPDATA%\Coclico\settings.json`, then `LocalizationService.SetLanguage`, `ThemeService.ApplyCurrentSettings`, then `SplashWindow` → `MainWindow`.

```text
App.OnStartup
  ├── Global exception hooks (AppDomain / TaskScheduler / Dispatcher)
  ├── ServiceContainer.Build(...)          ← Serilog + base + module services
  ├── RemoveLegacyAiCredentialVault()
  ├── SettingsService: load %APPDATA%\Coclico\settings.json
  ├── LocalizationService.SetLanguage(...)
  ├── ThemeService.ApplyCurrentSettings()
  └── SplashWindow → MainWindow
```

`ServiceContainer` is a static facade: `ServiceContainer.GetRequired<T>()`, `GetOptional<T>()`. Views (code-behind) resolve their services this way.

## 3. Code organization

```text
Coclico/
  Common/
    Core/          SettingsService, CacheService, StartupService, ServiceContainer,
                   UpdateCheckService, UpdateManager, LaunchMode, SimpleStemmer
    Diagnostics/   LoggingService (Serilog), AppErrorManager, AuditLogService
    Execution/     IProcessExecutionService (process execution filtered by ISecurityPolicy),
                   ProcessExecutionService, ProcessWatcherService, ResourceAllocatorService,
                   ResourceGuardService
    UI/            ThemeService, LocalizationService, ToastService, DialogService,
                   TrayService.Win32, KeyboardShortcutsService
  Converters/      FirstCharConverter, InverseBoolToVisibilityConverter, HexToBrushConverter,
                   EqualityToBoolConverter, FileIconConverter
  Modules/         One folder per module (see §4)
  Resources/
    Lang/          fr.xaml + en.xaml (string dictionaries)
    icone/         application icons
  App.xaml         Global styles (brushes, CornerCard/CornerBtn, button styles)
  MainWindow.*     Main window: top bar, navigation drawer, panels
```

Anatomy of a module (`Modules/<Name>/`):

- `Services/`: business logic, kept **pure and testable** as much as possible (no WPF dependency).
- `Views/`: `XxxView.xaml` + code-behind. Some views use a `ViewModels/` folder + CommunityToolkit instead.
- `Windows/`, `Models/`: as needed (modal windows, data models).

Repository root:

| Path | Role |
| --- | --- |
| `version.txt` | The single version number, read by the build and the installer script |
| `Directory.Build.props` | Shared build properties for all projects |
| `LICENSE.txt` | MIT license |
| `SECURITY.md` | How to report a security issue |
| `CONTRIBUTORS.md` | Credits |
| `README.md` / `README_FR.md` | User guides (EN / FR) |
| `READMEDEV.md` / `READMEDEV_FR.md` | This developer documentation (EN / FR) |
| `docs/` | Screenshots used by the user guides |
| `installer/build-installer.ps1` | The installer build script (§13) |
| `.github/workflows/` | CI, release and repository automation (§12) |

## 4. The modules, one by one

`Modules/` contains one folder per feature. They all follow the shape described in §3: the logic in services, the UI in views. The complete tour:

### Dashboard (`Modules/Dashboard`)

The customizable home page. `HomeCustomizationService` + `HomeCustomizationModel` remember which tiles are visible; `CustomShortcutsService` adds your own shortcuts; `HomeCustomizerWindow` is the customization dialog. Tiles go through the same public navigation path as the menu (§6), and the first launch shows the welcome message.

### AI assistant (`Modules/AI`)

The built-in, fully local assistant — the chat panel opened with `Ctrl+Space`. Detailed in §5.

### Cleaning (`Modules/Cleaning`)

`CleaningService` (plus ViewModel and view) lists every target — temporary files, caches… — and shows what will be removed **before** anything happens. Deletions go to the **Recycle Bin**, and the result reports the freed space and the skipped items. Nothing runs without an explicit confirmation.

### Health & Defense (`Modules/HealthDefense`)

- `ISecurityPolicy` / `SecurityPolicyService`: the central command policy. `IProcessExecutionService` consults it before running anything (§8), so forbidden commands are blocked in one single place, covered by `SecurityPolicyTests`.
- `SystemHealthService`: system status checks.
- `StartupHealthService`: reviews what runs at Windows startup.

### Disk Analyzer (`Modules/DiskAnalyzer`)

- `DiskAnalyzerService`: scans a drive, builds the folder tree, computes the zones breakdown and the largest files.
- `DuplicateFinderService`: the three-phase duplicate scan (size index → candidate groups → hash refinement) is fully static and unit-tested; NTFS hard links are deduplicated by file identity so a linked file is never reported twice. `DuplicateScanOptions` controls the thresholds and how many groups are shown.
- `RecycleBinHelper`: safe deletion through `SHFileOperation` with `FOF_ALLOWUNDO`.
- The view offers a segmented mode bar (analysis / duplicates), drive cards with a used-space percentage and selection, a zones panel, and a duplicates list with per-file "open location" and delete-to-Recycle-Bin actions.

### RAM Cleaner (`Modules/RamCleaner`)

- `MemoryCleanerService`: on-demand working-set cleanup, globally or per process.
- `SmartMemoryDaemonService` (+ `ISmartMemoryDaemonService`): the background daemon and its profiles (including Deep) — state protected by a lock, loop in a `Task.Run` with a cancellation token, `StatusChanged` events, settings persisted in `AppSettings`, clean stop when the view unloads.
- The view shows live RAM/CPU readouts and the top processes.

### Network (`Modules/Network`)

The largest module. Interfaces (`INetworkBenchmarkService`, `INetworkDiagnosticsService`, `INetworkDiscoveryService`, `INetworkExperimentEngine`, `INetworkMemoryService`, `INetworkOptimizerService`, `INetworkSafetyService`, `INetworkScoringService`, `INetworkSnapshotService`, `INetworkWatchdogService`), models (`NetworkModels`, `NetworkEngineModels`) and their implementations: `NetworkBenchmarkService`, `NetworkDiagnosticsService`, `NetworkDiscoveryService`, `NetworkExperimentEngine`, `NetworkMemoryService`, `NetworkMonitorService`, `NetworkOptimizerService`, `NetworkSafetyService`, `NetworkScoringService`, `NetworkSnapshotService`, `NetworkWatchdogService`, plus `NetworkOptimizerViewModel` / `NetworkOptimizerView` and the `NetworkResultsWindow`.

Safety design: a snapshot is taken **before** any modification and a one-click rollback restores it; `INetworkSafetyService` validates every tweak; presets apply known-good configurations; the watchdog keeps an eye on the connection. No direct registry writes.

### CPU & Power (`Modules/Power`)

- `PowerPlanCatalog`: plan definitions and the matching `powercfg` arguments (unit-tested).
- `CpuPowerService`: admin-gated wrapper around `powercfg`. Every setting is validated with `powercfg /q` before being applied, unsupported ones are skipped and reported; Coclico-created plans can always be deleted, and the app returns to the Balanced plan first. No direct registry writes.
- `CpuPowerBenchmark`: CPU load benchmark.
- `AdaptivePowerService`: the adaptive daemon — it switches power plans based on CPU load, with thresholds, hysteresis and a cooldown. The rule lives in the pure `AdaptivePowerRules`, unit-tested without the daemon.

### Programs (`Modules/Programs`)

`InstalledProgramsService` enumerates installed programs (per-user and per-machine); `UninstallCommandParser` turns raw uninstall strings into a safe executable + arguments (unit-tested); the view lets you open, locate or uninstall each entry.

### Installer — in-app updates (`Modules/Installer`)

- `UpdateCheckService` (singleton) queries the latest GitHub release at startup, then roughly every 6 hours with a small random offset. The `UpdateAvailable` event is consumed by `MainWindow`, which highlights the sync button and opens `UpdateWindow` (versions, release notes, progress). "Ignore this version" persists in `AppSettings.SkippedUpdateVersion`; version comparison (`IsNewerVersion`) is numeric and handles prereleases.
- `UpdateManager`: `DownloadReleaseAsync` (HTTPS and GitHub domains only, mandatory SHA-256, progress in MB) then `LaunchInstaller(path, expectedTag)`, which re-verifies the hash **and** the file version (`FileVersionInfo`, without executing) before the `runas` elevation.
- `InstallerService` wires the in-app module. It is distinct from the standalone `Coclico.Installer` project (§13).

### Settings (`Modules/Settings`)

`SettingsView` (+ ViewModel), `ProfileService` (user profiles), `UserAccountService` (accounts and custom avatars), and the supporting windows: `SplashWindow`, `ProfileWindow` (with `AvatarCropWindow` to crop a custom avatar) and `ConfirmationDialog`.

### Help (`Modules/Help`)

`HelpView`: the in-app help page.

## 5. The AI assistant in detail

Everything runs **locally**. There is no cloud call and no API key anywhere in the code base; `AiLocalOnlyTests` guards this (no cloud provider may appear, and Ollama is only ever contacted on loopback).

- **Providers.** `MultiProviderClient` (with `AiProviderContracts`) talks to the model backends: local GGUF files through LLamaSharp, or a local Ollama server. `AiHardwareService` detects the GPU/VRAM and decides the layer offload; when the CUDA backend is not available, LLamaSharp falls back to the bundled CPU backend automatically.
- **Chat.** `AiChatService` builds the compact system prompt, keeps the conversation memory with pruning, handles stop tokens, and answers "what is this app?" questions deterministically. The assistant always knows where you are: navigation updates `IAiService.CurrentStatusContext` (§6). The UI lives in `AiChatView` (+ the `AiChatView.Actions.cs` partial).
- **Knowledge (RAG).** `RagService` runs BM25/TF-IDF retrieval over the bundled knowledge base (`resource/docs/coclico.md`), which describes every module, action and safety rule; `SimpleStemmer` handles French and English (stop words, suffixes, plurals).
- **Actions.** `AiActionParser` parses the model's structured output into actions (navigate to a module, clean RAM, optimize the network…). `AiToolExecutionService` executes them with strict sanitization — allowlisted navigation targets, validated package identifiers, numeric clamps — and requires an explicit user confirmation before anything that changes the system.
- **Vision.** `AiVisionService` handles image understanding with the same local models.

## 6. Navigation and menu

- The main menu is the **hamburger drawer** (`ArchitecturalDropdownOverlay` in `MainWindow.xaml`). It is a grouped accordion: Dashboard, then the collapsible groups **MAINTENANCE** (Cleaning, Health & Defense, Disk), **PERFORMANCE** (RAM, Network, CPU & Power) and **APPLICATIONS** (Programs, Installer), then Settings and Help, always visible. The expanded/collapsed state is persisted in `AppSettings.MenuExpandedGroups`.
- Each card carries a `Tag` (the module id). A click goes through `MainWindow.NavigateTo(tag)` → `NavigateToTagAsync(tag)` which:
  1. resolves or creates the view (cached through a `_xxxView ??= new XxxView()` field);
  2. updates `IAiService.CurrentStatusContext` (the AI assistant's context);
  3. plays the transition and updates the active indicator.
- Dashboard tiles and AI actions (`open_cleaning`, `open_power`…) go through this same public path; never raise a `Click` event on a `RadioButton` manually.
- Global shortcuts: `KeyboardShortcutsService` (AI, RAM clean…), plus `F11` (full screen) and `Ctrl+Space` (AI panel) handled in `MainWindow`.

## 7. Adding a module, step by step

Concrete example: the CPU & Power module (`Modules/Power`). Steps:

1. **Create the module**: `Modules/<Name>/Services/*.cs` (logic) and `Modules/<Name>/Views/<Name>View.xaml(.cs)`.
2. **Register the services** in `App.xaml.cs` (`services.AddSingleton<...>()`, or `AddTransient` for short-lived stateful services).
3. **Wire navigation** in `MainWindow.xaml.cs`:
   - a private `_xxxView` field;
   - a `"Tag" => _xxxView ??= ...` case in the `NavigateToTagAsync` switch;
   - the matching AI context sentence;
   - (optional) an `open_xxx` case in `AiChatPanel_ActionRequested`.
4. **Add the card** to the drawer (`MainWindow.xaml`): a `Border` with `Tag="<Tag>"` and `MouseDown="DropdownItem_MouseDown"`, inside the appropriate group. Icons: only existing `Wpf.Ui.Controls.SymbolRegular` symbols.
5. **Language keys**: add every key to `Resources/Lang/fr.xaml` **and** `en.xaml` (both dictionaries must stay in sync). Module views may also embed French strings directly (like DiskAnalyzer); keep it consistent per module.
6. **Tests**: pure logic in `Coclico.Tests`; for a simple view, an STA instantiation test avoids `XamlParseException` at first click (see `DiskAnalyzerTests.DiskAnalyzerView_InstantiatesWithoutXamlErrors`). Note: an instantiation test requires a view with no mandatory DI dependency in its constructor.
7. **Verify**: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` (new code must be clean).

## 8. Service patterns

**Pure, testable service.** Separate the algorithm from anything touching the system. Example `DuplicateFinderService`: the grouping steps (`IndexBySize`, `CandidateGroups`, `RefineByHash`, `ToDuplicateGroups`) are static and testable; the full scan is just a `Task.Run` around them. Same approach in `PowerPlanCatalog` (data + powercfg argument building) and `AdaptivePowerRules.Evaluate` (pure rule, tested without the daemon).

**Process execution.**

- Standard path: `IProcessExecutionService.ExecuteAsync`, filtered by `ISecurityPolicy.IsCommandBlocked`, timeout, cancellation, configured encodings. `requiresElevation: true` sets `Verb = "runas"` → **one UAC prompt per call**.
- For an admin-gated tool run in series (e.g. `powercfg` in `CpuPowerService`), do **not** use per-command elevation: call `powercfg.exe` directly (the `ProcessStartInfo` pattern from `NetworkOptimizerService`) and **gate the UI** on `App.IsRunningAsAdministrator()`. The user elevates the app once; no UAC storm.

**Native interop.** Repository convention: classic `DllImport` (not `LibraryImport`, which would require `AllowUnsafeBlocks`). Examples: `GetSystemPowerStatus` (battery/AC, `CpuPowerService`), `GetSystemTimes` (CPU load, `AdaptivePowerService`), `SHFileOperation` (Recycle Bin, `RecycleBinHelper`). Always wrap in a small internal class with a failure-tolerant public method.

**Daemons.** Two references: `SmartMemoryDaemonService` (memory cleaning) and `AdaptivePowerService` (plan switching from CPU load). Common points: state protected by a `lock`, loop in a `Task.Run` with a `CancellationTokenSource`, `StatusChanged`-style events, persistence in `AppSettings`, clean stop on the views' `Unloaded`. Events raised on a background thread must be marshalled with `Dispatcher.CheckAccess()` / `Dispatcher.InvokeAsync`.

**System measurements.** Prefer Win32 APIs (`GetSystemTimes`) over performance counters: no dependency, no localization issue, trivial delta computation.

## 9. Localization and theming

- UI strings: `Resources/Lang/fr.xaml` and `en.xaml`, consumed as `{DynamicResource MyKey}`. `LocalizationService` swaps the merged dictionary on language change; `LocalizationService.Get(key)` is the code-behind fallback.
- **Absolute rule: every added key must exist in both files.**
- XAML resources: `XamlResourceValidationTests` statically verifies that every `{StaticResource}` (e.g. `CornerCard`, `CornerBtn`) and every merged dictionary resolves, otherwise the test build fails. Missing `{DynamicResource}` keys fail silently at load, hence the importance of checking keys.
- Global brushes in `App.xaml`: `BgCardBrush`, `BgSurfaceBrush`, `BgElevatedBrush`, `PrimaryBrush`, `SuccessBrush`, `DangerBrush`, `ArchitecturalYellowBrush`, `Text*Brush`… `ThemeService` applies accent/theme from settings.
- `FindResource` **throws** when absent; in code-behind, prefer `TryFindResource` or an `as Brush` cast with a fallback.

## 10. Settings and data

- `%APPDATA%\Coclico\settings.json` ↔ `AppSettings` (camelCase JSON through `[JsonPropertyName]`, defaults on the properties). Adding a property = declaring it with a default; everything else (loading, saving, gentle migration of missing fields) is handled by `SettingsService`.
- Other data: Serilog logs (`logs/`), audit trail (`IAuditLog`, configurable retention), network snapshots (Network module), user profiles (`ProfileService`).

## 11. Tests

`Coclico.Tests` (xunit): everything must pass before any merge. Current coverage:

| File | What it covers |
| --- | --- |
| `Common/Core/UpdateVersionTests.cs` | Numeric version comparison, including prereleases (`IsNewerVersion`) |
| `Modules/Ai/AiLocalOnlyTests.cs` | "Local-only AI" guards (no cloud keys, Ollama loopback) |
| `Modules/Ai/RagServiceTests.cs` | Retrieval over the knowledge base |
| `Modules/DiskAnalyzer/DiskAnalyzerTests.cs` | Tree scan, `FormatSize`, STA view instantiation (catches `XamlParseException`) |
| `Modules/DiskAnalyzer/DuplicateFinderTests.cs` | 3-phase duplicates, size threshold, top-N largest files, grouping and sorting |
| `Modules/HealthDefense/SecurityPolicyTests.cs` | Command policy: blocked commands stay blocked |
| `Modules/Power/PowerPlanCatalogTests.cs` | Plan names, powercfg arguments, value bounds |
| `Modules/Power/AdaptivePowerRulesTests.cs` | Adaptive rule (thresholds, hysteresis, cooldown), sliding window |
| `Modules/Programs/UninstallCommandParserTests.cs` | Uninstall-string parsing (quotes, arguments, edge cases) |
| `Ui/Xaml/XamlResourceValidationTests.cs` | `StaticResource` resolution, merged dictionaries and image paths in Coclico and Coclico.Installer |

`InternalsVisibleTo("Coclico.Tests")` lets the tests reach `internal` service methods.

## 12. Build, format, CI and release

Local commands (identical to CI):

```powershell
dotnet restore Coclico.slnx
dotnet format Coclico.slnx --verify-no-changes --no-restore
dotnet build Coclico.slnx --configuration Release --no-restore
dotnet test Coclico.slnx --configuration Release --no-build --no-restore
```

> Format rule: all new code must pass `dotnet format --verify-no-changes`. The repository is format-clean; if a local check fails, run `dotnet format Coclico.slnx` (without `--verify`) and commit the result in a dedicated PR to keep the diff reviewable.

Workflows in `.github/workflows/`:

- **`ci.yml`** — restore → format check → Release build → tests (TRX artifact) → on pushes to `main`, a win-x64 publish is uploaded as an artifact.
- **`release.yml`** — triggered by a `vX.Y.Z` tag: checks that the tag matches the version (`version.txt`), format check + tests, publish, then `./installer/build-installer.ps1 -SkipPublish`, portable ZIP + `SHA256SUMS.txt` (setup + zip), and the GitHub release with the three files.
- **`codeql.yml`** — CodeQL security scanning.
- **`dependency-review.yml`** — dependency review of package changes.
- **`scorecard.yml`** — OpenSSF Scorecard.
- **`labeler.yml`, `greetings.yml`, `stale.yml`** — repository automation (issue labels, welcome message, stale handling).

## 13. The installer pipeline

What `installer/build-installer.ps1` does, step by step (interactive console, FR/EN):

1. **Version**: read from `version.txt` at the repository root (or passed with `-Version`).
2. **Tests first**: `dotnet test` runs before anything is packaged (skip with `-SkipTests`) — the installer is never built on a red suite.
3. **Cleanup**: `publish/` and `artifacts/installer_build` are cleaned (skip with `-SkipClean`).
4. **Publish Coclico**: `dotnet publish` win-x64, **framework-dependent** (`--self-contained false`), no single file, no PDBs → `publish/`. The .NET 10 Desktop Runtime is handled by the installer at install time (below).
5. **Payload**: `publish/` is zipped into `payload.zip` inside `Coclico.Installer/` (optimal compression). Excluded: `.pdb` files and, unless `-IncludeCuda`, the CUDA runtime files (`ggml-cuda.dll`, `cudart64_12.dll`, `cublas…`). Without the CUDA DLLs, LLamaSharp cleanly uses the bundled CPU backend; NVIDIA users get the CUDA runtime downloaded during installation instead — that is how the installer stays small.
6. **Final installer**: `Coclico.Installer` is published as a self-contained compressed SingleFile with `payload.zip` embedded → `artifacts/installer/Coclico-<version>.exe` (variant `-Full-CUDA`: `Coclico-<version>-Full-CUDA.exe`), with its SHA-256 printed.
7. `-SkipPublish` reuses the existing `publish/` folder (this is what the release CI does after its own publish).

At install time, the standalone `Coclico.Installer` project takes over (`Services/`):

- `SystemRequirementService` — system requirements and the target drive derived from the chosen path.
- `GpuDetector` — NVIDIA GPU detection (never throws).
- `DotnetRuntimeService` — checks the .NET 10 Desktop Runtime; when missing, downloads the pinned Microsoft installer and runs it silently (`/install /passive /norestart /log`, with retries if Windows Installer is busy).
- `CudaRuntimeDownloader` + `AiModelDownloader` — optional download of the CUDA runtime and of a local AI model (catalog in `ai-models.json`, size-aware, resumable).
- `InstallService` — installs for the current user (default `%LOCALAPPDATA%\Programs\Coclico`), creates shortcuts (`ShortcutHelper`), registers the uninstall entry (from the installer itself, not scattered registry writes), deploys the uninstaller to `Uninstall\Coclico.Installer.exe`, closes a running Coclico first, and preserves settings and data on uninstall.
- `InstallerLocalization` — FR/EN installer strings; `UninstallWindow` offers to keep user data when uninstalling.

## 14. Security practices

- **Confirmations** before any destructive action; **Recycle Bin** (`SHFileOperation` + `FOF_ALLOWUNDO`) instead of permanent deletion for user files.
- **Network snapshots** before any modification + one-click rollback; a safety service validates every tweak.
- **Command filtering**: go through `IProcessExecutionService` whenever possible (`ISecurityPolicy` blocks forbidden commands, tested by `SecurityPolicyTests`).
- **Power module**: only `powercfg` (no direct registry writes), every setting validated with `powercfg /q` before being applied (unsupported settings are skipped and reported), Coclico plans deletable at any time, back to the Balanced plan first.
- **Local-only AI**: no cloud key vault; Ollama restricted to loopback; guards tested by `AiLocalOnlyTests`. AI actions are sanitized and require user confirmation.
- **Updates**: HTTPS and GitHub domains only, mandatory SHA-256 verified at download **and** again before launch, file version checked without executing; releases ship `SHA256SUMS.txt`.
- **On-demand elevation**: the app starts as a standard user; affected modules show their state and point to the existing elevation button.
- **Logs**: never contain personal data; `LoggingService.LogException` in every `catch`.

## 15. Publishing a release

1. Update `version.txt` at the repository root — the single source of the version, picked up by the build and the installer script.
2. Update the version shown in the status bar of `MainWindow.xaml` if needed.
3. Merge to `main` (CI runs: format, build, tests).
4. Create the tag: `git tag vX.Y.Z && git push origin vX.Y.Z`. The release (setup + portable ZIP + SHA256SUMS.txt) is generated automatically, provided the tag matches the version.
5. Check the GitHub release: the three expected files, and the SHA-256 printed by the local installer script matches `SHA256SUMS.txt`.

---

<p align="center"><i>Back to: <a href="README.md">user guide (EN)</a> · <a href="README_FR.md">guide utilisateur (FR)</a> · <a href="READMEDEV_FR.md">documentation développeur (FR)</a></i></p>
