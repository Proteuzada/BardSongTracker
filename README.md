# BardSongTracker

Dalamud plugin that tracks Bard songs and damage-over-time effects for tanks, healers, and DPS. The overlay shows the time remaining, and a sound plays when an effect reaches the alert threshold. In-game command: `/bst`.

The menu has five tabs:

- **General** — song and DoT overlay, volume, and sound test
- **Meter** — damage meter window, and which columns to show
- **Logs** — ACT or IINACT network log folder, and the FF Logs API client used for the parse curve
- **Bard** — thresholds for Mage's Ballad, Army's Paeon, and The Wanderer's Minuet
- **DoTs** — role and job filters, search, and a toggle for each skill

In combat, only your current job is tracked. Enemy effects are read from your current target. Doton, Salted Earth, and Slipstream use the ground-effect timer on you.

Jobs without a tracked effect (Warrior, Viper, Dancer, and Red Mage) stay in the list so that is explicit. Reaper tracks Death's Design. Fanged Muse is included as the Pictomancer DoT in PvP. Song of Torment and Nightbloom share one status. Bad Breath is omitted because its poison does not have a unique status id.

- Target: `net10.0-windows`
- Dalamud API 15, DalamudPackager 15.0.0
- Audio: NAudio 2.2.1 (WASAPI). The General tab picks the alert. Classic is the original sound. Chime, Sparkle, Pop, Bell, Twinkle, and Slide are original. The voice alerts are original recordings by DarkNightPrincess, dedicated to the public domain under CC0 on Freesound. They are not clips from an anime.

## Live parse

The damage meter tails the newest `Network_*.log`. An empty Logs tab uses the usual Advanced Combat Tracker or IINACT folder. Choose another folder there when the logs are stored somewhere else. The Meter tab shows or hides the overlay. Only rDPS and parse leaves each row as rDPS, the job icon, the first name, and the parse. The other checkboxes add damage percent, a death count after someone dies, the fight time and zone above the columns, and a click on a row for crit rate, direct hit rate, and the biggest hit. Keep the last pull leaves those numbers up after a wipe until the next pull. rDPS is shown in thousands, such as 12k or 40k, unless the Meter tab is set to full numbers. Eight players fit. rDPS is raid-contributing damage per second: damage gained from other players' raid buffs is removed, and the damage those buffs gave to other players is added, using the split FF Logs publishes. The parse is that rDPS placed on the site's ranking curve for the same fight and job. Ultimates are split internally into the parts FF Logs ranks, but the meter keeps one running total for damage, rDPS, and the fight clock. The parse is a time-weighted mix of those parts and refreshes about every ten seconds, so a new part does not send it back to zero. A savage floor or a dungeon stays one fight until the zone changes or combat drops. The Logs tab and the bottom of the meter say whether the API client is connected. The number shows as soon as that job's ranking curve starts loading, then moves with rDPS for the rest of the pull. The bottom of the meter says when the curve is still loading or when FF Logs has no encounter for the fight. Time spent dead lowers rDPS because the fight clock keeps running. Pet damage is added to the owner. The meter clears on a wipe, and when a monster drops aggro without dying, including a striking dummy reset from the right-click menu, unless the last pull is kept. A kill keeps the numbers and stops the clock. Drag any corner of the meter to resize it. Meter scale changes the size of the rows, names, and numbers. Show only me leaves your row and hides the rest. Your parse is still your own rDPS on that boss's curve. Overlay scale on the General tab changes the song and DoT overlay only.

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

The voice alerts are recordings by DarkNightPrincess, published on Freesound under CC0 1.0. The license lets the plugin include them. They are not lines taken from an anime.

- [Woah](https://freesound.org/people/DarkNightPrincess/sounds/658568/)
- [Wow](https://freesound.org/people/DarkNightPrincess/sounds/658569/)
- [Okay](https://freesound.org/people/DarkNightPrincess/sounds/648637/)
- [Ah](https://freesound.org/people/DarkNightPrincess/sounds/646618/)
- [Nya](https://freesound.org/people/DarkNightPrincess/sounds/625317/)
- [Giggle](https://freesound.org/people/DarkNightPrincess/sounds/611780/)
- [Laugh](https://freesound.org/people/DarkNightPrincess/sounds/611781/)
- [Oh](https://freesound.org/people/DarkNightPrincess/sounds/621771/)
- [Sigh](https://freesound.org/people/DarkNightPrincess/sounds/621773/)
- [Sneeze](https://freesound.org/people/DarkNightPrincess/sounds/686480/)
- [Love You](https://freesound.org/people/DarkNightPrincess/sounds/658571/)
