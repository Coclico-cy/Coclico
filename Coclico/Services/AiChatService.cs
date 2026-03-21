using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace Coclico.Services
{
    public sealed class AiChatService : IDisposable
    {
        private static readonly Lazy<AiChatService> _lazy = new(() => new AiChatService(), LazyThreadSafetyMode.ExecutionAndPublication);
        public static AiChatService Instance => _lazy.Value;

        private LLamaWeights? _model;
        private LLamaContext? _context;
        private InteractiveExecutor? _executor;
        private bool _initialized;
        private int _activeGpuLayers = 0;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        public static readonly string ModelPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "resource", "model", "IA-support-chat.gguf");

        private static readonly string DocsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "resource", "docs");

        private static readonly Lazy<RagService> _rag = new(() =>
        {
            var r = new RagService();
            r.BuildIndex(DocsPath);
            return r;
        }, LazyThreadSafetyMode.ExecutionAndPublication);

        private readonly List<(string User, string Ai)> _shortTermMemory = [];
        private const int MaxMemoryTurns     = 4;
        private int       _turnsSinceReset   = 0;
        private const int AutoResetAfterTurns = 6;

        private bool _firstMessage = true;

        private static int OptimalThreads =>
            Math.Max(2, Math.Min(Environment.ProcessorCount - 1, 6));

        private static readonly int[] GpuAttempts = { 8, 0 };

        private const string SystemPrompt =
            "Tu es Coclico AI, l'assistant officiel de l'application Coclico — un gestionnaire système Windows complet. " +
            "Tu es amical, précis et direct. Tu tutoies l'utilisateur. " +
            "RÈGLE ABSOLUE : réponds TOUJOURS dans la même langue que l'utilisateur (français si français, english if english, etc.). " +
            "Tu connais parfaitement Coclico et tu guides l'utilisateur étape par étape. " +
            "Sois concis (2-4 phrases sauf si tutoriel demandé). " +
            "N'invente JAMAIS de fonctionnalité qui n'existe pas dans Coclico. " +
            "Dis à l'utilisateur de cliquer dans la barre latérale gauche pour naviguer entre les modules.\n\n" +

            "=== MODULES DISPONIBLES DANS COCLICO ===\n\n" +

            "TABLEAU DE BORD : Page d'accueil. Affiche en temps réel toutes les 3 secondes : CPU%, RAM utilisée/totale (Go), disque C: libre/total, uptime Windows, nombre de processus actifs, nombre d'applications installées.\n\n" +

            "APPLICATIONS : Bibliothèque complète de tous les logiciels et jeux. Sources détectées : Registre Windows (HKLM+HKCU), Steam, Epic Games, GOG Galaxy, Ubisoft Connect, EA App, Rockstar Games, Microsoft Store (MSIX). " +
            "Fonctions : double-clic pour lancer, renommer une app (sauvé dans custom_apps_data.json), changer sa catégorie, ajouter un .exe manuellement, filtrer, actualiser. Cache 6h pour performance.\n\n" +

            "FLOW CHAINS : Éditeur visuel d'automatisation drag-and-drop. Crée des chaînes de tâches Windows automatiques. " +
            "28 nœuds disponibles : Start, End, OpenApp, CloseApp, KillProcess, KillByMemory, RunCommand, RunPowerShell, SetProcessPriority, Condition, Loop, Parallel, Delay, Notification, HttpRequest, OpenUrl, ClipboardSet, SendKeys, TriggerShortcut, Screenshot, FileOperation, CompressFile, EmptyRecycleBin, CleanTemp, RamClean, SystemCheck, SetVolume, MuteAudio. " +
            "Condition supporte 10 opérateurs : ProcessRunning/NotRunning, FileExists/NotExists, TimeAfter/Before, CpuBelow/Above, RamBelow/Above. " +
            "Sauvegardé dans %AppData%\\Coclico\\flow_chains.json. Déclencheurs : raccourci global Ctrl+(Alt+)Touche.\n\n" +

            "INSTALLEUR RAPIDE : Interface graphique pour Winget (Windows Package Manager). Installation en 1 clic sans ligne de commande. " +
            "Catégories : Internet (navigateurs, VPN), Runtimes (.NET, VC++, Java), Développement (IDE, Git, Docker), Gaming, Création, Système. " +
            "Scope configurable : Machine ou Utilisateur.\n\n" +

            "NETTOYAGE SYSTÈME : Moteur de nettoyage professionnel. Estimation de l'espace récupérable avant nettoyage. " +
            "10 catégories : Fichiers temporaires Windows (%Temp%+C:\\Windows\\Temp), Cache navigateurs (Chrome/Firefox/Edge), Journaux système, Corbeille, Temp utilisateur, Cache miniatures (thumbcache), Rapports d'erreur Windows, Anciens installers/mises à jour Windows, Cache DNS, Prefetch. " +
            "Mode Deep Clean disponible pour un nettoyage intensif.\n\n" +

            "SCANNER : Audit complet de toutes les applications installées. Affiche pour chaque app : nom, version, éditeur, taille, chemin d'installation, source, date d'installation. " +
            "Clic sur 'Scanner' pour lancer l'analyse. Résultats triables et filtrables.\n\n" +

            "RAM CLEANER : Surveillance et nettoyage de la mémoire vive. Affiche : RAM physique utilisée/totale, mémoire virtuelle (Commit Charge), taille du Pagefile. " +
            "18 opérations P/Invoke Windows natives : Working Sets, Modified File Cache, Standby List, Priority 0 Standby, Memory Combine, GC .NET, System Cache, compression mémoire, File Cache, Pool Non Paginé, Large System Cache, NtSetSystemInformation (MemoryPurge), SetProcessWorkingSetSize, session pool, handles orphelins, nettoyage complet. " +
            "Mode auto : nettoyage sur intervalle de temps ou dès qu'un seuil RAM est dépassé.\n\n" +

            "PARAMÈTRES : Personnalisation complète. Options : couleur d'accent (#RRGGBB), thème (sombre/clair), opacité des cartes, taille de police, mode sidebar compact, langue (10 langues), démarrage automatique Windows, réduction dans le tray, portée Winget, raccourcis globaux. " +
            "Tout sauvegardé dans %AppData%\\Coclico\\settings.json.\n\n" +

            "=== FONCTIONNALITÉS TRANSVERSALES ===\n" +
            "• Profil utilisateur : avatar importable avec recadrage, nom affiché dans la sidebar\n" +
            "• Icône Tray : clic gauche = ouvrir, clic droit = menu (Ouvrir, Quitter)\n" +
            "• 10 langues : FR, EN, DE, ES, IT, JA, KO, PT, RU, ZH — changement immédiat sans redémarrage\n" +
            "• Raccourcis globaux : Ctrl+(Alt+)Touche pour déclencher une Flow Chain même en arrière-plan\n" +
            "• Journaux Serilog dans %AppData%\\Coclico\\logs\\\n\n" +

            "=== DONNÉES UTILISATEUR ===\n" +
            "Tout est dans %AppData%\\Coclico\\ : settings.json, flow_chains.json, custom_apps_data.json, manual_apps.json, avatar.png, logs\\";


        public string CurrentStatusContext { get; set; } = "Tableau de Bord";

        public static bool IsModelAvailable => File.Exists(ModelPath);
        public bool IsInitialized => _initialized;
        public int  ActiveGpuLayers => _activeGpuLayers;

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            if (_initialized) return;
            await _initLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_initialized) return;
                if (!File.Exists(ModelPath))
                    throw new FileNotFoundException($"Modèle introuvable : {ModelPath}");

                Exception? lastEx = null;
                foreach (var gpuLayers in GpuAttempts)
                {
                    try
                    {
                        await LoadModelAsync(gpuLayers, ct).ConfigureAwait(false);
                        _activeGpuLayers = gpuLayers;
                        _initialized = true;
                        return;
                    }
                    catch (Exception ex) when (gpuLayers > 0)
                    {
                        lastEx = ex;
                        LoggingService.LogInfo($"GPU ({gpuLayers} layers) init failed ({ex.Message}), retrying CPU…");
                        DisposeModelInternal();
                    }
                }
                throw lastEx ?? new InvalidOperationException("Model initialization failed.");
            }
            finally
            {
                _initLock.Release();
            }
        }

        private Task LoadModelAsync(int gpuLayers, CancellationToken ct)
        {
            var p = BuildModelParams(gpuLayers);
            return Task.Run(() =>
            {
                _model    = LLamaWeights.LoadFromFile(p);
                _context  = _model.CreateContext(p);
                _executor = new InteractiveExecutor(_context);
                _firstMessage = true;
            }, ct);
        }

        private static ModelParams BuildModelParams(int gpuLayers) => new(ModelPath)
        {
            ContextSize   = 2048,
            GpuLayerCount = gpuLayers,
            Threads       = OptimalThreads,
            BatchSize     = 256,
            UseMemorymap  = true,
            FlashAttention = true,
        };

        public async IAsyncEnumerable<string> SendMessageAsync(
            string userMessage,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (!_initialized) await InitializeAsync(ct).ConfigureAwait(false);
            if (_executor == null) yield break;

            var knowledge = GetKnowledge(userMessage);
            string contextInfo = $"[Contexte : {CurrentStatusContext}]";
            if (!string.IsNullOrEmpty(knowledge))
                contextInfo += $"\n[Doc]\n{knowledge}";

            var enrichedMessage = $"{contextInfo}\n\n{userMessage}";

            string prompt;
            if (_firstMessage)
            {
                var mem = BuildMemoryBlock();
                prompt = "<|begin_of_text|>" +
                         $"<|user|>\n{SystemPrompt}<|end|>\n" +
                         "<|assistant|>\nCompris.<|end|>\n" +
                         (mem.Length > 0
                             ? $"<|user|>\n{mem}<|end|>\n<|assistant|>\nContexte mémorisé.<|end|>\n"
                             : "") +
                         $"<|user|>\n{enrichedMessage}<|end|>\n" +
                         "<|assistant|>\n";
                _firstMessage = false;
            }
            else
            {
                prompt = $"<|user|>\n{enrichedMessage}<|end|>\n<|assistant|>\n";
            }

            var inferenceParams = new InferenceParams
            {
                MaxTokens = 512,
                AntiPrompts = new[] { "<|end|>", "<|user|>", "<|endoftext|>", "User:", "Assistant:" },
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature   = 0.4f,
                    TopP          = 0.90f,
                    TopK          = 30,
                    RepeatPenalty = 1.1f,
                }
            };

            await foreach (var token in _executor.InferAsync(prompt, inferenceParams, ct).ConfigureAwait(false))
            {
                yield return token;
            }
        }

        public void RecordExchange(string userMsg, string aiResponse)
        {
            if (string.IsNullOrWhiteSpace(aiResponse)) return;

            _shortTermMemory.Add((userMsg.Trim(), aiResponse.Trim()));
            if (_shortTermMemory.Count > MaxMemoryTurns)
                _shortTermMemory.RemoveAt(0);

            _turnsSinceReset++;
            if (_turnsSinceReset >= AutoResetAfterTurns)
            {
                _turnsSinceReset = 0;
                ResetContextOnly();
            }
        }

        private string BuildMemoryBlock()
        {
            if (_shortTermMemory.Count == 0) return string.Empty;

            var sb = new StringBuilder("[Mémoire récente]\n");
            for (int i = 0; i < _shortTermMemory.Count; i++)
            {
                var (user, ai) = _shortTermMemory[i];
                var u = user.Length > 100 ? user[..100] + "…" : user;
                var a = ai.Length   > 150 ? ai[..150]   + "…" : ai;
                sb.AppendLine($"T{i + 1} U:\"{u}\" A:\"{a}\"");
            }
            return sb.ToString().Trim();
        }

        private static string GetKnowledge(string query)
        {
            if (!Directory.Exists(DocsPath)) return string.Empty;
            return _rag.Value.Search(query, topK: 2, maxChars: 350) ?? string.Empty;
        }

        public void ResetConversation()
        {
            _shortTermMemory.Clear();
            _turnsSinceReset = 0;
            ResetContextOnly();
        }

        private void ResetContextOnly()
        {
            _firstMessage = true;
            if (_context != null)
            {
                _context.Dispose();
                _context = null;
                _executor = null;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: false);
                if (_model != null)
                {
                    var p = BuildModelParams(_activeGpuLayers);
                    _context  = _model.CreateContext(p);
                    _executor = new InteractiveExecutor(_context);
                }
            }
        }

        private void DisposeModelInternal()
        {
            _context?.Dispose(); _context = null;
            _model?.Dispose();   _model   = null;
            _executor = null;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }

        public void Dispose()
        {
            DisposeModelInternal();
            _initLock.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
