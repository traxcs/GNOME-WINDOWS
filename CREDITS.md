# Crédits

GnomeWin reproduit l'expérience de **GNOME Shell** sur Windows. Tout le mérite du design revient au projet
GNOME et à Canonical ; GnomeWin est une réécriture indépendante, **non affiliée et non approuvée** par eux.

## GNOME Shell — le design reproduit

- Projet : [GNOME Shell](https://gitlab.gnome.org/GNOME/gnome-shell) (miroir : [github.com/GNOME/gnome-shell](https://github.com/GNOME/gnome-shell))
- Auteurs : le projet GNOME et ses contributrices et contributeurs
- Licence : GPL-2.0-or-later

**Aucun code de GNOME Shell n'est copié ni redistribué dans GnomeWin.** GnomeWin est écrit en C# / WPF ;
GNOME Shell est écrit en JavaScript sur GTK, Clutter et Mutter, et ne fonctionne pas sur Windows.

Ce qui est repris de la source de GNOME, ce sont des **valeurs de mise en page** : tailles, marges, rayons
d'arrondi, couleurs et constantes de comportement, afin que les proportions soient justes plutôt
qu'approximatives. Les fichiers concernés sont cités en commentaire à l'endroit où la valeur est utilisée :

| Source GNOME | Ce qui en est repris | Où dans GnomeWin |
|---|---|---|
| `data/theme/gnome-shell-sass/_common.scss` | `$base_padding: 6px`, `$base_margin: 4px`, `$base_border_radius: 8px`, `$modal_radius: 16px`, `$scalable_icon_size: 16px`, `%heading: 11pt`, `%caption: 9pt` | valeurs de base de toute l'interface |
| `_colors.scss`, `_default-colors.scss`, `_palette.scss` | `$system_overlay_bg_color` = `mix(#222226, #fafafb, 90%)` = `#38383B` | `Brush.OverlayBg` des thèmes |
| `widgets/_search-results.scss` | espacement des sections, marges et rayon des cartes, colonne du fournisseur | `Shell/Search/SearchResultsView.cs` |
| `widgets/_search-entry.scss` | largeur `24em` de la zone de recherche | `Shell/Overview/OverviewWindow.cs` |
| `widgets/_panel.scss` | hauteur `2.2em`, texte en gras, pastille d'espace de travail `$scalable_icon_size * 0.5` | `Shell/SystemPanel/TopBarView.xaml` |
| `widgets/_app-grid.scss` | rayon des tuiles `$base_border_radius * 3` | `Shell/ApplicationGrid/AppGridView.cs` |
| `widgets/_quick-settings.scss` | hauteur des bascules `$scalable_icon_size * 3`, espacement `$base_padding * 2`, tailles de texte | `Shell/SystemPanel/QuickSettingsWindow.cs` |
| `js/ui/search.js` | `MAX_LIST_SEARCH_RESULTS_ROWS = 5` et l'étiquette « N de plus » | `Services/Search/SearchService.cs` |
| `js/ui/iconGrid.js` | tailles d'icônes 96 / 64 / 48 / 32 et dispositions de page | `Shell/ApplicationGrid/AppGridView.cs` |

## Ubuntu / Yaru

- Thème : [Yaru](https://github.com/ubuntu/yaru) — Canonical et la communauté Ubuntu, licences GPL-3.0 et CC-BY-SA-4.0
- Extension : [Dash to Dock](https://github.com/micheleg/dash-to-dock) — Michele Gaio, GPL-2.0-or-later
  (taille d'icône du dock par défaut, mode panneau, icônes centrées)

Repris : la palette Yaru, la couleur d'accent et la disposition du dock d'Ubuntu.

## Polices embarquées

- **Ubuntu** — Canonical, [Ubuntu Font Licence 1.0](https://ubuntu.com/legal/font-licence)
- **Cantarell** — Dave Crossland et contributeurs, SIL Open Font License 1.1
- **Fira Sans** — Mozilla, SIL Open Font License 1.1

Les textes de licence accompagnent les fichiers dans `src/GnomeWin/Assets/Fonts/`.

## Marques

« GNOME » est une marque déposée de la GNOME Foundation. « Ubuntu » est une marque déposée de Canonical Ltd.
Ces noms sont employés ici uniquement pour décrire ce que GnomeWin reproduit.

---

GnomeWin lui-même est distribué sous licence MIT.
