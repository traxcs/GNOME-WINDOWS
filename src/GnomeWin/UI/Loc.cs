using System.Globalization;
using System.Windows.Markup;
using GnomeWin.Services.Settings;

namespace GnomeWin.UI;

public static class Loc
{
    public static bool IsFrench { get; private set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";

    public static void Apply(UiLanguage lang)
    {
        IsFrench = lang switch
        {
            UiLanguage.French => true,
            UiLanguage.English => false,
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr",
        };
    }

    public static CultureInfo Culture => IsFrench ? CultureInfo.GetCultureInfo("fr-FR") : CultureInfo.CurrentCulture;

    public static string T(string key) =>
        Strings.TryGetValue(key, out var pair) ? (IsFrench ? pair.Fr : pair.En) : key;

    public static string F(string key, params object[] args) => string.Format(Culture, T(key), args);

    private static readonly Dictionary<string, (string Fr, string En)> Strings = new()
    {
        ["Activities"] = ("Activités", "Activities"),
        ["Search"] = ("Rechercher", "Type to search"),
        ["Applications"] = ("Applications", "Applications"),
        ["ShowApplications"] = ("Afficher les applications", "Show Applications"),
        ["OpenWindows"] = ("Fenêtres ouvertes", "Open windows"),
        ["Settings"] = ("Paramètres", "Settings"),
        ["WindowsSettings"] = ("Paramètres Windows", "Windows settings"),
        ["RecentFiles"] = ("Fichiers récents", "Recent files"),
        ["Calculator"] = ("Calculatrice", "Calculator"),
        ["NoResults"] = ("Aucun résultat", "No results"),
        ["Workspace"] = ("Espace de travail {0}", "Workspace {0}"),
        ["NewWorkspace"] = ("Nouvel espace de travail", "New workspace"),
        ["RemoveWorkspace"] = ("Supprimer l'espace de travail", "Remove workspace"),
        ["Launch"] = ("Lancer", "Launch"),
        ["NewWindow"] = ("Nouvelle fenêtre", "New Window"),
        ["Pin"] = ("Épingler au dock", "Pin to Dash"),
        ["Unpin"] = ("Retirer du dock", "Unpin"),
        ["OpenLocation"] = ("Ouvrir l'emplacement du fichier", "Open file location"),
        ["RunAsAdmin"] = ("Exécuter en tant qu'administrateur", "Run as administrator"),
        ["Quit"] = ("Quitter", "Quit"),
        ["QuitAll"] = ("Fermer {0} fenêtres", "Close {0} windows"),
        ["Close"] = ("Fermer", "Close"),
        ["Minimize"] = ("Réduire", "Minimize"),
        ["Maximize"] = ("Agrandir / Restaurer", "Maximize / Restore"),
        ["MoveToWorkspace"] = ("Déplacer vers l'espace {0}", "Move to workspace {0}"),
        ["MoveToNewWorkspace"] = ("Déplacer vers un nouvel espace", "Move to a new workspace"),
        ["MoveToMonitor"] = ("Déplacer vers l'écran {0}", "Move to display {0}"),
        ["Notifications"] = ("Notifications", "Notifications"),
        ["NoNotifications"] = ("Aucune notification", "No Notifications"),
        ["ClearAll"] = ("Tout effacer", "Clear"),
        ["NotificationsUnavailable"] = ("Windows ne permet pas à cette application de lire les notifications (accès refusé ou identité de package requise).", "Windows does not let this application read notifications (access denied or package identity required)."),
        ["OpenNotificationCenter"] = ("Ouvrir le centre de notifications Windows", "Open Windows notification center"),
        ["Volume"] = ("Volume", "Volume"),
        ["Brightness"] = ("Luminosité", "Brightness"),
        ["WiFi"] = ("Wi-Fi", "Wi-Fi"),
        ["Wired"] = ("Filaire", "Wired"),
        ["Network"] = ("Réseau", "Network"),
        ["Disconnected"] = ("Déconnecté", "Disconnected"),
        ["Bluetooth"] = ("Bluetooth", "Bluetooth"),
        ["DarkStyle"] = ("Style sombre", "Dark Style"),
        ["NightLight"] = ("Éclairage nocturne", "Night Light"),
        ["AirplaneMode"] = ("Mode avion", "Airplane Mode"),
        ["Vpn"] = ("VPN", "VPN"),
        ["PowerSaver"] = ("Économie d'énergie", "Power Saver"),
        ["Battery"] = ("Batterie", "Battery"),
        ["Charging"] = ("En charge", "Charging"),
        ["Screenshot"] = ("Capture d'écran", "Screenshot"),
        ["Lock"] = ("Verrouiller", "Lock"),
        ["Power"] = ("Éteindre", "Power Off"),
        ["Restart"] = ("Redémarrer", "Restart"),
        ["Suspend"] = ("Mettre en veille", "Suspend"),
        ["LogOut"] = ("Se déconnecter", "Log Out"),
        ["ConfirmPowerOff"] = ("Éteindre l'ordinateur ?", "Power off the computer?"),
        ["ConfirmRestart"] = ("Redémarrer l'ordinateur ?", "Restart the computer?"),
        ["ConfirmLogOut"] = ("Fermer la session ?", "Log out?"),
        ["Cancel"] = ("Annuler", "Cancel"),
        ["Today"] = ("Aujourd'hui", "Today"),
        ["SafeModeBanner"] = ("Mode sans échec : taskbar Windows conservée, raccourcis globaux désactivés.", "Safe mode: Windows taskbar kept, global shortcuts disabled."),
        ["QuitShell"] = ("Quitter GnomeWin (restaurer Windows)", "Quit GnomeWin (restore Windows)"),
        ["Untitled"] = ("Sans titre", "Untitled"),
        ["MoveNotSupported"] = ("Déplacer des fenêtres entre bureaux n'est pas pris en charge sur cette version de Windows.", "Moving windows between desktops is not supported on this Windows build."),
    };
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }
    public TrExtension(string key) => Key = key;
    public string Key { get; set; } = string.Empty;
    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
