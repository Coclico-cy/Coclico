<div align="center">

<img src="Coclico/Resources/icone/plage.png" alt="Coclico icon" width="72" />

# Coclico Developer Documentation

### Architecture, patterns, build pipeline and CI, explained module by module

[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE.txt)

[User guide (EN)](README.md) · [Guide utilisateur (FR)](README_FR.md) · **Developer (EN)** · [Développeur (FR)](READMEDEV_FR.md)

</div>

---

## Table of contents

1. [Tech stack and projects](#1-tech-stack-and-projects)
2. [Application startup](#2-application-startup)
3. [Code organization](#3-code-organization)
4. [Navigation and menu](#4-navigation-and-menu)
5. [Adding a module, step by step](#5-adding-a-module-step-by-step)
6. [Service patterns](#6-service-patterns)
7. [Localization and theming](#7-localization-and-theming)
8. [Settings and data](#8-settings-and-data)
9. [Tests](#9-tests)
10. [Build, format, CI and release](#10-build-format-ci-and-release)
11. [The installer pipeline](#11-the-installer-pipeline)
12. [Security practices](#12-security-practices)
13. [Publishing a release](#13-publishing-a-release)

---

## 1. Tech stack and projects

| Item | Detail |
| --- | --- |
| Target | `net10.0-windows10.0.22621.0`, WPF, `OutputType=WinExe` |
| Language | C# (`LangVersion latest`), `Nullable` and `ImplicitUsings` enabled |
| UI | [WPF-UI](https://github.com/lepoco/wpfui) 4.2.0 (Fluent controls: `FluentWindow`, `SymbolIcon`, `Snackbar`…) |
| MVVM | CommunityToolkit.Mvvm 8.4 (used for some ViewModels; many views stay code-behind) |
| DI / hosting | Microsoft.Extensions.DependencyInjection 10 + the in-house static `ServiceContainer` facade |
| Logging | Serilog (daily file in `%APPDATA%\Coclico\logs`, 14-day retention) |
| Local AI | LLamaSharp 0.26 + CUDA 12 backend (automatic CPU fallback) |
| Misc | System.Management (WMI), System.Reactive, System.Text.Json |

The `Coclico.slnx` solution contains **two projects**:

```text
Coclico/         WPF application (the main app)
Coclico.Tests/   xunit tests (references Coclico through InternalsVisibleTo)
```

`Coclico.Installer/` is **not** part of the solution: it is a standalone WPF project compiled only by `installer/build-installer.ps1` (see §11).

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
    Core/          SettingsService, CacheService, StartupService, UpdateCheckService...
    Diagnostics/   LoggingService (Serilog), AppErrorManager, AuditLogService
    Execution/     IProcessExecutionService (process execution filtered by ISecurityPolicy)
    UI/            ThemeService, LocalizationService, ToastService, DialogService,
                   TrayService, KeyboardShortcutsService
  Converters/      XAML value converters
  Modules/         One folder per module (see below)
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

## 4. Navigation and menu

- The main menu is the **hamburger drawer** (`ArchitecturalDropdownOverlay` in `MainWindow.xaml`). It is a grouped accordion: Dashboard, then the collapsible groups **MAINTENANCE** (Cleaning, Health & Defense, Disk), **PERFORMANCE** (RAM, Network, CPU & Power) and **APPLICATIONS** (Programs, Installer), then Settings and Help, always visible. The expanded/collapsed state is persisted in `AppSettings.MenuExpandedGroups`.
- Each card carries a `Tag` (the module id). A click goes through `MainWindow.NavigateTo(tag)` → `NavigateToTagAsync(tag)` which:
  1. resolves or creates the view (cached through a `_xxxView ??= new XxxView()` field);
  2. updates `IAiService.CurrentStatusContext` (the AI assistant's context);
  3. plays the transition and updates the active indicator.
- Dashboard tiles and AI actions (`open_cleaning`, `open_power`…) go through this same public path; never raise a `Click` event on a `RadioButton` manually.
- Global shortcuts: `KeyboardShortcutsService` (AI, RAM clean…), plus `F11` (full screen) and `Ctrl+Space` (AI panel) handled in `MainWindow`.

## 5. Adding a module, step by step

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

## 6. Service patterns

**Pure, testable service.** Separate the algorithm from anything touching the system. Example `DuplicateFinderService`: the grouping steps (`IndexBySize`, `CandidateGroups`, `RefineByHash`, `ToDuplicateGroups`) are static and testable; the full scan is just a `Task.Run` around them. Same approach in `PowerPlanCatalog` (data + powercfg argument building) and `AdaptivePowerRules.Evaluate` (pure rule, tested without the daemon).

**Process execution.**

- Standard path: `IProcessExecutionService.ExecuteAsync`, filtered by `ISecurityPolicy.IsCommandBlocked`, timeout, cancellation, configured encodings. `requiresElevation: true` sets `Verb = "runas"` → **one UAC prompt per call**.
- For an admin-gated tool run in series (e.g. `powercfg` in `CpuPowerService`), do **not** use per-command elevation: call `powercfg.exe` directly (the `ProcessStartInfo` pattern from `NetworkOptimizerService`) and **gate the UI** on `App.IsRunningAsAdministrator()`. The user elevates the app once; no UAC storm.

**Native interop.** Repository convention: classic `DllImport` (not `LibraryImport`, which would require `AllowUnsafeBlocks`). Examples: `GetSystemPowerStatus` (battery/AC, `CpuPowerService`), `GetSystemTimes` (CPU load, `AdaptivePowerService`), `SHFileOperation` (Recycle Bin, `RecycleBinHelper`). Always wrap in a small internal class with a failure-tolerant public method.

**Daemons.** Two references: `SmartMemoryDaemonService` (memory cleaning) and `AdaptivePowerService` (plan switching from CPU load). Common points: state protected by a `lock`, loop in a `Task.Run` with a `CancellationTokenSource`, `StatusChanged`-style events, persistence in `AppSettings`, clean stop on the views' `Unloaded`. Events raised on a background thread must be marshalled with `Dispatcher.CheckAccess()` / `Dispatcher.InvokeAsync`.

**System measurements.** Prefer Win32 APIs (`GetSystemTimes`) over performance counters: no dependency, no localization issue, trivial delta computation.

**Update system.** `UpdateCheckService` (singleton) queries the latest GitHub release at startup then roughly every 6 hours with a small random offset; the `UpdateAvailable` event is consumed by `MainWindow`, which highlights the sync button in yellow and opens the `Modules/Installer/Windows/UpdateWindow` dialog (versions, release notes, progress). The install flow: `UpdateManager.DownloadReleaseAsync` (HTTPS + GitHub domain only, mandatory SHA-256, progress in MB) → `LaunchInstaller(path, expectedTag)` which re-verifies the hash **and** the file version (`FileVersionInfo`, without executing) before the `runas` elevation. "Ignore this version" persists in `AppSettings.SkippedUpdateVersion`; version comparison (`IsNewerVersion`) is numeric and handles prereleases.

## 7. Localization and theming

- UI strings: `Resources/Lang/fr.xaml` and `en.xaml`, consumed as `{DynamicResource MyKey}`. `LocalizationService` swaps the merged dictionary on language change; `LocalizationService.Get(key)` is the code-behind fallback.
- **Absolute rule: every added key must exist in both files.**
- XAML resources: `XamlResourceValidationTests` statically verifies that every `{StaticResource}` (e.g. `CornerCard`, `CornerBtn`) and every merged dictionary resolves, otherwise the test build fails. Missing `{DynamicResource}` keys fail silently at load, hence the importance of checking keys.
- Global brushes in `App.xaml`: `BgCardBrush`, `BgSurfaceBrush`, `BgElevatedBrush`, `PrimaryBrush`, `SuccessBrush`, `DangerBrush`, `ArchitecturalYellowBrush`, `Text*Brush`… `ThemeService` applies accent/theme from settings.
- `FindResource` **throws** when absent; in code-behind, prefer `TryFindResource` or an `as Brush` cast with a fallback.

## 8. Settings and data

- `%APPDATA%\Coclico\settings.json` ↔ `AppSettings` (camelCase JSON through `[JsonPropertyName]`, defaults on the properties). Adding a property = declaring it with a default; everything else (loading, saving, gentle migration of missing fields) is handled by `SettingsService`.
- Other data: Serilog logs (`logs/`), audit trail (`IAuditLog`, configurable retention), network snapshots (Network module), user profiles (`ProfileService`).

## 9. Tests

`Coclico.Tests` (xunit): everything must pass before any merge:

| File | What it covers |
| --- | --- |
| `DiskAnalyzerTests.cs` | Tree scan, `FormatSize`, STA view instantiation (catches `XamlParseException`) |
| `XamlResourceValidationTests.cs` | `StaticResource` resolution, merged dictionaries and image paths in Coclico and Coclico.Installer |
| `AiLocalOnlyTests.cs` | "Local-only AI" guards (no cloud keys, Ollama loopback) |
| `DuplicateFinderTests.cs` | 3-phase duplicates, size threshold, top-N largest files, grouping and sorting |
| `PowerPlanCatalogTests.cs` | Plan names, powercfg arguments, value bounds |
| `AdaptivePowerRulesTests.cs` | Adaptive rule (thresholds, hysteresis, cooldown), sliding window |

`InternalsVisibleTo("Coclico.Tests")` lets the tests reach `internal` service methods.

## 10. Build, format, CI and release

Local commands (identical to CI):

```powershell
dotnet restore Coclico.slnx
dotnet format Coclico.slnx --verify-no-changes --no-restore
dotnet build Coclico.slnx --configuration Release --no-restore
dotnet test Coclico.slnx --configuration Release --no-build --no-restore
```

> Format rule: all new code must pass `dotnet format --verify-no-changes`. The repository is format-clean; if a local check fails, run `dotnet format Coclico.slnx` (without `--verify`) and commit the result in a dedicated PR to keep the diff reviewable.

**CI (`.github/workflows/ci.yml`)**: restore → format check → Release build → tests (TRX artifact) → on pushes to `main`, a self-contained win-x64 publish is uploaded as an artifact.

**Release (`.github/workflows/release.yml`)**: triggered by a `vX.Y.Z` tag:

1. checks that the tag matches the `Version` in `Coclico/Coclico.csproj` (fails otherwise);
2. format check + tests;
3. self-contained `dotnet publish` for win-x64;
4. `./installer/build-installer.ps1 -Version X.Y.Z -SkipPublish`;
5. portable ZIP + `SHA256SUMS.txt` (setup + zip);
6. GitHub release with the three files.

## 11. The installer pipeline

What `installer/build-installer.ps1` does, step by step:

1. **Version**: read from `Coclico.csproj` (or passed with `-Version`).
2. **Publish** Coclico: `dotnet publish` win-x64, self-contained, no symbols → `publish/`.
3. **Light uninstaller**: `Coclico.Installer` is compiled as a SingleFile **without** the payload (`-p:EmbedPayload=false`) → `uninstaller.bin` (about 45 MB); this is the binary that will uninstall from Windows Settings.
4. **Payload**: `publish/` is zipped into `payload.zip` (optimal compression). By default the `cuda12` folder is **excluded** (light ~50 MB installer): without `ggml-cuda.dll`, LLamaSharp would load the CUDA backend and crash; without the folder it falls back to the bundled CPU variant. `-IncludeCuda` restores the full runtime (~1.1 GB).
5. **Final installer**: `Coclico.Installer` is compiled again as a SingleFile **with** `payload.zip` and `uninstaller.bin` embedded → `artifacts/installer/Coclico-<version>.exe` (variant `-Full-CUDA`: `Coclico-<version>-Full-CUDA.exe`), with its SHA-256 printed.
6. `-SkipPublish` reuses the existing `publish/` folder (this is what the release CI does after its own publish); `-BuildUninstaller` also compiles the legacy standalone uninstaller if present.

The installer installs for the current user account, creates the uninstall entry in the registry (through the installer itself, not scattered registry writes) and preserves settings and data on uninstall.

## 12. Security practices

- **Confirmations** before any destructive action; **Recycle Bin** (`SHFileOperation` + `FOF_ALLOWUNDO`) instead of permanent deletion for user files.
- **Network snapshots** before any modification + one-click rollback.
- **Command filtering**: go through `IProcessExecutionService` whenever possible (`ISecurityPolicy` blocks forbidden commands).
- **Power module**: only `powercfg` (no direct registry writes), every setting validated with `powercfg /q` before being applied (unsupported settings are skipped and counted), Coclico plans deletable at any time, back to the Balanced plan first.
- **Local-only AI**: no cloud key vault; Ollama restricted to loopback; guards tested by `AiLocalOnlyTests`.
- **On-demand elevation**: the app starts as a standard user; affected modules show their state and point to the existing elevation button.
- **Logs**: never contain personal data; `LoggingService.LogException` in every `catch`.

## 13. Publishing a release

1. Update `<Version>` in `Coclico/Coclico.csproj`.
2. Update the version shown in the status bar of `MainWindow.xaml` if needed.
3. Merge to `main` (CI runs: format, build, tests).
4. Create the tag: `git tag vX.Y.Z && git push origin vX.Y.Z`. The release (setup + ZIP + SHA256SUMS) is generated automatically, provided the tag matches the `.csproj` version.
5. Check the GitHub release: the three expected files, and the SHA-256 printed by the local installer script matches `SHA256SUMS.txt`.

---

<p align="center"><i>Back to: <a href="README.md">user guide (EN)</a> · <a href="README_FR.md">guide utilisateur (FR)</a> · <a href="READMEDEV_FR.md">documentation développeur (FR)</a></i></p>
