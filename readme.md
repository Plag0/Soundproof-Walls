Soundproof Walls is a mod for Barotrauma that overhauls the vanilla sound system to include more advanced audio features like reverb, distortion, sidechaining, occlusion, dynamic lowpassing, and more. It fixes many vanilla isssues, adds two new sound-based mechanics (eavesdropping and hydrophone monitoring), and comes with 230+ unique configuation options in a custom in-game menu.

Soundproof Walls uses the OpenAL Soft audio engine packaged with the game and takes advantage of the previously unused Effects Extension to integrate reverb, distortion, and dynamic lowpassing. The vanilla DFS algorithm for sound paths and muffling has been swapped out with an A* implementation for better quality paths that count obstructions for additive muffling. Occlusion is done with simple LOS ray casting using the game's existing Farseer Physics Engine methods. Sidechaining is event-driven and triggered by sounds referenced in the "Custom Sounds" list in the Advanced menu tab, and other effects like the radio filters and Static mode's reverb filter were built with reference to common implementations (e.g., Schroeder-style reverb, hard clipping distortion, etc).

The easiest way to use this mod is by subscribing on the [Steam workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=3153737715).

Alternatively, if you want to try unreleased features or just prefer compiling the mod yourself, follow the build instructions below.

## Build Instructions

### 1. Install the .NET SDK

Download and install the **.NET 8 SDK or newer** from the [official Microsoft website](https://dotnet.microsoft.com/download/dotnet).

### 2. Clone this Repository

If you have Git installed, run this command to download the repository to your local machine:

```bash
git clone https://github.com/Plag0/Soundproof-Walls.git
```

Alternatively, click the green "Code" button at the top of the GitHub page and select "Download ZIP", then extract its contents wherever you want.

### 3. Set Up References

To compile correctly, you need external references:

1. Inside the `Soundproof-Walls` directory you just downloaded, create a folder called `Refs`. The file structure should look like:
```
Soundproof-Walls/
├── Assets/
├── ClientProject/
├── Refs/
├── ServerProject/
├── SharedProject/
├── Soundproof Walls.sln
└── ... (other files)
```

2. Download the latest `luacsforbarotrauma_refs.zip` file from the [LuaCsForBarotrauma Releases page](https://github.com/evilfactory/LuaCsForBarotrauma/releases/download/latest/luacsforbarotrauma_refs.zip).

3. Extract all contents from the ZIP into the `Refs` folder.

4. Navigate to your Barotrauma installation directory, locate `NVorbis.dll`, and copy it into the `Refs` folder.

### 4. Build the Project

1. Open the `Build.props` file inside the `Soundproof-Walls` directory with any text editor, and change the value between the `ModDeployDir` tags to point to your system’s `Barotrauma/LocalMods` folder. For Windows users, this may already be set correctly if Barotrauma is installed on your C: drive.

2. Open a terminal and run the following command - replacing the example path with the actual directory of the project folder (the folder containing the "Soundproof Walls.sln" file):
```bash
dotnet build "example/path/to/Soundproof-Walls"
```

Alternatively, you can use Visual Studio to build the project.

**Note:**  
You can safely ignore any build warnings. If there are any errors, refer to the troubleshooting section below.

## In-Game Usage

1. Subscribe to [Lua For Barotrauma](https://steamcommunity.com/sharedfiles/filedetails/?id=2559634234) and follow the instructions in the description to install client-side Lua.

2. Launch Barotrauma. If client-side Lua has been installed correctly, you will see an "Open LuaCs Settings" button in the top left corner of the main menu. Click this button and tick "Enable Csharp Scripting".

3. Navigate to your mods list and enable Soundproof Walls. If there are two copies of the mod, choose the one with the grey pencil icon.

4. Play the game and enjoy! If you're hosting a multiplayer session, any player joining will automatically download and use your local version of the mod.

## Troubleshooting

- **Missing assembly errors?** Ensure all files and folders from `luacsforbarotrauma_refs.zip` and the `NVorbis.dll` are placed inside the `Refs` folder located at `Soundproof-Walls/Refs`.
- **Being used by another process error?** Believe it or not, just repeat the command until it works.
- **Command not found?** Check your .NET SDK installation is valid by running `dotnet --info`. If there are errors, or your SDK version is less than 8.0, reinstall your .NET SDK.
