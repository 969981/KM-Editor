// SPDX-License-Identifier: GPL-3.0-only

use std::path::{Path, PathBuf};

pub fn find(target: &str) -> Result<Vec<String>, String> {
    let names: &[&str] = match target {
        "ryujinx" => &["Ryujinx", "Ryubing", "ryujinx", "ryubing"],
        "eden" => &["eden", "Eden"],
        _ => return Err("Choose a supported emulator.".to_owned()),
    };
    let mut parents = Vec::new();
    for variable in [
        "APPDATA",
        "LOCALAPPDATA",
        "XDG_DATA_HOME",
        "XDG_CONFIG_HOME",
    ] {
        if let Some(path) = std::env::var_os(variable).map(PathBuf::from) {
            if path.is_absolute() {
                parents.push(path);
            }
        }
    }
    if let Some(home) = std::env::var_os("HOME")
        .or_else(|| std::env::var_os("USERPROFILE"))
        .map(PathBuf::from)
        .filter(|path| path.is_absolute())
    {
        parents.extend([
            home.join("AppData/Roaming"),
            home.join(".config"),
            home.join(".local/share"),
            home.join("Library/Application Support"),
        ]);
        let applications: &[&str] = match target {
            "ryujinx" => &["org.ryujinx.Ryujinx", "org.ryubing.Ryujinx"],
            _ => &["dev.eden_emu.eden"],
        };
        for application in applications {
            parents.push(home.join(".var/app").join(application).join("data"));
            parents.push(home.join(".var/app").join(application).join("config"));
        }
    }
    let candidates = parents
        .iter()
        .flat_map(|parent| names.iter().map(move |name| parent.join(name)));
    Ok(matching_folders(candidates, target))
}

pub fn matching_folders(
    candidates: impl IntoIterator<Item = PathBuf>,
    target: &str,
) -> Vec<String> {
    let mut found = Vec::new();
    let mut identities = Vec::new();
    for candidate in candidates {
        if !has_required_folders(&candidate, target) {
            continue;
        }
        // Resolve aliases only for duplicate detection. Keep the usable display path.
        let Ok(identity) = candidate.canonicalize() else {
            continue;
        };
        if identities.contains(&identity) {
            continue;
        }
        identities.push(identity);
        found.push(candidate.to_string_lossy().into_owned());
    }
    found
}

fn has_required_folders(root: &Path, target: &str) -> bool {
    let folders = match target {
        "ryujinx" => ["mods", "sdcard"],
        "eden" => ["load", "sdmc"],
        _ => return false,
    };
    root.is_absolute() && folders.iter().all(|name| root.join(name).is_dir())
}
