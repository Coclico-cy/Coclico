using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Coclico.Models.Network;
using Coclico.Services.Network;

namespace Coclico.Services.AI;

public sealed record AiActionRequest(string ActionType, string? Parameter, string OriginalTag, bool Confirmed = false);

public sealed record AiActionResult(bool Success, string Message, string? Details = null);

public sealed class AiToolExecutionService
{
    private static readonly Regex ActionRegex = new(
        @"\[\s*ACTION\s*[:=]\s*([A-Za-z0-9_\-]+)(?:\s+([^\s\]][^\]]*?))?\s*\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NonWordRegex = new(@"[^\w\s]", RegexOptions.Compiled);
    private static readonly Regex MultiSpaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SimulatedStepRegex = new(@"(?:étape|etape|step)\s*\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FakeDoneStreamingRegex = new(@"\b(?:nettoyage\s+terminé|action\s+terminée|opération\s+terminée|terminé\s*!)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FakeDoneRegex = new(@"\b(?:nettoyage\s+terminé|action\s+terminée|opération\s+terminée|terminé\s*!|terminée\s*!)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SimulatedProcessListRegex = new(@"(?:processus\s*\d+|chrome\s*-\s*\d+|spotify\s*-\s*\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex InterrogativeIntentRegex = new(@"\b(pourquoi|comment|qu\s*est\s*ce|c\s*est\s*quoi|explique|dis\s*moi|quel|quelle|quels|quelles|est\s*ce\s*que)\b", RegexOptions.Compiled);
    private static readonly Regex OpenAppIntentRegex = new(@"(?:ouvre|lance|démarre|demarre)\s+(?:l'application|le logiciel|le programme)?\s*([a-zA-Z0-9_\-\.]+)(?:\.exe)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> ConfirmationRequiredActions =
    [
        "CLEAN_RAM", "CLEAN_TEMP", "OPTIMIZE_NETWORK", "RESET_NETWORK_DEFAULTS",
        "SCAN_SFC", "SCAN_DISM", "DEFENDER_SCAN", "FULL_REPAIR", "RESET_NETWORK",
        "RESET_UPDATE", "OPEN_APP", "INSTALL_WINGET", "CONFIGURE_RAM_DAEMON",
        "RESTORE_NETWORK_SNAPSHOT", "UPDATE_APPS"
    ];

    private static readonly string[] QuestionMarkers =
    [
        "comment ", "comment,", "pourquoi", "qu'est-ce", "qu est-ce", "qu'est ce",
        "c'est quoi", "que signifie", "explique", "how ", "how to", "why ",
        "what is", "what does", "what's ", "différence entre", "difference entre",
        "à quoi", "a quoi sert", "quoi sert", "que fais-tu", "que fais tu",
        "qui es-tu", "qui es tu", "présente", "presente", "parle-moi", "parle moi",
        "tes fonctions", "tes capacité", "fonctionnalité"
    ];

    private static readonly HashSet<string> Greetings =
    [
        "bonjour", "salut", "hello", "hi", "hey", "coucou", "yo", "bonsoir", "bjr", "slt", "cc",
        "comment vas tu", "comment ca va", "comment tu vas", "ca va", "ça va", "cv", "tu vas bien", "tu va bien",
        "qui es tu", "qui est tu", "tu es qui", "tu sers a quoi", "que peux tu faire", "t es qui", "tes qui",
        "aide", "aide moi", "merci", "merci beaucoup", "au revoir", "bye", "a plus", "bonne journee", "bonne soiree",
        "commetn vas tyu", "coment vas tu", "comment va tu", "commet vas tu", "comment allez vous", "tout va bien"
    ];

    public event EventHandler<string>? NavigationRequested;

    private static INetworkOptimizerService ResolveNetworkService()
    {
        return ServiceContainer.GetOptional<INetworkOptimizerService>() ?? new NetworkOptimizerService();
    }

    private static SystemHealthService ResolveHealthService()
    {
        return ServiceContainer.GetOptional<SystemHealthService>() ?? new SystemHealthService();
    }

    private static InstallerService ResolveInstallerService()
    {
        return ServiceContainer.GetOptional<InstallerService>() ?? new InstallerService();
    }

    private static string NormalizeWords(string text)
    {
        return MultiSpaceRegex.Replace(NonWordRegex.Replace(text.Trim().ToLowerInvariant(), " ").Trim(), " ");
    }

    public static bool RequiresConfirmation(string actionType)
    {
        return ConfirmationRequiredActions.Contains(NormalizeActionType(actionType));
    }

    public static string NormalizeActionType(string rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType))
        {
            return string.Empty;
        }

        string upper = rawType.Trim().ToUpperInvariant();

        if (upper.Contains("CONFIG_RAM") || upper.Contains("RAM_DAEMON") || upper.Contains("AUTOCLEAN_RAM"))
        {
            return "CONFIGURE_RAM_DAEMON";
        }

        if (upper.Contains("RAM_PROFILE") || upper.Contains("PROFIL_RAM"))
        {
            return "SET_RAM_PROFILE";
        }

        if (upper.Contains("SNAPSHOT") || upper.Contains("INSTANTANE") || upper.Contains("INSTANTANÉ"))
        {
            return upper.Contains("RESTORE") || upper.Contains("RESTAUR") ? "RESTORE_NETWORK_SNAPSHOT" : "SNAPSHOT_NETWORK";
        }

        if (upper.Contains("UPDATE_ALL") || upper.Contains("UPGRADE_ALL") || upper.Contains("UPDATE_APPS") ||
            upper.Contains("MISE_A_JOUR_APP") || upper.Contains("MAJ_APP") || upper.Contains("MAJ_LOGICIELS"))
        {
            return "UPDATE_APPS";
        }

        if (upper.Contains("OPTIMIZE_NETWORK") || upper.Contains("OPTIMISER_RESEAU") || upper.Contains("PING_OPTIMIZE"))
        {
            return "OPTIMIZE_NETWORK";
        }

        if (upper.Contains("BENCHMARK_NETWORK") || upper.Contains("TEST_RESEAU") || upper.Contains("BENCHMARK_DNS"))
        {
            return "BENCHMARK_NETWORK";
        }

        if (upper.Contains("RESET_NETWORK_DEFAULTS") || upper.Contains("RESEAU_DEFAUT"))
        {
            return "RESET_NETWORK_DEFAULTS";
        }

        if (upper.Contains("ANALYZE_CRASH") || upper.Contains("SCAN_BSOD") || upper.Contains("ANALYSER_CRASH"))
        {
            return "ANALYZE_CRASHES";
        }

        if (upper.Contains("RAM") || upper.Contains("MEMOIRE") || upper.Contains("MÉMOIRE"))
        {
            if (upper.Contains("CLEAN") || upper.Contains("NETTOY") || upper.Contains("PURGE") ||
                upper.Contains("VIDER") || upper.Contains("LIBERER") || upper == "RAM" || upper == "RAM_CLEAN" ||
                upper == "CLEAN_MEMOIRE")
            {
                return "CLEAN_RAM";
            }
        }

        if (upper.Contains("TEMP") || upper.Contains("CACHE") || upper.Contains("CORBEILLE"))
        {
            if (upper.Contains("CLEAN") || upper.Contains("NETTOYAGE") || upper.Contains("NETTOYER") ||
                upper.Contains("VIDER") || upper.Contains("PURGE") || upper == "TEMP" || upper == "CACHE" ||
                upper.Contains("TEMP_CACHE") || upper.Contains("CORBEILLE"))
            {
                return "CLEAN_TEMP";
            }
        }

        if (upper.Contains("SFC") || upper.Contains("SCANNOW"))
        {
            return "SCAN_SFC";
        }

        if (upper.Contains("DISM") || upper.Contains("RESTOREHEALTH"))
        {
            return "SCAN_DISM";
        }

        if (upper.Contains("DEFENDER") || upper.Contains("ANTIVIRUS") || upper.Contains("SCAN_VIRUS") || upper.Contains("SCAN_MALWARE") || upper.Contains("VIRUS") || upper.Contains("MALWARE"))
        {
            return "DEFENDER_SCAN";
        }

        if (upper.Contains("FULL_REPAIR") || upper.Contains("REPARATION_COMPLETE") || upper.Contains("REMISE_A_NEUF"))
        {
            return "FULL_REPAIR";
        }

        if (upper.Contains("RESET_NETWORK") || upper.Contains("REINITIALISER_RESEAU") || upper.Contains("FLUSH_DNS") || upper.Contains("WINSOCK"))
        {
            return "RESET_NETWORK";
        }

        if (upper.Contains("RESET_UPDATE") || upper.Contains("REINITIALISER_UPDATE") || upper.Contains("DEBLOQUER_UPDATE"))
        {
            return "RESET_UPDATE";
        }

        return upper.Contains("DIAGNOSE") || upper.Contains("DIAGNOSTIC")
            ? "DIAGNOSE_SYSTEM"
            : upper.Contains("NAVIGAT") || upper.Contains("NAVIGU")
            ? "NAVIGATE"
            : upper.Contains("OPEN_APP") || upper.Contains("LANCER_APP") || upper.Contains("OUVRIR_APP")
            ? "OPEN_APP"
            : upper.Contains("WINGET") || upper.Contains("INSTALL") ? "INSTALL_WINGET" : upper;
    }

    public List<AiActionRequest> ExtractActions(string text)
    {
        var list = new List<AiActionRequest>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return list;
        }

        MatchCollection matches = ActionRegex.Matches(text);
        foreach (Match m in matches)
        {
            string rawAction = m.Groups[1].Value;
            string action = NormalizeActionType(rawAction);
            string? param = m.Groups[2].Success && !string.IsNullOrWhiteSpace(m.Groups[2].Value)
                ? m.Groups[2].Value.Trim()
                : null;

            if (!list.Any(a => a.ActionType == action && a.Parameter == param))
            {
                list.Add(new AiActionRequest(action, param, m.Value));
            }
        }

        return list;
    }

    public List<AiActionRequest> DetectUserIntentActions(string text)
    {
        var list = new List<AiActionRequest>();
        if (string.IsNullOrWhiteSpace(text) || IsCasualOrGreeting(text) || IsQuestion(text))
        {
            return list;
        }

        string normalized = text.Trim().ToLowerInvariant();
        string clean = NormalizeWords(text);

        if ((clean.Contains("ram") || clean.Contains("memoire") || clean.Contains("mémoire")) &&
            (clean.Contains("auto") || clean.Contains("demon") || clean.Contains("démon") || clean.Contains("planifi") || clean.Contains("toutes les") || clean.Contains("seuil")))
        {
            list.Add(new AiActionRequest("CONFIGURE_RAM_DAEMON", text, $"[ACTION:CONFIGURE_RAM_DAEMON {text}]"));
            return list;
        }

        if ((clean.Contains("ram") || clean.Contains("memoire") || clean.Contains("mémoire")) &&
            (clean.Contains("profil") || clean.Contains("mode")) &&
            (clean.Contains("gamer") || clean.Contains("extreme") || clean.Contains("extrême") || clean.Contains("ultra") || clean.Contains("agressif") || clean.Contains("smart")))
        {
            string prof = clean.Contains("gamer") ? "Gamer" :
                          clean.Contains("extreme") || clean.Contains("extrême") || clean.Contains("ultra") ? "Extreme" :
                          clean.Contains("agressif") ? "Aggressive" : "Smart";
            list.Add(new AiActionRequest("SET_RAM_PROFILE", prof, $"[ACTION:SET_RAM_PROFILE {prof}]"));
            return list;
        }

        if ((clean.Contains("ram") || clean.Contains("memoire") || clean.Contains("mémoire")) &&
            (clean.Contains("nettoy") || clean.Contains("nettoi") || clean.Contains("vide") || clean.Contains("vider") ||
             clean.Contains("liber") || clean.Contains("libér") || clean.Contains("purg") || clean.Contains("optimis") || clean.Contains("clean") || clean.Contains("lancer")))
        {
            list.Add(new AiActionRequest("CLEAN_RAM", null, "[ACTION:CLEAN_RAM]"));
            return list;
        }

        if ((clean.Contains("temp") || clean.Contains("temporair") || clean.Contains("cache") || clean.Contains("corbeille")) &&
            (clean.Contains("nettoy") || clean.Contains("nettoi") || clean.Contains("vide") || clean.Contains("vider") ||
             clean.Contains("supprim") || clean.Contains("effac") || clean.Contains("purg") || clean.Contains("clean") || clean.Contains("lancer")))
        {
            list.Add(new AiActionRequest("CLEAN_TEMP", null, "[ACTION:CLEAN_TEMP]"));
            return list;
        }

        if (clean.Contains("sfc") || clean.Contains("scannow") || ((clean.Contains("integrit") || clean.Contains("intégrit")) && clean.Contains("fichier")))
        {
            list.Add(new AiActionRequest("SCAN_SFC", null, "[ACTION:SCAN_SFC]"));
            return list;
        }

        if (clean.Contains("dism") || clean.Contains("restorehealth") || (clean.Contains("image") && clean.Contains("system")))
        {
            list.Add(new AiActionRequest("SCAN_DISM", null, "[ACTION:SCAN_DISM]"));
            return list;
        }

        if (clean.Contains("antivirus") || clean.Contains("defender") ||
            ((clean.Contains("scan") || clean.Contains("analyse") || clean.Contains("analys")) && (clean.Contains("virus") || clean.Contains("malware"))))
        {
            list.Add(new AiActionRequest("DEFENDER_SCAN", null, "[ACTION:DEFENDER_SCAN]"));
            return list;
        }

        if (clean.Contains("reparation complete") || clean.Contains("réparation complète") ||
            clean.Contains("remise a neuf") || clean.Contains("remise à neuf") || clean.Contains("full repair") ||
            clean.Contains("repare tout") || clean.Contains("répare tout"))
        {
            list.Add(new AiActionRequest("FULL_REPAIR", null, "[ACTION:FULL_REPAIR]"));
            return list;
        }

        if (clean.Contains("diagnostic") || clean.Contains("diagnostique") ||
            clean.Contains("bilan du pc") || clean.Contains("etat du pc") || clean.Contains("état du pc") ||
            clean.Contains("sante du pc") || clean.Contains("santé du pc") || clean.Contains("performances du pc"))
        {
            list.Add(new AiActionRequest("DIAGNOSE_SYSTEM", null, "[ACTION:DIAGNOSE_SYSTEM]"));
            return list;
        }

        if (clean.Contains("crash") || clean.Contains("plantage") || clean.Contains("ecran bleu") || clean.Contains("écran bleu") ||
            clean.Contains("bsod") || clean.Contains("minidump") || clean.Contains("pourquoi le pc redemarre") || clean.Contains("pourquoi mon pc redémarre"))
        {
            list.Add(new AiActionRequest("ANALYZE_CRASHES", null, "[ACTION:ANALYZE_CRASHES]"));
            return list;
        }

        if ((clean.Contains("reseau") || clean.Contains("réseau") || clean.Contains("connexion") || clean.Contains("carte")) &&
            (clean.Contains("instantane") || clean.Contains("instantan") || clean.Contains("snapshot") || clean.Contains("point de restauration")))
        {
            list.Add(new AiActionRequest(
                clean.Contains("restaur") || clean.Contains("revien") || clean.Contains("revient") ? "RESTORE_NETWORK_SNAPSHOT" : "SNAPSHOT_NETWORK",
                null,
                "[ACTION:SNAPSHOT_NETWORK]"));
            return list;
        }

        if ((clean.Contains("mets a jour") || clean.Contains("met a jour") || clean.Contains("mise a jour") || clean.Contains("mises a jour") || clean.Contains("update") || clean.Contains("actualise")) &&
            (clean.Contains("application") || clean.Contains("appli") || clean.Contains("logiciel") || clean.Contains("programme") || clean.Contains("tout") || clean.Contains("mes apps")))
        {
            list.Add(new AiActionRequest("UPDATE_APPS", null, "[ACTION:UPDATE_APPS]"));
            return list;
        }

        if ((clean.Contains("reseau") || clean.Contains("réseau") || clean.Contains("connexion") || clean.Contains("ping") || clean.Contains("latence") || clean.Contains("wifi") || clean.Contains("ethernet")) &&
            (clean.Contains("optimis") || clean.Contains("acceler") || clean.Contains("accél") || clean.Contains("baiss") || clean.Contains("boost") || clean.Contains("amelior") || clean.Contains("amélior") || clean.Contains("auto tune") || clean.Contains("autotun") || clean.Contains("tuning")))
        {
            string mode = clean.Contains("auto") || clean.Contains("tune") || clean.Contains("tuning") || clean.Contains("complet") ? "autotune" :
                          clean.Contains("debit") || clean.Contains("débit") || clean.Contains("stream") || clean.Contains("telecharg") || clean.Contains("télécharg") ? "throughput" :
                          clean.Contains("fast") || clean.Contains("rapide") ? "quick" : "gaming";
            list.Add(new AiActionRequest("OPTIMIZE_NETWORK", mode, $"[ACTION:OPTIMIZE_NETWORK {mode}]"));
            return list;
        }

        if ((clean.Contains("reseau") || clean.Contains("réseau") || clean.Contains("connexion") || clean.Contains("dns") || clean.Contains("ping")) &&
            (clean.Contains("benchmark") || clean.Contains("teste") || clean.Contains("tester") || clean.Contains("mesur") || clean.Contains("meilleur dns")))
        {
            list.Add(new AiActionRequest("BENCHMARK_NETWORK", null, "[ACTION:BENCHMARK_NETWORK]"));
            return list;
        }

        if ((clean.Contains("reseau") || clean.Contains("réseau") || clean.Contains("connexion")) &&
            (clean.Contains("defaut") || clean.Contains("défaut") || clean.Contains("origine") || clean.Contains("restaur")))
        {
            list.Add(new AiActionRequest("RESET_NETWORK_DEFAULTS", null, "[ACTION:RESET_NETWORK_DEFAULTS]"));
            return list;
        }

        if ((clean.Contains("reseau") || clean.Contains("réseau") || clean.Contains("dns") || clean.Contains("winsock")) &&
            (clean.Contains("reset") || clean.Contains("reinitialis") || clean.Contains("réinitialis") || clean.Contains("flush") || clean.Contains("purg")))
        {
            list.Add(new AiActionRequest("RESET_NETWORK", null, "[ACTION:RESET_NETWORK]"));
            return list;
        }

        if ((clean.Contains("update") || clean.Contains("mise a jour") || clean.Contains("mises a jour")) &&
            (clean.Contains("reset") || clean.Contains("reinitialis") || clean.Contains("réinitialis") || clean.Contains("debloqu") || clean.Contains("débloqu")))
        {
            list.Add(new AiActionRequest("RESET_UPDATE", null, "[ACTION:RESET_UPDATE]"));
            return list;
        }

        if (clean.Contains("ouvre") || clean.Contains("affiche") || clean.Contains("va sur") || clean.Contains("va dans") || clean.Contains("navigue") ||
            clean.Contains("analyse le disque") || clean.Contains("analyse mon disque") || clean.Contains("espace disque") || clean.Contains("qui prend de la place") || clean.Contains("ou en est mon stockage"))
        {
            if (clean.Contains("disque") || clean.Contains("stockage") || clean.Contains("prend de la place"))
            {
                list.Add(new AiActionRequest("NAVIGATE", "disk", "[ACTION:NAVIGATE disk]"));
                return list;
            }

            if (clean.Contains("dashboard") || clean.Contains("tableau de bord") || clean.Contains("accueil"))
            {
                list.Add(new AiActionRequest("NAVIGATE", "dashboard", "[ACTION:NAVIGATE dashboard]"));
                return list;
            }
            if (clean.Contains("reseau") || clean.Contains("réseau") || clean.Contains("latence") || clean.Contains("ping") || clean.Contains("network"))
            {
                list.Add(new AiActionRequest("NAVIGATE", "network", "[ACTION:NAVIGATE network]"));
                return list;
            }
            if (clean.Contains("ram") || clean.Contains("ram cleaner") || clean.Contains("memoire") || clean.Contains("mémoire"))
            {
                list.Add(new AiActionRequest("NAVIGATE", "ram", "[ACTION:NAVIGATE ram]"));
                return list;
            }
            if (clean.Contains("nettoyage") || clean.Contains("cleaning"))
            {
                list.Add(new AiActionRequest("NAVIGATE", "cleaning", "[ACTION:NAVIGATE cleaning]"));
                return list;
            }
            if (clean.Contains("application") || clean.Contains("logiciel") || clean.Contains("program"))
            {
                list.Add(new AiActionRequest("NAVIGATE", "programs", "[ACTION:NAVIGATE programs]"));
                return list;
            }
            if (clean.Contains("installeur") || clean.Contains("installer") || clean.Contains("winget"))
            {
                list.Add(new AiActionRequest("NAVIGATE", "installer", "[ACTION:NAVIGATE installer]"));
                return list;
            }
            if (clean.Contains("sante") || clean.Contains("santé") || clean.Contains("defense") || clean.Contains("défense"))
            {
                list.Add(new AiActionRequest("NAVIGATE", "health", "[ACTION:NAVIGATE health]"));
                return list;
            }
            if (clean.Contains("parametre") || clean.Contains("paramètre") || clean.Contains("settings") || clean.Contains("config"))
            {
                list.Add(new AiActionRequest("NAVIGATE", "settings", "[ACTION:NAVIGATE settings]"));
                return list;
            }
        }

        Match appMatch = OpenAppIntentRegex.Match(normalized);
        if (appMatch.Success)
        {
            string app = appMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(app) && app.Length >= 3 &&
                app != "copilot" && app != "coclico" && app != "l'application" && app != "le logiciel")
            {
                list.Add(new AiActionRequest("OPEN_APP", app, $"[ACTION:OPEN_APP {app}]"));
                return list;
            }
        }

        return list;
    }

    public string StripActionTags(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? string.Empty : ActionRegex.Replace(text, string.Empty).Trim();
    }

    public string GetDefaultActionConfirmation(AiActionRequest action)
    {
        string normalized = NormalizeActionType(action.ActionType);
        return normalized switch
        {
            "CLEAN_RAM" => "Parfait, je lance l'optimisation de la mémoire vive pour vous.",
            "CONFIGURE_RAM_DAEMON" => "Parfait, je configure les paramètres du démon de nettoyage automatique de la mémoire vive pour vous.",
            "SET_RAM_PROFILE" => $"Parfait, j'applique le profil de mémoire vive {action.Parameter ?? "Smart"} pour vous.",
            "OPTIMIZE_NETWORK" => "Parfait, je lance l'optimisation des performances et de la latence de vos cartes réseau pour vous.",
            "BENCHMARK_NETWORK" => "Parfait, je lance le benchmark complet de latence et des serveurs DNS pour vous.",
            "RESET_NETWORK_DEFAULTS" => "Parfait, je remets l'ensemble des paramètres réseau aux valeurs d'origine Microsoft pour vous.",
            "ANALYZE_CRASHES" => "Parfait, j'analyse les fichiers minidumps et le journal des plantages Windows pour vous.",
            "CLEAN_TEMP" => "Parfait, je lance le nettoyage des fichiers temporaires pour vous.",
            "SCAN_SFC" => "Parfait, je lance l'analyse d'intégrité des fichiers système (SFC) pour vous.",
            "SCAN_DISM" => "Parfait, je lance la restauration de l'image Windows (DISM) pour vous.",
            "DEFENDER_SCAN" => "Parfait, je lance l'analyse antivirus avec Windows Defender pour vous.",
            "FULL_REPAIR" => "Parfait, je lance la procédure de réparation intégrale du système pour vous.",
            "RESET_NETWORK" => "Parfait, je lance la réinitialisation de la configuration réseau et du cache DNS pour vous.",
            "RESET_UPDATE" => "Parfait, je lance la réinitialisation des composants Windows Update pour vous.",
            "DIAGNOSE_SYSTEM" => "Parfait, je lance le diagnostic complet des performances système pour vous.",
            "SNAPSHOT_NETWORK" => "Parfait, je crée un instantané de sécurité de votre configuration réseau pour vous.",
            "RESTORE_NETWORK_SNAPSHOT" => "Parfait, je restaure la configuration réseau depuis le dernier instantané de sécurité pour vous.",
            "UPDATE_APPS" => "Parfait, je lance la mise à jour de toutes vos applications via Winget pour vous.",
            "NAVIGATE" => !string.IsNullOrWhiteSpace(action.Parameter)
                ? $"Parfait, je vous redirige vers le module {action.Parameter}."
                : "Parfait, je vous redirige vers le module demandé.",
            "OPEN_APP" => !string.IsNullOrWhiteSpace(action.Parameter)
                ? $"Parfait, je lance l'application {action.Parameter} pour vous."
                : "Parfait, je lance l'application demandée pour vous.",
            "INSTALL_WINGET" => !string.IsNullOrWhiteSpace(action.Parameter)
                ? $"Parfait, je lance l'installation de {action.Parameter} via Winget pour vous."
                : "Parfait, je lance l'installation demandée via Winget pour vous.",
            _ => "Parfait, je lance l'opération demandée pour vous."
        };
    }

    public string SanitizeActionResponse(string rawAiText, IReadOnlyList<AiActionRequest> actions)
    {
        if (actions == null || actions.Count == 0)
        {
            return rawAiText;
        }

        string stripped = StripActionTags(AiActionParser.Clean(rawAiText));
        if (string.IsNullOrWhiteSpace(stripped))
        {
            return GetDefaultActionConfirmation(actions[0]);
        }

        if (ContainsSimulatedProgressOrFakeCompletion(stripped))
        {
            string? cleanOpening = ExtractCleanOpeningSentence(stripped);
            return !string.IsNullOrWhiteSpace(cleanOpening) ? cleanOpening : GetDefaultActionConfirmation(actions[0]);
        }

        return stripped.Trim();
    }

    public string SanitizeStreamingText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cutIndices = new List<int>();

        int idx = text.IndexOf("[Vérification", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            cutIndices.Add(idx);
        }

        Match stepMatch = SimulatedStepRegex.Match(text);
        if (stepMatch.Success)
        {
            cutIndices.Add(stepMatch.Index);
        }

        Match doneMatch = FakeDoneStreamingRegex.Match(text);
        if (doneMatch.Success)
        {
            cutIndices.Add(doneMatch.Index);
        }

        if (cutIndices.Count > 0)
        {
            text = text[..cutIndices.Min()].TrimEnd();
        }

        return text;
    }

    public static bool ContainsSimulatedProgressOrFakeCompletion(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.Contains("[Vérification", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Vérification en cours", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return SimulatedStepRegex.IsMatch(text) ||
            FakeDoneRegex.IsMatch(text) ||
            text.Contains("redémarrer votre PC", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("redémarrer le PC", StringComparison.OrdinalIgnoreCase) ||
            SimulatedProcessListRegex.IsMatch(text);
    }

    public static string? ExtractCleanOpeningSentence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToList();

        foreach (string line in lines)
        {
            if (line.StartsWith("#") || line.StartsWith("*") || line.StartsWith("-") || line.StartsWith("["))
            {
                continue;
            }

            if ((line.StartsWith("**") && line.EndsWith("**")) || (line.StartsWith("**") && line.EndsWith(":**")))
            {
                continue;
            }

            if (ContainsSimulatedProgressOrFakeCompletion(line))
            {
                continue;
            }

            if (line.Length >= 8 && line.Length <= 180 &&
                (line.Contains("très bien", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("parfait", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("d'accord", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("entendu", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("compris", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("je m'en occupe", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("je lance", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("j'optimise", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("je nettoie", StringComparison.OrdinalIgnoreCase)))
            {
                return line;
            }
        }

        return null;
    }

    public static bool IsDirectActionCommand(string text, IReadOnlyList<AiActionRequest> detectedActions)
    {
        if (detectedActions == null || detectedActions.Count == 0 ||
            string.IsNullOrWhiteSpace(text) || text.Contains('?'))
        {
            return false;
        }

        return !InterrogativeIntentRegex.IsMatch(NormalizeWords(text));
    }

    public bool ShouldExecuteActions(string userMessage, IReadOnlyList<AiActionRequest> actions)
    {
        return actions != null && actions.Count != 0 && !string.IsNullOrWhiteSpace(userMessage) && !IsCasualOrGreeting(userMessage);
    }

    internal static bool IsQuestion(string text)
    {
        string lower = text.ToLowerInvariant();
        foreach (string marker in QuestionMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsCasualOrGreeting(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        string clean = NormalizeWords(text);

        if (clean.Length <= 3 && !clean.Any(char.IsDigit))
        {
            return true;
        }

        if (Greetings.Contains(clean))
        {
            return true;
        }

        if (Regex.IsMatch(clean, @"\b(bonjour|salut|hello|coucou|bonsoir|bjr|slt)\b"))
        {
            return true;
        }

        return Regex.IsMatch(clean, @"\b(comm?e[nt]{1,2})\s*(va|vas|tu|tyu|allez)\b") ||
            Regex.IsMatch(clean, @"\b(ca\s*va|ça\s*va|cv|tu\s*va[s]?\s*bien|tout\s*va\s*bien)\b") ||
            Regex.IsMatch(clean, @"\b(qui\s*(es|est)\s*tu|tu\s*es\s*qui|t\s*es\s*qui|tu\s*sers\s*a\s*quoi)\b") ||
            Regex.IsMatch(clean, @"\b(merci|thx|thanks|au\s*revoir|a\s*plus|bonne\s*journee)\b");
    }

    public static bool TryGetConversationalResponse(string? text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? response)
    {
        response = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string clean = NormalizeWords(text);

        if (clean == "b" || clean == "bjr" || clean == "slt" || clean == "cc" || clean == "yo" || clean == "hi" || clean == "hey" ||
            (clean.Length <= 2 && !clean.Any(char.IsDigit)))
        {
            response = "Bonjour ! Comment puis-je vous aider aujourd'hui sur votre PC ? Vous pouvez me demander de libérer la RAM, nettoyer les fichiers temporaires ou analyser la santé de votre système.";
            return true;
        }

        if (clean == "commetn vas tyu" || clean == "coment vas tu" || clean == "comment va tu" || clean == "commet vas tu" ||
            clean == "comment vas tu" || clean == "comment tu vas" || clean == "comment ca va" || clean == "ca va" || clean == "ça va" ||
            clean == "cv" || clean == "tu vas bien" || clean == "tu va bien" || clean == "tout va bien" ||
            Regex.IsMatch(clean, @"^(bonjour|salut|hello|coucou|yo|bjr|slt|cc)?\s*comm?e[nt]{1,2}\s*(va|vas|tu|tyu|allez)\b") ||
            Regex.IsMatch(clean, @"^(bonjour|salut|hello|coucou|yo|bjr|slt|cc)?\s*(ca|ça)\s*va\b") ||
            Regex.IsMatch(clean, @"^(bonjour|salut|hello|coucou|yo|bjr|slt|cc)?\s*tu\s*va[s]?\s*bien\b") ||
            clean == "quoi de neuf" || clean == "ca roule" || clean == "la forme" || clean == "bien ou quoi" || clean == "bien ou bien")
        {
            response = "Bonjour ! Je vais très bien, merci. Mon système est pleinement opérationnel et prêt à vous aider. Que souhaitez-vous faire aujourd'hui ?";
            return true;
        }

        if (clean == "bonjour" || clean == "salut" || clean == "hello" || clean == "bonsoir" || clean == "coucou" ||
            Regex.IsMatch(clean, @"^(bonjour|salut|hello|bonsoir|coucou)\s*(coclico|copilot|a\s*toi|l\s*ami)?$"))
        {
            response = "Bonjour ! Je suis ravi de vous retrouver. Que puis-je faire pour vous aujourd'hui sur votre PC ?";
            return true;
        }

        if (clean == "qui es tu" || clean == "qui est tu" || clean == "tu es qui" || clean == "tu sers a quoi" || clean == "t es qui" || clean == "tes qui" ||
            clean == "c est quoi coclico" || clean == "c est quoi cette application" || clean == "qui t a cree" ||
            Regex.IsMatch(clean, @"^qui\s*(es|est)\s*tu") || Regex.IsMatch(clean, @"^tu\s*(es|est)\s*qui") ||
            Regex.IsMatch(clean, @"^c\s*est\s*quoi\s*(coclico|cette\s*application)"))
        {
            response = "Je suis Coclico Copilot, votre assistant Windows dédié à l'optimisation, la maintenance et la sécurité de votre ordinateur. Je peux nettoyer la mémoire RAM, supprimer les fichiers inutiles, vérifier l'intégrité système avec SFC/DISM et lancer vos applications.";
            return true;
        }

        if (clean == "merci" || clean == "merci beaucoup" || clean == "thanks" || clean == "thx" ||
            Regex.IsMatch(clean, @"^merci\b"))
        {
            response = "Avec grand plaisir ! N'hésitez pas si vous avez d'autres demandes d'optimisation ou des questions sur votre PC.";
            return true;
        }

        if (clean == "au revoir" || clean == "bye" || clean == "a plus" || clean == "bonne journee" || clean == "bonne soiree" || clean == "a bientot" ||
            Regex.IsMatch(clean, @"^(au\s*revoir|bonne\s*journee|bonne\s*soiree|a\s*plus)\b"))
        {
            response = "Au revoir et excellente journée ! Coclico reste à votre entière disposition en tâche de fond si besoin.";
            return true;
        }

        if (clean == "aide" || clean == "aide moi" || clean == "help" || clean == "que peux tu faire" ||
            Regex.IsMatch(clean, @"^(aide|help|aide\s*moi|peux\s*tu\s*m\s*aider|comment\s*m\s*aider)\b"))
        {
            response = "Voici quelques exemples de ce que je peux accomplir pour vous :\n• ⚡ Nettoyer la RAM : 'Nettoie la RAM'\n• 🧹 Nettoyage système : 'Vide les fichiers temporaires'\n• 🔍 Vérification d'intégrité : 'Lance un scan SFC'\n• 🛠️ Réparation Windows : 'Répare avec DISM'\n• 🛡️ Antivirus : 'Analyse avec Defender'\n• 📊 Diagnostic : 'Fais un diagnostic du PC'\n\nQue désirez-vous exécuter ?";
            return true;
        }

        if (clean.Length == 1 && !char.IsDigit(clean[0]))
        {
            response = "Bonjour ! Comment puis-je vous aider aujourd'hui ? Vous pouvez me poser une question ou choisir une action rapide ci-dessous (Nettoyer RAM, Vider Temp, Diagnostic PC...).";
            return true;
        }

        return false;
    }

    private static string? SanitizeParameter(string? parameter, string actionType)
    {
        if (string.IsNullOrWhiteSpace(parameter))
        {
            return null;
        }

        string trimmed = parameter.Trim();
        if (trimmed.Length > 96)
        {
            trimmed = trimmed[..96];
        }

        return actionType switch
        {
            "OPEN_APP" => new string(trimmed.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '.' or '-' or '_').ToArray()).Trim(),
            "INSTALL_WINGET" => new string(trimmed.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_').ToArray()),
            "NAVIGATE" => new string(trimmed.Where(c => char.IsLetterOrDigit(c)).ToArray()),
            _ => trimmed
        };
    }

    public async Task<AiActionResult> ExecuteActionAsync(AiActionRequest action, CancellationToken ct = default)
    {
        try
        {
            string norm = NormalizeActionType(action.ActionType);
            string? safeParameter = SanitizeParameter(action.Parameter, norm);
            return RequiresConfirmation(norm) && !action.Confirmed
                ? new AiActionResult(false, "Confirmation utilisateur requise avant cette action.")
                : norm switch
                {
                    "CLEAN_RAM" => await ExecuteRamCleanAsync(ct),
                    "CONFIGURE_RAM_DAEMON" => await ExecuteConfigureRamDaemonAsync(safeParameter),
                    "SET_RAM_PROFILE" => await ExecuteSetRamProfileAsync(safeParameter),
                    "OPTIMIZE_NETWORK" => await ExecuteOptimizeNetworkAsync(safeParameter, ct),
                    "BENCHMARK_NETWORK" => await ExecuteBenchmarkNetworkAsync(ct),
                    "RESET_NETWORK_DEFAULTS" => await ExecuteResetNetworkDefaultsAsync(ct),
                    "ANALYZE_CRASHES" => await ExecuteAnalyzeCrashesAsync(ct),
                    "CLEAN_TEMP" => await ExecuteTempCleanAsync(ct),
                    "SCAN_HEALTH" => await ExecuteHealthScanAsync(ct),
                    "SCAN_SFC" => await ExecuteSfcScanAsync(ct),
                    "SCAN_DISM" => await ExecuteDismScanAsync(ct),
                    "DEFENDER_SCAN" => await ExecuteDefenderScanAsync(ct),
                    "FULL_REPAIR" => await ExecuteFullRepairAsync(ct),
                    "RESET_NETWORK" => await ExecuteResetNetworkAsync(ct),
                    "RESET_UPDATE" => await ExecuteResetUpdateAsync(ct),
                    "DIAGNOSE_SYSTEM" => await GetSystemDiagnosticsResultAsync(),
                    "SNAPSHOT_NETWORK" => await ExecuteNetworkSnapshotAsync(ct),
                    "RESTORE_NETWORK_SNAPSHOT" => await ExecuteRestoreNetworkSnapshotAsync(ct),
                    "UPDATE_APPS" => await ExecuteUpdateAllAppsAsync(ct),
                    "NAVIGATE" => ExecuteNavigate(safeParameter),
                    "OPEN_APP" => await ExecuteOpenAppAsync(safeParameter),
                    "INSTALL_WINGET" => await ExecuteInstallWingetAsync(safeParameter, ct),
                    _ => new AiActionResult(false, $"Action inconnue : {action.ActionType}")
                };
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, $"AiToolExecutionService.ExecuteActionAsync({action.ActionType})");
            return new AiActionResult(false, $"Erreur lors de l'exécution : {ex.Message}");
        }
    }

    private static async Task<AiActionResult> ExecuteRamCleanAsync(CancellationToken ct)
    {
        MemoryCleanerService.CleanResult cleanResult = await MemoryCleanerService.FullCleanAsync(false, null, ct).ConfigureAwait(false);
        long freedMb = cleanResult.TotalFreed / (1024 * 1024);
        MemoryCleanerService.RamInfo after = cleanResult.After;

        return new AiActionResult(
            true,
            $"✅ Mémoire RAM nettoyée avec succès (~{Math.Max(0, freedMb)} Mo libérés).",
            $"RAM utilisée : {after.UsedPhysBytes / (1024 * 1024 * 1024.0):F1} Go / {after.TotalPhysBytes / (1024 * 1024 * 1024.0):F1} Go ({after.PhysUsedPercent:F1}%)");
    }

    private static Task<AiActionResult> ExecuteConfigureRamDaemonAsync(string? param)
    {
        ISmartMemoryDaemonService? daemon = ServiceContainer.GetOptional<ISmartMemoryDaemonService>();
        if (daemon == null)
        {
            return Task.FromResult(new AiActionResult(false, "Service de démon RAM non disponible."));
        }

        AutoCleanMode mode = AutoCleanMode.Interval;
        int value = 30;
        MemoryCleanerService.CleanProfile profile = MemoryCleanerService.CleanProfile.Smart;
        bool enabled = true;

        if (!string.IsNullOrWhiteSpace(param))
        {
            string lower = param.ToLowerInvariant();
            if (lower.Contains("seuil") || (lower.Contains("%") && !lower.Contains("min")))
            {
                mode = AutoCleanMode.ThresholdPercent;
                Match matchPct = Regex.Match(lower, @"(\d+)\s*%");
                value = matchPct.Success && int.TryParse(matchPct.Groups[1].Value, out int pctVal)
                    ? Math.Clamp(pctVal, 10, 99)
                    : 80;
            }
            else if (lower.Contains("hybrid") || lower.Contains("hybride") || lower.Contains("islc"))
            {
                mode = AutoCleanMode.Hybrid;
                value = 1024;
            }
            else
            {
                mode = AutoCleanMode.Interval;
                Match matchMin = Regex.Match(lower, @"(\d+)\s*(?:min|minute|m\b)");
                if (matchMin.Success && int.TryParse(matchMin.Groups[1].Value, out int minVal) && minVal > 0)
                {
                    value = minVal;
                }
                else
                {
                    Match matchH = Regex.Match(lower, @"(\d+)\s*(?:h|heure)");
                    if (matchH.Success && int.TryParse(matchH.Groups[1].Value, out int hVal) && hVal > 0)
                    {
                        value = hVal * 60;
                    }
                }
            }

            profile = lower.Contains("deep") || lower.Contains("profond") || lower.Contains("ultra") || lower.Contains("extreme") || lower.Contains("gamer")
                ? MemoryCleanerService.CleanProfile.Deep
                : lower.Contains("quick") || lower.Contains("rapide") || lower.Contains("leger") || lower.Contains("léger")
                    ? MemoryCleanerService.CleanProfile.Quick
                    : lower.Contains("normal") ? MemoryCleanerService.CleanProfile.Normal : MemoryCleanerService.CleanProfile.Smart;

            if (lower.Contains("désactive") || lower.Contains("desactive") || lower.Contains("arret") || lower.Contains("stop") || lower.Contains("coupe"))
            {
                enabled = false;
            }
        }

        daemon.Configure(enabled, mode, value, true, profile);

        string modeDesc = mode switch
        {
            AutoCleanMode.ThresholdPercent => $"dès que la RAM dépasse {value}%",
            AutoCleanMode.Hybrid => "en mode Hybride ISLC (surveillance continue)",
            _ => $"toutes les {value} minutes"
        };

        return Task.FromResult(new AiActionResult(
            true,
            enabled
                ? $"✅ Démon RAM configuré : Nettoyage {modeDesc}, Profil {profile}."
                : "✅ Démon de nettoyage automatique de la RAM désactivé.",
            $"Mode : {mode} ({value}) | Profil : {profile} | État : {(enabled ? "Actif" : "Désactivé")}"));
    }

    private static Task<AiActionResult> ExecuteSetRamProfileAsync(string? param)
    {
        ISmartMemoryDaemonService? daemon = ServiceContainer.GetOptional<ISmartMemoryDaemonService>();
        if (daemon == null)
        {
            return Task.FromResult(new AiActionResult(false, "Service de démon RAM non disponible."));
        }

        MemoryCleanerService.CleanProfile profile = MemoryCleanerService.CleanProfile.Smart;
        if (!string.IsNullOrWhiteSpace(param))
        {
            string lower = param.ToLowerInvariant();
            if (lower.Contains("deep") || lower.Contains("profond") || lower.Contains("ultra") || lower.Contains("extreme") || lower.Contains("gamer"))
            {
                profile = MemoryCleanerService.CleanProfile.Deep;
            }
            else if (lower.Contains("quick") || lower.Contains("rapide") || lower.Contains("leger") || lower.Contains("léger"))
            {
                profile = MemoryCleanerService.CleanProfile.Quick;
            }
            else if (lower.Contains("normal"))
            {
                profile = MemoryCleanerService.CleanProfile.Normal;
            }
        }

        daemon.Configure(daemon.IsEnabled, daemon.Mode, daemon.Value, daemon.ProtectForeground, profile);

        return Task.FromResult(new AiActionResult(
            true,
            $"✅ Profil de mémoire vive activé : {profile}.",
            $"Le profil {profile} est désormais actif pour toutes les opérations de nettoyage automatique."));
    }

    private static async Task<AiActionResult> ExecuteOptimizeNetworkAsync(string? param, CancellationToken ct)
    {
        INetworkOptimizerService netSvc = ResolveNetworkService();

        NetworkOptimizationPreset preset = NetworkOptimizationPreset.GamingUltra;
        if (!string.IsNullOrWhiteSpace(param))
        {
            string p = param.ToLowerInvariant();
            if (p.Contains("cognitif") || p.Contains("adaptatif") || p.Contains("engine") || p.Contains("expérimental") || p.Contains("autonome"))
            {
                string cognitiveReport = await netSvc.RunAdaptiveEngineSessionAsync(ct: ct).ConfigureAwait(false);
                return new AiActionResult(
                    true,
                    "✅ Session d'optimisation cognitive NDIS terminée avec succès sous surveillance Watchdog.",
                    cognitiveReport);
            }

            if (p.Contains("fast") || p.Contains("rapide") || p.Contains("bureau"))
            {
                preset = NetworkOptimizationPreset.Fast;
            }
            else if (p.Contains("debit") || p.Contains("débit") || p.Contains("throughput") || p.Contains("stream"))
            {
                preset = NetworkOptimizationPreset.MaxThroughput;
            }
            else if (p.Contains("auto") || p.Contains("tune") || p.Contains("complet"))
            {
                preset = NetworkOptimizationPreset.AutoTuned;
            }
        }

        string snapshotNote = "Instantané de sécurité créé automatiquement : demandez la restauration d'un instantané pour annuler cette optimisation.";
        try
        {
            IReadOnlyList<NetworkAdapterInfo> adapters = await netSvc.GetNetworkAdaptersAsync(ct).ConfigureAwait(false);
            NetworkAdapterInfo? adapter = adapters.FirstOrDefault(a => a.IsActive) ?? adapters.FirstOrDefault();
            if (adapter != null)
            {
                _ = await netSvc.SnapshotService
                    .CaptureSnapshotAsync(adapter.Id, adapter.Name, "Avant optimisation IA", ct)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            snapshotNote = "Attention : l'instantané de sécurité n'a pas pu être créé avant l'optimisation.";
        }

        NetworkBenchmarkReport report = await netSvc.ApplyPresetAsync(preset, null, null, ct).ConfigureAwait(false);
        return new AiActionResult(
            true,
            report.SummaryText,
            $"Ping : {report.BaselinePingMs:F1} ms ➔ {report.OptimizedPingMs:F1} ms ({report.GainPercentage:F1}% de gain). {report.AppliedTweaksCount} optimisations appliquées. {snapshotNote}");
    }

    private static async Task<AiActionResult> ExecuteBenchmarkNetworkAsync(CancellationToken ct)
    {
        INetworkOptimizerService netSvc = ResolveNetworkService();

        (double ping, double jitter, double loss) = await netSvc.MeasureLatencyAsync(count: 4, ct: ct).ConfigureAwait(false);
        IReadOnlyList<DnsBenchmarkResult> dns = await netSvc.RunDnsBenchmarkAsync(null, ct).ConfigureAwait(false);
        DnsBenchmarkResult? fastestDns = dns.FirstOrDefault(d => d.IsFastest);

        return new AiActionResult(
            true,
            $"✅ Benchmark Réseau terminé : Ping {ping:F1} ms (Gigue : ±{jitter:F1} ms, Pertes : {loss:F0}%).",
            fastestDns != null ? $"DNS le plus rapide détecté : {fastestDns.ProviderName} ({fastestDns.LatencyMs:F1} ms)." : "Serveur DNS joignable.");
    }

    private static async Task<AiActionResult> ExecuteResetNetworkDefaultsAsync(CancellationToken ct)
    {
        INetworkOptimizerService netSvc = ResolveNetworkService();

        bool ok = await netSvc.RestoreWindowsDefaultsAsync(null, null, ct).ConfigureAwait(false);
        return new AiActionResult(
            ok,
            ok ? "✅ Paramètres d'origine Windows restaurés (TCP/IP, Winsock, DNS DHCP et Registre)." : "⚠️ Restauration réseau incomplète.",
            "Tous les réglages par défaut de Microsoft sont rétablis.");
    }

    private static async Task<AiActionResult> ExecuteNetworkSnapshotAsync(CancellationToken ct)
    {
        INetworkOptimizerService netSvc = ResolveNetworkService();

        IReadOnlyList<NetworkAdapterInfo> adapters = await netSvc.GetNetworkAdaptersAsync(ct).ConfigureAwait(false);
        NetworkAdapterInfo? adapter = adapters.FirstOrDefault(a => a.IsActive) ?? adapters.FirstOrDefault();

        if (adapter == null)
        {
            return new AiActionResult(false, "Aucune carte réseau détectée sur ce système.");
        }

        NetworkConfigurationSnapshot snapshot = await netSvc.SnapshotService
            .CaptureSnapshotAsync(adapter.Id, adapter.Name, "Instantané Copilot IA", ct)
            .ConfigureAwait(false);

        return new AiActionResult(
            true,
            $"✅ Instantané réseau créé pour la carte « {adapter.Name} ».",
            "La configuration actuelle est sauvegardée. Vous pouvez revenir à cet état à tout moment en demandant la restauration d'un instantané.");
    }

    private static async Task<AiActionResult> ExecuteRestoreNetworkSnapshotAsync(CancellationToken ct)
    {
        INetworkOptimizerService netSvc = ResolveNetworkService();

        NetworkConfigurationSnapshot? baseline = netSvc.SnapshotService.InitialBaselineSnapshot;
        if (baseline == null && netSvc.SnapshotService.GetStoredSnapshots().Count == 0)
        {
            return new AiActionResult(false, "Aucun instantané réseau disponible à restaurer.");
        }

        bool ok = await netSvc.SnapshotService.RollbackToBaselineAsync(ct).ConfigureAwait(false);
        return new AiActionResult(
            ok,
            ok ? "✅ Configuration réseau restaurée depuis le dernier instantané de sécurité." : "⚠️ Restauration de l'instantané incomplète.",
            "Les paramètres réseau sont revenus à l'état sauvegardé avant la dernière optimisation.");
    }

    private static async Task<AiActionResult> ExecuteUpdateAllAppsAsync(CancellationToken ct)
    {
        InstallerService installer = ResolveInstallerService();

        List<InstallerService.WingetPackage>? upgrades = null;
        try
        {
            upgrades = await installer.GetAvailableUpgradesAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            upgrades = null;
        }

        if (upgrades != null && upgrades.Count == 0)
        {
            return new AiActionResult(true, "✅ Toutes vos applications sont déjà à jour.", "Aucune mise à jour Winget disponible.");
        }

        bool success = await installer.UpgradeAllAsync(null, ct).ConfigureAwait(false);
        return new AiActionResult(
            success,
            success
                ? $"✅ Mise à jour de toutes les applications terminée via Winget{(upgrades != null ? $" ({upgrades.Count} mises à jour)" : "")}."
                : "⚠️ Mise à jour des applications partiellement terminée. Consultez les journaux pour plus de détails.",
            upgrades != null
                ? "Applications mises à jour : " + string.Join(", ", upgrades.Take(8).Select(u => u.Name)) + (upgrades.Count > 8 ? ", …" : "")
                : null);
    }

    private static async Task<AiActionResult> ExecuteAnalyzeCrashesAsync(CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                var sb = new StringBuilder();
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string minidumpDir = Path.Combine(winDir, "Minidump");
                int dumpCount = 0;
                DateTime? lastDumpDate = null;

                if (Directory.Exists(minidumpDir))
                {
                    List<FileInfo> dumpFiles = new DirectoryInfo(minidumpDir).EnumerateFiles("*.dmp").ToList();
                    dumpCount = dumpFiles.Count;
                    if (dumpCount > 0)
                    {
                        FileInfo last = dumpFiles.OrderByDescending(f => f.LastWriteTime).First();
                        lastDumpDate = last.LastWriteTime;
                    }
                }

                var criticalEvents = new List<string>();
                try
                {
                    using var sysLog = new EventLog("System");
                    DateTime sevenDaysAgo = DateTime.Now.AddDays(-7);
                    int scanned = 0;
                    for (int i = sysLog.Entries.Count - 1; i >= 0 && scanned < 300 && criticalEvents.Count < 5; i--)
                    {
                        scanned++;
                        EventLogEntry entry = sysLog.Entries[i];
                        if (entry.EntryType == EventLogEntryType.Error || entry.InstanceId == 41 || entry.InstanceId == 1001 || entry.InstanceId == 6008)
                        {
                            if (entry.TimeGenerated > sevenDaysAgo)
                            {
                                string msg = entry.Message.Split('\n')[0].Trim();
                                if (msg.Length > 80)
                                {
                                    msg = msg[..80] + "...";
                                }

                                criticalEvents.Add($"[{entry.TimeGenerated:dd/MM HH:mm}] {entry.Source} : {msg}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogException(ex, "AiToolExecution.AnalyzeCrashes.EventLog");
                }

                if (dumpCount == 0 && criticalEvents.Count == 0)
                {
                    return new AiActionResult(
                        true,
                        "✅ Aucun crash mémoire récent ni écran bleu (BSOD) répertorié.",
                        "Dossier Windows/Minidump vierge. Zéro événement critique d'arrêt inopiné (Kernel-Power 41) dans les 7 derniers jours.");
                }

                _ = sb.AppendLine($"🔍 {dumpCount} fichier(s) minidump trouvé(s) dans Windows/Minidump.");
                if (lastDumpDate.HasValue)
                {
                    _ = sb.AppendLine($"Dernier crash mémoire enregistré : {lastDumpDate:g}");
                }

                if (criticalEvents.Count > 0)
                {
                    _ = sb.AppendLine("Derniers événements système critiques :");
                    foreach (string ev in criticalEvents.Take(3))
                    {
                        _ = sb.AppendLine($" • {ev}");
                    }
                }

                return new AiActionResult(
                    true,
                    $"⚠️ Analyse terminée : {dumpCount} crash(s) mémoire / plantage(s) récents répertoriés.",
                    sb.ToString());
            }
            catch (Exception ex)
            {
                return new AiActionResult(false, $"Erreur lors de l'analyse des crashs : {ex.Message}");
            }
        }, ct).ConfigureAwait(false);
    }

    private static async Task<AiActionResult> ExecuteTempCleanAsync(CancellationToken ct)
    {
        CleaningService cleaner = ServiceContainer.GetOptional<CleaningService>() ?? new CleaningService();
        List<CleaningService.CleaningCategory> cats = cleaner.GetAvailableCategories();

        foreach (CleaningService.CleaningCategory cat in cats)
        {
            cat.IsSelected = cat.Name.Contains("temporaires", StringComparison.OrdinalIgnoreCase) ||
                             cat.Name.Contains("Corbeille", StringComparison.OrdinalIgnoreCase) ||
                             cat.Name.Contains("navigateurs", StringComparison.OrdinalIgnoreCase);
        }

        CleaningService.CleaningResult result = await cleaner.ExecuteDeepCleanAsync(cats, null, ct).ConfigureAwait(false);
        double mb = result.TotalBytesFreed / (1024.0 * 1024.0);

        return new AiActionResult(
            true,
            $"✅ Nettoyage des fichiers temporaires terminé ({mb:F1} Mo récupérés).",
            $"{result.FilesDeleted} fichiers supprimés. Erreurs : {result.Errors.Count}");
    }

    private static async Task<AiActionResult> ExecuteHealthScanAsync(CancellationToken ct)
    {
        SystemHealthService health = ResolveHealthService();
        SystemHealthService.DefenderStatusInfo status = await health.GetDefenderStatusAsync().ConfigureAwait(false);

        bool isOk = status.IsAntivirusActive && status.IsRealTimeProtectionOn;
        return new AiActionResult(
            isOk,
            isOk
                ? "✅ Protection Windows Defender active et en temps réel."
                : "⚠️ Windows Defender signale une désactivation de la protection temps réel.",
            $"Version des signatures : {status.SignatureVersion} (Mise à jour : {status.LastUpdatedTime:g})");
    }

    private static async Task<AiActionResult> ExecuteSfcScanAsync(CancellationToken ct)
    {
        SystemHealthService health = ResolveHealthService();
        SystemHealthService.DiagnosticResult result = await health.RunSfcScanAsync(null, ct).ConfigureAwait(false);

        return new AiActionResult(
            result.Success,
            result.Success ? "✅ Analyse d'intégrité SFC terminée sans corruption." : "⚠️ Analyse SFC terminée avec des détections.",
            result.Summary);
    }

    private static async Task<AiActionResult> ExecuteDismScanAsync(CancellationToken ct)
    {
        SystemHealthService health = ResolveHealthService();
        SystemHealthService.DiagnosticResult result = await health.RunDismRestoreHealthAsync(null, ct).ConfigureAwait(false);

        return new AiActionResult(
            result.Success,
            result.Success ? "✅ Restauration de l'image Windows DISM réussie." : "⚠️ DISM a rencontré un problème lors de la réparation.",
            result.Summary);
    }

    private static async Task<AiActionResult> ExecuteDefenderScanAsync(CancellationToken ct)
    {
        SystemHealthService health = ResolveHealthService();
        SystemHealthService.DiagnosticResult result = await health.RunQuickDefenderScanAsync(null, ct).ConfigureAwait(false);

        return new AiActionResult(
            result.Success,
            result.Success ? "✅ Analyse rapide Windows Defender terminée sans menace active." : "⚠️ Analyse Windows Defender signalée.",
            result.Summary);
    }

    private static async Task<AiActionResult> ExecuteFullRepairAsync(CancellationToken ct)
    {
        SystemHealthService health = ResolveHealthService();
        SystemHealthService.DiagnosticResult result = await health.RunFullSystemRepairAsync(null, null, ct).ConfigureAwait(false);

        return new AiActionResult(
            result.Success,
            result.Success ? "✅ Réparation intégrale Windows 1-Clic terminée avec succès !" : "⚠️ La réparation Windows a signalé des avertissements.",
            result.Summary);
    }

    private static async Task<AiActionResult> ExecuteResetNetworkAsync(CancellationToken ct)
    {
        SystemHealthService health = ResolveHealthService();
        SystemHealthService.DiagnosticResult result = await health.ResetNetworkStackAsync(null, ct).ConfigureAwait(false);

        return new AiActionResult(
            result.Success,
            result.Success ? "✅ Pile réseau Winsock et cache DNS réinitialisés avec succès." : "⚠️ Réinitialisation réseau incomplète.",
            result.Summary);
    }

    private static async Task<AiActionResult> ExecuteResetUpdateAsync(CancellationToken ct)
    {
        SystemHealthService health = ResolveHealthService();
        SystemHealthService.DiagnosticResult result = await health.ResetWindowsUpdateAsync(null, ct).ConfigureAwait(false);

        return new AiActionResult(
            result.Success,
            result.Success ? "✅ Services et cache de téléchargement Windows Update réinitialisés." : "⚠️ Réinitialisation de Windows Update incomplète.",
            result.Summary);
    }

    private AiActionResult ExecuteNavigate(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return new AiActionResult(false, "Cible de navigation manquante.");
        }

        string normalized = target.ToLowerInvariant().Replace(" ", "").Replace("_", "");
        string? route = normalized switch
        {
            "dashboard" or "home" or "accueil" or "tableaudebord" => "open_dashboard",
            "programs" or "apps" or "applications" or "logiciels" => "open_programs",
            "disk" or "disque" or "diskspace" or "espacedisque" or "stockage" => "open_disk",
            "health" or "defense" or "sante" or "securite" or "security" => "open_health",
            "network" or "reseau" or "réseau" or "latence" or "ping" => "open_network",
            "installer" or "installeur" or "winget" => "open_installer",
            "cleaning" or "nettoyage" or "clean" => "open_cleaning",
            "ram" or "ramcleaner" or "memoire" => "open_ramcleaner",
            "settings" or "parametres" or "config" => "open_settings",
            "help" or "aide" or "support" => "open_help",
            _ => null
        };

        if (route == null)
        {
            return new AiActionResult(false, $"Module inconnu : {target}");
        }

        NavigationRequested?.Invoke(this, route);
        return new AiActionResult(true, $"Navigation vers '{target}' effectuée.");
    }

    private static async Task<AiActionResult> ExecuteOpenAppAsync(string? appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return new AiActionResult(false, "Nom de l'application manquant.");
        }

        return await Task.Run(async () =>
        {
            ISecurityPolicy? security = ServiceContainer.GetOptional<ISecurityPolicy>();
            IProcessExecutionService? procExec = ServiceContainer.GetOptional<IProcessExecutionService>();

            if (security != null && security.IsApplicationAllowed(appName, out string? policyResolved) && !string.IsNullOrWhiteSpace(policyResolved))
            {
                if (procExec != null && procExec.LaunchApplication(policyResolved))
                {
                    return new AiActionResult(true, $"Application '{appName}' lancée.");
                }

                try
                {
                    _ = Process.Start(new ProcessStartInfo(policyResolved) { UseShellExecute = true });
                    return new AiActionResult(true, $"Application '{appName}' lancée.");
                }
                catch (Exception ex)
                {
                    return new AiActionResult(false, $"Impossible de lancer '{appName}' : {ex.Message}");
                }
            }

            string? targetExe = null;
            InstalledProgramsService? programs = ServiceContainer.GetOptional<InstalledProgramsService>();
            if (programs != null)
            {
                List<InstalledProgramsService.ProgramInfo> all = await programs.GetAllInstalledProgramsAsync(false, CancellationToken.None).ConfigureAwait(false);
                List<InstalledProgramsService.ProgramInfo> launchable = all
                    .Where(p => !string.IsNullOrWhiteSpace(p.ExePath) && File.Exists(p.ExePath))
                    .ToList();

                InstalledProgramsService.ProgramInfo? match =
                    launchable.FirstOrDefault(p => string.Equals(p.Name, appName, StringComparison.OrdinalIgnoreCase))
                    ?? launchable.FirstOrDefault(p => p.Name.StartsWith(appName, StringComparison.OrdinalIgnoreCase))
                    ?? launchable.FirstOrDefault(p => p.Name.Contains(appName, StringComparison.OrdinalIgnoreCase));

                if (match != null && Path.GetExtension(match.ExePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    targetExe = match.ExePath;
                }
            }

            if (targetExe == null && File.Exists(appName) && Path.GetExtension(appName).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                targetExe = Path.GetFullPath(appName);
            }

            if (targetExe != null)
            {
                if (security != null && security.IsCommandBlocked(targetExe.ToLowerInvariant()))
                {
                    return new AiActionResult(false, $"L'application '{appName}' est bloquée par la politique de sécurité.");
                }

                if (procExec != null && procExec.LaunchApplication(targetExe))
                {
                    return new AiActionResult(true, $"Application '{Path.GetFileNameWithoutExtension(targetExe)}' lancée.");
                }

                try
                {
                    _ = Process.Start(new ProcessStartInfo(targetExe) { UseShellExecute = true });
                    return new AiActionResult(true, $"Application '{Path.GetFileNameWithoutExtension(targetExe)}' lancée.");
                }
                catch (Exception ex)
                {
                    return new AiActionResult(false, $"Échec du lancement de '{targetExe}' : {ex.Message}");
                }
            }

            return new AiActionResult(false, $"L'application '{appName}' n'est pas reconnue ou n'est pas autorisée par la politique de sécurité.");
        });
    }

    private static async Task<AiActionResult> ExecuteInstallWingetAsync(string? packageId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return new AiActionResult(false, "Identifiant de paquet Winget manquant.");
        }

        if (!InstallerService.IsValidPackageId(packageId))
        {
            return new AiActionResult(false, "Identifiant de paquet Winget invalide.");
        }

        InstallerService installer = ResolveInstallerService();
        bool success = await installer.InstallPackageAsync(packageId, null, ct).ConfigureAwait(false);

        return new AiActionResult(
            success,
            success
                ? $"✅ Installation de '{packageId}' réussie via Winget."
                : $"❌ Échec de l'installation de '{packageId}'. Consultez les logs pour plus de détails.");
    }

    public async Task<string> GetLiveDiagnosticsAsync()
    {
        return await Task.Run(() =>
        {
            var sb = new StringBuilder();
            MemoryCleanerService.RamInfo ram = MemoryCleanerService.GetRamInfo();
            double cpu = MemoryCleanerService.GetSystemCpuPercent();

            double ramUsedGb = ram.UsedPhysBytes / (1024.0 * 1024.0 * 1024.0);
            double ramTotalGb = ram.TotalPhysBytes / (1024.0 * 1024.0 * 1024.0);

            _ = sb.AppendLine("=== DIAGNOSTIC SYSTÈME EN DIRECT ===");
            _ = sb.AppendLine($"• CPU : {cpu:F1}% d'utilisation");
            _ = sb.AppendLine($"• RAM : {ramUsedGb:F1} Go / {ramTotalGb:F1} Go ({ram.PhysUsedPercent:F1}% utilisé)");

            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                {
                    double freeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    double totalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    _ = sb.AppendLine($"• Disque {drive.Name.TrimEnd('\\')} : {freeGb:F1} Go libres sur {totalGb:F1} Go");
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "AiToolExecution.Diagnostics.Drives");
            }

            Process[] procs = Process.GetProcesses();
            try
            {
                IEnumerable<string> topProcs = procs
                    .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0; } })
                    .Take(5)
                    .Select(p =>
                    {
                        try { return $"{p.ProcessName} ({p.WorkingSet64 / (1024 * 1024)} Mo)"; }
                        catch { return p.ProcessName; }
                    });
                _ = sb.AppendLine($"• Top 5 processus RAM : {string.Join(", ", topProcs)}");
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, "AiToolExecution.Diagnostics.TopProcesses");
            }
            finally
            {
                foreach (Process p in procs)
                {
                    try { p.Dispose(); } catch (Exception ex) { LoggingService.LogException(ex, "AiToolExecutionService.GetLiveDiagnosticsAsync.DisposeProcesses"); }
                }
            }

            return sb.ToString();
        });
    }

    private async Task<AiActionResult> GetSystemDiagnosticsResultAsync()
    {
        string diag = await GetLiveDiagnosticsAsync().ConfigureAwait(false);
        return new AiActionResult(true, "✅ Diagnostic du PC généré.", diag);
    }
}