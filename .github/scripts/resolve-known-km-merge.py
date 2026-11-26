#!/usr/bin/env python3
"""Resolve the reviewed KM Editor portable/upstream app-data convergence.

Only the pinned 2026-11-26 Git merge stages are accepted. Preserve the upstream
performance, diagnostics and unified storage code, plus the fork's portable
Tauri startup, updater isolation and user-data directory. Unknown conflicts
must stop synchronization rather than overwriting source.
"""
from pathlib import Path
import subprocess
import sys

EXPECTED = {
    "apps/desktop/src-tauri/src/lib.rs": ("24907a97c2511cc13f2e3d1ac44c22654004c0fb", "33adab053366a219bb3668a8df98d33a80f583c1"),
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
    if upstream_sha == "1345293a389bf0f8203988ef09dcd441e7ee6166":
        current = Path(path).read_text(encoding="utf-8")
        Path(path).write_text(merge_appdata(current), encoding="utf-8", newline="\n")
        subprocess.run(["git", "add", "--", path], check=True)
    elif upstream_sha != "7bf1cfdeaeb1b66e386b9ac792ad1190aa50d783":
        raise RuntimeError(f"Unified upstream app-data resolver changed: {upstream_sha}")
    remaining = subprocess.check_output(
        ["git", "diff", "--name-only", "--diff-filter=U"]
    ).strip()
    if remaining:
        raise RuntimeError(f"Unresolved paths: {remaining.decode()}")
    print("Merged reviewed Rust source conflict, retained portable data, diagnostics and upstream optimizations.")

if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, ValueError, subprocess.CalledProcessError) as error:
        print(f"::error::{error}", file=sys.stderr)
        sys.exit(1)
