#!/usr/bin/env python3
"""Resolve the reviewed KM Editor portable/upstream app-data convergence.

Only the pinned 2026-11-16 Git merge stages are accepted. Preserve the upstream
performance, diagnostics and unified storage code, plus the fork's portable
Tauri startup, updater isolation and user-data directory. Unknown conflicts
must stop synchronization rather than overwriting source.
"""
from pathlib import Path
import subprocess
import sys

EXPECTED = {
    "apps/desktop/src-tauri/src/lib.rs": ("45c8b37d048e8e3be0af844545e3680db1ded722", "e348588eb7b485d13a21389301ea9c48569e940a"),
    "src/KM.Core/Output/OutputWorkspaceStorage.cs": ("c399be36868bc91bfa6fbb1b156a0e9d85bdb723", "806ba0d0b019b288142f065f3bdad51f78390d1d"),
    "src/KM.PLA/Caching/PlaCacheManager.cs": ("b2f85fc41ebf11eee7f242ab1e6c3c9942716ff9", "25126c20e6bb3699056637a92dcd74190cf5de69"),
    "src/KM.SV/Workflows/SvCacheManager.cs": ("19ea84f9836fa444629f0ebe20418df67d1ec438", "16ae20c8c5523da0f3174832c90e1a862863c485"),
    "src/KM.SwSh/Workflows/SwShCacheManager.cs": ("e56d65e1d7161249b593caa47d5c198ba1fba31b", "44742453f0516de0a8ef7e4cb5679a63d93928a3"),
    "src/KM.Tools/Application/ChangeSetApplicationService.cs": ("1b31f1d61bf1996505ecc50c97d0aca59e0aea01", "e1999223f96d14aaf526b7f24c019aa9cb932b02"),
    "src/KM.Tools/Application/ResearchAnnotationApplicationService.cs": ("278773b931d08a77975bd05108e08607b5de1544", "e6cd4ef45cf66c6a345d28c77b26954a6fd76dcf"),
    "src/KM.Tools/Application/ResearchLabApplicationService.cs": ("c0c8633901cb524067c948362f0feea107b8045e", "2f6a171bf3879b988a00aaf37970605fa6fedfcd"),
    "src/KM.Tools/Application/WorkspaceDraftApplicationService.cs": ("0392be2e6275475289dfa56d27ed7ba9c11047a5", "7ed9fa26a6904b6e86ed8a7f9d6d1d2cd1f90974"),
    "src/KM.Tools/Application/WorkspacePersonalStateApplicationService.cs": ("3b091104adc9e680f0fbfd009c8c4652ab210202", "0e2fa0e2c65083d49cdf2712465a3d6b3b1bb3ba"),
    "src/KM.ZA/Workflows/ZaCacheManager.cs": ("82319d5685fb1397e6b8428687996577bf1065ac", "1bf88e9ab712ced46d91e0255bf3fa569efbbd11"),
}

def git(*args):
    return subprocess.check_output(["git", *args]).decode().strip()

def stage(path, side):
    return subprocess.check_output(["git", "show", f":{side}:{path}"]).decode("utf-8-sig")

def splice(text, old, new):
    if text.count(old) != 1:
        raise RuntimeError(f"Anchor changed, refusing update: {old[:100]!r}")
    return text.replace(old, new, 1)

def merge_rust(ours, theirs):
    # The local additions are bounded by these stable Rust entrypoint anchors.
    anchor = "// Portable mode is activated by the marker shipped alongside the executable."
    start = ours.index(anchor)
    end = ours.index("#[cfg_attr(mobile, tauri::mobile_entry_point)]", start)
    portable = ours[start:end]
    theirs = splice(theirs, "#[cfg_attr(mobile, tauri::mobile_entry_point)]",
                    portable + "#[cfg_attr(mobile, tauri::mobile_entry_point)]")
    theirs = splice(theirs, "pub fn run() {\n",
                    "pub fn run() {\n    #[cfg(windows)]\n"
                    "    configure_portable_environment()\n"
                    "        .expect(\"KM Editor portable data directory is not writable\");\n")
    theirs = splice(theirs,
        "            cleanup_stale_updater_temp_directories();",
        "            if !is_portable_mode() {\n"
        "                cleanup_stale_updater_temp_directories();\n"
        "            }")
    theirs = splice(theirs,
        "            #[cfg(desktop)]\n"
        "            app.handle()\n"
        "                .plugin(tauri_plugin_updater::Builder::new().build())?;",
        "            #[cfg(desktop)]\n"
        "            if !is_portable_mode() {\n"
        "                app.handle()\n"
        "                    .plugin(tauri_plugin_updater::Builder::new().build())?;\n"
        "            }")
    theirs = splice(theirs,
        "        .invoke_handler(tauri::generate_handler![\n",
        "        .invoke_handler(tauri::generate_handler![\n"
        "            is_portable_mode,\n")
    for required in ("get_support_diagnostics", "support_reports::save_support_report",
                     "is_portable_mode", "KM_EDITOR_PORTABLE_DATA_DIR"):
        if required not in theirs:
            raise RuntimeError(f"Missing upstream or portable Rust feature: {required}")
    return theirs

def merge_appdata(text):
    anchor = "        var overridePath = Environment.GetEnvironmentVariable(\"KM_EDITOR_APP_DATA_ROOT\");"
    port = (
        "        // The Windows portable package sets this before launching the managed sidecar.\n"
        "        // Its Data folder is the root; individual managed services use KM Editor beneath it.\n"
        "        var portableRoot = Environment.GetEnvironmentVariable(\"KM_EDITOR_PORTABLE_DATA_DIR\");\n"
        "        if (portableRoot is not null)\n"
        "        {\n"
        "            if (string.IsNullOrWhiteSpace(portableRoot) || !Path.IsPathFullyQualified(portableRoot))\n"
        "                throw new InvalidOperationException(\"KM_EDITOR_PORTABLE_DATA_DIR must be an absolute path.\");\n"
        "            return Path.Combine(Path.GetFullPath(portableRoot), \"KM Editor\");\n"
        "        }\n\n"
    )
    return splice(text, anchor, port + anchor)

def main():
    conflicts = set(filter(None, subprocess.check_output(
        ["git", "diff", "--name-only", "--diff-filter=U", "-z"]
    ).decode().split("\0")))
    if conflicts != set(EXPECTED):
        raise RuntimeError(f"Unexpected conflicts: {sorted(conflicts ^ set(EXPECTED))}")
    for path, hashes in EXPECTED.items():
        actual = (git("rev-parse", f":2:{path}"), git("rev-parse", f":3:{path}"))
        if actual != hashes:
            raise RuntimeError(f"Upstream or local {path} changed; manual review required: {actual}")
    for path in EXPECTED:
        content = stage(path, 3)
        if path.endswith("/src/lib.rs"):
            content = merge_rust(stage(path, 2), content)
        if any(marker in content for marker in ("<<<<<<<", "=======", ">>>>>>>")):
            raise RuntimeError(f"Conflict marker in {path}")
        Path(path).write_text(content, encoding="utf-8", newline="\n")
        subprocess.run(["git", "add", "--", path], check=True)
        print("Reconciled", path)
    path = "src/KM.Core/Workspace/KmEditorAppData.cs"
    upstream_sha = git("hash-object", path)
    if upstream_sha != "1345293a389bf0f8203988ef09dcd441e7ee6166":
        raise RuntimeError(f"Unified upstream app-data resolver changed: {upstream_sha}")
    current = Path(path).read_text(encoding="utf-8")
    Path(path).write_text(merge_appdata(current), encoding="utf-8", newline="\n")
    subprocess.run(["git", "add", "--", path], check=True)
    remaining = subprocess.check_output(
        ["git", "diff", "--name-only", "--diff-filter=U"]
    ).strip()
    if remaining:
        raise RuntimeError(f"Unresolved paths: {remaining.decode()}")
    print("Merged 11 source conflicts, retained portable data, diagnostics and upstream optimizations.")

if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, ValueError, subprocess.CalledProcessError) as error:
        print(f"::error::{error}", file=sys.stderr)
        sys.exit(1)
