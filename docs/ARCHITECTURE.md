# Architecture de GnomeWin

## Vue générale

```
                ┌────────────────────────── Program.Main ─────────────────────────┐
                │ --watchdog → Watchdog (sans WPF)   --restore/--diag/--install…  │
                │ instance unique (mutex) → transfert de commande par WM_COPYDATA │
                └──────────────────────────────┬──────────────────────────────────┘
                                               │
                                    App (WPF) → ShellHost
                                               │
     ┌───────────────────────────┬─────────────┼───────────────┬──────────────────────────┐
     │ Platform                  │ Core        │ Services      │ Shell (UI)               │
     │ WinEventHooks ───────────▶│ WindowMgr ──┼──────────────▶│ OverviewController       │
     │ VirtualDesktopService ───▶│ WorkspaceMgr│ AppDiscovery ▶│ DockController/DockWindow│
     │ ShellMessageWindow        │ AppManager ◀┤ Search        │ TopBar / QuickSettings   │
     │ AppBar, TaskbarController │ MonitorMgr  │ Notifications │ Calendar, Switcher, OSD  │
     │ DWM thumbnails, CoreAudio │ Fullscreen  │ SystemStatus  │ SettingsWindow           │
     └───────────────────────────┴─────────────┴───────────────┴──────────────────────────┘
                       Input: KeyboardHookService (thread dédié) → ShellAction → ShellHost
```

Tout se passe sur le **thread UI (STA)**, sauf :

| Thread | Rôle |
|---|---|
| `GnomeWin.KeyboardHook` | Hook `WH_KEYBOARD_LL` + boucle de messages ; ne fait que décider « avaler / laisser passer » et poster l'action au thread UI. |
| `GnomeWin.AppDiscovery` (STA) | Énumération de `shell:AppsFolder`, icônes. |
| `GnomeWin.StaWorker` (STA) | Icônes d'exécutables (IShellItemImageFactory n'est fiable qu'en STA). |
| `GnomeWin.VDWatch` | `RegNotifyChangeKeyValue` sur l'état des bureaux virtuels. |
| `GnomeWin.Log` | Écriture des journaux. |

## Flux principaux

### Ouverture de la vue d'ensemble

1. `Super` relâchée seule → le hook avale le relâchement, injecte *masque + Win↑*, poste `ToggleOverview`.
2. `OverviewController.Open` : rafraîchit les espaces, place une `OverviewWindow` par écran (sous la barre
   supérieure), fond d'écran net + version floutée pré-calculée.
3. Pour chaque fenêtre de l'espace courant (ordre Z de bas en haut) : `WindowPreview` + `DwmRegisterThumbnail`.
4. `WindowLayout.Compute` calcule les emplacements (algorithme « naturel » GNOME : rangées, conservation de
   l'ordre spatial, pas d'agrandissement).
5. `FrameAnimation` (abonnée à `CompositionTarget.Rendering` le temps de l'animation) interpole chaque image :
   position réelle → emplacement, fond net → flou assombri, recherche/bande/dock en fondu-glissé. Les rectangles
   DWM et la chrome WPF sont mis à jour dans la même image.
6. Fermeture : animation inverse, puis activation de la fenêtre choisie *sous* la surface encore visible, puis
   masquage → transition sans saut.

### Espaces de travail

`WorkspaceManager` reflète les bureaux virtuels (`VirtualDesktopService`) :

- liste/courant : backend COM interne (validé) ou registre d'explorer (repli) ;
- répartition des fenêtres : `IVirtualDesktopManager.GetWindowDesktopId` (API publique) ;
- dynamique : après chaque changement (anti-rebond 700 ms), suppression des espaces vides non courants,
  ajout d'un espace vide final, maximum 16 ; suspendu pendant un glisser-déposer ;
- à la fermeture du shell, l'espace vide final ajouté est retiré.

### Barre des tâches native

`TaskbarController.Hide` sauvegarde l'état d'auto-masquage d'origine dans `state\taskbar.json`
**avant** toute modification. `Restore` est idempotent et appelable depuis n'importe quel processus
(shell, watchdog, `--restore`, désinstallateur, démarrage suivant).

### Watchdog

Processus séparé `GnomeWin.exe --watchdog <pid>` (aucune UI, ~25 Mo) :

- attend la fin du shell ; si l'état indique encore « masquée » → restauration ;
- ping toutes les 3 s (`SendMessageTimeout` sur la fenêtre de messages) : 15 s sans réponse → restauration,
  60 s → arrêt du shell ;
- enregistre `Ctrl+Alt+Maj+F12` (`RegisterHotKey`) : arrêt d'urgence + restauration.

Le shell relance le watchdog s'il disparaît (3 fois max).

### Démarrage et boucle de plantage

`state\session.lock` est créé au démarrage et supprimé à la sortie propre. S'il est présent au démarrage,
la fin précédente était anormale : elle est comptée ; 3 fins anormales en 10 minutes → mode sans échec
automatique.

## Performance

| Mesure (Windows 11 24H2, 1 écran 1080p, build publiée) | Valeur |
|---|---|
| Démarrage complet du shell | ~1,3 s (catalogue ~0,4 s, icônes en arrière-plan) |
| CPU au repos (souris immobile) | 0–50 ms par 30 s (≈0,1 % d'un cœur) |
| Mémoire privée | ~100 Mo (working set ~205 Mo, dont images partagées mappées) |
| Minuteurs périodiques | horloge (1/min) ; statistiques en mode `--verbose` uniquement |

Choix qui y contribuent :

- aucun polling : hooks WinEvent hors contexte filtrés dès l'entrée ; `EVENT_OBJECT_LOCATIONCHANGE` (émis à chaque mouvement de souris du système) n'est abonné que si le dock est en masquage intelligent/automatique ;
- surfaces de la vue d'ensemble, fond d'écran décodé et grille libérés après 2 min d'inactivité, puis mémoire rendue au système (`SetProcessWorkingSetSize`) ;
- événements coalescés (`DispatcherTimer` 40–700 ms selon le cas) ;
- animations image par image seulement pendant qu'elles tournent ;
- surfaces de la vue d'ensemble créées à la première ouverture puis réutilisées ;
- fond flouté rendu une seule fois (480 px) ;
- GC poste de travail non concurrent + `ConserveMemory`, compaction unique après le démarrage ;
- publication ReadyToRun **non compressée** : le code est mappé depuis l'exécutable au lieu d'être
  décompressé en mémoire privée.

## Points d'extension

- **Nouveau raccourci** : ajouter une valeur à `ShellAction`, sa liaison par défaut dans
  `KeyboardSettings.DefaultBindings`, le libellé dans `SettingsWindow.BuildKeyboard` et le traitement dans
  `ShellHost.HandleAction`.
- **Nouveau fournisseur de recherche** : `SearchService.Search` + une section dans `SearchResultsView`.
- **Nouveau build Windows pour les bureaux virtuels** : ajouter l'IID de `IVirtualDesktopManagerInternal`
  dans `InternalComBackend.ManagerInternalIids` après vérification de l'ordre des méthodes utilisées.
- **Schéma de configuration** : incrémenter `AppSettings.CurrentVersion` et enregistrer l'étape dans
  `SettingsMigrator.CreateDefault`.

## Mémoire (mesures, Windows 11 24H2, 1080p, version publiée)

| Processus / état | Working set | Mémoire privée |
|---|---|---|
| Shell au repos (mode « Économie de mémoire ») | ~33 Mo | ~79 Mo |
| Shell au repos (rendu GPU) | ~30–44 Mo | ~96 Mo |
| Vue d'ensemble ouverte (libérée 2 min après) | ~120 Mo | ~120 Mo |
| Watchdog | ~11 Mo | ~22 Mo |

Leviers : WinRT (radios, SSID, notifications) et WMI (luminosité) chargés à la première ouverture des panneaux
concernés ; rendu logiciel WPF (option, activée par défaut) ; surfaces de la vue d'ensemble libérées après
inactivité ; mémoire rendue au système après le démarrage et à la fermeture des panneaux ; watchdog qui ne
charge jamais WPF ; mode efficacité Windows (EcoQoS) au repos, désactivé pendant la vue d'ensemble.