# AccentHold

Sélecteur d'accents façon macOS pour Windows 11 : maintenez une lettre enfoncée, un
popup acrylique apparaît près du curseur de texte, choisissez la variante avec
`1‑9`, les flèches + `Entrée`, ou un clic.

- Mapping identique à macOS : `a c e i l n o s u y z` (+ majuscules), ex. `e` → è é ê ë ē ė ę.
- Indépendant du layout clavier (AZERTY, QWERTY, …) : la touche est résolue via la
  disposition active de l'application au premier plan.
- Le popup n'apparaît que si un champ de texte est actif (caret détecté) ; il se place
  au-dessus de la ligne tapée sans jamais la masquer, et reste dans l'écran.
- Tourne en arrière-plan (icône de zone de notification), ne vole jamais le focus.

## Installation

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1
```

Publie l'exécutable dans `%LOCALAPPDATA%\Programs\AccentHold`, l'enregistre au
démarrage de Windows (clé `Run` utilisateur, pas d'admin) et le lance.
Prérequis : [.NET Desktop Runtime 10](https://dotnet.microsoft.com/download/dotnet/10.0) (proposé automatiquement au premier lancement s'il manque).
Désinstallation : `scripts\uninstall.ps1`. Le menu de l'icône tray permet aussi de
désactiver temporairement ou de retirer le lancement automatique.

## Utilisation

1. Maintenez une lettre accentuable (ex. `e`) — la lettre s'affiche puis le popup s'ouvre.
2. Tapez le chiffre de la variante, ou naviguez avec `←`/`→` puis `Entrée` ; `Échap` annule.
3. Toute autre touche ferme le popup et s'insère normalement (comportement macOS).

## Développement

```powershell
dotnet build src\AccentHold          # build
dotnet run --project src\AccentHold  # lancer
AccentHold.exe --demo                # affiche le popup 6 s près de la souris (test visuel)
```

## Architecture

- `Core/KeyboardHook.cs`, `Core/MouseHook.cs` — hooks bas niveau (WH_KEYBOARD_LL / WH_MOUSE_LL).
- `Core/HoldController.cs` — machine à états : détection du maintien (1ʳᵉ auto-répétition),
  avalement des répétitions, sélection, commit.
- `Core/AccentMap.cs` — table des variantes macOS.
- `Core/CaretLocator.cs` — position du caret : caret Win32 → caret MSAA → UI Automation.
- `Core/TextInjector.cs` — remplace la lettre de base par la variante (`SendInput` Unicode).
- `UI/AccentPopup.xaml` — popup acrylique Win11 (DWM backdrop, coins arrondis, `WS_EX_NOACTIVATE`).
- `UI/TrayIcon.cs` — icône et menu de la zone de notification.

## Limites connues

- Les applications élevées (admin) ne reçoivent pas l'injection sans lancer AccentHold élevé.
- Si aucune API d'accessibilité n'expose le caret (jeux, apps exotiques), le popup ne
  s'affiche pas — la touche répète alors normalement, par design.
