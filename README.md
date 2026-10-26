
<p align="center">
  <img width="466" height="69" alt="logo-middle" src="https://github.com/user-attachments/assets/1aa4bbc5-2f09-4adf-bb1b-7d78a45682b7" />
</p>

## 

DOS Game Vault is a Windows application for organizing and playing DOS games through DOSBox, with support for **English and Simplified Chinese**. 

Browse your collection, manage favorites and cover artwork, and customize how each game launches. 

**Game files, DOSBox, and cover images are not included.**

<img width="1650" height="1031" alt="dosgamevault-preview2" src="https://github.com/user-attachments/assets/30e6296f-c4a6-4f2c-b049-eaf100b239e5" />

## Download and start

1. Download `DOSGameVault-x.x.x-win-x64.zip` from the [project's releases](https://github.com/stevenx615/dos-game-vault/releases).
2. Extract the ZIP file and run `DOSGameVault.exe`. 
4. Follow the setup guide to choose your game collection folder.
5. Scan your collection.
6. Find or add your DOSBox installations and choose a default version.
7. Select a game and click **Play**, or double-click it.

## Organize your collection

Keeping games together in a collection folder makes scanning and moving the collection easier. DOSBox can be inside that folder or registered from another location.

Example:
```text
DOSGames/
    DOOM/
        DOOM.EXE
    PRINCE/
        PRINCE.EXE
    DOSBOX/
        dosbox.exe
    _game_previews/
        DOOM.jpg
        PRINCE.png
        _preview_index.json
```

## Troubleshooting

- **A game will not launch:** check its startup-file path and selected DOSBox installation in Game Settings. Confirm that the required game files still exist.
- **DOSBox is missing:** register its executable in Settings and choose a default version.
- **A game was not recognized:** look in Unsorted and verify its startup program, or add it individually.
- **Covers are missing after reinstalling:** select the original cover folder and rescan. Keep `_cover_index.json` with the images.
