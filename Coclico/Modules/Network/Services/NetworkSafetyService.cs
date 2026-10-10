using Coclico.Models.Network;

namespace Coclico.Services.Network;

public class NetworkSafetyService : INetworkSafetyService
{
    private static readonly Dictionary<string, (ParameterCategory Category, ParameterSafetyLevel Safety, string Description)> KnownParameters =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["*InterruptModeration"] = (ParameterCategory.InterruptHandling, ParameterSafetyLevel.MediumRisk,
            "Régule les interruptions CPU générées par la carte réseau. Désactivé pour une latence immédiate au prix d'un usage CPU très légèrement supérieur."),
            ["*InterruptModerationRate"] = (ParameterCategory.InterruptHandling, ParameterSafetyLevel.MediumRisk,
            "Fréquence de modération des interruptions."),
            ["*PacketCoalescing"] = (ParameterCategory.InterruptHandling, ParameterSafetyLevel.LowRisk,
            "Regroupement des paquets reçus avant de notifier le système. Désactivé pour minimiser la gigue et le ping."),

            ["*FlowControl"] = (ParameterCategory.FlowControl, ParameterSafetyLevel.MediumRisk,
            "Contrôle de flux IEEE 802.3x (trames PAUSE). Désactivé pour éliminer les micro-blocages de transmission lors des sessions en ligne."),

            ["*EEE"] = (ParameterCategory.EnergyManagement, ParameterSafetyLevel.LowRisk,
            "Energy Efficient Ethernet (IEEE 802.3az). Désactivé pour supprimer les micro-latences de réveil de la couche physique PHY."),
            ["EnableGreenEthernet"] = (ParameterCategory.EnergyManagement, ParameterSafetyLevel.LowRisk,
            "Green Ethernet (Realtek). Désactivé pour garantir une pleine puissance continue du lien physique."),
            ["GigaLite"] = (ParameterCategory.EnergyManagement, ParameterSafetyLevel.LowRisk,
            "Mode d'économie Gigabit à faible débit. Désactivé pour forcer la bande passante maximale."),
            ["EEELinkAdvertisement"] = (ParameterCategory.EnergyManagement, ParameterSafetyLevel.LowRisk,
            "Annonce de capacité d'économie d'énergie lors de l'auto-négociation. Désactivé pour la stabilité."),
            ["PowerSavingMode"] = (ParameterCategory.EnergyManagement, ParameterSafetyLevel.LowRisk,
            "Gestion d'alimentation interne du contrôleur réseau."),

            ["*ReceiveBuffers"] = (ParameterCategory.BuffersAndQueues, ParameterSafetyLevel.LowRisk,
            "Nombre de tampons mémoire RX alloués à la réception. Une valeur élevée prévient la perte de paquets sous trafic intense."),
            ["*TransmitBuffers"] = (ParameterCategory.BuffersAndQueues, ParameterSafetyLevel.LowRisk,
            "Nombre de tampons mémoire TX alloués à l'émission. Une valeur adéquate évite la saturation de la file de transmission."),
            ["*RSS"] = (ParameterCategory.BuffersAndQueues, ParameterSafetyLevel.Safe,
            "Receive Side Scaling : distribue le traitement des flux réseau sur les multiples cœurs du processeur."),
            ["*NumRssQueues"] = (ParameterCategory.BuffersAndQueues, ParameterSafetyLevel.Safe,
            "Nombre de files RSS allouées aux cœurs CPU."),

            ["*LsoV2IPv4"] = (ParameterCategory.HardwareOffload, ParameterSafetyLevel.LowRisk,
            "Large Send Offload v2 (IPv4). Désactiver réduit le bufferbloat au détriment d'un calcul de segmentation reporté sur le CPU."),
            ["*LsoV2IPv6"] = (ParameterCategory.HardwareOffload, ParameterSafetyLevel.LowRisk,
            "Large Send Offload v2 (IPv6). Désactiver évite les paquets TCP anormalement volumineux en local."),
            ["*IPChecksumOffloadIPv4"] = (ParameterCategory.HardwareOffload, ParameterSafetyLevel.Safe,
            "Calcul matériel de la somme de contrôle IP."),
            ["*TCPChecksumOffloadIPv4"] = (ParameterCategory.HardwareOffload, ParameterSafetyLevel.Safe,
            "Calcul matériel de la somme de contrôle TCP (IPv4)."),
            ["*TCPChecksumOffloadIPv6"] = (ParameterCategory.HardwareOffload, ParameterSafetyLevel.Safe,
            "Calcul matériel de la somme de contrôle TCP (IPv6)."),
            ["*UDPChecksumOffloadIPv4"] = (ParameterCategory.HardwareOffload, ParameterSafetyLevel.Safe,
            "Calcul matériel de la somme de contrôle UDP (IPv4)."),
            ["*UDPChecksumOffloadIPv6"] = (ParameterCategory.HardwareOffload, ParameterSafetyLevel.Safe,
            "Calcul matériel de la somme de contrôle UDP (IPv6)."),

            ["ThroughputBoosterEnabled"] = (ParameterCategory.WirelessRadio, ParameterSafetyLevel.Safe,
            "Booster de débit Wi-Fi Intel : optimise le groupement de trames pour les transferts rapides."),
            ["RoamAggressiveness"] = (ParameterCategory.WirelessRadio, ParameterSafetyLevel.LowRisk,
            "Agressivité d'itinérance Wi-Fi : fréquence de recherche d'une meilleure borne Wi-Fi."),
            ["MIMOPowerSaveMode"] = (ParameterCategory.WirelessRadio, ParameterSafetyLevel.LowRisk,
            "Mode d'économie d'énergie MIMO sans fil. Désactivé pour maintenir toutes les antennes actives."),

            ["*SpeedDuplex"] = (ParameterCategory.MtuAndFraming, ParameterSafetyLevel.HighRisk,
            "Vitesse et mode duplex de la liaison physique. Risque de coupure totale si forcé incorrectement."),
            ["*JumboPacket"] = (ParameterCategory.MtuAndFraming, ParameterSafetyLevel.HighRisk,
            "Trames géantes MTU > 1500. Risque de rejet de paquets par le routeur/FAI."),

            ["TcpNoDelay"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.LowRisk,
            "Désactivation de l'algorithme de Nagle pour un envoi immédiat des paquets sans temporisation de 40ms."),
            ["TcpAckFrequency"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.LowRisk,
            "Accusé de réception TCP immédiat (ACK) après chaque paquet au lieu de regrouper par paires."),
            ["TCPDelAckTicks"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.LowRisk,
            "Temporisation d'envoi des acquittements TCP décalés (0 = immédiat pour gaming)."),
            ["NonSackRttResiliency"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Résilience RTT pour les paquets non-SACK : améliore la tolérance aux pertes partielles."),
            ["DefaultTTL"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Time To Live par défaut des paquets IP sortants (64 = optimal gaming)."),
            ["MaxUserPort"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Plage maximale des ports éphémères sortants (65534 prévient l'épuisement de sockets)."),
            ["TcpTimedWaitDelay"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Délai d'attente de fermeture des sockets TCP (30s = recyclage rapide de connexion)."),
            ["SynAttackProtect"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Protection anti-inondation SYN sans ralentir la négociation normale."),
            ["EnablePMTUDiscovery"] = (ParameterCategory.MtuAndFraming, ParameterSafetyLevel.Safe,
            "Découverte automatique du MTU de chemin pour éviter la fragmentation des routeurs intermédiaires."),
            ["EnablePMTUBHDetect"] = (ParameterCategory.MtuAndFraming, ParameterSafetyLevel.Safe,
            "Détection des trous noirs MTU (routeurs qui ignorent le drapeau DF)."),
            ["NetworkThrottlingIndex"] = (ParameterCategory.SystemThrottling, ParameterSafetyLevel.Safe,
            "Suppression du bridage réseau multimédia de Windows (0xFFFFFFFF = débridé à 100%)."),
            ["SystemResponsiveness"] = (ParameterCategory.SystemThrottling, ParameterSafetyLevel.Safe,
            "Priorité accordée aux paquets réactifs et jeux vidéo face aux tâches système en arrière-plan."),
            ["NonBestEffortLimit"] = (ParameterCategory.SystemThrottling, ParameterSafetyLevel.Safe,
            "Limite de bande passante réservée par la QoS Windows (0 = aucune réserve artificielle)."),
            ["DoNotUseNLA"] = (ParameterCategory.SystemThrottling, ParameterSafetyLevel.Safe,
            "Désactive la temporisation de localisation réseau pour accélérer l'accès Internet."),
            ["DisableTaskOffload"] = (ParameterCategory.HardwareOffload, ParameterSafetyLevel.Safe,
            "Maintien du délestage matériel actif sur la puce réseau."),
            ["TCPMaxDataRetransmissions"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.LowRisk,
            "Nombre maximum de retransmissions de paquets avant abandon (3 = réactivité accrue)."),
            ["InitialRto"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.LowRisk,
            "Délai de retransmission initial TCP (1000ms au lieu de 3000ms)."),
            ["MinRto"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.LowRisk,
            "Délai minimal de retransmission TCP."),
            ["AutoTuningLevel"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Ajustement dynamique de la fenêtre de réception TCP Windows."),
            ["CongestionProvider"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Algorithme de contrôle de congestion TCP (CTCP pour latence, CUBIC pour débit)."),
            ["NetbiosOptions"] = (ParameterCategory.DnsAndResolution, ParameterSafetyLevel.Safe,
            "Désactivation du protocole NetBIOS sur TCP/IP pour éliminer le trafic broadcast parasite."),
            ["EnableDCA"] = (ParameterCategory.HardwareOffload, ParameterSafetyLevel.Safe,
            "Direct Cache Access : permet aux paquets réseau d'accéder directement au cache processeur L3."),
            ["SackOpts"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Acquittements sélectifs TCP (SACK) : retransmet uniquement les segments manquants lors de pertes."),
            ["FastSendDatagramThreshold"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Seuil d'envoi rapide des datagrammes sans duplication mémoire intermédiaire."),
            ["DefaultTOSValue"] = (ParameterCategory.SystemThrottling, ParameterSafetyLevel.Safe,
            "Priorité DSCP / Type of Service pour marquer les paquets jeu et streaming comme prioritaires."),
            ["DisableIPSourceRouting"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Sécurité de couche IP interdisant le contournement de la table de routage standard."),
            ["EnableICMPRedirect"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Protection désactivant les modifications non sollicitées de la route par des tiers."),

            ["*PriorityVLANTag"] = (ParameterCategory.HardwareOffload, ParameterSafetyLevel.Safe,
            "Active le marquage de priorité 802.1p et VLAN au niveau matériel pour accélérer le trafic critique."),
            ["*RscIPv4"] = (ParameterCategory.HardwareOffload, ParameterSafetyLevel.LowRisk,
            "Receive Segment Coalescing (IPv4) : fusionne les segments TCP reçus. Désactivé pour réduire le jitter en jeu."),
            ["*RscIPv6"] = (ParameterCategory.HardwareOffload, ParameterSafetyLevel.LowRisk,
            "Receive Segment Coalescing (IPv6) : fusionne les segments TCP reçus."),
            ["*UsoIPv4"] = (ParameterCategory.HardwareOffload, ParameterSafetyLevel.Safe,
            "UDP Segmentation Offload (IPv4) : décharge le partitionnement des trames UDP sur la carte réseau."),
            ["*UsoIPv6"] = (ParameterCategory.HardwareOffload, ParameterSafetyLevel.Safe,
            "UDP Segmentation Offload (IPv6) : décharge le partitionnement UDP matériel."),
            ["*WakeOnMagicPacket"] = (ParameterCategory.EnergyManagement, ParameterSafetyLevel.Safe,
            "Réveil du PC par paquet magique réseau (WoL)."),
            ["*WakeOnPattern"] = (ParameterCategory.EnergyManagement, ParameterSafetyLevel.LowRisk,
            "Réveil par motif réseau. Désactivé pour éviter les réveils intempestifs et le polling continu."),
            ["*PMARPOffload"] = (ParameterCategory.EnergyManagement, ParameterSafetyLevel.Safe,
            "Délégation de réponse aux requêtes ARP en mode veille basse consommation."),
            ["*PMNSOffload"] = (ParameterCategory.EnergyManagement, ParameterSafetyLevel.Safe,
            "Délégation de sollicitation de voisins IPv6 en mode veille."),
            ["EnableWlanLowLatency"] = (ParameterCategory.WirelessRadio, ParameterSafetyLevel.Safe,
            "Mode latence ultra-faible pour cartes Wi-Fi (réduit les tampons internes du pilote radio)."),
            ["ScanWhenAssociated"] = (ParameterCategory.WirelessRadio, ParameterSafetyLevel.LowRisk,
            "Recherche de bornes Wi-Fi en arrière-plan lorsque connecté. Désactivé pour éliminer les micro-saccades en jeu."),
            ["FastOpen"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "TCP Fast Open (RFC 7413) : permet l'envoi de données dès la négociation SYN initiale."),
            ["TcpCreateAndConnectDataUnchecked"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Chemin rapide de création de socket TCP sans vérification redondante du buffer noyau."),
            ["MaxConnectionsPerServer"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Nombre maximum de connexions simultanées HTTP 1.1 par serveur (augmente la vitesse web)."),
            ["MaxConnectionsPer1_0Server"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Nombre maximum de connexions simultanées HTTP 1.0 par serveur distant."),
            ["DnsCacheEntries"] = (ParameterCategory.DnsAndResolution, ParameterSafetyLevel.Safe,
            "Nombre maximum d'entrées conservées dans le résolveur DNS local Windows."),
            ["MaxCacheEntryTtlLimit"] = (ParameterCategory.DnsAndResolution, ParameterSafetyLevel.Safe,
            "Durée de validité maximale des enregistrements DNS résolus dans le cache local."),
            ["NegativeCacheTime"] = (ParameterCategory.DnsAndResolution, ParameterSafetyLevel.Safe,
            "Délai d'attente avant de re-tester une requête DNS échouée (0 = nouvel essai instantané)."),
            ["NetFailureCacheTime"] = (ParameterCategory.DnsAndResolution, ParameterSafetyLevel.Safe,
            "Durée de mise en mémoire des erreurs réseau temporaires."),
            ["DisableDHCPMediaSense"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.LowRisk,
            "Maintient la configuration IP active lors de brèves déconnexions pour éviter le gel de la connexion."),
            ["TCPWindowSize"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Taille globale du tampon de réception TCP initial."),
            ["GlobalMaxTcpWindowSize"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Taille maximale absolue autorisée pour la fenêtre glissante TCP."),
            ["EcnCapability"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Explicit Congestion Notification (RFC 3168) : prévient la saturation des routeurs sans perte de paquets."),
            ["Timestamps"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Horodatage TCP (RFC 1323) pour une estimation RTT plus précise sur liens rapides."),
            ["Pacing"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Régulation d'espacement des paquets TCP pour fluidifier le débit et éviter les salves agressives."),
            ["TCPChimney"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.LowRisk,
            "Délestage matériel TCP Chimney (désactivé pour prévenir les corruptions et pertes de paquets sous Windows 10/11)."),
            ["NetDMA"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Network Direct Memory Access : transfert direct des paquets dans la RAM hôte."),
            ["IGMPLevel"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Support du protocole multicast IGMP (niveau 2 pour un trafic streaming et multijoueur optimal)."),
            ["ArpRetryCount"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Nombre de retransmissions de requêtes ARP pour accélérer la résolution d'adresse locale."),
            ["ArpCacheLife"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Durée de vie des entrées de table ARP en secondes pour rafraîchir dynamiquement les voisins réseau."),
            ["TcpInitialRTT"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Temps d'aller-retour initial estimé par la pile TCP pour accélérer l'ouverture des nouvelles connexions."),
            ["DisabledComponents"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Priorité & Composants IPv6 (32 = Préférer IPv4 sur IPv6 : élimine les temps de latence et timeouts DNS tout en préservant IPv6 natif)."),
            ["IPv6TeredoState"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Tunneling IPv6 Teredo : désactivé pour supprimer l'encapsulation UDP et les micro-saccades en jeu."),
            ["IPv6IsatapState"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Tunneling IPv6 ISATAP : désactivé pour éliminer les latences de routage intra-site inutiles."),
            ["IPv66to4State"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Tunneling IPv6 6to4 : protocole de transition obsolète désactivé pour éviter les goulets d'étranglement."),
            ["TcpNoDelayIPv6"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.LowRisk,
            "Désactivation de l'algorithme de Nagle sur IPv6 pour un envoi instantané des paquets sans délai."),
            ["TcpAckFrequencyIPv6"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.LowRisk,
            "Acquittement immédiat TCP sur IPv6 (ACK 1 au lieu de 2)."),
            ["TcpHeuristics"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Heuristiques de fenêtre TCP Windows : désactivées pour empêcher Windows de restreindre arbitrairement l'Auto-Tuning."),
            ["*RssBaseProcNumber"] = (ParameterCategory.BuffersAndQueues, ParameterSafetyLevel.Safe,
            "Cœur CPU de base pour la distribution RSS (0 pour le premier cœur physique)."),
            ["*MaxRssProcessors"] = (ParameterCategory.BuffersAndQueues, ParameterSafetyLevel.Safe,
            "Nombre maximum de cœurs processeur alloués au traitement RSS multi-cœur."),
            ["*LsoV1IPv4"] = (ParameterCategory.HardwareOffload, ParameterSafetyLevel.LowRisk,
            "Large Send Offload v1 (IPv4). Désactiver réduit la latence et élimine les micro-saccades en jeu."),
            ["2.4GHzChannelWidth"] = (ParameterCategory.WirelessRadio, ParameterSafetyLevel.LowRisk,
            "Largeur de bande canal 2.4 GHz (20 MHz évite les interférences radio des réseaux voisins)."),
            ["5GHzChannelWidth"] = (ParameterCategory.WirelessRadio, ParameterSafetyLevel.LowRisk,
            "Largeur de canal Wi-Fi 5 GHz."),
            ["ChannelWidth24"] = (ParameterCategory.WirelessRadio, ParameterSafetyLevel.LowRisk,
            "Largeur de canal radio 2.4 GHz."),
            ["ChannelWidth5"] = (ParameterCategory.WirelessRadio, ParameterSafetyLevel.LowRisk,
            "Largeur de canal radio 5 GHz."),
            ["MaxNegativeCacheTtl"] = (ParameterCategory.DnsAndResolution, ParameterSafetyLevel.Safe,
            "Durée maximale de mise en cache des échecs DNS (0s = nouvel essai instantané)."),
            ["CacheHashTableBucketSize"] = (ParameterCategory.DnsAndResolution, ParameterSafetyLevel.Safe,
            "Taille des compartiments de hachage du résolveur DNS pour accélération des requêtes."),
            ["TCPTimestamps"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Horodatages TCP RFC 1323 globaux (désactiver supprime l'overhead de 12 octets par paquet)."),
            ["TCPRss"] = (ParameterCategory.BuffersAndQueues, ParameterSafetyLevel.Safe,
            "Mise à l'échelle côté réception TCP globale (netsh RSS)."),
            ["TCPRsc"] = (ParameterCategory.HardwareOffload, ParameterSafetyLevel.LowRisk,
            "Coalescence de segments TCP de réception (netsh RSC). Désactiver pour éliminer les à-coups."),
            ["TCPEcnCapability"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Notification de congestion explicite globale (netsh ECN)."),
            ["NetshIPv6Randomize"] = (ParameterCategory.TcpStack, ParameterSafetyLevel.Safe,
            "Identifiants aléatoires d'interface IPv6 (désactiver pour une liaison stable sans changement d'ID)."),
            ["NdisAffinity"] = (ParameterCategory.InterruptHandling, ParameterSafetyLevel.MediumRisk,
            "Affinité d'interruption NIC vers CPU (attache les interruptions de la carte réseau aux cœurs physiques dédiés)."),
            ["RoamThreshold"] = (ParameterCategory.WirelessRadio, ParameterSafetyLevel.LowRisk,
            "Seuil de transition RSSI pour l'itinérance Wi-Fi."),
            ["WirelessMode"] = (ParameterCategory.WirelessRadio, ParameterSafetyLevel.MediumRisk,
            "Mode radio Wi-Fi (force les protocoles modernes 802.11ax/ac et élimine le surcoût de rétro-compatibilité)."),
            ["EnableAutoDnsOverHttps"] = (ParameterCategory.DnsAndResolution, ParameterSafetyLevel.Safe,
            "DNS over HTTPS (DoH) automatique Windows 11 pour chiffrement et accélération des résolutions DNS.")
        };

    public void ClassifyParameter(DynamicNetworkParameter parameter)
    {
        if (KnownParameters.TryGetValue(parameter.RegistryKeyword, out (ParameterCategory Category, ParameterSafetyLevel Safety, string Description) info))
        {
            parameter.Category = info.Category;
            parameter.SafetyLevel = info.Safety;
            parameter.ImpactDescription = info.Description;
            parameter.DetailedExplanation = info.Description;

            parameter.Scope = info.Category switch
            {
                ParameterCategory.TcpStack => "Pile TCP/IP Kernel",
                ParameterCategory.SystemThrottling => "QoS & Système Windows",
                ParameterCategory.DnsAndResolution => "DNS & Résolution",
                ParameterCategory.MtuAndFraming => "MTU & Trame Réseau",
                _ => "Pilote Matériel NDIS"
            };
            return;
        }

        string kw = parameter.RegistryKeyword;
        string name = parameter.DisplayName;

        if (kw.Contains("Interrupt", StringComparison.OrdinalIgnoreCase) || name.Contains("Interruption", StringComparison.OrdinalIgnoreCase))
        {
            parameter.Category = ParameterCategory.InterruptHandling;
            parameter.SafetyLevel = ParameterSafetyLevel.MediumRisk;
            parameter.ImpactDescription = "Gestion des interruptions matérielles du contrôleur.";
        }
        else if (kw.Contains("Flow", StringComparison.OrdinalIgnoreCase) || name.Contains("Flux", StringComparison.OrdinalIgnoreCase))
        {
            parameter.Category = ParameterCategory.FlowControl;
            parameter.SafetyLevel = ParameterSafetyLevel.MediumRisk;
            parameter.ImpactDescription = "Contrôle de flux de la liaison réseau.";
        }
        else if (kw.Contains("Energy", StringComparison.OrdinalIgnoreCase) || kw.Contains("Green", StringComparison.OrdinalIgnoreCase) ||
                 kw.Contains("EEE", StringComparison.OrdinalIgnoreCase) || name.Contains("Énergie", StringComparison.OrdinalIgnoreCase))
        {
            parameter.Category = ParameterCategory.EnergyManagement;
            parameter.SafetyLevel = ParameterSafetyLevel.LowRisk;
            parameter.ImpactDescription = "Économie d'énergie du port ou de la puce.";
        }
        else if (kw.Contains("Buffer", StringComparison.OrdinalIgnoreCase) || name.Contains("Tampon", StringComparison.OrdinalIgnoreCase))
        {
            parameter.Category = ParameterCategory.BuffersAndQueues;
            parameter.SafetyLevel = ParameterSafetyLevel.LowRisk;
            parameter.ImpactDescription = "Capacité des tampons de paquets.";
        }
        else if (kw.Contains("LSO", StringComparison.OrdinalIgnoreCase) || kw.Contains("Checksum", StringComparison.OrdinalIgnoreCase) ||
                 kw.Contains("Offload", StringComparison.OrdinalIgnoreCase) || name.Contains("Décharge", StringComparison.OrdinalIgnoreCase))
        {
            parameter.Category = ParameterCategory.HardwareOffload;
            parameter.SafetyLevel = ParameterSafetyLevel.LowRisk;
            parameter.ImpactDescription = "Décharge matérielle de calcul.";
        }
        else if (kw.Contains("Duplex", StringComparison.OrdinalIgnoreCase) || kw.Contains("Speed", StringComparison.OrdinalIgnoreCase) ||
                 kw.Contains("Jumbo", StringComparison.OrdinalIgnoreCase))
        {
            parameter.Category = ParameterCategory.MtuAndFraming;
            parameter.SafetyLevel = ParameterSafetyLevel.HighRisk;
            parameter.ImpactDescription = "Paramètre critique de couche physique.";
        }
        else
        {
            parameter.Category = ParameterCategory.Unknown;
            parameter.SafetyLevel = ParameterSafetyLevel.Unknown;
            parameter.ImpactDescription = "Paramètre non documenté ou spécifique au constructeur : non modifiable par sécurité.";
        }
    }

    public bool IsAllowedInMode(DynamicNetworkParameter parameter, EngineExecutionMode mode)
    {
        return parameter.SafetyLevel is not ParameterSafetyLevel.Unknown and not ParameterSafetyLevel.Dangerous and not ParameterSafetyLevel.ReadOnly && mode switch
        {
            EngineExecutionMode.ReadOnly => false,
            EngineExecutionMode.Simulation => true,
            EngineExecutionMode.Safe => parameter.SafetyLevel is ParameterSafetyLevel.Safe or ParameterSafetyLevel.LowRisk,
            EngineExecutionMode.Auto => parameter.SafetyLevel is ParameterSafetyLevel.Safe or ParameterSafetyLevel.LowRisk or ParameterSafetyLevel.MediumRisk,
            EngineExecutionMode.Expert => parameter.SafetyLevel is ParameterSafetyLevel.Safe or ParameterSafetyLevel.LowRisk or ParameterSafetyLevel.MediumRisk or ParameterSafetyLevel.HighRisk,
            _ => false
        };
    }

    public bool ValidateProposedValue(DynamicNetworkParameter parameter, string proposedValue, out string failureReason)
    {
        if (parameter.SafetyLevel is ParameterSafetyLevel.Unknown or ParameterSafetyLevel.Dangerous or ParameterSafetyLevel.ReadOnly)
        {
            failureReason = $"Paramètre {parameter.RegistryKeyword} interdit de modification (Niveau {parameter.SafetyLevel}).";
            return false;
        }

        if (parameter.ValueType == DynamicParameterType.Enumeration)
        {
            if (parameter.ValidOptions.Count > 0)
            {
                bool matches = parameter.ValidOptions.Exists(o =>
                    string.Equals(o.RegistryValue, proposedValue, StringComparison.OrdinalIgnoreCase));
                if (!matches)
                {
                    failureReason = $"Valeur '{proposedValue}' non acceptée par le pilote (Options valides : {string.Join(", ", parameter.ValidOptions.ConvertAll(o => o.RegistryValue))}).";
                    return false;
                }
            }
        }
        else if (parameter.ValueType == DynamicParameterType.IntegerRange)
        {
            if (long.TryParse(proposedValue, out long val))
            {
                if (parameter.MinValue.HasValue && val < parameter.MinValue.Value)
                {
                    failureReason = $"Valeur {val} inférieure au minimum autorisé par le pilote ({parameter.MinValue.Value}).";
                    return false;
                }
                if (parameter.MaxValue.HasValue && val > parameter.MaxValue.Value)
                {
                    failureReason = $"Valeur {val} supérieure au maximum autorisé par le pilote ({parameter.MaxValue.Value}).";
                    return false;
                }
            }
            else
            {
                failureReason = $"La valeur '{proposedValue}' n'est pas un nombre entier valide.";
                return false;
            }
        }

        failureReason = string.Empty;
        return true;
    }

    public string GetExplanationForParameter(DynamicNetworkParameter parameter)
    {
        return KnownParameters.TryGetValue(parameter.RegistryKeyword, out (ParameterCategory Category, ParameterSafetyLevel Safety, string Description) info)
            ? info.Description
            : !string.IsNullOrWhiteSpace(parameter.DetailedExplanation)
            ? parameter.DetailedExplanation
            : (!string.IsNullOrWhiteSpace(parameter.ImpactDescription) ? parameter.ImpactDescription : "Paramètre matériel réseau du pilote NDIS.");
    }
}
