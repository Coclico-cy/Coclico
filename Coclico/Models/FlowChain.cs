using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Coclico.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum NodeType
    {
        Start,
        OpenApp,
        CloseApp,
        RunCommand,
        KillProcess,
        Delay,
        End,
        Condition,
        Loop,
        Parallel,
        Notification,
        HttpRequest,
        FileOperation,
        SystemCheck,
        RunPowerShell,
        OpenUrl,
        SetVolume,
        MuteAudio,
        SetProcessPriority,
        KillByMemory,
        CleanTemp,
        RamClean,
        ClipboardSet,
        Screenshot,
        SendKeys,
        CompressFile,
        EmptyRecycleBin,
        TriggerShortcut
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ConditionOperator
    {
        ProcessRunning,
        ProcessNotRunning,
        FileExists,
        FileNotExists,
        TimeAfter,
        TimeBefore,
        CpuBelow,
        CpuAbove,
        RamBelow,
        RamAbove
    }

    public class FlowItem : INotifyPropertyChanged
    {
        private string _name = "Nouvelle action";
        private double _x;
        private double _y;
        private double _width = 160;
        private double _height = 70;
        private string? _programPath;
        private string? _arguments;
        private string? _commandLine;
        private string? _processName;
        private int _delaySeconds = 1;
        private bool _waitForPreviousExit;
        private NodeType _nodeType = NodeType.OpenApp;
        private bool _isEnabled = true;
        private int _retryCount;
        private int _timeoutSeconds;

        private ConditionOperator _conditionOperator = ConditionOperator.ProcessRunning;
        private string? _conditionValue;
        private int _loopCount = 1;
        private int _loopDelayMs;
        private string? _notificationMessage;
        private string? _httpUrl;
        private string? _httpMethod;
        private string? _fileOperationSource;
        private string? _fileOperationDest;
        private string? _fileOperationType;

        private string? _powerShellScript;
        private string? _urlToOpen;
        private int _volumeLevel = 50;
        private string? _processNamePriority;
        private string? _priorityLevel;
        private string? _clipboardText;
        private string? _screenshotPath;
        private string? _sendKeysText;
        private string? _compressSource;
        private string? _compressDest;
        private string? _triggerShortcutKeys;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public NodeType NodeType
        {
            get => _nodeType;
            set { _nodeType = value; OnPropertyChanged(); OnPropertyChanged(nameof(NodeTypeLabel)); OnPropertyChanged(nameof(NodeTypeBrush)); OnPropertyChanged(nameof(NodeTypeIcon)); OnPropertyChanged(nameof(SubTitle)); }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        public int RetryCount
        {
            get => _retryCount;
            set { _retryCount = Math.Max(0, Math.Min(value, 10)); OnPropertyChanged(); }
        }

        public int TimeoutSeconds
        {
            get => _timeoutSeconds;
            set { _timeoutSeconds = Math.Max(0, value); OnPropertyChanged(); }
        }

        public ConditionOperator ConditionOperator
        {
            get => _conditionOperator;
            set { _conditionOperator = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        public string? ConditionValue
        {
            get => _conditionValue;
            set { _conditionValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        public int LoopCount
        {
            get => _loopCount;
            set { _loopCount = Math.Max(1, Math.Min(value, 1000)); OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        public int LoopDelayMs
        {
            get => _loopDelayMs;
            set { _loopDelayMs = Math.Max(0, value); OnPropertyChanged(); }
        }

        public string? NotificationMessage
        {
            get => _notificationMessage;
            set { _notificationMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        public string? HttpUrl
        {
            get => _httpUrl;
            set { _httpUrl = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        public string? HttpMethod
        {
            get => _httpMethod;
            set { _httpMethod = value; OnPropertyChanged(); }
        }

        public string? FileOperationSource
        {
            get => _fileOperationSource;
            set { _fileOperationSource = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        public string? FileOperationDest
        {
            get => _fileOperationDest;
            set { _fileOperationDest = value; OnPropertyChanged(); }
        }

        public string? FileOperationType
        {
            get => _fileOperationType;
            set { _fileOperationType = value; OnPropertyChanged(); }
        }

        public string? PowerShellScript
        {
            get => _powerShellScript;
            set { _powerShellScript = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        public string? UrlToOpen
        {
            get => _urlToOpen;
            set { _urlToOpen = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        public int VolumeLevel
        {
            get => _volumeLevel;
            set { _volumeLevel = Math.Clamp(value, 0, 100); OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        public string? ProcessNamePriority
        {
            get => _processNamePriority;
            set { _processNamePriority = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        public string? PriorityLevel
        {
            get => _priorityLevel;
            set { _priorityLevel = value; OnPropertyChanged(); }
        }

        public string? ClipboardText
        {
            get => _clipboardText;
            set { _clipboardText = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        public string? ScreenshotPath
        {
            get => _screenshotPath;
            set { _screenshotPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        public string? SendKeysText
        {
            get => _sendKeysText;
            set { _sendKeysText = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        public string? CompressSource
        {
            get => _compressSource;
            set { _compressSource = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        public string? CompressDest
        {
            get => _compressDest;
            set { _compressDest = value; OnPropertyChanged(); }
        }

        public string? TriggerShortcutKeys
        {
            get => _triggerShortcutKeys;
            set { _triggerShortcutKeys = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        [JsonIgnore]
        public string NodeTypeLabel => NodeType switch
        {
            NodeType.Start              => "START",
            NodeType.OpenApp            => "OUVRIR APP",
            NodeType.CloseApp           => "FERMER APP",
            NodeType.RunCommand         => "COMMANDE",
            NodeType.KillProcess        => "TUER PROCESSUS",
            NodeType.Delay              => "DÉLAI",
            NodeType.End                => "FIN",
            NodeType.Condition          => "CONDITION",
            NodeType.Loop               => "BOUCLE",
            NodeType.Parallel           => "PARALLÈLE",
            NodeType.Notification       => "NOTIFICATION",
            NodeType.HttpRequest        => "HTTP",
            NodeType.FileOperation      => "FICHIER",
            NodeType.SystemCheck        => "SYSTÈME",
            NodeType.RunPowerShell      => "POWERSHELL",
            NodeType.OpenUrl            => "OUVRIR URL",
            NodeType.SetVolume          => "VOLUME",
            NodeType.MuteAudio          => "MUET",
            NodeType.SetProcessPriority => "PRIORITÉ",
            NodeType.KillByMemory       => "TUE + RAM",
            NodeType.CleanTemp          => "TEMP",
            NodeType.RamClean           => "NETTOYER RAM",
            NodeType.ClipboardSet       => "PRESSE-PAPIERS",
            NodeType.Screenshot         => "CAPTURE",
            NodeType.SendKeys           => "TOUCHES",
            NodeType.CompressFile       => "COMPRESSER",
            NodeType.EmptyRecycleBin    => "CORBEILLE",
            NodeType.TriggerShortcut    => "RACCOURCI",
            _                           => "ACTION"
        };

        [JsonIgnore]
        public string NodeTypeBrush => NodeType switch
        {
            NodeType.Start              => "#22C55E",
            NodeType.End                => "#EF4444",
            NodeType.OpenApp            => "#6366F1",
            NodeType.CloseApp           => "#F97316",
            NodeType.RunCommand         => "#A855F7",
            NodeType.KillProcess        => "#DC2626",
            NodeType.Delay              => "#0EA5E9",
            NodeType.Condition          => "#EAB308",
            NodeType.Loop               => "#14B8A6",
            NodeType.Parallel           => "#8B5CF6",
            NodeType.Notification       => "#F472B6",
            NodeType.HttpRequest        => "#06B6D4",
            NodeType.FileOperation      => "#84CC16",
            NodeType.SystemCheck        => "#F59E0B",
            NodeType.RunPowerShell      => "#5B21B6",
            NodeType.OpenUrl            => "#0284C7",
            NodeType.SetVolume          => "#7DD3FC",
            NodeType.MuteAudio          => "#64748B",
            NodeType.SetProcessPriority => "#FB923C",
            NodeType.KillByMemory       => "#B91C1C",
            NodeType.CleanTemp          => "#A16207",
            NodeType.RamClean           => "#0E7490",
            NodeType.ClipboardSet       => "#0891B2",
            NodeType.Screenshot         => "#DB2777",
            NodeType.SendKeys           => "#7C3AED",
            NodeType.CompressFile       => "#CA8A04",
            NodeType.EmptyRecycleBin    => "#6B7280",
            NodeType.TriggerShortcut    => "#EC4899",
            _                           => "#6B7280"
        };

        [JsonIgnore]
        public string NodeTypeIcon => NodeType switch
        {
            NodeType.Start              => "▶",
            NodeType.OpenApp            => "⊕",
            NodeType.CloseApp           => "⊗",
            NodeType.RunCommand         => "⌨",
            NodeType.KillProcess        => "⚡",
            NodeType.Delay              => "⏱",
            NodeType.End                => "⏹",
            NodeType.Condition          => "❓",
            NodeType.Loop               => "🔄",
            NodeType.Parallel           => "⫸",
            NodeType.Notification       => "🔔",
            NodeType.HttpRequest        => "🌐",
            NodeType.FileOperation      => "📁",
            NodeType.SystemCheck        => "🔍",
            NodeType.RunPowerShell      => "⚙",
            NodeType.OpenUrl            => "🔗",
            NodeType.SetVolume          => "🔊",
            NodeType.MuteAudio          => "🔇",
            NodeType.SetProcessPriority => "⬆",
            NodeType.KillByMemory       => "💀",
            NodeType.CleanTemp          => "🧹",
            NodeType.RamClean           => "🧠",
            NodeType.ClipboardSet       => "📋",
            NodeType.Screenshot         => "📷",
            NodeType.SendKeys           => "⌨",
            NodeType.CompressFile       => "🗜",
            NodeType.EmptyRecycleBin    => "🗑",
            NodeType.TriggerShortcut    => "⌨️",
            _                           => "◆"
        };

        [JsonIgnore]
        public string SubTitle => NodeType switch
        {
            NodeType.OpenApp            => string.IsNullOrEmpty(ProgramPath) ? "Aucune application" : Path.GetFileNameWithoutExtension(ProgramPath),
            NodeType.CloseApp           => string.IsNullOrEmpty(ProgramPath) ? "Aucune cible"       : Path.GetFileNameWithoutExtension(ProgramPath),
            NodeType.RunCommand         => string.IsNullOrEmpty(CommandLine) ? "Aucune commande"    : (CommandLine!.Length > 28 ? CommandLine[..28] + "…" : CommandLine),
            NodeType.KillProcess        => string.IsNullOrEmpty(ProcessName) ? "Aucun processus"    : ProcessName,
            NodeType.Delay              => $"{DelaySeconds} s",
            NodeType.Start              => "Début de la chaîne",
            NodeType.End                => "Fin de la chaîne",
            NodeType.Condition          => $"{ConditionOperator}: {ConditionValue ?? "..."}",
            NodeType.Loop               => $"{LoopCount}x itérations",
            NodeType.Parallel           => "Exécution parallèle",
            NodeType.Notification       => NotificationMessage ?? "Message...",
            NodeType.HttpRequest        => HttpUrl ?? "URL...",
            NodeType.FileOperation      => FileOperationSource != null ? Path.GetFileName(FileOperationSource) : "Fichier...",
            NodeType.SystemCheck        => "Vérification système",
            NodeType.RunPowerShell      => string.IsNullOrEmpty(PowerShellScript) ? "Script vide" : (PowerShellScript!.Length > 28 ? PowerShellScript[..28] + "…" : PowerShellScript),
            NodeType.OpenUrl            => UrlToOpen ?? "URL...",
            NodeType.SetVolume          => $"{VolumeLevel}%",
            NodeType.MuteAudio          => "Couper le son",
            NodeType.SetProcessPriority => ProcessNamePriority ?? "Processus...",
            NodeType.KillByMemory       => "Tuer + gros consommateur",
            NodeType.CleanTemp          => "Vider dossier Temp",
            NodeType.RamClean           => "Libérer mémoire",
            NodeType.ClipboardSet       => string.IsNullOrEmpty(ClipboardText) ? "Texte..." : (ClipboardText!.Length > 25 ? ClipboardText[..25] + "…" : ClipboardText),
            NodeType.Screenshot         => ScreenshotPath ?? "Dossier...",
            NodeType.SendKeys           => SendKeysText ?? "Touches...",
            NodeType.CompressFile       => CompressSource != null ? Path.GetFileName(CompressSource) : "Source...",
            NodeType.EmptyRecycleBin    => "Vider la corbeille",
            NodeType.TriggerShortcut    => TriggerShortcutKeys ?? "Raccourci...",
            _                           => string.Empty
        };

        [JsonIgnore] public bool CanDelete  => NodeType != NodeType.Start && NodeType != NodeType.End;
        [JsonIgnore] public bool CanEdit    => NodeType != NodeType.Start && NodeType != NodeType.End;

        public double X
        {
            get => _x;
            set { _x = value; OnPropertyChanged(); OnPropertyChanged(nameof(CenterX)); OnPropertyChanged(nameof(RightX)); OnPropertyChanged(nameof(MiddleY)); }
        }

        public double Y
        {
            get => _y;
            set { _y = value; OnPropertyChanged(); OnPropertyChanged(nameof(CenterY)); OnPropertyChanged(nameof(MiddleY)); }
        }

        public double Width
        {
            get => _width;
            set { _width = value; OnPropertyChanged(); OnPropertyChanged(nameof(CenterX)); OnPropertyChanged(nameof(RightX)); }
        }

        public double Height
        {
            get => _height;
            set { _height = value; OnPropertyChanged(); OnPropertyChanged(nameof(CenterY)); OnPropertyChanged(nameof(MiddleY)); }
        }

        public double CenterX => X + Width / 2;
        public double CenterY => Y + Height / 2;
        public double RightX  => X + Width;
        public double MiddleY => Y + Height / 2;

        public string? ProgramPath
        {
            get => _programPath;
            set { _programPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        public string? Arguments
        {
            get => _arguments;
            set { _arguments = value; OnPropertyChanged(); }
        }

        public string? CommandLine
        {
            get => _commandLine;
            set { _commandLine = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        public string? ProcessName
        {
            get => _processName;
            set { _processName = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        public int DelaySeconds
        {
            get => _delaySeconds;
            set { _delaySeconds = value < 0 ? 0 : value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTitle)); }
        }

        public bool WaitForPreviousExit
        {
            get => _waitForPreviousExit;
            set { _waitForPreviousExit = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class FlowConnection : INotifyPropertyChanged
    {
        private FlowItem? _startItem;
        private FlowItem? _endItem;
        private string? _label;

        public FlowItem? StartItem
        {
            get => _startItem;
            set { _startItem = value; OnPropertyChanged(); }
        }

        public FlowItem? EndItem
        {
            get => _endItem;
            set { _endItem = value; OnPropertyChanged(); }
        }

        public string? Label
        {
            get => _label;
            set { _label = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class FlowInsertMarker
    {
        public int AfterIndex { get; set; }
    }

    public class FlowChain : INotifyPropertyChanged
    {
        private string _id          = Guid.NewGuid().ToString();
        private string _name        = "Nouvelle Chaîne";
        private string _description = string.Empty;
        private bool _isRunning;
        private string _lastRunStatus = string.Empty;
        private DateTime? _lastRunTime;
        private string? _triggerHotkey;
        private bool _autoTriggerEnabled;
        private int _autoTriggerIntervalSec = 300;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; OnPropertyChanged(); }
        }

        public string LastRunStatus
        {
            get => _lastRunStatus;
            set { _lastRunStatus = value; OnPropertyChanged(); }
        }

        public DateTime? LastRunTime
        {
            get => _lastRunTime;
            set { _lastRunTime = value; OnPropertyChanged(); }
        }

        public string? TriggerHotkey
        {
            get => _triggerHotkey;
            set { _triggerHotkey = value; OnPropertyChanged(); OnPropertyChanged(nameof(TriggerSummary)); }
        }

        public bool AutoTriggerEnabled
        {
            get => _autoTriggerEnabled;
            set { _autoTriggerEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(TriggerSummary)); }
        }

        public int AutoTriggerIntervalSec
        {
            get => _autoTriggerIntervalSec;
            set { _autoTriggerIntervalSec = Math.Max(10, value); OnPropertyChanged(); OnPropertyChanged(nameof(TriggerSummary)); }
        }

        [JsonIgnore]
        public string TriggerSummary
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(_triggerHotkey))
                    parts.Add($"⌨ {_triggerHotkey}");
                if (_autoTriggerEnabled)
                    parts.Add($"⏰ toutes les {(_autoTriggerIntervalSec < 60 ? $"{_autoTriggerIntervalSec}s" : $"{_autoTriggerIntervalSec / 60}min")}");
                return parts.Count > 0 ? string.Join("  ", parts) : "Pas de déclenchement auto";
            }
        }

        public ObservableCollection<FlowItem> Items { get; } = new ObservableCollection<FlowItem>();
        public ObservableCollection<FlowConnection> Connections { get; } = new ObservableCollection<FlowConnection>();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
