# GnomeWin — l'expérience GNOME Shell / Ubuntu sur Windows 11

GnomeWin est un shell de bureau **indépendant, réversible et sans modification système** qui reproduit
l'expérience GNOME Shell sur Windows 11 :

- **Vue d'ensemble (Activités)** déclenchée par la touche **Super** : les fenêtres glissent de leur position
  réelle vers une grille, avec de **vrais aperçus en direct** (miniatures DWM), recherche immédiate,
  bande des espaces de travail et dock intégré.
- **Espaces de travail dynamiques** = bureaux virtuels Windows (un espace vide toujours disponible à la fin,
  les espaces vides sont supprimés), glisser-déposer d'une fenêtre vers un autre espace.
- **Styles d'interface** (Paramètres › Général › Style), chacun en sombre ou clair, avec sa police embarquée :
  | Style | Palette / police | Accent | Dock |
  |---|---|---|---|
  | **GNOME** (défaut) | Adwaita / Cantarell | bleu | dash uniquement dans la vue d'ensemble, en bas |
  | **Ubuntu** | Yaru / Ubuntu | orange | Ubuntu Dock à gauche, pleine hauteur, toujours visible |
  | **Pop!_OS** | Pop / Fira Sans | bleu canard | dock flottant en bas, masquage intelligent |
  Choisir un style applique sa disposition ; tout reste ajustable ensuite (15 couleurs d'accent GNOME et
  Ubuntu, position et taille du dock, mode panneau, masquage).
- **Grille d'applications** alimentée par Windows (applications Win32 et UWP/MSIX, comme le menu Démarrer).
- **Paramètres façon GNOME Settings 47** : barre latérale avec recherche, mêmes panneaux (Wi‑Fi, Réseau, Bluetooth, Écrans, Son, Énergie, Multitâche, Apparence, Dock, Applications, Notifications, Recherche, Clavier, Accessibilité, Système…), listes en cartes, vignettes de style et pastilles d'accent. Coin actif (haut-gauche) comme GNOME.
- **Barre supérieure** : Activités / espaces, horloge + calendrier + notifications, indicateurs système et
  **Réglages rapides** (volume, luminosité, Wi‑Fi, Bluetooth, mode sombre, alimentation…).
- **Super+Tab** (sélecteur d'applications), raccourcis configurables, multi-écran, Hi‑DPI, détection plein écran.
- **Sécurité** : la barre des tâches Windows est toujours restaurée (fermeture, plantage, blocage, désinstallation),
  watchdog séparé, raccourci d'urgence, mode sans échec, protection contre les boucles de plantage.

> Statut : application fonctionnelle, compilée et testée sur Windows 11 24H2 (build 26100).

---

## Sommaire

1. [Installation](#installation)
2. [Désinstallation](#désinstallation)
3. [Raccourcis clavier](#raccourcis-clavier)
4. [Construire depuis les sources](#construire-depuis-les-sources)
5. [Choix techniques](#choix-techniques)
6. [Contraintes de Windows 11 et approximations](#contraintes-de-windows-11-et-approximations)
7. [Architecture](#architecture)
8. [Configuration](#configuration)
9. [Limitations connues](#limitations-connues)
10. [Dépannage](#dépannage)
11. [Récupération si la barre des tâches a disparu](#récupération-si-la-barre-des-tâches-a-disparu)
12. [Ligne de commande](#ligne-de-commande)

---

## Installation

### Avec l'installateur (recommandé)

1. Lancez `GnomeWin-Setup.exe` (produit dans `dist\` par `build.ps1`).
2. Choisissez les options : lancement au démarrage, raccourci bureau, réinitialisation de la configuration
   existante, lancement immédiat.
3. Cliquez **Installer**.

L'installation est **par utilisateur, sans droits administrateur** :

| Élément | Emplacement |
|---|---|
| Programme | `%LocalAppData%\Programs\GnomeWin\GnomeWin.exe` |
| Raccourcis | Menu Démarrer › GnomeWin (normal, mode sans échec, paramètres, **Restaurer la barre des tâches**) |
| Désinstallation | Paramètres Windows › Applications installées › GnomeWin |
| Lancement au démarrage | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (optionnel) |
| Paramètres / journaux | `%AppData%\GnomeWin\` (jamais dans le dossier d'installation) |

Relancer l'installateur sur une installation existante effectue une **mise à jour** (les paramètres sont conservés).
L'exécutable est autonome (« self-contained ») : **aucun runtime .NET n'est requis** sur la machine cible.

### Avec winget

Les manifestes winget (installation par utilisateur, silencieuse : `--install --quiet --startup`) sont générés par :

```powershell
.\tools\New-WingetManifest.ps1 -InstallerUrl https://github.com/traxcs/GnomeWin/releases/download/v1.0.0/GnomeWin-Setup.exe
winget validate --manifest packaging\winget\manifests\g\GnomeWin\GnomeWin\1.0.0
```

winget installe depuis une URL HTTPS publique : publiez `GnomeWin-Setup.exe` (release GitHub), régénérez le
manifeste avec cette URL, puis soumettez-le à `microsoft/winget-pkgs` (`wingetcreate submit`). Une fois accepté :

```powershell
winget install GnomeWin.GnomeWin
```

Test local avant publication : `winget settings --enable LocalManifestFiles` (admin) puis
`winget install --manifest packaging\winget\manifests\g\GnomeWin\GnomeWin\1.0.0`.

### Version portable

Décompressez `GnomeWin-Portable.zip` et lancez `GnomeWin.exe`. Rien n'est installé ; les paramètres
sont tout de même stockés dans `%AppData%\GnomeWin`. `Restore-Taskbar.cmd` est fourni à côté.

### Premier lancement

La fenêtre **Paramètres** s'ouvre une fois pour vous laisser ajuster le comportement. Appuyez sur **Super**
(touche Windows) pour ouvrir la vue d'ensemble.

---

## Désinstallation

- Paramètres Windows › Applications › Applications installées › **GnomeWin** › Désinstaller, ou
- Menu Démarrer › GnomeWin… ou `"%LocalAppData%\Programs\GnomeWin\GnomeWin.exe" --uninstall`
- Silencieux : `GnomeWin.exe --uninstall --quiet`

La désinstallation ferme le shell, **restaure la barre des tâches**, supprime le lancement au démarrage,
les raccourcis, l'entrée de désinstallation et les fichiers. Une case permet de supprimer aussi
`%AppData%\GnomeWin` (paramètres et journaux).

---

## Raccourcis clavier

Tous les raccourcis globaux sont **configurables** (Paramètres › Clavier) ; un raccourci se capture en
appuyant simplement sur la combinaison.

| Raccourci | Action |
|---|---|
| **Super** (seule) | Ouvrir / fermer la vue d'ensemble |
| **Super + A** | Grille des applications (de nouveau : fermer) |
| **Super + Tab** (maintenir Super) | Sélecteur d'applications (Tab / Maj+Tab, Super+` pour les fenêtres de l'app) |
| **Super + Page↑ / Page↓** | Espace de travail précédent / suivant |
| **Ctrl + Alt + ↑ / ↓** | Idem (variante GNOME) |
| **Super + Maj + Page↑ / Page↓** | Déplacer la fenêtre active vers l'espace précédent / suivant (et la suivre) |
| **Ctrl + Alt + Maj + ↑ / ↓** | Idem |
| **Super + 1…9** | Activer / lancer la n-ième application du dock |
| **Ctrl + Alt + Maj + F12** | **Urgence** : quitte GnomeWin et restaure Windows (géré par le watchdog, fonctionne même si le shell est figé) |

Dans la vue d'ensemble :

| Touche | Action |
|---|---|
| Taper du texte | Recherche immédiate (pas besoin de Ctrl+L) |
| Flèches | Naviguer entre les fenêtres / résultats / applications |
| Tab / Maj+Tab | Fenêtre suivante / précédente |
| Entrée | Activer la sélection |
| Page↑ / Page↓, molette | Changer d'espace de travail |
| Échap | Effacer la recherche › revenir aux fenêtres › fermer |
| Clic milieu sur un aperçu | Fermer la fenêtre |

Les raccourcis natifs de Windows restent intacts (Win+E, Win+L, Win+D, Ctrl+Win+←/→, Alt+Tab…). Le menu
Démarrer reste accessible par **Ctrl+Échap**. Raccourcis non attribués par défaut (pour ne pas casser
Windows) mais disponibles : réglages rapides, notifications, paramètres GnomeWin.

---

## Construire depuis les sources

Prérequis : Windows 10 19041+ / Windows 11, **SDK .NET 8** (`winget install Microsoft.DotNet.SDK.8`).

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
powershell -ExecutionPolicy Bypass -File build.ps1 -SkipTests
powershell -ExecutionPolicy Bypass -File build.ps1 -Runtime win-arm64
```

Résultat dans `dist\` : `GnomeWin-Setup.exe` et `GnomeWin-Portable.zip`.

Développement :

```powershell
dotnet build GnomeWin.sln -p:Platform=x64
dotnet test tests\GnomeWin.Tests -p:Platform=x64
src\GnomeWin\bin\x64\Debug\net8.0-windows10.0.19041.0\GnomeWin.exe --verbose
```

Une build *Debug* est dépendante du framework : il faut le runtime **.NET 8 Desktop** (ou définir
`DOTNET_ROOT` vers le SDK). Les paquets de `dist\` sont autonomes.

---

## Choix techniques

**C# / .NET 8 (LTS) + WPF + Win32 + DWM + quelques API WinRT officielles.**

Pourquoi **WPF plutôt que WinUI 3** :

| Critère | WPF | WinUI 3 |
|---|---|---|
| Miniatures DWM (`DwmRegisterThumbnail`) | Chaque surface est une vraie fenêtre Win32 : idéal | Une seule HWND par fenêtre XAML, composition plus délicate |
| Fenêtres outils / non activables / appbars / topmost | Contrôle total via `HwndSource` | Plus contraint |
| Multi-DPI par écran | Per-Monitor V2 natif (manifeste) | Oui |
| Démarrage / mémoire | Démarrage rapide, pas de dépendance Windows App SDK | Runtime Windows App SDK à déployer |
| Animations | Composition WPF + animations pilotées image par image | Composition |
| Stabilité / maturité | Très mature | Plus récent |

Le cœur de GnomeWin (aperçus DWM synchronisés avec des animations, surfaces shell non activables,
appbars, hooks) demande une intégration Win32 fine : WPF offre le meilleur compromis
performance / stabilité / intégration. Les API WinRT officielles (radios, réseau, notifications) sont
utilisées via la projection du SDK Windows (`net8.0-windows10.0.19041.0`).

---

## Contraintes de Windows 11 et approximations

> Règle suivie : aucune API inventée, aucune modification de fichiers système, rien d'irréversible.

| Fonction GNOME | Limitation Windows | Approximation retenue |
|---|---|---|
| Touche **Super** seule | `RegisterHotKey` ne peut pas enregistrer Win seule | Hook clavier bas niveau (`WH_KEYBOARD_LL`) sur un thread dédié. Win appuyée passe telle quelle (tous les Win+X natifs fonctionnent) ; si Win est relâchée seule, le relâchement est remplacé par « touche masque (VK 0xE8) + Win relâchée », technique d'AutoHotkey : le menu Démarrer ne s'ouvre pas. Aucun réglage système modifié. |
| Aperçus des fenêtres | — | **Miniatures DWM** (`DwmRegisterThumbnail`) : rendu en direct par le compositeur, synchronisé avec la fenêtre réelle, coût CPU quasi nul. Repli sur l'icône si Windows ne fournit pas d'image (certaines fenêtres réduites). |
| Workspaces | Aucune API **publique** pour créer/supprimer/changer de bureau ni déplacer la fenêtre d'un autre processus | 1) **Backend complet** via les services COM internes d'explorer (`IVirtualDesktopManagerInternal`, `IApplicationViewCollection`) : non documentés, donc **limités aux IID connus de Windows 11 23H2/24H2**, seules les méthodes stables sont appelées et le backend **s'auto-valide** au démarrage ; tout appel passe par le marshalling COM (une incompatibilité donne une erreur, jamais une corruption). 2) Sinon **repli documenté** : état lu dans le registre d'explorer + raccourcis officiels Ctrl+Win+←/→/D/F4 (pas de déplacement de fenêtres d'autres processus). |
| Changement d'espace détecté | Pas d'événement public | `RegNotifyChangeKeyValue` sur l'état d'explorer + événements `EVENT_OBJECT_CLOAKED` (aucun polling). |
| Remplacer la barre des tâches | Pas d'API « remplacer le shell » non destructive | La barre native passe en **masquage automatique** (`SHAppBarMessage ABM_SETSTATE`, état d'origine sauvegardé) puis ses fenêtres sont masquées ; explorer la réaffichant de façon asynchrone (bande de 2 px), elle est re-masquée pendant 6 s puis à chaque réapparition. Tout est restauré à la fermeture ; un **watchdog** restaure en cas de plantage ou blocage. |
| Barre supérieure | — | AppBar documentée (`SHAppBarMessage`) : les fenêtres maximisées restent dessous ; reçoit `ABN_FULLSCREENAPP`. |
| Icônes de la zone de notification (tray) des autres applications | Aucune API pour les lire sans injecter du code dans explorer | Non reproduites (le bouton qui réaffichait la barre Windows a été retiré pour préserver le design). Les applications concernées restent accessibles par la vue d'ensemble et le dock. |
| Notifications | `UserNotificationListener` exige l'autorisation de l'utilisateur et, selon les builds, une identité de package ; les boutons d'action des toasts ne sont pas exposés | Lecture / suppression / « tout effacer » via l'API officielle quand Windows l'autorise (testé : autorisé sur 24H2) ; sinon message explicite + bouton vers le centre de notifications Windows. Clic = ouvrir l'application. |
| Wi‑Fi / Bluetooth | — | `Windows.Devices.Radios` (bascule directe si autorisée), sinon ouverture de la page Paramètres. |
| Volume | — | Core Audio (`IAudioEndpointVolume`) avec notifications. |
| Luminosité | WMI uniquement pour les dalles internes (portables) | Curseur affiché si disponible. |
| Mode avion, VPN, éclairage nocturne, économie d'énergie | Pas d'API publique de bascule | Ouverture de la page Paramètres correspondante (préférence de la consigne). |
| SSID Wi‑Fi | Depuis 24H2, peut exiger l'autorisation de localisation | Affiché si Windows le fournit. |
| Animation du changement de bureau hors vue d'ensemble | Pas d'API publique d'animation | OSD GNOME (indicateur d'espaces) ; dans la vue d'ensemble, transition glissée des aperçus. |
| Fenêtres élevées (administrateur) | UIPI : un processus non élevé ne reçoit pas les touches et ne peut pas toujours les activer | Documenté ; lancer GnomeWin en administrateur n'est **pas** recommandé. |

---

## Architecture

```
src/GnomeWin
├── Program.cs / App.xaml(.cs)   Point d'entrée : CLI, instance unique, watchdog, installateur, gestion des exceptions
├── Core/                        Modèle et logique (sans UI)
│   ├── WindowManager            Suivi des fenêtres par hooks WinEvent (zéro polling), MRU, fenêtre active
│   ├── WindowFilter             Règles « fenêtre d'application » (mêmes règles qu'Alt+Tab, cloaking)
│   ├── ApplicationManager       Catalogue, association fenêtre → application, favoris, lancement
│   ├── WorkspaceManager         Espaces = bureaux virtuels, comportement dynamique GNOME
│   ├── MonitorManager           Écrans, zones de travail, DPI
│   └── FullscreenDetector       Plein écran par écran (géométrie + SHQueryUserNotificationState)
├── Shell/                       Surfaces et contrôleurs
│   ├── ShellHost                Racine de composition : événements système, raccourcis, arrêt propre
│   ├── Overview/                Vue d'ensemble : contrôleur, surface par écran, aperçu, algorithme de disposition
│   ├── Dock/                    Dash : vue, contrôleur, fenêtre flottante, sélecteur de fenêtres
│   ├── ApplicationGrid/         Grille des applications
│   ├── Search/                  Vue des résultats
│   ├── WorkspaceView/           Bande des espaces, OSD
│   ├── SystemPanel/             Barre supérieure, réglages rapides, calendrier + notifications
│   ├── Switcher/                Super+Tab
│   └── Settings/                Application Paramètres
├── Platform/                    Accès Windows isolés
│   ├── Win32/                   P/Invoke documentés, hooks WinEvent, AppBar, activation, icônes, fenêtre de messages
│   ├── Dwm/                     Miniatures DWM
│   ├── VirtualDesktop/          API publique + backend COM interne validé + backend de repli documenté
│   ├── Taskbar/                 Masquage / restauration réversible de la barre native
│   ├── Audio/                   Core Audio
│   └── Startup/                 Lancement au démarrage
├── Services/                    Settings (versionnés), AppDiscovery, Search, Notifications, SystemStatus,
│                                Wallpaper, Logging, Recovery (watchdog, boucle de plantage)
├── Input/                       GlobalHotkeys (hook bas niveau, capture), KeyboardNavigation (navigation spatiale)
├── UI/                          Thèmes Adwaita clair/sombre, animations, composants (ShellWindow, popups), localisation FR/EN
└── Setup/                       Installateur / désinstallateur par utilisateur
tests/GnomeWin.Tests             Tests unitaires (disposition, raccourcis, recherche, calculatrice, migration, navigation)
```

Principes :

- **Événementiel** : WinEvent hooks, `RegNotifyChangeKeyValue`, Core Audio, `NetworkStatusChanged`,
  `WM_POWERBROADCAST`, `TaskbarCreated`… L'horloge est le seul minuteur périodique (1 tick/minute).
  Au repos, le CPU est à ~0 %.
- **Animations pilotées image par image** (`CompositionTarget.Rendering`) uniquement pendant une animation :
  position WPF et miniatures DWM bougent ensemble ; l'abonnement est retiré à la fin.
- **Thread dédié** pour le hook clavier : un thread UI occupé ne peut ni retarder la saisie, ni faire
  retirer le hook par Windows.
- **Messages synchrones** (`WM_COPYDATA`, `WM_POWERBROADCAST`…) traités après retour, car les appels COM
  vers explorer y sont interdits (`RPC_E_CANTCALLOUT_ININPUTSYNCCALL`).
- **Robustesse** : chaque surface est une `ShellWindow` (outil, hors Alt+Tab, visible sur tous les bureaux),
  toute erreur d'interface est journalisée sans faire tomber le shell ; au-delà de 20 erreurs, restauration
  et sortie.

Détails supplémentaires : [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

---

## Configuration

`%AppData%\GnomeWin\settings.json` — JSON lisible, **versionné** (`"Version"`) :

- migration pas à pas des anciens formats (`SettingsMigrator`) ;
- écriture atomique (fichier temporaire + remplacement, `.bak` de la version précédente) ;
- fichier corrompu → renommé en `.corrupt-<date>` puis valeurs par défaut ;
- fichier d'une version plus récente → copie de sauvegarde conservée ;
- export / import / réinitialisation depuis Paramètres › Maintenance ou `--reset-settings`.

Autres fichiers : `logs\` (5 fichiers × 2 Mo max), `state\taskbar.json` (état d'origine de la barre),
`state\session.lock` (détection des fins anormales).

---

## Limitations connues

- **Workspaces complets** (créer/supprimer/réordonner/déplacer une fenêtre) uniquement sur les builds de
  Windows 11 dont l'interface interne est connue (23H2, 24H2). Ailleurs : mode limité documenté
  (changement/création/fermeture de l'espace courant via les raccourcis Windows, pas de déplacement de
  fenêtres). Le diagnostic (`--diag`) indique le mode actif.
- Le **mode dynamique** n'est actif qu'avec le backend complet.
- Les **icônes de la zone de notification** tierces ne sont pas reproduites (voir tableau ci-dessus).
- Les **boutons d'action** des notifications ne sont pas exposés par Windows.
- Les fenêtres **en administrateur** peuvent ignorer les raccourcis quand elles ont le focus (UIPI).
- Certaines applications **réduites** n'ont pas d'image DWM : leur aperçu montre leur icône.
- Le contenu des miniatures des **autres espaces** n'est pas dessiné dans la bande (contours + icônes),
  pour rester léger.
- Le changement de **langue** est complet au redémarrage du shell.
- En **jeu plein écran**, par défaut, le dock et la barre sont masqués et les raccourcis désactivés
  (la touche Windows retrouve son comportement natif).

---

## Dépannage

| Symptôme | Solution |
|---|---|
| La touche Super ouvre le menu Démarrer | Paramètres › Clavier › « Intercepter la touche Super » activé ? Pas en mode sans échec ? Une application plein écran est-elle détectée ? Fenêtre élevée au premier plan (UIPI) ? |
| Le dock n'apparaît pas | Mode « Intelligent » : il se cache quand une fenêtre le chevauche ; amenez la souris au bord de l'écran. Vérifiez Paramètres › Écrans › Dock. |
| Les espaces ne se déplacent pas | `GnomeWin.exe --diag` : si le backend est « Documented shortcuts (limited) », votre build de Windows n'est pas pris en charge pour ces opérations. |
| Notifications vides | Paramètres Windows › Confidentialité › Notifications : autoriser l'accès aux notifications. |
| GnomeWin redémarre en mode sans échec | 3 fins anormales en 10 min. Consultez `%AppData%\GnomeWin\logs`, puis relancez normalement. |
| Rendu flou | L'application est Per-Monitor V2 ; signalez l'écran/l'échelle avec un `--diag`. |
| Journaux détaillés | Lancez avec `--verbose` ou activez la journalisation détaillée dans `settings.json` (`VerboseLogging`). |

Rapport de diagnostic : `GnomeWin.exe --diag` (écrit dans `%AppData%\GnomeWin\logs\diag-*.txt`).

---

## Récupération si la barre des tâches a disparu

Par ordre de préférence :

1. **Ctrl + Alt + Maj + F12** (raccourci d'urgence géré par le watchdog).
2. Menu Démarrer (**Ctrl+Échap**) › GnomeWin › **Restaurer la barre des tâches Windows**.
3. `Win+R` → `"%LocalAppData%\Programs\GnomeWin\GnomeWin.exe" --restore`
4. `tools\Restore-Taskbar.cmd` (fonctionne même sans GnomeWin : relance explorer).
5. Gestionnaire des tâches (**Ctrl+Maj+Échap**) › Fichier › Exécuter `explorer.exe`, ou redémarrer
   « Explorateur Windows » : explorer recrée toujours une barre visible.
6. Si la barre reste en masquage automatique non désiré : Paramètres › Personnalisation › Barre des tâches ›
   Comportements › décocher « Masquer automatiquement la barre des tâches ».

Garanties intégrées : restauration à la fermeture normale, à la fermeture de session, en cas
d'exception, par le watchdog si le processus disparaît ou ne répond plus (15 s), au démarrage suivant si
la session précédente s'est mal terminée, et à la désinstallation.

---

## Ligne de commande

```
GnomeWin.exe                    démarre le shell (ou ouvre la vue d'ensemble s'il tourne déjà)
GnomeWin.exe --safe-mode        sans remplacement de barre des tâches ni hook clavier global
GnomeWin.exe --settings         ouvre les paramètres
GnomeWin.exe --overview         ouvre la vue d'ensemble
GnomeWin.exe --apps             ouvre la grille d'applications
GnomeWin.exe --search <texte>   ouvre la vue d'ensemble et recherche <texte>
GnomeWin.exe --quit             quitte le shell (la barre native est restaurée)
GnomeWin.exe --restore          restaure la barre des tâches (et quitte le shell s'il tourne)
GnomeWin.exe --reset-settings   sauvegarde puis réinitialise les paramètres
GnomeWin.exe --diag             rapport de diagnostic
GnomeWin.exe --install          installe pour l'utilisateur courant
GnomeWin.exe --uninstall [--quiet]
GnomeWin.exe --verbose          journalisation détaillée
```

Licence : MIT.
