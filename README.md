# BardSongTracker

Plugin de Dalamud para o timer das músicas do Bardo e dos DoTs de tank, healer e DPS. O overlay mostra o tempo que falta e o som toca quando entra no limiar. Comando no jogo: `/bst`.

O menu tem três abas:

- **Geral** — overlay, volume e teste do som
- **Bard** — limiar de Mage's Ballad, Army's Paeon e The Wanderer's Minuet
- **DoTs** — filtro por função e job, busca, e um interruptor por skill

Em combate só o job atual é monitorado. DoT no inimigo usa o alvo atual. Doton, Salted Earth e Slipstream usam o tempo da poça em você.

Jobs sem DoT de jogador (Warrior, Reaper, Viper, Dancer, Red Mage) aparecem no menu para deixar isso explícito. Fanged Muse entra como DoT de Pictomancer no PvP. Song of Torment e Nightbloom dividem o mesmo status. Bad Breath não entra: o veneno dele não tem um status id só dele.

- Alvo: `net10.0-windows`
- Dalamud API 15, DalamudPackager 15.0.0
- Áudio: NAudio 2.2.1 (WASAPI), arquivo `Resources/alert.mp3` embutido na DLL

## Instalar pelo Dalamud, sem compilar

O plugin já está compilado para Dalamud API 15. No jogo, abra `/xlsettings`, vá em **Experimental** e adicione este repositório customizado:

```
https://raw.githubusercontent.com/Proteuzada/BardSongTracker/main/pluginmaster.json
```

Salve, abra o instalador de plugins (`/xlplugins`) e instale **BardSongTracker**. O zip inclui a DLL, o manifesto e o NAudio. O repositório público é https://github.com/Proteuzada/BardSongTracker.

## Build

As referências do Dalamud vêm de `%AppData%\XIVLauncher\addon\Hooks\dev\`. Abra a pasta no Visual Studio ou no Rider e compile em Release, ou:

```powershell
dotnet build BardSongTracker.csproj -c Release
```

A saída fica em `bin\Release\`.

## Instalar para testar

Copie estes arquivos de `bin\Release\` para `%AppData%\XIVLauncher\devPlugins\BardSongTracker\`:

- `BardSongTracker.dll`
- `BardSongTracker.json`
- `NAudio.dll`
- `NAudio.Core.dll`
- `NAudio.Wasapi.dll`

Ative os dev plugins no Dalamud e carregue pelo `/xlplugins`.

Não use uma pasta `ReadyToInstall` antiga. O build anterior dessa pasta tinha uma DLL menor, sem o MP3 e sem o NAudio, e o plugin não carrega o som desta versão sem essas DLLs ao lado.
