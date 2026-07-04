# Echo-S9 Battle Script

This source is part of Echo-S9 enabling battle lines to be shown on top of playable characters.

## Releases

**PLEASE NOTE:** Downloadable releases are always built on top of the [latest Memoria canary release](https://github.com/Albeoris/Memoria/releases/tag/canary).

- [Latest stable release](https://github.com/tsunamods-codes/EchoS9-BattleScript/releases/latest)
- [Latest canary release](https://github.com/tsunamods-codes/EchoS9-BattleScript/releases/tag/canary)

## Build

### Visual Studio

0. Download the the latest [Visual Studio Community](https://visualstudio.microsoft.com/vs/community/) installer
1. Run the installer and import this [.vsconfig](.vsconfig) file in the installer to pick the required components to build this project
2. Install the game ( [Steam](https://store.steampowered.com/app/377840/FINAL_FANTASY_IX/) or [GOG](https://www.gog.com/en/game/final_fantasy_ix), whichever you prefer )
3. Install Memoria ( https://github.com/Albeoris/Memoria#install )
4. Download Echo-S9 from the Mod Catalog
5. Open the file [`EchoS9-BattleScript.csproj`](src/EchoS9-BattleScript.csproj) in Visual Studio and click the build button
6. Launch the game and test the script

## Credits

Thanks to:

- [SamsamTS](https://github.com/SamsamTS) for the original plugin code
- [DV666](https://github.com/DV666) for various feature additions to the code
- [WarpedEdge](https://github.com/WarpedEdge) for the flexible mod name loading
- [TrueOdin](https://github.com/julianxhokaxhiu) for allowing multiple Battle IDs to be targeted in a single cell, Github publishing and CI/CD releases
