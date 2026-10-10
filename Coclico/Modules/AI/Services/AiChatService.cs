using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Coclico.Services.AI;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;

namespace Coclico.Services;

public sealed class AiChatService : IAiService, IDisposable
{
    private static readonly string DocsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "resource", "docs");

    private static readonly Lazy<RagService> _rag = new(() =>
    {
        var r = new RagService();
        r.BuildIndex(DocsPath);
        return r;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly object _cudaConfigLock = new();
    private static bool _nativeCudaConfigured;
    private static bool _nativeConfigLocked;
    private static bool _nativeEngineBroken;
    private static string? _cudaLlamaDllPath;

    private static readonly string[] TurnStopTokens =
    [
        "<|eot_id|>",
        "<|end_of_text|>",
        "<|im_end|>",
        "<|end|>",
        "<|start_header_id|>",
        "<|end_header_id|>",
        "<|im_start|>",
        "\nUser:",
        "\nAssistant:",
        "\nUtilisateur:",
        "\nAssistant :"
    ];

    private const string SystemPrompt =
        "Tu es Coclico Copilot, l'assistant IA intégré à Coclico, l'application Windows d'optimisation, de maintenance et de sécurité du PC.\n" +
        "Modules Coclico : Tableau de bord (vue des ressources), Applications (logiciels installés), RAM Cleaner (mémoire vive), Nettoyage (fichiers temporaires, caches, corbeille), Santé & Défense (SFC, DISM, Defender, analyse BSOD), Réseau (optimisation et benchmark), Installeur (Winget), Paramètres.\n" +
        "RÈGLES :\n" +
        "1. Réponds toujours en français, avec courtoisie, précision et concision.\n" +
        "2. QUESTION : si l'utilisateur pose une question ou demande une information, réponds directement à partir de ta connaissance de Coclico et du contexte fourni. N'émets JAMAIS de balise d'action pour une simple question.\n" +
        "3. ACTION : si l'utilisateur demande explicitement une opération système, réponds en UNE seule phrase courte de confirmation, puis insère la balise exacte :\n" +
        "[ACTION:CLEAN_RAM] [ACTION:CLEAN_TEMP] [ACTION:SCAN_SFC] [ACTION:SCAN_DISM] [ACTION:DEFENDER_SCAN] [ACTION:FULL_REPAIR] [ACTION:DIAGNOSE_SYSTEM] [ACTION:ANALYZE_CRASHES] [ACTION:BENCHMARK_NETWORK] [ACTION:SNAPSHOT_NETWORK] [ACTION:RESTORE_NETWORK_SNAPSHOT] [ACTION:RESET_NETWORK] [ACTION:RESET_NETWORK_DEFAULTS] [ACTION:RESET_UPDATE] [ACTION:UPDATE_APPS] [ACTION:OPTIMIZE_NETWORK <gaming|quick|throughput|autotune>] [ACTION:CONFIGURE_RAM_DAEMON <paramètres>] [ACTION:SET_RAM_PROFILE <Smart|Gamer|Extreme|Aggressive>] [ACTION:NAVIGATE <dashboard|programs|ram|network|cleaning|disk|installer|health|settings|help>] [ACTION:OPEN_APP <nom>] [ACTION:INSTALL_WINGET <id>]\n" +
        "4. Une seule balise par action demandée, jamais de balise inventée ou modifiée. Si la demande est ambiguë, demande une précision sans balise.\n" +
        "5. N'annonce jamais une action comme terminée et n'invente aucune statistique système : l'interface Coclico affiche les résultats réels.";

    private const string AppIdentityAnswer =
        "Coclico est un gestionnaire système complet pour Windows, 100 % local et gratuit, conçu pour optimiser, maintenir et sécuriser votre PC.\n\n" +
        "Ses modules : Tableau de bord (vue des ressources), Applications (gestion des logiciels installés), RAM Cleaner (libération de mémoire), Nettoyage (fichiers temporaires, caches, corbeille), Santé & Défense (SFC, DISM, Defender, analyse des écrans bleus), Réseau (optimisation et benchmark DNS), Installeur (Winget) et Paramètres.\n\n" +
        "En tant que Copilot intégré, je peux lancer ces opérations directement depuis cette conversation : dites par exemple « nettoie la RAM », « vide les fichiers temporaires », « lance un scan SFC » ou « fais un diagnostic du PC ». Que souhaitez-vous faire ?";

    private static readonly Regex AppIdentityQuestionRegex = new(
        @"\b(a\s*quoi\s*(sert|sers|servent)|pr[eé]sente(?:\s*(?:toi|l\s*application))?|pr[eé]sentation|parle\s*moi\s*de|que\s*fais\s*tu|tes\s*fonctions|tes\s*capacit[eé]s|tes\s*features|fonctionnalit[eé]s|qu\s*est\s*ce\s*que\s*coclico|c\s*est\s*quoi\s*(?:coclico|cette\s*application|ce\s*logiciel|ce\s*programme))\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const int MaxMemoryTurns = 4;
    private const int AutoResetAfterTurns = 8;
    private const int MaxPromptChars = 11000;

    private readonly SettingsService _settings;
    private readonly MultiProviderClient _multiClient;
    private readonly object _memLock = new();
    private readonly List<(string User, string Ai)> _shortTermMemory = [];
    private readonly SemaphoreSlim _chatSem = new(1, 1);
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private LLamaWeights? _model;
    private volatile ChatCtx? _chatCtx;
    private bool _initialized;
    private Timer? _idleTimer;
    private int _turnsSinceReset;
    private int _unloading;

    private sealed class ChatCtx(LLamaContext ctx) : IDisposable
    {
        public readonly LLamaContext Context = ctx;
        public readonly InteractiveExecutor Executor = new(ctx);

        public void Dispose()
        {
            try
            {
                Context.Dispose();
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "AiChatService.ChatCtx.Dispose");
            }
        }
    }

    public AiChatService(SettingsService settings, MultiProviderClient localClient)
    {
        _settings = settings;
        _multiClient = localClient ?? throw new ArgumentNullException(nameof(localClient));
        MigrateLegacyAiSettings();
    }

    public AiProviderType CurrentProvider
    {
        get => ResolveSupportedProvider(_settings.Settings.AiProvider);
        set
        {
            _settings.Settings.AiProvider = value.ToString();
            _ = SaveSettingsObservedAsync();
        }
    }

    public string CurrentModel
    {
        get => ResolveSupportedModel(CurrentProvider, _settings.Settings.AiModel);
        set
        {
            _settings.Settings.AiModel = value;
            _ = SaveSettingsObservedAsync();
        }
    }

    public string CurrentStatusContext { get; set; } = "Tableau de Bord";

    public bool IsEnabled => _settings.Settings.AiEnabled;
    public bool UseGpu => _settings.Settings.AiUseGpu;
    public bool IsInitialized => CurrentProvider != AiProviderType.LocalGGUF || _initialized;
    public int ActiveGpuLayers { get; private set; }
    public static bool IsModelAvailable => File.Exists(ModelPath);
    public static string ModelPath => ResolveModelFile("IA-support-chat.gguf");

    private static int OptimalThreads => Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

    public bool IsModelDownloaded(string? modelId = null)
    {
        string id = modelId ?? CurrentModel;
        string path = GetLocalModelFullPath(id);
        return File.Exists(path) || (id == "IA-support-chat.gguf" && File.Exists(ModelPath));
    }

    public string GetLocalModelFullPath(string modelFileName)
    {
        return ResolveModelFile(modelFileName);
    }

    private static string ResolveModelFile(string fileName)
    {
        string primary = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resource", "model", fileName);
        if (File.Exists(primary))
        {
            return primary;
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "..", "..", "..", "..", "Coclico", "bin", "Debug", "net10.0-windows10.0.22621.0", "resource", "model", fileName),
            Path.Combine(baseDir, "..", "..", "..", "..", "Coclico", "bin", "Release", "net10.0-windows10.0.22621.0", "resource", "model", fileName),
            Path.Combine(baseDir, "..", "..", "..", "..", "Coclico", "resource", "model", fileName),
            Path.Combine(baseDir, "..", "..", "..", "..", "Coclico", "publish", "resource", "model", fileName)
        ];

        foreach (string candidate in candidates)
        {
            string full = Path.GetFullPath(candidate);
            if (File.Exists(full))
            {
                return full;
            }
        }

        return primary;
    }

    private async Task SaveSettingsObservedAsync()
    {
        try
        {
            await _settings.SaveAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AiChatService.SaveSettings");
        }
    }

    private void MigrateLegacyAiSettings()
    {
        bool providerIsSupported = Enum.TryParse(_settings.Settings.AiProvider, out AiProviderType parsedProvider) &&
            Enum.IsDefined(parsedProvider);
        AiProviderType provider = ResolveSupportedProvider(_settings.Settings.AiProvider);

        bool modelIsSupported = string.Equals(
            ResolveSupportedModel(provider, _settings.Settings.AiModel),
            _settings.Settings.AiModel,
            StringComparison.OrdinalIgnoreCase);
        if (modelIsSupported && providerIsSupported)
        {
            return;
        }

        _settings.Settings.AiProvider = provider.ToString();
        _settings.Settings.AiModel = AiModelCatalog.GetDefaultModel(provider).Id;
        _ = SaveSettingsObservedAsync();
    }

    internal static AiProviderType ResolveSupportedProvider(string? savedProvider)
    {
        return Enum.TryParse(savedProvider, out AiProviderType provider) && Enum.IsDefined(provider)
            ? provider
            : AiProviderType.LocalGGUF;
    }

    internal static string ResolveSupportedModel(AiProviderType provider, string? savedModel)
    {
        return AiModelCatalog.GetModelsForProvider(provider)
            .FirstOrDefault(model => string.Equals(model.Id, savedModel, StringComparison.OrdinalIgnoreCase))
            ?.Id ?? AiModelCatalog.GetDefaultModel(provider).Id;
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        _settings.Settings.AiEnabled = enabled;
        await _settings.SaveAsync().ConfigureAwait(false);
        if (!enabled)
        {
            await UnloadLocalModelAsync().ConfigureAwait(false);
            _ = MemoryCleanerService.ForceGcCollect();
            LoggingService.LogInfo("[AiChatService] Copilot désactivé : mémoire libérée (0 Mo RAM/VRAM).");
        }
    }

    public async Task SetHardwareModeAsync(bool useGpu, int layers = 99)
    {
        _settings.Settings.AiUseGpu = useGpu;
        _settings.Settings.AiGpuLayers = Math.Clamp(layers, 0, 128);
        await _settings.SaveAsync().ConfigureAwait(false);

        if (_initialized && CurrentProvider == AiProviderType.LocalGGUF)
        {
            await UnloadLocalModelAsync().ConfigureAwait(false);
            await InitializeAsync().ConfigureAwait(false);
        }
        LoggingService.LogInfo($"[AiChatService] Mode matériel changé : {(useGpu ? $"GPU ({_settings.Settings.AiGpuLayers} layers)" : "CPU pur")}");
    }

    public async Task SwitchProviderAsync(AiProviderType provider, string? model = null)
    {
        AiProviderType oldProvider = CurrentProvider;
        string oldModel = CurrentModel;
        CurrentProvider = provider;

        CurrentModel = !string.IsNullOrWhiteSpace(model) ? model : AiModelCatalog.GetDefaultModel(provider).Id;

        if (oldProvider == AiProviderType.LocalGGUF &&
            (provider != AiProviderType.LocalGGUF || !string.Equals(oldModel, CurrentModel, StringComparison.OrdinalIgnoreCase)))
        {
            await UnloadLocalModelAsync().ConfigureAwait(false);
        }
        else if (oldProvider != AiProviderType.LocalGGUF && provider == AiProviderType.LocalGGUF)
        {
            _initialized = false;
        }

        ResetConversation();
        LoggingService.LogInfo($"[AiChatService] Fournisseur changé pour {provider} (modèle: {CurrentModel})");
    }

    public async Task UnloadAsync()
    {
        await UnloadLocalModelAsync().ConfigureAwait(false);
    }

    public static void EnsureNativeBackendConfigured(bool useGpu)
    {
        _ = TryEnsureNativeBackend(useGpu);
    }

    internal static bool TryEnsureNativeBackend(bool useGpu)
    {
        lock (_cudaConfigLock)
        {
            if (_nativeEngineBroken)
            {
                LoggingService.LogWarning("[AiChatService] Moteur natif en échec irrécupérable dans ce processus : redémarrez Coclico.");
                return false;
            }

            if (!useGpu)
            {
                return TryPrepareCpuBackend();
            }

            if (_nativeCudaConfigured)
            {
                return true;
            }

            if (_nativeConfigLocked)
            {
                LoggingService.LogWarning("[AiChatService] Configuration du moteur natif déjà chargée : impossible de basculer en mode CUDA sans redémarrer Coclico.");
                return false;
            }

            try
            {
                string nativeDir = AiHardwareService.ResolveNativeDirectory();
                string? cuda12Dir = AiHardwareService.ResolveCudaDirectory();
                string avx2Dir = Path.Combine(nativeDir, "avx2");

                if (cuda12Dir == null)
                {
                    LoggingService.LogInfo("[AiChatService] Runtime CUDA 12 absent du disque : bascule AVX2/CPU.");
                    return TryPrepareCpuBackend();
                }

                string? cudart = AiHardwareService.ResolveDllPath("cudart64_12.dll");
                string? cublas = AiHardwareService.ResolveDllPath("cublas64_12.dll");
                string? cublasLt = AiHardwareService.ResolveDllPath("cublasLt64_12.dll");
                string? cufft = AiHardwareService.ResolveDllPath("cufft64_11.dll");
                string? nvrtc = AiHardwareService.ResolveDllPath("nvrtc64_120_0.dll");

                if (cudart == null || cublas == null || cublasLt == null || cufft == null || nvrtc == null)
                {
                    LoggingService.LogWarning("[AiChatService] Dépendances CUDA incomplètes (cublas/cudart manquantes) : bascule AVX2/CPU sans toucher au moteur natif.");
                    return TryPrepareCpuBackend();
                }

                string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                string curPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                string extendedPath = $"{cuda12Dir};{nativeDir};{avx2Dir};{baseDir};{curPath}";
                if (!curPath.StartsWith(cuda12Dir + ";", StringComparison.OrdinalIgnoreCase))
                {
                    Environment.SetEnvironmentVariable("PATH", extendedPath);
                }

                string llamaCuda = Path.Combine(cuda12Dir, "llama.dll");
                string ggmlBase = Path.Combine(cuda12Dir, "ggml-base.dll");
                string ggmlCuda = Path.Combine(cuda12Dir, "ggml-cuda.dll");
                string ggmldll = Path.Combine(cuda12Dir, "ggml.dll");
                string? ggmlCpuPath = ResolveCpuNativeDll("ggml-cpu.dll");

                if (ggmlCpuPath == null)
                {
                    LoggingService.LogWarning("[AiChatService] ggml-cpu.dll introuvable (native/avx2) : indispensable au backend CUDA et au repli CPU.");
                }

                bool ready = ggmlCpuPath == null
                    ? TryLoadRequired(cudart, cublasLt, cublas, cufft, nvrtc, ggmlBase, ggmlCuda, ggmldll, llamaCuda)
                    : TryLoadRequired(cudart, cublasLt, cublas, cufft, nvrtc, ggmlBase, ggmlCpuPath, ggmlCuda, ggmldll, llamaCuda);

                if (!ready)
                {
                    LoggingService.LogWarning("[AiChatService] Échec du chargement des bibliothèques CUDA : bascule AVX2/CPU sans toucher au moteur natif.");
                    return TryPrepareCpuBackend();
                }

                try
                {
                    _cudaLlamaDllPath = llamaCuda;
                    _ = NativeLibraryConfig.LLama.WithLibrary(llamaCuda);
                    TryApplyLibraryConfig(useGpu: true);
                }
                catch (InvalidOperationException)
                {
                    _nativeConfigLocked = true;
                    LoggingService.LogWarning("[AiChatService] Bibliothèque LLama déjà chargée : impossible de sélectionner llama.dll CUDA sans redémarrer Coclico.");
                    return false;
                }

                _nativeCudaConfigured = true;
                LoggingService.LogInfo("[AiChatService] Backend NVIDIA CUDA 12 initialisé avec succès.");
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "AiChatService.EnsureNativeBackendConfigured");
                LoggingService.LogWarning($"[AiChatService] Impossible d'initialiser le backend GPU CUDA 12 : {ex.Message}. Exécution sur CPU (AVX2).");
                return TryPrepareCpuBackend();
            }
        }
    }

    private static bool TryPrepareCpuBackend()
    {
        string nativeDir = AiHardwareService.ResolveNativeDirectory();
        string avx2Dir = Path.Combine(nativeDir, "avx2");

        string? llamaCpu = ResolveCpuNativeDll("llama.dll");
        if (llamaCpu == null)
        {
            LoggingService.LogWarning($"[AiChatService] llama.dll CPU introuvable dans '{nativeDir}' : dossier runtimes\\win-x64\\native incomplet. Réinstallez Coclico.");
            LogNativeDiagnostics();
            return false;
        }

        string curPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        string extendedPath = $"{nativeDir};{avx2Dir};{curPath}";
        if (!curPath.StartsWith(nativeDir + ";", StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("PATH", extendedPath);
        }

        if (!TryLoadRequired(llamaCpu))
        {
            LoggingService.LogWarning("[AiChatService] Chargement du moteur CPU impossible : DLL natives manquantes ou incompatibles. Réinstallez Coclico (runtimes\\win-x64\\native).");
            LogNativeDiagnostics();
            return false;
        }

        string? mtmd = ResolveCpuNativeDll("mtmd.dll");
        if (mtmd != null)
        {
            TryLoadOptional(mtmd);
        }

        TryApplyLibraryConfig(useGpu: false);
        return true;
    }

    private static void TryApplyLibraryConfig(bool useGpu)
    {
        if (_nativeConfigLocked)
        {
            return;
        }

        try
        {
            if (useGpu)
            {
                _ = NativeLibraryConfig.All
                    .WithCuda(true)
                    .SkipCheck(true)
                    .WithAutoFallback(false);
            }
            else
            {
                _ = NativeLibraryConfig.All.WithCuda(false).WithAutoFallback(true);
            }

            _nativeConfigLocked = true;
        }
        catch (InvalidOperationException)
        {
            _nativeConfigLocked = true;
            LoggingService.LogWarning("[AiChatService] Configuration du moteur natif déjà appliquée (bibliothèque chargée) : reconfiguration impossible sans redémarrer Coclico.");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AiChatService.TryApplyLibraryConfig");
        }
    }

    private static string? ResolveCpuNativeDll(string fileName)
    {
        string nativeDir = AiHardwareService.ResolveNativeDirectory();
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(nativeDir, fileName),
            Path.Combine(nativeDir, "avx2", fileName),
            Path.Combine(nativeDir, "avx", fileName),
            Path.Combine(nativeDir, "avx512", fileName),
            Path.Combine(baseDir, fileName),
            Path.Combine(baseDir, "runtimes", "win-x64", "native", fileName)
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool TryLoadRequired(params string[] paths)
    {
        foreach (string path in paths)
        {
            if (!File.Exists(path))
            {
                LoggingService.LogWarning($"[AiChatService] Bibliothèque native introuvable : '{path}'.");
                return false;
            }

            try
            {
                _ = System.Runtime.InteropServices.NativeLibrary.Load(path);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning($"[AiChatService] Chargement natif impossible '{path}' : {ex.Message}");
                return false;
            }
        }

        return true;
    }

    private static void TryLoadOptional(string path)
    {
        try
        {
            _ = System.Runtime.InteropServices.NativeLibrary.Load(path);
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[AiChatService] Préchargement optionnel impossible '{path}' : {ex.Message}");
        }
    }

    private static void LogNativeDiagnostics()
    {
        try
        {
            string nativeDir = AiHardwareService.ResolveNativeDirectory();
            string? cuda12Dir = AiHardwareService.ResolveCudaDirectory();
            string avx2Dir = Path.Combine(nativeDir, "avx2");

            var report = new StringBuilder();
            report.Append("[AiChatService] Diagnostic natif — native: '").Append(nativeDir).Append("'");
            report.Append(" | llama.dll: ").Append(DescribePresence(Path.Combine(nativeDir, "llama.dll"), Path.Combine(avx2Dir, "llama.dll")));
            report.Append(", ggml.dll: ").Append(DescribePresence(Path.Combine(nativeDir, "ggml.dll"), Path.Combine(avx2Dir, "ggml.dll")));
            report.Append(", ggml-base.dll: ").Append(DescribePresence(Path.Combine(nativeDir, "ggml-base.dll"), Path.Combine(avx2Dir, "ggml-base.dll")));
            report.Append(", ggml-cpu.dll: ").Append(DescribePresence(Path.Combine(nativeDir, "ggml-cpu.dll"), Path.Combine(avx2Dir, "ggml-cpu.dll")));
            report.Append(", mtmd.dll: ").Append(DescribePresence(Path.Combine(nativeDir, "mtmd.dll"), Path.Combine(avx2Dir, "mtmd.dll")));

            report.Append(cuda12Dir == null
                ? " | cuda12: ABSENT"
                : " | cuda12: llama.dll: " + DescribePresence(Path.Combine(cuda12Dir, "llama.dll")) +
                  ", ggml.dll: " + DescribePresence(Path.Combine(cuda12Dir, "ggml.dll")) +
                  ", ggml-base.dll: " + DescribePresence(Path.Combine(cuda12Dir, "ggml-base.dll")) +
                  ", ggml-cuda.dll: " + DescribePresence(Path.Combine(cuda12Dir, "ggml-cuda.dll")));

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            report.Append(" | racine: cudart64_12.dll: ").Append(DescribePresence(Path.Combine(baseDir, "cudart64_12.dll")));
            report.Append(", cublas64_12.dll: ").Append(DescribePresence(Path.Combine(baseDir, "cublas64_12.dll")));
            report.Append(", cublasLt64_12.dll: ").Append(DescribePresence(Path.Combine(baseDir, "cublasLt64_12.dll")));
            report.Append(", cufft64_11.dll: ").Append(DescribePresence(Path.Combine(baseDir, "cufft64_11.dll")));
            report.Append(", nvrtc64_120_0.dll: ").Append(DescribePresence(Path.Combine(baseDir, "nvrtc64_120_0.dll")));

            LoggingService.LogWarning(report.ToString());
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AiChatService.LogNativeDiagnostics");
        }
    }

    private static string DescribePresence(params string[] paths)
    {
        foreach (string path in paths)
        {
            if (File.Exists(path))
            {
                return "OK (" + path + ")";
            }
        }

        return "ABSENT";
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (!_settings.Settings.AiEnabled)
        {
            LoggingService.LogInfo("[AiChatService] Initialisation ignorée : Copilot désactivé par l'utilisateur.");
        }
        else if (CurrentProvider != AiProviderType.LocalGGUF)
        {
            _initialized = true;
        }
        else
        {
            await InitializeLocalModelAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task InitializeLocalModelAsync(CancellationToken ct)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            if (_nativeEngineBroken)
            {
                throw new InvalidOperationException("Le moteur natif IA est en échec depuis un précédent essai dans ce processus. Redémarrez Coclico et vérifiez les DLL natives (llama.dll, ggml.dll, ggml-base.dll, ggml-cpu.dll) dans le dossier d'installation.");
            }

            string targetPath = ResolveLocalModelPath();
            bool useGpuRequested = _settings.Settings.AiUseGpu;
            AiGpuVendor vendor = AiHardwareService.DetectVendor();
            bool cudaReady = AiHardwareService.IsCudaRuntimeAvailable();
            bool gpuAllowed = useGpuRequested && vendor == AiGpuVendor.Nvidia && cudaReady;
            int targetLayers = gpuAllowed
                ? Math.Clamp(_settings.Settings.AiGpuLayers > 0 ? _settings.Settings.AiGpuLayers : 99, 1, 99)
                : 0;

            if (useGpuRequested && !gpuAllowed)
            {
                if (vendor == AiGpuVendor.Amd)
                {
                    LoggingService.LogWarning("[AiChatService] GPU AMD détecté : aucune accélération IA disponible sur Windows pour AMD, exécution sur CPU (AVX2).");
                }
                else if (vendor == AiGpuVendor.Nvidia && !cudaReady)
                {
                    LoggingService.LogWarning("[AiChatService] GPU NVIDIA détecté mais runtime CUDA 12 absent : exécution sur CPU (AVX2). Le runtime est téléchargeable depuis le module IA ou l'installeur Coclico.");
                }
                else
                {
                    LoggingService.LogWarning("[AiChatService] Aucun GPU NVIDIA détecté : exécution sur CPU (AVX2).");
                }
            }

            bool fellBackFromGpu = false;

            if (gpuAllowed)
            {
                if (TryEnsureNativeBackend(useGpu: true))
                {
                    try
                    {
                        await LoadModelAsync(targetPath, targetLayers, ct).ConfigureAwait(false);
                        ActiveGpuLayers = targetLayers;
                        _initialized = true;
                        LoggingService.LogInfo($"[AiChatService] Modèle local chargé sur GPU NVIDIA ({targetLayers} couches en VRAM) : {targetPath}");
                        ResetIdleTimer();
                        return;
                    }
                    catch (TypeInitializationException ex)
                    {
                        _nativeEngineBroken = true;
                        LogNativeDiagnostics();
                        throw new InvalidOperationException("Le moteur natif CUDA a échoué à son initialisation et ne peut plus être réinitialisé dans ce processus. Redémarrez Coclico et vérifiez le dossier cuda12 de l'installation.", ex);
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogWarning("[AiChatService] Échec du chargement sur GPU (" + ex.Message + "). Basculement automatique sur CPU...");
                        DisposeModelInternal();
                        fellBackFromGpu = true;
                    }
                }
                else
                {
                    LoggingService.LogWarning("[AiChatService] Backend CUDA indisponible : exécution sur CPU (AVX2).");
                    fellBackFromGpu = true;
                }
            }

            if (!TryEnsureNativeBackend(useGpu: false))
            {
                throw new InvalidOperationException("DLL natives LLama indisponibles : le dossier runtimes\\win-x64\\native de l'installation est incomplet (llama.dll, ggml.dll, ggml-base.dll, ggml-cpu.dll). Réinstallez Coclico ou relancez le téléchargement du runtime natif via l'installeur.");
            }

            try
            {
                await LoadModelAsync(targetPath, 0, ct).ConfigureAwait(false);
            }
            catch (TypeInitializationException ex)
            {
                _nativeEngineBroken = true;
                LogNativeDiagnostics();
                throw new InvalidOperationException("Le moteur natif IA (repli CPU) a échoué à son initialisation et ne peut plus être réinitialisé dans ce processus. Redémarrez Coclico et vérifiez le dossier runtimes\\win-x64\\native de l'installation.", ex);
            }

            ActiveGpuLayers = 0;
            _initialized = true;
            LoggingService.LogInfo(fellBackFromGpu
                ? "[AiChatService] Modèle local chargé sur CPU (repli réussi) : " + targetPath
                : "[AiChatService] Modèle local chargé sur CPU pur (0 couche VRAM) : " + targetPath);
            ResetIdleTimer();
        }
        finally
        {
            _initLock.Release();
        }
    }

    private string ResolveLocalModelPath()
    {
        string targetPath = GetLocalModelFullPath(CurrentModel);
        if (File.Exists(targetPath))
        {
            return targetPath;
        }

        if (CurrentModel == "IA-support-chat.gguf" && File.Exists(ModelPath))
        {
            return ModelPath;
        }

        AiModelInfo? fallback = AiModelCatalog.GetModelsForProvider(AiProviderType.LocalGGUF)
            .FirstOrDefault(m => File.Exists(GetLocalModelFullPath(m.Id)));
        if (fallback is null)
        {
            throw new FileNotFoundException("Le modèle local '" + CurrentModel + "' n'est pas encore téléchargé. Téléchargez-le depuis la bannière.");
        }

        CurrentModel = fallback.Id;
        return GetLocalModelFullPath(fallback.Id);
    }

    private Task LoadModelAsync(string modelPath, int gpuLayers, CancellationToken ct)
    {
        ModelParams p = BuildModelParams(modelPath, gpuLayers);
        return Task.Run(() =>
        {
            _model = LLamaWeights.LoadFromFile(p);
            Interlocked.Exchange(ref _chatCtx, new ChatCtx(_model.CreateContext(p)))?.Dispose();
        }, ct);
    }

    private static ModelParams BuildModelParams(string modelPath, int gpuLayers)
    {
        return new ModelParams(modelPath)
        {
            ContextSize = 4096u,
            GpuLayerCount = gpuLayers,
            MainGpu = 0,
            SplitMode = GPUSplitMode.None,
            Threads = gpuLayers > 0 ? 4 : OptimalThreads,
            BatchSize = 512u,
            UseMemorymap = gpuLayers <= 0,
            FlashAttention = true
        };
    }

    public IAsyncEnumerable<string> SendMessageAsync(string userMessage, CancellationToken ct = default)
    {
        return SendMessageWithVisionAsync(userMessage, null, null, ct);
    }

    public async IAsyncEnumerable<string> SendMessageWithVisionAsync(string userMessage, string? base64Image = null, string? imageMimeType = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(base64Image) && AiToolExecutionService.TryGetConversationalResponse(userMessage, out string? response))
        {
            string[] words = response.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                yield return i == 0 ? words[i] : " " + words[i];
                await Task.Delay(12, ct).ConfigureAwait(false);
            }
            yield break;
        }

        if (string.IsNullOrEmpty(base64Image) && TryGetAppIdentityResponse(userMessage, out string? identity))
        {
            string[] words = identity.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                yield return i == 0 ? words[i] : " " + words[i];
                await Task.Delay(12, ct).ConfigureAwait(false);
            }
            yield break;
        }

        string systemWithContext = BuildEnrichedSystemPrompt(userMessage);
        if (CurrentProvider == AiProviderType.Ollama)
        {
            List<(string User, string Ai)> historyCopy;
            lock (_memLock)
            {
                historyCopy = _shortTermMemory.ToList();
            }
            await foreach (string token in _multiClient.StreamChatAsync(
                CurrentModel,
                _settings.Settings.OllamaEndpoint,
                systemWithContext,
                historyCopy,
                userMessage,
                base64Image,
                ct).ConfigureAwait(false))
            {
                yield return token;
            }
            yield break;
        }

        if (!_initialized)
        {
            await InitializeAsync(ct).ConfigureAwait(false);
        }

        await _chatSem.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ResetContextOnlyInternal();
            ChatCtx? chatCtx = _chatCtx;
            if (chatCtx == null)
            {
                yield break;
            }

            await foreach (string token in InferLocalAsync(chatCtx, userMessage, ct).ConfigureAwait(false))
            {
                yield return token;
            }
        }
        finally
        {
            _chatSem.Release();
            ResetIdleTimer();
        }
    }

    private static bool TryGetAppIdentityResponse(string userMessage, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? response)
    {
        response = null;
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        if (AppIdentityQuestionRegex.IsMatch(userMessage))
        {
            response = AppIdentityAnswer;
            return true;
        }

        return false;
    }

    private async IAsyncEnumerable<string> InferLocalAsync(ChatCtx chatCtx, string userMessage, [EnumeratorCancellation] CancellationToken ct = default)
    {
        string prompt = BuildLocalPrompt(userMessage);
        var inferenceParams = new InferenceParams
        {
            MaxTokens = 512,
            AntiPrompts = TurnStopTokens,
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = 0.25f,
                TopP = 0.9f,
                TopK = 40,
                RepeatPenalty = 1.1f
            }
        };

        var rollingBuffer = new StringBuilder();
        await foreach (string token in chatCtx.Executor.InferAsync(prompt, inferenceParams, ct).ConfigureAwait(false))
        {
            int stopIndex = IndexOfEarliestStopToken(token);
            if (stopIndex >= 0)
            {
                string partial = token.Substring(0, stopIndex);
                if (!string.IsNullOrEmpty(partial))
                {
                    yield return partial;
                }
                yield break;
            }

            rollingBuffer.Append(token);
            if (IndexOfEarliestStopToken(rollingBuffer.ToString()) >= 0)
            {
                yield break;
            }

            yield return token;
        }
    }

    private static int IndexOfEarliestStopToken(string text)
    {
        int earliest = -1;
        foreach (string stopToken in TurnStopTokens)
        {
            if (stopToken.Length == 0)
            {
                continue;
            }

            int index = text.IndexOf(stopToken, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (earliest < 0 || index < earliest))
            {
                earliest = index;
            }
        }
        return earliest;
    }

    private string BuildEnrichedSystemPrompt(string userMessage)
    {
        var sb = new StringBuilder(SystemPrompt);

        if (!AiToolExecutionService.IsCasualOrGreeting(userMessage) && !string.IsNullOrWhiteSpace(CurrentStatusContext))
        {
            _ = sb.AppendLine($"\n[Contexte navigation : l'utilisateur se trouve dans le module '{CurrentStatusContext}' de Coclico. Ne mentionne ce contexte que si l'utilisateur demande explicitement une aide sur le module actuel.]");
        }

        if (!AiToolExecutionService.IsCasualOrGreeting(userMessage) && userMessage.Length >= 12)
        {
            string knowledge = GetKnowledge(userMessage);
            if (!string.IsNullOrWhiteSpace(knowledge))
            {
                _ = sb.AppendLine($"\n[Documentation technique pertinente]\n{knowledge}");
            }
        }

        return sb.ToString();
    }

    private static int SumTurnChars(List<(string User, string Ai)> turns)
    {
        int total = 0;
        foreach ((string u, string a) in turns)
        {
            total += u.Length + a.Length + 32;
        }
        return total;
    }

    private string BuildLocalPrompt(string userMessage)
    {
        string modelName = CurrentModel.ToLowerInvariant();
        bool isQwen = modelName.Contains("qwen");

        var sb = new StringBuilder();
        string sys = BuildEnrichedSystemPrompt(userMessage);

        var history = new List<(string User, string Ai)>();
        lock (_memLock)
        {
            history.AddRange(_shortTermMemory);
        }

        int historyBudget = MaxPromptChars - sys.Length - userMessage.Length;
        while (history.Count > 0 && SumTurnChars(history) > historyBudget)
        {
            history.RemoveAt(0);
        }

        if (isQwen)
        {
            _ = sb.Append("<|im_start|>system\n").Append(sys).Append("<|im_end|>\n");

            foreach ((string u, string a) in history)
            {
                _ = sb.Append("<|im_start|>user\n").Append(u).Append("<|im_end|>\n");
                _ = sb.Append("<|im_start|>assistant\n").Append(a).Append("<|im_end|>\n");
            }

            _ = sb.Append("<|im_start|>user\n").Append(userMessage).Append("<|im_end|>\n<|im_start|>assistant\n");
        }
        else
        {
            _ = sb.Append("<|begin_of_text|><|start_header_id|>system<|end_header_id|>\n\n");
            _ = sb.Append(sys);
            _ = sb.Append("<|eot_id|>");

            foreach ((string u, string a) in history)
            {
                _ = sb.Append("<|start_header_id|>user<|end_header_id|>\n\n");
                _ = sb.Append(u);
                _ = sb.Append("<|eot_id|><|start_header_id|>assistant<|end_header_id|>\n\n");
                _ = sb.Append(a);
                _ = sb.Append("<|eot_id|>");
            }

            _ = sb.Append("<|start_header_id|>user<|end_header_id|>\n\n");
            _ = sb.Append(userMessage);
            _ = sb.Append("<|eot_id|><|start_header_id|>assistant<|end_header_id|>\n\n");
        }

        return sb.ToString();
    }

    public void RecordExchange(string userMsg, string aiResponse)
    {
        if (string.IsNullOrWhiteSpace(aiResponse))
        {
            return;
        }

        bool shouldReset = false;
        lock (_memLock)
        {
            _shortTermMemory.Add((userMsg.Trim(), aiResponse.Trim()));
            if (_shortTermMemory.Count > MaxMemoryTurns)
            {
                _shortTermMemory.RemoveAt(0);
            }

            _turnsSinceReset++;
            if (_turnsSinceReset >= AutoResetAfterTurns)
            {
                _turnsSinceReset = 0;
                shouldReset = true;
            }
        }

        if (shouldReset)
        {
            ResetContextOnly();
        }
    }

    public void ResetConversation()
    {
        lock (_memLock)
        {
            _shortTermMemory.Clear();
            _turnsSinceReset = 0;
        }
        ResetContextOnly();
    }

    public void ResetContextOnly()
    {
        _chatSem.Wait();
        try
        {
            ResetContextOnlyInternal();
        }
        finally
        {
            _ = _chatSem.Release();
        }
    }

    private void ResetContextOnlyInternal()
    {
        if (_model == null)
        {
            return;
        }

        string targetPath = GetLocalModelFullPath(CurrentModel);
        if (!File.Exists(targetPath))
        {
            targetPath = ModelPath;
        }

        var newCtx = new ChatCtx(_model.CreateContext(BuildModelParams(targetPath, ActiveGpuLayers)));
        Interlocked.Exchange(ref _chatCtx, newCtx)?.Dispose();
    }

    private static string GetKnowledge(string query)
    {
        return !Directory.Exists(DocsPath) ? string.Empty : _rag.Value.Search(query, topK: 3, maxChars: 700) ?? string.Empty;
    }

    private void ResetIdleTimer()
    {
        int minutes = _settings.Settings.AiIdleTimeoutMinutes;
        if (minutes <= 0)
        {
            _ = (_idleTimer?.Change(Timeout.Infinite, Timeout.Infinite));
            return;
        }

        int ms = minutes * 60_000;
        if (_idleTimer is null)
        {
            _idleTimer = new Timer(OnIdleTimeout, null, ms, Timeout.Infinite);
        }
        else
        {
            _ = _idleTimer.Change(ms, Timeout.Infinite);
        }
    }

    private void OnIdleTimeout(object? state)
    {
        if (Interlocked.CompareExchange(ref _unloading, 1, 0) != 0)
        {
            return;
        }

        _ = UnloadOnIdleTimeoutAsync();
    }

    private async Task UnloadOnIdleTimeoutAsync()
    {
        try
        {
            await UnloadLocalModelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AiChatService.OnIdleTimeout");
        }
        finally
        {
            _ = Interlocked.Exchange(ref _unloading, 0);
        }
    }

    private async Task UnloadLocalModelAsync()
    {
        if (!_initialized)
        {
            return;
        }

        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_model == null)
            {
                return;
            }

            await _chatSem.WaitAsync().ConfigureAwait(false);
            try
            {
                DisposeModelInternal();
                _initialized = false;
                LoggingService.LogInfo("[AiChatService] Modèle local déchargé de la mémoire (0 Mo RAM)");
            }
            finally
            {
                _ = _chatSem.Release();
            }

            _ = MemoryCleanerService.ForceGcCollect();
        }
        finally
        {
            _initLock.Release();
        }
    }

    private void DisposeModelInternal()
    {
        Interlocked.Exchange(ref _chatCtx, null)?.Dispose();
        _model?.Dispose();
        _model = null;
    }

    public void Dispose()
    {
        _idleTimer?.Dispose();
        DisposeModelInternal();
        _initLock.Dispose();
        _chatSem.Dispose();
        GC.SuppressFinalize(this);
    }
}