# BardSongTracker

Dalamud plugin that tracks Bard songs and damage-over-time effects for tanks, healers, and DPS. The overlay shows the time remaining, and a sound plays when an effect reaches the alert threshold. In-game command: `/bst`.

The menu has three tabs:

- **General** — overlay, volume, and sound test
- **Bard** — thresholds for Mage's Ballad, Army's Paeon, and The Wanderer's Minuet
- **DoTs** — role and job filters, search, and a toggle for each skill

In combat, only your current job is tracked. Enemy effects are read from your current target. Doton, Salted Earth, and Slipstream use the ground-effect timer on you.

Jobs without a tracked effect (Warrior, Viper, Dancer, and Red Mage) stay in the list so that is explicit. Reaper tracks Death's Design. Fanged Muse is included as the Pictomancer DoT in PvP. Song of Torment and Nightbloom share one status. Bad Breath is omitted because its poison does not have a unique status id.

- Target: `net10.0-windows`
- Dalamud API 15, DalamudPackager 15.0.0
- Audio: NAudio 2.2.1 (WASAPI). The General tab picks the alert. Classic is the original sound. Chime, Sparkle, Pop, Bell, Twinkle, and Slide are original. The voice alerts (Woah, Wow, Okay, Ah, Nya, Giggle) are original recordings by DarkNightPrincess, dedicated to the public domain under CC0 on Freesound. They are not clips from an anime.

## Install from Dalamud, without compiling

The plugin is already built for Dalamud API 15. In game, open `/xlsettings`, go to **Experimental**, and add this custom plugin repository:

```
https://raw.githubusercontent.com/Proteuzada/BardSongTracker/main/pluginmaster.json
```

Save, open the plugin installer (`/xlplugins`), and install **BardSongTracker**. The zip includes the DLL, the manifest, and NAudio. The public repository is https://github.com/Proteuzada/BardSongTracker.

## Build

Dalamud references come from `%AppData%\XIVLauncher\addon\Hooks\dev\`. Open the folder in Visual Studio or Rider and build Release, or run:

```powershell
dotnet build BardSongTracker.csproj -c Release
```

The output is in `bin\Release\`.

## Install for testing

Copy these files from `bin\Release\` to `%AppData%\XIVLauncher\devPlugins\BardSongTracker\`:

- `BardSongTracker.dll`
- `BardSongTracker.json`
- `NAudio.dll`
- `NAudio.Core.dll`
- `NAudio.Wasapi.dll`

Enable dev plugins in Dalamud and load the plugin from `/xlplugins`.

Do not use an old `ReadyToInstall` folder. That earlier build had a smaller DLL, with no MP3 and no NAudio, and this version cannot play the alert without those DLLs beside it.

## Voice alerts

Woah, Wow, Okay, Ah, Nya, and Giggle are voice recordings by DarkNightPrincess, published on Freesound under CC0 1.0. The license lets the plugin include them. They are not lines taken from an anime.

- [Woah](https://freesound.org/people/DarkNightPrincess/sounds/658568/)
- [Wow](https://freesound.org/people/DarkNightPrincess/sounds/658569/)
- [Okay](https://freesound.org/people/DarkNightPrincess/sounds/648637/)
- [Ah](https://freesound.org/people/DarkNightPrincess/sounds/646618/)
- [Nya](https://freesound.org/people/DarkNightPrincess/sounds/625317/)
- [Giggle](https://freesound.org/people/DarkNightPrincess/sounds/611780/)
