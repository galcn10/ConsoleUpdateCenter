# Changelog

## 1.0.0

First public release.

- Downloads side panel in the style of a console control center, with Playnite dimmed behind it and the logo's own colors (same family of glass rows, icons and Inter/Nunito fonts as Console Overlay).
- Opens on the focused game only when a game has the focus; from the hub or menus it shows all downloads.
- Patch notes in your language: official Steam translation when available, otherwise Google Translate (optional).
- Steam: per-game detection with progress, speed, time left and free-space check; Check for update, Patch notes, Verify files and "Always keep this game updated".
- Epic Games: unfinished downloads and quiet launcher restart. GOG Galaxy: quiet restart. Xbox / Microsoft Store: opens Downloads and updates.
- Notice when selecting a game with an update (works with any theme).
- "Update complete" and low-disk-space notices; notices received while playing are shown afterwards.
- Ask before launching a game with a pending update.
- Shut down or sleep when downloads finish, with a cancellable countdown.
- Guide button on the home screen opens Downloads (other buttons available in settings).
- Button icons for Xbox, PlayStation and Nintendo controllers.
- 15 languages (6 in beta).
- Optional theme integration (cover badge and data for buttons) and an experimental cover badge that needs no theme changes.
- `status.json` with the download status for other plugins.
