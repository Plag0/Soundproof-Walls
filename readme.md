# Soundproof Walls for Barotrauma

**Soundproof Walls** is a mod for *Barotrauma* that overhauls the vanilla sound system to create a more immersive and dynamic audio experience. It introduces sound occlusion, propagation, sidechain compression, simulated reverb, and realistic muffling. The mod also adds new gameplay mechanics, such as eavesdropping and hydrophone monitoring, along with numerous smaller tweaks, improvements, and fixes to the vanilla game's audio.

The easiest way to use this mod is by subscribing on the [Steam workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=3153737715).

Alternatively, if you want to try unreleased features or prefer compiling the mod yourself, follow the build instructions below.

---

## 🛠️ Build Instructions

This project is built with .NET 8.0 and is designed for cross-platform use via Visual Studio or command-line using the .NET SDK.

Follow these steps to build the project on any platform:

### 1. Install the .NET SDK

Download and install the **.NET 8 SDK or newer** from the official Microsoft website:  
👉 [https://dotnet.microsoft.com/download/dotnet](https://dotnet.microsoft.com/download/dotnet)

---

### 2. Clone the Repository

If you have Git installed, run this command to download the repository to your local machine:

```bash
git clone https://github.com/Plag0/Soundproof-Walls.git
```

Alternatively, click the green "Code" button at the top of the GitHub page and select "Download ZIP", then extract its contents wherever you want.

---

### 3. Set Up References

To compile correctly, you'll need external references:

1. Inside the `Soundproof-Walls` directory you just downloaded, create a folder named `Refs`. The file structure should look like:
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

---

### 4. Build the Project

1. Open the `Build.props` file inside the `Soundproof-Walls` directory with any text editor, and change the value between the `ModDeployDir` tags to point to your system’s `Barotrauma/LocalMods` folder. For Windows users, this may already be set correctly if *Barotrauma* is installed on your C: drive.

2. Open a terminal/command prompt and prepare to run the following command:

```bash
dotnet build "path/to/Soundproof-Walls"
```

Replace `"path/to/Soundproof-Walls"` with the actual path to the project folder containing the "Soundproof Walls.sln" file and run the command.
For example, on Windows:

```bash
dotnet build "C:/Users/YourName/Downloads/Soundproof-Walls"
```

Alternatively, you could use Visual Studio to build the project but the command is much easier.

**Note:**  
You can safely ignore any build warnings. If there are errors, refer to the troubleshooting section below.

---

## 🧪 In-Game Usage

Once built, first make sure you are subscribed to [Lua For Barotrauma](https://steamcommunity.com/sharedfiles/filedetails/?id=2559634234) and have client-side Lua installed. I recommend using the Steam launch option installation method shown in the mod's description.

Then launch *Barotrauma* and navigate to your mods list:

- You should see two versions of **Soundproof Walls**.
- Enable the one with the **grey pencil icon** - this is the local version you just built.
- Host your game server.
- Any player joining will automatically download your custom version of the mod.

---

## ❓ Troubleshooting

- **Missing assembly errors?** Ensure all files and folders from `luacsforbarotrauma_refs.zip` and the `NVorbis.dll` are placed inside the `Refs` folder located at `Soundproof-Walls/Refs`.
- **Being used by another process error?** Try the command again. I tend to run the command until the project builds successfully twice in a row.
- **Command not found?** Check your .NET SDK installation is valid by running `dotnet --info`. If there are errors, or your SDK version is less than 8.0, reinstall your .NET SDK.
---
