using System;
using System.Collections.Generic;

namespace Coclico.Installer.Models;

public enum InstallerLanguage
{
    French,
    English
}

public class InstallerLocalization
{
    public static InstallerLanguage CurrentLanguage { get; set; } =
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase)
        ? InstallerLanguage.French
        : InstallerLanguage.English;

    private static readonly Dictionary<string, (string Fr, string En)> Strings = new()
    {
        ["AppTitle"] = ("Coclico", "Coclico"),
        ["AppSubtitle"] = ("Assistant Système & Maintenance Windows", "Windows System & Maintenance Companion"),
        ["BetaTag"] = ("BÊTA", "BETA"),
        ["BetaNotice"] = ("Coclico est actuellement en version Bêta. Certaines fonctionnalités peuvent être expérimentales.",
                          "Coclico is currently in Beta. Some features may be experimental."),
        ["BtnNext"] = ("Suivant >", "Next >"),
        ["BtnBack"] = ("< Précédent", "< Back"),
        ["BtnCancel"] = ("Annuler", "Cancel"),
        ["BtnInstall"] = ("Installer maintenant", "Install now"),
        ["BtnLaunch"] = ("Lancer Coclico", "Launch Coclico"),
        ["BtnFinish"] = ("Terminer", "Finish"),
        ["BtnBrowse"] = ("Parcourir...", "Browse..."),

        ["StepWelcome"] = ("Bienvenue", "Welcome"),
        ["StepLicense"] = ("Licence", "License"),
        ["StepLocation"] = ("Dossier", "Folder"),
        ["StepComponents"] = ("Composants", "Components"),
        ["StepSummary"] = ("Prêt", "Ready"),
        ["StepInstall"] = ("Installation", "Installing"),
        ["StepFinish"] = ("Terminé", "Finished"),

        ["WelcomeTitle"] = ("Bienvenue dans l'installation de Coclico", "Welcome to the Coclico Setup"),
        ["WelcomeDesc"] = ("Cet assistant va installer Coclico sur votre ordinateur en quelques étapes.",
                           "This wizard will install Coclico on your computer in just a few steps."),
        ["DotNetCheckTitle"] = ("Environnement d'exécution .NET 10", ".NET 10 Runtime Environment"),
        ["DotNetFound"] = ("Runtime .NET 10 Desktop détecté ({0})", ".NET 10 Desktop Runtime detected ({0})"),
        ["DotNetMissing"] = ("Non détecté globalement — Le runtime autonome inclus dans Coclico sera utilisé.",
                             "Not globally detected — The self-contained runtime included with Coclico will be used."),
        ["GpuCheckTitle"] = ("Accélération Matérielle Graphique", "Hardware Graphics Acceleration"),
        ["GpuNvidiaFound"] = ("GPU NVIDIA détecté ({0}) : Accélération CUDA disponible.",
                              "NVIDIA GPU detected ({0}): CUDA acceleration available."),
        ["GpuStandard"] = ("Carte standard ({0}) : Mode CPU haute performance activé.",
                           "Standard GPU ({0}): High-performance CPU mode enabled."),

        ["LicenseTitle"] = ("Contrat de Licence Logicielle", "Software License Agreement"),
        ["LicenseDesc"] = ("Veuillez lire attentivement les termes de la licence avant de continuer.",
                           "Please read the following license terms carefully before proceeding."),
        ["LicenseAccept"] = ("J'accepte les termes du contrat de licence", "I accept the terms in the License Agreement"),

        ["LocationTitle"] = ("Dossier de Destination", "Destination Folder"),
        ["LocationDesc"] = ("Choisissez le répertoire dans lequel Coclico sera installé.",
                            "Choose the folder in which to install Coclico."),
        ["LocationPrivacyNote"] = ("Installation isolée pour votre session utilisateur (%LocalAppData%). Aucun droit administrateur requis.",
                                   "User-level installation (%LocalAppData%). No administrator privileges required."),
        ["SpaceRequired"] = ("Espace disque requis : ~{0} Mo", "Space required: ~{0} MB"),
        ["SpaceAvailable"] = ("Espace disponible sur le lecteur {0} : {1} Go", "Space available on drive {0}: {1} GB"),

        ["ComponentsTitle"] = ("Composants & Modèles d'IA", "Components & AI Models"),
        ["ComponentsDesc"] = ("Sélectionnez les composants optionnels à intégrer à votre installation.",
                              "Select optional components to include with your installation."),
        ["CompCoreTitle"] = ("Application Coclico (Obligatoire)", "Coclico Application (Required)"),
        ["CompCoreDesc"] = ("Exécutable principal, tableau de bord, outils de nettoyage et d'optimisation.",
                            "Main executable, dashboard, cleaning and system optimization tools."),
        ["CompCudaTitle"] = ("Bibliothèques NVIDIA CUDA 12 (téléchargées à la demande)", "NVIDIA CUDA 12 Libraries (downloaded on demand)"),
        ["CompCudaDesc"] = ("Accélération GPU pour les cartes NVIDIA RTX / GTX compatibles. Le runtime (~1,2 Go) n'est pas inclus dans l'installateur : il est téléchargé pendant l'installation.",
                            "GPU acceleration for compatible NVIDIA RTX / GTX cards. The runtime (~1.2 GB) is not bundled: it is downloaded during installation."),
        ["CompAiTitle"] = ("Modèle IA Local Coclico Copilot (~1.1 Go)", "Coclico Copilot Local AI Model (~1.1 GB)"),
        ["CompAiDesc"] = ("Pré-télécharge le modèle GGUF pour utiliser l'assistant IA localement sans internet.",
                          "Pre-downloads the GGUF model to use the AI assistant locally without internet."),
        ["OptionsGroupTitle"] = ("Raccourcis & Démarrage", "Shortcuts & Startup"),
        ["OptDesktop"] = ("Créer une icône sur le Bureau", "Create a Desktop shortcut"),
        ["OptStartMenu"] = ("Ajouter au menu Démarrer", "Add to Start Menu"),
        ["OptAutoLaunch"] = ("Lancer Coclico à la fermeture de l'installeur", "Launch Coclico when setup exits"),

        ["SummaryTitle"] = ("Prêt à installer", "Ready to Install"),
        ["SummaryDesc"] = ("Le programme est prêt à commencer l'installation sur votre ordinateur.",
                           "Setup is now ready to begin installing Coclico on your computer."),
        ["SummaryDestLabel"] = ("Destination :", "Destination:"),
        ["SummaryComponentsLabel"] = ("Composants sélectionnés :", "Selected components:"),

        ["InstallingTitle"] = ("Installation de Coclico en cours...", "Installing Coclico..."),
        ["InstallingDesc"] = ("Veuillez patienter pendant que les fichiers sont copiés et configurés...",
                              "Please wait while files are being copied and configured..."),

        ["FinishTitle"] = ("Merci d'avoir installé Coclico !", "Thank you for installing Coclico!"),
        ["FinishDesc"] = ("Coclico a été installé avec succès sur votre ordinateur.",
                          "Coclico has been successfully installed on your computer."),
        ["FinishNote"] = ("Vous pouvez maintenant lancer l'application et profiter de ses fonctionnalités.",
                          "You can now launch the application and enjoy its features."),
        ["FinishThanksTitle"] = ("Un immense merci pour votre confiance", "A huge thank you for your trust"),
        ["FinishThanksDesc"] = ("Choisir Coclico, c'est apporter un soutien immense à ce projet. Savoir que l'application est utilisée est la plus belle des motivations, et votre confiance donne tout son sens au travail accompli. Merci de faire partie de l'aventure !",
                                "Choosing Coclico means a huge amount of support for this project. Knowing the app is being used is the greatest motivation, and your trust gives real meaning to this work. Thank you for being part of the adventure!"),

        ["UninstallTitle"] = ("Désinstaller Coclico", "Uninstall Coclico"),
        ["UninstallDesc"] = ("Êtes-vous sûr de vouloir supprimer Coclico de votre système ?\nVos réglages et données personnelles seront conservés.",
                             "Are you sure you want to remove Coclico from your system?\nYour personal settings and data will be kept."),
        ["UninstallBtn"] = ("Désinstaller", "Uninstall"),
        ["UninstallFinished"] = ("Coclico a été désinstallé avec succès.", "Coclico was successfully uninstalled."),
        ["UninstallGoodbyeTitle"] = ("Merci d'avoir utilisé Coclico !", "Thank you for using Coclico!"),
        ["UninstallGoodbyeDesc"] = ("Nous espérons vous revoir très bientôt. Bonne continuation !", "We hope to see you again very soon. All the best!"),

        ["UnTitle"] = ("Désinstallation complète de Coclico", "Complete Coclico uninstall"),
        ["UnSubtitle"] = ("Cette fenêtre supprime l'application, vos données et toutes ses traces sur le système.", "This window removes the application, your data and all its traces from the system."),
        ["UnWarning"] = ("ATTENTION : cette opération est définitive. Aucune sauvegarde ne sera effectuée.", "WARNING: this operation is permanent. No backup will be made."),
        ["UnListTitle"] = ("Les éléments suivants seront supprimés de manière permanente :", "The following items will be permanently deleted:"),
        ["UnItem1"] = ("Dossier d'installation", "Installation folder"),
        ["UnItem2"] = ("Raccourcis Bureau et Menu Démarrer", "Desktop and Start Menu shortcuts"),
        ["UnItem3"] = ("Données et configuration", "Data and configuration"),
        ["UnItem4"] = ("Entrées de registre Windows", "Windows registry entries"),
        ["UnItem5"] = ("Cache et fichiers temporaires", "Cache and temporary files"),
        ["UnReady"] = ("Prêt à désinstaller.", "Ready to uninstall."),
        ["UnStarting"] = ("Démarrage de la désinstallation...", "Starting uninstall..."),
        ["UnCancelled"] = ("Désinstallation annulée.", "Uninstall cancelled."),
        ["UnCheck"] = ("Je comprends que TOUTES les données Coclico seront supprimées définitivement et je confirme la désinstallation.", "I understand that ALL Coclico data will be permanently deleted and I confirm the uninstall."),
        ["UnKeepData"] = ("Conserver mes données (profils, réglages, modèles IA)", "Keep my data (profiles, settings, AI models)"),
        ["UnConfirmTitle"] = ("Confirmation requise", "Confirmation required"),
        ["UnConfirmMsg"] = ("Vous devez cocher la case de confirmation pour désinstaller Coclico complètement.", "You must check the confirmation box to completely uninstall Coclico."),
        ["UnNotFound"] = ("Non trouvé", "Not found"),
        ["UnBtnCancel"] = ("Annuler", "Cancel"),
        ["UnBtnUninstall"] = ("Désinstaller TOUT", "Uninstall ALL"),
        ["UnByeTitle"] = ("Merci d'avoir utilisé Coclico !", "Thank you for using Coclico!"),
        ["UnByeDesc"] = ("La désinstallation est terminée : l'application, vos données, les raccourcis et les entrées de registre ont été supprimés avec succès.", "The uninstall is complete: the application, your data, shortcuts and registry entries have been successfully removed."),
        ["UnByeFooter"] = ("Nous espérons vous revoir très bientôt. Bonne continuation !", "We hope to see you again very soon. All the best!")
    };

    public static string Get(string key)
    {
        if (Strings.TryGetValue(key, out var val))
        {
            return CurrentLanguage == InstallerLanguage.French ? val.Fr : val.En;
        }
        return key;
    }
}