# Portable Windows ZIP

This fork supports an install-free Windows x64 archive alongside the upstream
installer architecture.

## Build

Build on Windows using Node.js 24.16+, pnpm 11.5.2, .NET SDK 10 (global.json),
Rust 1.96, Visual Studio C++ Build Tools, Windows SDK, and WebView2 Runtime.

From the repository root:

```powershell
pnpm install --frozen-lockfile
dotnet restore .\KM.Editor.slnx
pnpm --dir apps/desktop exec tauri build --ci --no-bundle --no-sign
.\scripts\Build-KmPortable.ps1 -CargoTargetDirectory .\apps\desktop\src-tauri\target -OutputDirectory .\portable-dist
```

Alternatively use **Actions > Portable Windows ZIP > Run workflow**, then download
the ZIP from the run's Artifacts section. Pull requests and master pushes also
build this archive for validation.

## Distribution and data

The archive contains one folder:

```text
KM.Editor.Portable/
  km-editor-desktop.exe
  km-tools-bridge.exe
  portable.flag
  Data/
```

Run `km-editor-desktop.exe` directly. Keep both EXEs and `portable.flag` in
the same folder. The marker enables portable mode before Tauri initializes
its webview or spawns the managed project bridge.

Portable behavior:

- WebView2 localStorage, cookies and browser cache are under `Data/WebView2`.
- .NET game caches, workspaces, notes, personalization and project state
  are under `Data/KM Editor`.
- Process temporary files and .NET single-file extraction go to `Data/Temp`
  and `Data/DotNetBundle`.
- The native updater is not initialized; the in-app update check does not
  contact GitHub or launch the installer.
- Environment changes affect only the running application and its children.
- ROM inputs and mod output folders explicitly chosen by the user remain in
  their chosen locations.

Without `portable.flag`, the original Windows user profile behavior applies.
Do not remove the marker after creating data under `Data`.

**Requirements and limits:** Windows must have WebView2 Runtime; the ZIP does
not embed Microsoft's fixed-version runtime. Extract to a writable location,
not Program Files, and move the complete folder to keep data. Data written by
older installed KM Editor versions is not automatically migrated into `Data`.
Portable mode intentionally does not require WiX, the upstream signing key or
the upstream installer's EULA acceptance.
