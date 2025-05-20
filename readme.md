# Soundproof Walls for Barotrauma

**Soundproof Walls** is a mod for *Barotrauma* that overhauls many aspects of the vanilla sound system to create a more immersive and dynamic experience.

The easiest way to use this mod is by subscribing on the [Steam workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=3153737715).

Alternatively, if you want to try unreleased features early or prefer compiling the mod yourself, follow the build instructions below.

---

## 🛠️ Build Instructions

This project is built with .NET and is designed for cross-platform use via Visual Studio or command-line using the .NET SDK.

Follow these steps to build the project on any platform:

### 1. Install the .NET SDK

Download and install the **.NET 6 SDK or newer** from the official Microsoft website:  
👉 [https://dotnet.microsoft.com/download/dotnet](https://dotnet.microsoft.com/download/dotnet)

---

### 2. Clone the Repository

Clone or download the repository to your local machine:

```bash
git clone https://github.com/Plag0/Soundproof-Walls.git
```

Alternatively, download it as a ZIP and extract it.

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

Open a terminal/command prompt and run the following command:

```bash
dotnet build "path/to/Soundproof-Walls"
```

Replace `"path/to/Soundproof-Walls"` with the actual path to the project folder containing the "Soundproof Walls.sln" file.
For example:

```bash
dotnet build "C:/Users/YourName/Downloads/Soundproof-Walls-master"
```

Alternatively, if you're on Windows, you could use Visual Studio to build the project instead of a command.

**Note:**  
You can safely ignore any build warnings. If there are errors, refer to the troubleshooting section below.

---

## 🧪 In-Game Usage

Once built, launch *Barotrauma* and navigate to your mods list:

- You should see two versions of **Soundproof Walls**.
- Enable the one with the **grey pencil icon** - this is the local version you just built.
- Host your game server.
- Any player joining will automatically download your custom version of the mod.

---

## ❓ Troubleshooting

- **Missing assembly errors?** Ensure all files and folders from `luacsforbarotrauma_refs.zip` and the `NVorbis.dll` are placed inside the `Refs` folder located at `Soundproof-Walls/Refs`.
- **Being used by another process error?** Try the command again. I tend to run the command until the project builds successfully twice in a row.
- **Command not found?** Check your .NET SDK installation is valid by running `dotnet --info`. If there are errors, try reinstalling.
---
