# 🏖️ Coclico — Enterprise Windows Management Platform

![Version](https://img.shields.io/badge/Version-1.0.3%20Beta-blue)
![License](https://img.shields.io/badge/License-Proprietary-red)
![Platform](https://img.shields.io/badge/Platform-Windows%2010%2B%20%26%20Windows%2011-0078d4)
![Built with](https://img.shields.io/badge/Built%20with-.NET%2010%20%26%20WPF-512bd4)

> **Surveillance. Automation. Cleaning. Privacy.**
> 
> A unified Windows system management suite with 8 powerful modules, local AI, secure automation workflows, and zero cloud dependency.

---

## 🎯 Quick Start

### System Requirements
- **OS**: Windows 10 (v2004+) or Windows 11
- **.NET**: 10.0 or later
- **RAM**: 512 MB minimum
- **Admin Rights**: Required for OS-level operations (UAC elevation on launch)

### Installation
1. Download the latest release from [Releases](https://github.com/YOUR_GITHUB_USERNAME/Coclico/releases)
2. Extract `Coclico-setup.exe`
3. Run the installer (admin required)
4. Launch Coclico from Start Menu

### Build from Source
```bash
# Clone repository
git clone https://github.com/YOUR_GITHUB_USERNAME/Coclico.git
cd Coclico

# Build
dotnet build Coclico.slnx -c Release

# Run (requires admin)
dotnet run --project Coclico/Coclico.csproj

# Run tests
dotnet test Coclico.Tests/Coclico.Tests.csproj

# Publish (creates standalone .exe)
dotnet publish Coclico/Coclico.csproj -c Release -r win-x64 --self-contained
```

---

## 📦 8 Core Modules

### 1. **Dashboard** 📊
Real-time system monitoring (updated every 3 seconds):
- CPU usage, RAM (used/total), C: drive space
- Windows uptime, active processes, installed apps count
- Quick system health overview

### 2. **Applications** 🎮
Complete software library from 8 sources:
- Windows Registry (HKLM + HKCU)
- Steam, Epic Games, GOG, Ubisoft, EA App, Rockstar, Microsoft Store
- Launch, rename, categorize, add custom .exe files
- Persistent custom categories and groups

### 3. **Flow Chains** ⚙️
Visual automation editor with 30+ node types:
- Drag-and-drop workflow builder
- Nodes: Start/End, OpenApp, CloseApp, RunCommand, KillProcess, Delay, Condition, Loop, Parallel, Notification, HttpRequest, FileOperation, and more
- Global keyboard shortcuts (Ctrl+Key, Ctrl+Alt+Key)
- Retry & timeout per node
- JSON storage in `%AppData%\Coclico\`

### 4. **Installer** 📥
Winget GUI with curated software catalog:
- Categories: Internet, Runtimes, Development, Gaming, Creation, System
- Install, update, search, auto-repair Winget sources
- 100+ pre-configured applications

### 5. **System Cleaner** 🧹
Professional cleaning engine with 10 categories:
- Windows temp files, browser caches (Chrome/Firefox/Edge)
- System logs, recycle bin, user temp, thumbnails
- Windows error reports, old installers, DNS cache, prefetch
- Pre-scan estimation, category selection, final report with freed space

### 6. **Scanner** 🔍
Comprehensive application audit:
- Detect all installed apps from all sources
- Display: version, publisher, size, install path, source
- Export-ready format

### 7. **RAM Cleaner** 💾
Memory management & monitoring:
- Real-time physical RAM, virtual RAM, page file tracking
- 9 cleaning profiles + extras
- Manual or automatic cleanup (by interval or % threshold)

### 8. **Settings** ⚙️️
Full customization suite:
- Accent color picker (#RRGGBB), theme, card opacity, font size
- Sidebar width, compact mode, language (FR/EN/DE/ES/IT/JA/KO/PT/RU/ZH)
- Windows startup integration, system tray minimize
- Global keyboard shortcuts configuration
- Saved to `%AppData%\Coclico\settings.json`

---

## 🤖 Coclico AI Assistant

Integrated conversational AI (100% local, zero cloud):
- Model: GGUF local inference via LLamaSharp
- Features: Q&A, tutorials, step-by-step guidance, action commands
- Privacy: No internet, no telemetry, all processing on-device
- Context: Built-in documentation with RAG (Retrieval-Augmented Generation)

**Model file** (not included in repo, ~5GB):
```
resource/model/IA-support-chat.gguf
```
Download from your model provider and place in `resource/model/` to enable AI.

---

## 🏗️ Architecture

### Tech Stack
- **UI Framework**: WPF + WPF-UI 4.2.0 (Fluent Design)
- **Language**: C# with .NET 10, LangVersion=preview
- **MVVM**: CommunityToolkit.Mvvm (ObservableObject, RelayCommand, source generators)
- **AI**: LLamaSharp 0.26.0 with CPU backend
- **Logging**: Serilog → `%AppData%\Coclico\logs\` (rolling daily, 14-day retention)
- **Testing**: xUnit + Moq

### Project Structure
```
Coclico/
├── Coclico.csproj              # Main WPF app (.NET 10)
├── Coclico.Tests.csproj        # xUnit test suite
├── Views/                        # XAML + code-behind
├── ViewModels/                   # MVVM ViewModels
├── Services/                     # Business logic (singletons & transients)
├── Models/                       # Data models (FlowChain, LaunchMode, etc.)
├── Converters/                   # XAML value converters
├── Resources/
│   ├── Lang/                    # WPF ResourceDictionaries (fi.xaml, en.xaml, etc.)
│   ├── software_list.json       # Installer catalog
│   └── icone/                   # Application icons
├── resource/
│   ├── docs/                    # Markdown documentation
│   └── model/                   # GGUF AI model (place here)
│
tests/
├── Coclico.Tests.csproj
├── ServiceCoreTests.cs
├── FeatureExecutionEngineTests.cs
├── RagServiceTests.cs
└── ... (15+ test files)

docs/                           # Web documentation (static HTML + JSON i18n)
├── index.html
├── docs.html
├── en.json, fr.json, de.json, ... (10 languages)
├── script.js
└── style.css
```

### Dependency Injection
Custom `ServiceContainer` pattern (wraps `Microsoft.Extensions.DependencyInjection`):

**Singletons** (static Instance + DI):
- `SettingsService`, `ThemeService`, `LocalizationService`
- `AiChatService`, `NetworkMonitorService`, `ProcessWatcherService`
- `AppResourceGuardService`, `KeyboardShortcutsService`
- `FeatureExecutionEngine`, `FlowExecutionService`, `ProfileService`
- `InstalledProgramsService`

**Transients** (DI only):
- `CleaningService`, `DeepCleaningService`, `InstallerService`
- `FlowChainService`, `FlowChainExecutionService`
- `StartupHealthService`, `UserAccountService`

---

## 🔄 Auto-Update System

Coclico includes `UpdateManager.cs` for GitHub Releases integration:

```csharp
var updateMgr = new UpdateManager(logger, settingsService);

// Check for updates
var latest = await updateMgr.CheckForUpdatesAsync("1.0.3", cancellationToken);
if (latest != null)
{
    // Download and install
    await updateMgr.DownloadReleaseAsync(latest, "Coclico_Setup.exe", "./downloads/", ct);
    updateMgr.LaunchInstaller("./downloads/Coclico_Setup.exe");
}
```

**Configuration** (in `UpdateManager.cs`):
```csharp
private const string GitHubOwner = "YOUR_GITHUB_USERNAME";
private const string GitHubRepo = "Coclico";
```

---

## 🌐 Localization

### Supported Languages
- 🇫🇷 Français
- 🇬🇧 English
- 🇩🇪 Deutsch
- 🇪🇸 Español
- 🇮🇹 Italiano
- 🇯🇵 日本語
- 🇰🇷 한국어
- 🇵🇹 Português
- 🇷🇺 Русский
- 🇨🇳 中文

### Translation Files
- **App UI**: `Coclico/Resources/Lang/{lang}.xaml` (WPF ResourceDictionaries)
- **Web Docs**: `docs/{lang}.json` (i18n JSON)

Change language in Settings → instant UI refresh.

---

## 📝 Flow Chains — 30+ Node Types

Supported automation nodes:
```
Start, End, OpenApp, CloseApp, RunCommand, KillProcess, Delay, 
Condition, Loop, Parallel, Notification, HttpRequest, FileOperation, 
SystemCheck, RunPowerShell, OpenUrl, SetVolume, MuteAudio, 
SetProcessPriority, KillByMemory, CleanTemp, RamClean, ServiceStart, 
ServiceStop, RegistrySet, ClipboardSet, Screenshot, SendKeys, 
CompressFile, MonitorWait, LogMessage, SetEnvVar, CheckInternet, 
PlaySound, FocusWindow, RenameFile, EmptyRecycleBin, WakeOnLan
```

---

## 🔐 Security & Privacy

✅ **100% Local Processing**
- No cloud services, no telemetry
- All data stored in `%AppData%\Coclico\` (JSON)
- Admin elevation only when needed (UAC)

✅ **Built-In Security Tools**
- Secret scanning (regex-based detection)
- System audit & app verification
- Secure process management

✅ **Code Quality**
- xUnit test suite (15+ test files)
- Serilog comprehensive logging
- Error handling with graceful degradation

---

## 📊 Performance

### Optimizations
- **.NET 10 PGO (Tiered)**: Profiling-guided optimization
- **Concurrent GC**: Parallel garbage collection
- **Task Parallel**: Multi-threaded operations (UI responsive)
- **Lazy Initialization**: Services load on-demand

### Memory Footprint
- ~150-200 MB baseline (WPF + AI model when loaded)
- ~500 MB with full AI model in memory

---

## 🐛 Troubleshooting

### Common Issues

**"Admin required"**
→ Coclico requires administrator privileges. Right-click → Run as Administrator

**AI Chat not working**
→ Download GGUF model and place in `resource/model/IA-support-chat.gguf`

**Winget errors**
→ Ensure "App Installer" is installed from Microsoft Store (Windows 10 21H2+, auto on Win11)

**Build errors (.NET 10)**
→ Install .NET 10 SDK: https://dotnet.microsoft.com/download

---

## 🧪 Testing

```bash
# Run all tests
dotnet test Coclico.Tests/Coclico.Tests.csproj

# Run by category
dotnet test Coclico.Tests/Coclico.Tests.csproj --filter "FullyQualifiedName~ServiceCoreTests"

# Run single test
dotnet test Coclico.Tests/Coclico.Tests.csproj --filter "FullyQualifiedName~ServiceCoreTests.TestMethod"

# With coverage
dotnet test Coclico.Tests/ --collect:"XPlat Code Coverage"
```

---

## 📜 License

**Proprietary License** — © Coclico 2024-2026  
All rights reserved. Unauthorized reproduction prohibited.

For licensing inquiries: [contact info]

---

## 🤝 Contributing

This is a **closed-source commercial project**. External contributions are not accepted at this time.

For bug reports or feature requests, please contact the development team.

---

## 🚀 Release Notes

### v1.0.3 Beta
- ✅ Complete 8-module suite
- ✅ Local AI assistant
- ✅ 30+ Flow Chain nodes
- ✅ Auto-update system (GitHub)
- ✅ 10 language support
- ⚠️ GGUF model file required for AI (external download)

### Roadmap
- [ ] Cloud sync (optional, encrypted)
- [ ] Custom node SDK
- [ ] Performance profiler
- [ ] Native ARM64 support
- [ ] Microsoft Store distribution

---

## 📚 Documentation

- **User Guide**: [Launch → Help → Guide](docs/index.html)
- **Developer Wiki**: [docs/wiki.html](docs/wiki.html)
- **FAQ**: [docs/faq.html](docs/faq.html)
- **API Docs**: See [CLAUDE.md](CLAUDE.md) for architecture & build commands

---

## 💬 Support

- 🐛 **Bug Reports**: [GitHub Issues](https://github.com/YOUR_GITHUB_USERNAME/Coclico/issues)
- 📧 **Email**: support@coclico.dev (placeholder)
- 💬 **Discord**: [Community Server](https://discord.gg/your-invite)

---

**Made with ❤️ by the Coclico Team**
