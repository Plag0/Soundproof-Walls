# Soundproof Walls for Barotrauma

**Soundproof Walls** is a mod for *Barotrauma* that overhauls many aspects of the sound system. This project is built with .NET and is designed for cross-platform use via Visual Studio or command-line using the .NET SDK.

---

## 🛠️ Build Instructions

Follow these steps to build the project on any platform:

### 1. Install the .NET SDK

Download and install the latest .NET SDK from the official Microsoft website:  
👉 [https://dotnet.microsoft.com/en-us/download](https://dotnet.microsoft.com/en-us/download)

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

1. Inside the root `Soundproof-Walls` directory, create a folder named `Refs`.

2. Download the latest `luacsforbarotrauma_refs.zip` from the [LuaCsForBarotrauma Releases](https://github.com/evilfactory/LuaCsForBarotrauma/releases/download/latest/luacsforbarotrauma_refs.zip).

3. Extract all contents from the ZIP into the `Refs` folder.

4. Navigate to your Barotrauma installation directory, locate `NVorbis.dll`, and copy it into the `Refs` folder.

---

### 4. Build the Project

Open a terminal (or command prompt) and run the following command:

```bash
dotnet build "path\to\Soundproof-Walls"
```

Replace `"path\to\Soundproof-Walls"` with the actual path to the project folder. For example:

```bash
dotnet build "C:\Users\YourName\Downloads\Soundproof-Walls"
```

**Note:**  
If the console output shows only 1-2 errors, try running the build command again. If you encounter more, check that all references were placed correctly.

---

## 🧪 In-Game Usage

Once built, launch *Barotrauma* and navigate to your mods list:

- You should see two versions of **Soundproof Walls**.
- **Enable the one with the grey pencil icon** — this is the local version you just built.
- Host your game server.
- Any player joining will automatically download your custom version of the mod.

---

## ❓ Troubleshooting

- **Missing assembly errors?** Ensure all files and folders from `luacsforbarotrauma_refs.zip` and the `NVorbis.dll` are placed inside the `Refs` folder.
- **Being used by another process error?** Wait a moment and try the command again. Sometimes weird stuff can happen when building this project. I tend to run the command until the project builds successfully twice in a row.
- **Command not found?** Check your .NET SDK installation is working by running `dotnet --info`. If there are errors, try reinstalling it from step 1.
---
