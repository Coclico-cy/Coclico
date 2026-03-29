# Coclico

> This project was built with the assistance of AI (Claude by Anthropic) for code generation, language correction, and multi-language support (French, German, Spanish). We believe in transparency.

**Native Windows system management, automation, and AI-assisted analysis -- entirely offline.**

![Version](https://img.shields.io/badge/Version-1.0.4-blue?style=flat-square)
![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078d4?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-10.0-512bd4?style=flat-square)
[![Website](https://img.shields.io/badge/Website-coclico--cy.github.io-orange?style=flat-square)](https://coclico-cy.github.io/Coclico/)

---

Coclico is a desktop application for Windows system administration, process supervision, automated maintenance, and AI-assisted analysis. It runs natively on .NET 10 with WPF and operates with zero cloud dependency. An embedded local large language model, powered by LLamaSharp 0.26, delivers AI-driven insights and autonomous optimization without ever transmitting data outside the machine.

[Website](https://coclico-cy.github.io/Coclico/) |
[Download Latest Release](https://github.com/Coclico-cy/Coclico/releases/latest) |
[Source Code](https://github.com/Coclico-cy/Coclico)

---

## Table of Contents

1. [Features at a Glance](#features-at-a-glance)
2. [System Requirements](#system-requirements)
3. [Installation](#installation)
4. [Building from Source](#building-from-source)
5. [Modules](#modules)
   - [Dashboard](#1-dashboard)
   - [Applications](#2-applications)
   - [Flow Chains](#3-flow-chains)
   - [Installer](#4-installer)
   - [Cleaning](#5-cleaning)
   - [Scanner](#6-scanner)
   - [RAM Cleaner](#7-ram-cleaner)
   - [AI Assistant](#8-ai-assistant-coclico-ai)
6. [Architecture](#architecture)
   - [Application Lifecycle](#application-lifecycle)
   - [Dependency Injection](#dependency-injection)
   - [Dual LLM Executor](#dual-llm-executor)
   - [Autonomous Core](#autonomous-core)
   - [MVVM Structure](#mvvm-structure)
7. [Telemetry and Supervision](#telemetry-and-supervision)
8. [Security Model](#security-model)
9. [Technology Stack](#technology-stack)
10. [Project Structure](#project-structure)
11. [Running Tests](#running-tests)
12. [Configuration](#configuration)
13. [Localization](#localization)
14. [Contributing](#contributing)
15. [Open Source](#open-source)

---

## Features at a Glance

- **Eight integrated modules** covering real-time monitoring, application management, workflow automation, software installation, disk cleaning, system scanning, RAM optimization, and AI chat.
- **Fully offline AI** -- a local LLM running on LLamaSharp 0.26 with dual isolated executor contexts. No API keys, no cloud accounts, no data exfiltration.
- **Autonomous optimization engine** -- a three-stage pipeline (Collect, Decide, Execute) that runs every 30 seconds, combining AI cognitive decisions with deterministic fallback.
- **Roslyn-powered code analysis** -- AST-level static analysis with cyclomatic complexity, Halstead metrics (Volume, Difficulty, Effort), and Maintainability Index computation.
- **Digital Twin validation** -- every AI-proposed patch is validated against a digital twin to ensure cyclomatic complexity does not increase.
- **Enterprise safety net** -- append-only audit log, rollback snapshots before every disk write, configurable security policy, and audit-only mode for AI patches by default.
- **Workflow automation** -- a visual drag-and-drop editor with 28 node types, 10 condition operators, 3 error handling strategies, and hotkey triggers.
- **Application discovery from 8 sources** -- Registry (HKLM + HKCU), Steam, Epic Games, GOG Galaxy, Ubisoft Connect, EA App, Rockstar Games Launcher, and Microsoft Store.
- **Multi-language interface** -- English, French, German, and Spanish, with runtime switching.
- **Performance-tuned runtime** -- TieredPGO, concurrent garbage collection, and speed-optimized compilation profile.

---

## System Requirements

| Requirement | Minimum | Recommended |
|---|---|---|
| Operating System | Windows 10 22H2 or later | Windows 11 |
| .NET Runtime | .NET 10.0 | .NET 10.0 |
| RAM | 4 GB | 8 GB (for AI features) |
| Disk Space | 250 MB (application) | 3 GB (with LLM model) |
| Privileges | Administrator (UAC) | Administrator (UAC) |
| GPU | Not required | Vulkan-compatible GPU (AI acceleration) |

The application enforces administrator elevation at startup through a UAC prompt. This is required for system-level operations such as WMI queries, service management, registry modifications, working set trimming, and standby list flushing.

---

## Installation

### Pre-built Installer

Download the latest installer from the [Releases](https://github.com/Coclico-cy/Coclico/releases/latest) page and run it. The installer handles .NET runtime detection, file placement, and Start Menu shortcuts.

### Portable

Download the portable archive from the [Releases](https://github.com/Coclico-cy/Coclico/releases/latest) page, extract it to a directory of your choice, and run `Coclico.exe`. Administrator privileges are still required.

---

## Building from Source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- Git
- Windows 10 22H2+ or Windows 11
- Administrator privileges (to run the application after building)

### Clone, Restore, Build, and Run

```bash
git clone https://github.com/Coclico-cy/Coclico.git
cd Coclico
dotnet restore
dotnet build Coclico/Coclico.csproj
dotnet run --project Coclico/Coclico.csproj
```

The `dotnet run` command must be executed from an elevated (Administrator) terminal. If the process is not elevated, the application will trigger a UAC prompt and restart itself with the required privileges.

### GPU Acceleration (Optional)

By default, Coclico ships with the CPU backend for LLamaSharp. To enable Vulkan-based GPU acceleration for faster AI inference:

1. Open `Coclico/Coclico.csproj`.
2. Replace the package reference `LLamaSharp.Backend.Cpu` with `LLamaSharp.Backend.Vulkan`.
3. Rebuild the project.

The application automatically detects the GPU at runtime and falls back to CPU inference if the Vulkan backend fails to initialize.

---

## Modules

Coclico is organized into eight modules, each accessible from the sidebar navigation in the main window.

### 1. Dashboard

The dashboard provides a real-time overview of system health: CPU usage, RAM consumption, disk utilization, network throughput, and active process count. A composite health score aggregates these metrics into a single indicator. Quick-action buttons provide one-click access to common operations (disk clean, RAM free, system scan). Three operational modes -- Zen, Gamer, and Work -- adjust the monitoring frequency and UI density to match different usage contexts.

### 2. Applications

The applications module discovers and catalogs installed software from eight distinct sources:

- **Registry**: both `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` and the `HKCU` equivalent
- **Game Platforms**: Steam, Epic Games, GOG Galaxy, Ubisoft Connect, EA App, and Rockstar Games Launcher
- **Microsoft Store**: packaged applications registered with the system

Each discovered application shows its name, version, publisher, installation path, estimated size, and source. Users can rename entries, assign categories, and manually add applications that are not automatically detected. Discovery results are cached for six hours to avoid redundant registry and file system scans.

### 3. Flow Chains

The flow chain editor is a visual drag-and-drop automation builder for creating multi-step system workflows. It supports 28 distinct node types, including application launch, process termination, shell commands, PowerShell script execution, file operations (copy, move, delete), registry read/write, WMI queries, HTTP requests, conditional branching, loops, delays, and toast notifications.

Each node supports 10 condition operators for branching logic and one of three error-handling actions:

- **ContinueNext**: proceed to the next node regardless of failure
- **StopChain**: halt the entire chain on failure
- **SkipToEnd**: jump to the chain's final node on failure

Flow chains can be triggered by hotkeys for hands-free automation. Chain definitions are persisted as JSON files and can be exported and shared.

### 4. Installer

The installer module provides a graphical interface for the Windows Package Manager (winget). Software is organized into six categories for easy browsing. Users can select machine-wide or per-user installation scope and initiate one-click installs with progress tracking. Batch installation is supported for provisioning new machines.

### 5. Cleaning

The cleaning module targets ten categories of disposable data:

1. Temporary files (`%TEMP%`, Windows Temp)
2. Browser caches (Chrome, Firefox, Edge, Brave, Opera)
3. System logs
4. Recycle Bin contents
5. Thumbnail caches
6. Windows Error Reporting files
7. Old Windows installers and update residuals
8. DNS resolver cache
9. Prefetch data
10. Miscellaneous application caches

A pre-estimation scan calculates the total reclaimable space before any deletion occurs. Deep clean mode extends the scan to additional system directories. All operations report individual and aggregate sizes so the user can review what will be removed.

### 6. Scanner

The scanner module performs a full audit of every installed application on the system. For each entry, it reports the application name, version, publisher, estimated disk size, installation path, discovery source, and install date. Results are presented in a sortable, filterable table. This module is useful for software inventory, compliance auditing, and identifying forgotten or redundant installations.

### 7. RAM Cleaner

The RAM cleaner provides real-time memory monitoring alongside active optimization. It uses P/Invoke to call native Windows APIs for operations that the .NET garbage collector cannot perform:

- **Working set trimming** across all processes
- **Standby list flushing** to reclaim cached memory pages
- **DNS client cache clearing**
- **ARP table flushing**
- **File system cache trimming**

Three cleaning profiles control the aggressiveness of the operation:

- **Quick**: GC collection and working set trimming only
- **Normal**: adds standby list flush and DNS/ARP clearing
- **Deep**: includes all operations plus aggressive per-process memory reclamation

An automatic mode can be configured to trigger cleaning when available RAM drops below a configurable threshold or on a fixed interval.

### 8. AI Assistant (Coclico AI)

The AI assistant is a local LLM chat interface powered by LLamaSharp 0.26 running a GGUF model file stored at `resource/model/IA-support-chat.gguf`. It operates entirely offline with zero cloud dependency.

Key characteristics:

- **Dual executor isolation**: the chat context (`_chatCtx`) and the optimization engine context (`_engineCtx`) run on separate semaphore-guarded executors, preventing user conversations from blocking background AI tasks.
- **RAG integration**: the assistant can query embedded documentation via `RagService` for context-aware responses about Coclico's own features.
- **Auto-unload**: after a configurable idle timeout (default: 5 minutes), the model is unloaded from memory, freeing approximately 2.5 GB of RAM. It reloads transparently on the next interaction.
- **GPU optional**: the default build uses CPU inference. Switching to the Vulkan backend enables GPU acceleration on compatible hardware.

---

## Architecture

### Application Lifecycle

1. **UAC Enforcement**: `App.OnStartup` checks for administrator elevation. If the process is not elevated, it relaunches itself with a UAC prompt.
2. **DI Container Build**: `ServiceContainer.Build(...)` registers all singletons and transients into a `Microsoft.Extensions.DependencyInjection` container with `ValidateOnBuild = true`, ensuring that missing dependencies fail fast at startup rather than at runtime.
3. **Splash Screen**: `SplashWindow` is displayed while services initialize, the LLM model is optionally preloaded, and the startup health check runs.
4. **Main Window**: `MainWindow` opens with sidebar navigation to all eight modules.
5. **First-Run Configuration**: on the first launch, `LauncherWindow` lets the user configure the startup mode before entering the main interface.

### Dependency Injection

All services use constructor injection exclusively. There are no static singletons or service locator patterns in application code. The DI container is built once at startup and never modified.

**Interface-bound services (enforced contracts):**

| Interface | Implementation | Responsibility |
|---|---|---|
| `ICacheService` | `CacheService` | In-memory caching with configurable expiration policies |
| `IDynamicTracer` | `DynamicTracerService` | Runtime telemetry spans, P2 quantile estimation, anomaly detection |
| `IResourceAllocator` | `ResourceAllocatorService` | Process priority, CPU affinity, and working set management |
| `IRollbackService` | `RollbackService` | File snapshot creation and restoration before disk writes |
| `ISourceAnalyzer` | `SourceAnalyzerService` | Roslyn AST analysis: cyclomatic complexity, Halstead metrics, Maintainability Index |
| `IStateValidator` | `StateValidatorService` | Validates that AI-proposed patches do not increase cyclomatic complexity |
| `ICodePatcher` | `CodePatcherService` | Applies validated patches with approval workflow and audit trail |
| `IModuleOrchestrator` | `ModuleOrchestratorService` | Coordinates autonomous modules (Phase 3.5) |
| `IOptimizationEngine` | `OptimizationEngineService` | 30-second optimization cycle: Collect, Decide, Execute |
| `IAuditLog` | `AuditLogService` | Append-only NDJSON audit log with AI decision context |
| `ISecurityPolicy` | `SecurityPolicyService` | Configurable security rules with non-suppressible hardcoded defaults |
| `IAiService` | `AiChatService` | Dual-context LLM inference (chat and engine) |

**Concrete singleton services:**

`SettingsService`, `ThemeService`, `LocalizationService`, `ProfileService`, `ProcessWatcherService`, `NetworkMonitorService`, `ResourceGuardService`, `KeyboardShortcutsService`, `StartupService`, `FeatureExecutionEngine`.

**Transient services:**

`CleaningService`, `InstallerService`, `WorkflowService`, `FlowChainService`, `FlowChainExecutionService`, `StartupHealthService`, `UserAccountService`.

### Dual LLM Executor

`AiChatService` maintains two fully isolated `LLamaContext` + `InteractiveExecutor` pairs:

- **Chat context** (`_chatCtx` / `_chatSem`): serves the user-facing AI chat bound to `AiChatView`. Protected by a `SemaphoreSlim` to serialize user requests.
- **Engine context** (`_engineCtx` / `_engineSem`): serves the background `OptimizationEngineService`. Operates on its own semaphore so optimization cycles never block user conversations.

Both contexts use an **Immutable Context Swap** pattern: when a context needs to be reset, a new `LLamaContext` is created and swapped in atomically via `Interlocked.Exchange`. The old context is disposed asynchronously, so active consumers are never blocked. Neither semaphore is ever acquired from the UI thread.

After a configurable idle period (`aiIdleTimeoutMinutes`, default 5 minutes), both contexts are unloaded to free approximately 2.5 GB of RAM. They are transparently recreated on the next request.

### Autonomous Core

The autonomous core is a set of services that enable Coclico to analyze, propose, validate, and apply optimizations with minimal human intervention.

**OptimizationEngineService** runs a three-stage pipeline every 30 seconds:

1. **Collect**: gathers telemetry snapshots from `IDynamicTracer` and `ResourceGuardService`.
2. **Decide**: submits the telemetry to the engine LLM context for cognitive analysis. If the LLM is unavailable or times out, a deterministic fallback heuristic takes over.
3. **Execute**: applies the decided actions through `FeatureExecutionEngine`, which wraps all operations with circuit-breaker logic and telemetry recording.

**SourceAnalyzerService** uses Roslyn to parse C# source files into syntax trees and semantic models. For each method, it computes:

- Cyclomatic complexity (McCabe)
- Halstead metrics: Volume (V), Difficulty (D), Effort (E)
- Maintainability Index

**StateValidatorService** acts as a validation gate. Before any AI-proposed code patch is applied, the digital twin compares the cyclomatic complexity of the original method against the patched version. If complexity increases, the patch is rejected.

**CodePatcherService** manages the full patch lifecycle:

- `GetPendingProposals()` -- lists patches awaiting review
- `ApproveAndApplyAsync()` -- applies a human-approved patch after creating a rollback snapshot
- `RejectProposalAsync()` -- discards a proposal with an audit log entry

By default, the auto-patcher operates in **audit-only mode**: patches are logged but not applied. This is controlled via `SettingsService.Settings.CodePatcherAuditOnly` and exposed in the Settings UI under "AI & Audit".

### MVVM Structure

The application follows the Model-View-ViewModel pattern:

- **Views** (`Views/`): WPF XAML files with minimal code-behind for UI event handling.
- **ViewModels** (`ViewModels/`): use `CommunityToolkit.Mvvm` for property change notification (`ObservableProperty`) and command binding (`RelayCommand`).
- **Services**: injected into ViewModels via constructor injection.

All UI updates from background threads go through `Dispatcher.InvokeAsync`. The UI thread never waits on LLM inference, WMI queries, or file system operations.

---

## Telemetry and Supervision

### Structured Logging

All services log through `LoggingService`, which delegates to Serilog with daily rolling file sinks stored at `%APPDATA%/Coclico/logs/`. Log files are retained for 14 days and automatically pruned. Every `catch` block includes structured exception logging with the originating class and method name. Critical operations (AI inference, AST analysis, process priority changes, memory operations, workflow execution) include entry and exit trace logs.

### Audit Trail

`AuditLogService` writes append-only NDJSON (newline-delimited JSON) entries to `%APPDATA%/Coclico/audit/`. Each entry records:

- Timestamp, action type, and actor (user or engine)
- `AiDecisionContext` for LLM-driven decisions: prompt hash, response summary, and confidence score
- Operation outcome and duration

Automatic pruning runs at startup based on the configurable `AuditRetentionDays` setting.

### Runtime Telemetry

`DynamicTracerService` provides fine-grained runtime observability:

- **Operation spans**: start/end timestamps, duration, and success/failure status for every traced operation.
- **Quantile estimation**: uses a T-Digest algorithm for P2 quantile estimation, computing P50, P95, and P99 latencies with minimal memory overhead.
- **Anomaly detection**: combines Exponentially Weighted Moving Average (EWMA) for trend tracking with an Isolation Forest detector for identifying statistical outliers in telemetry streams.

### Resource Monitoring

`ResourceGuardService` continuously monitors the application's own resource footprint:

- CPU usage, working set size, and private memory consumption
- **Pressure level classification**: Normal, Elevated, High, Critical
- Automatic GC triggering and working set trimming when pressure reaches elevated thresholds
- Observable events for other services to react to pressure changes

---

## Security Model

Coclico's security architecture is built on six principles:

### Zero Cloud

All processing, storage, AI inference, and telemetry remain strictly local. No data is transmitted to external servers. There are no analytics endpoints, no crash reporting services, and no update telemetry. The application functions identically with or without network connectivity.

### Rollback Safety

Every file modification triggered by the AI engine or by system maintenance operations is preceded by a snapshot created through `IRollbackService.CreateSnapshot()`. Snapshots capture the original file content before any write occurs, enabling full restoration if an operation produces undesirable results.

### Digital Twin Gate

AI-proposed code patches must pass through `IStateValidator` validation. The digital twin parses both the original and patched code with Roslyn, computes cyclomatic complexity for each affected method, and rejects any patch that would increase complexity. This prevents the AI from introducing structural degradation.

### Audit-Only Mode

The `CodePatcherAuditOnly` setting (enabled by default) ensures that AI-generated patch proposals are logged to the audit trail but never applied automatically. A human operator must explicitly review and approve each patch through the approval API (`ApproveAndApplyAsync`). This setting is exposed in the Settings UI under "AI & Audit" for easy configuration.

### Security Policy

`ISecurityPolicy` / `SecurityPolicyService` loads configurable rules from `%APPDATA%/Coclico/security-policy.json` and merges them with hardcoded non-suppressible defaults. The hardcoded defaults cannot be overridden by user configuration, ensuring that critical safety invariants (such as requiring rollback snapshots before writes) are always enforced. All system-modifying operations in flow chains pass through this policy layer.

### Audit Log Integrity

The audit log is append-only NDJSON. Each entry includes an `AiDecisionContext` when the action was driven by the LLM, providing full traceability of what the AI was asked, what it responded, and what confidence level it reported. This enables post-hoc review of every autonomous decision.

---

## Technology Stack

| Component | Technology | Version |
|---|---|---|
| Runtime | .NET | 10.0 |
| Language | C# | Preview (latest features) |
| UI Framework | WPF + WPF-UI | 4.2.0 |
| Local AI | LLamaSharp | 0.26.0 |
| AST Analysis | Roslyn (Microsoft.CodeAnalysis.CSharp) | 4.12.0 |
| Dependency Injection | Microsoft.Extensions.DependencyInjection | 10.0.0 |
| Hosting | Microsoft.Extensions.Hosting | 10.0.0 |
| Caching | Microsoft.Extensions.Caching.Memory | 10.0.0 |
| HTTP | Microsoft.Extensions.Http | 10.0.0 |
| MVVM | CommunityToolkit.Mvvm | 8.4.0 |
| Reactive Extensions | System.Reactive | 6.0.1 |
| Logging | Serilog + Serilog.Sinks.File | 4.2.0 / 6.0.0 |
| System Management | System.Management | 10.0.0 |
| Service Control | System.ServiceProcess.ServiceController | 10.0.0 |
| Testing | xUnit + Moq | -- |

### Performance Configuration

The project file enables several .NET runtime optimizations:

- **TieredPGO**: Profile-Guided Optimization for hot path recompilation
- **TieredCompilation**: background JIT recompilation of frequently called methods
- **OptimizationPreference**: set to `Speed` for throughput over binary size
- **ConcurrentGarbageCollection**: enabled for reduced GC pause times on multi-core systems

---

## Project Structure

```
Coclico/
    Coclico.slnx                     Solution file

    README.md                        This file (English)
    README.fr.md                     French README
    Coclico_Setup.iss                Inno Setup installer script
    Coclico/
        App.xaml.cs                  Application entry point, DI registration, UAC enforcement
        MainWindow.xaml.cs           Primary navigation window with sidebar
        Converters/                  WPF value converters (6 files)
            BooleanToVisibilityInvertedConverter.cs
            EqualityToBooleanConverter.cs
            FileIconToImageSourceConverter.cs
            HexToBrushConverter.cs
            ObjectEqualsConverter.cs
            StringFirstCharConverter.cs
        Models/                      Domain models
            WorkflowPipeline.cs      Flow chain definition and node types
            LaunchMode.cs            Startup configuration modes
        Services/                    Core business logic (50+ files)
            I*.cs                    Service contracts (12 interfaces)
            *Service.cs              Service implementations
            Algorithms/              Specialized algorithms
                IsolationForestDetector.cs   Anomaly detection
                MethodReplacer.cs            AST method replacement
                SimpleStemmer.cs             Text stemming for RAG
                TDigest.cs                   Quantile estimation
            ServiceContainer.cs      DI container wrapper (lazy, thread-safe)
            SecurityHelpers.cs       Input sanitization and path validation
            SeverityClassifier.cs    Threat severity classification
            FeatureExecutionEngine.cs Circuit-breaker wrapped feature execution
        ViewModels/                  MVVM ViewModels (5 files)
            DashboardViewModel.cs
            CleaningViewModel.cs
            ScannerViewModel.cs
            SettingsViewModel.cs
            WorkflowPipelinesViewModel.cs
        Views/                       WPF Views and code-behind (17 files)
            DashboardView.xaml       Real-time system monitoring
            ProgramsView.xaml        Application library
            WorkflowPipelinesView.xaml Flow chain editor
            InstallerView.xaml       Winget GUI
            CleaningView.xaml        Disk cleaning
            ScannerView.xaml         Application audit
            RamCleanerView.xaml      Memory optimization
            AiChatView.xaml          AI assistant chat
            SettingsView.xaml        Application settings
            HelpView.xaml            Embedded help
            SplashWindow.xaml        Startup splash screen
            LauncherWindow.xaml      First-run configuration
            ProfileWindow.xaml       User profile editor
            AdminPromptWindow.xaml   UAC elevation prompt
            AvatarCropWindow.xaml    Profile avatar cropping
            RuleSelectionDialog.xaml Security rule picker
            PipelineLink.cs          Flow chain visual connector
        Resources/
            Lang/                    Localization resource dictionaries
                de.xaml              German
                en.xaml              English
                es.xaml              Spanish
                fr.xaml              French
            icone/                   Application icons
            software_list.json       Installer software catalog
        resource/
            docs/                    Embedded help documentation (11 files)
                dashboard.md         Dashboard module help
                applications.md      Applications module help
                flow_chains.md       Flow chains module help
                installeur.md        Installer module help
                nettoyage.md         Cleaning module help
                scanner.md           Scanner module help
                ram_cleaner.md       RAM cleaner module help
                parametres.md        Settings help
                general.md           General usage help
                ADR-007.md           Architecture Decision Record
                ADR-008.md           Architecture Decision Record
            model/                   LLM model file (GGUF format)
    Coclico.Tests/                   xUnit + Moq test suite (26 test files)
        ServiceCoreTests.cs          Core service unit tests
        AutonomousEngineTests.cs     Optimization engine tests
        AlgorithmTests.cs            Algorithm implementation tests
        SecurityAndExecutionTests.cs Security layer tests
        FlowExecutionTests.cs        Flow chain execution tests
        IntegrationTests.cs          Cross-service integration tests
        ...                          (and 20 more test files)
    website/                         Astro + Tailwind CSS source for the project website
    docs/                            Built website output (GitHub Pages)
```

---

## Running Tests

The test suite uses xUnit as the test framework and Moq for dependency mocking. Tests cover service logic, algorithm correctness, converter behavior, ViewModel interactions, security enforcement, and cross-service integration.

```bash
# Run all tests
dotnet test

# Run a specific test class
dotnet test --filter "FullyQualifiedName~ServiceCoreTests"

# Run tests matching a pattern
dotnet test --filter "FullyQualifiedName~Security"

# Run with detailed output
dotnet test --verbosity normal
```

---

## Configuration

### Application Settings

Coclico stores its settings in `%APPDATA%/Coclico/`. Key configurable values (managed through `SettingsService` and the Settings UI):

| Setting | Description | Default |
|---|---|---|
| `CodePatcherAuditOnly` | When enabled, AI patches are logged but not applied | `true` |
| `AuditRetentionDays` | Number of days to retain audit log entries | 30 |
| `aiIdleTimeoutMinutes` | Minutes of inactivity before unloading the LLM from memory | 5 |
| `Theme` | Application theme (Light, Dark, System) | System |
| `Language` | Interface language (en, fr, de, es) | System locale |

### Security Policy

The security policy file at `%APPDATA%/Coclico/security-policy.json` allows customization of enforcement rules for flow chain execution. User-defined rules are merged with hardcoded non-suppressible defaults at load time.

### Log Files

Structured logs are written to `%APPDATA%/Coclico/logs/` as daily rolling files with 14-day retention. Audit entries are written to `%APPDATA%/Coclico/audit/` in NDJSON format.

---

## Localization

The interface is available in four languages:

| Language | Resource File |
|---|---|
| English | `Resources/Lang/en.xaml` |
| French | `Resources/Lang/fr.xaml` |
| German | `Resources/Lang/de.xaml` |
| Spanish | `Resources/Lang/es.xaml` |

Language can be switched at runtime through the Settings view. The `LocalizationService` handles resource dictionary swapping without requiring an application restart.

The project website at [coclico-cy.github.io/Coclico](https://coclico-cy.github.io/Coclico/) is also available in all four languages, built with Astro and Tailwind CSS.

---

## Contributing

Contributions are welcome. To contribute:

1. Fork the repository on [GitHub](https://github.com/Coclico-cy/Coclico).
2. Create a feature branch from `main`.
3. Follow the existing coding conventions:
   - Classes and interfaces: `PascalCase`
   - Private fields: `_camelCase`
   - Async methods: suffix with `Async`
   - Constructor injection for all dependencies
   - Log through `LoggingService` -- never swallow exceptions silently
   - Use `ConfigureAwait(false)` in background services
   - Use `record` types for DTOs and telemetry snapshots
4. Add or update tests for any new functionality.
5. Ensure `dotnet build Coclico/Coclico.csproj` and `dotnet test` pass cleanly.
6. Open a pull request with a clear description of the changes.

---

## Open Source

This project is open source with no license. You are free to use, modify, and distribute it.

© 2026 Coclico-cy

---

## Language & AI Assistance

The French, German, and Spanish translations in this project were reviewed and corrected with the assistance of [Claude](https://claude.ai) (Anthropic), which also helps maintain linguistic consistency across all supported languages.
