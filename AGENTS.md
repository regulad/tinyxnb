# Agent notes for `regulad/tinyxnb`

This repository is tinyxnb: Explorer thumbnails and a **Save as PNG** verb for
`.xnb` files, from a COM thumbnail provider and the `xnb2png` CLI.
[README.md](README.md) covers the layout, the build and what has been tested.

## Debugging

- Debug decoding through `xnb2png` first, and touch the COM layer only once
  the CLI's PNG is right.
- The handler runs in `dllhost.exe`, not Explorer. `DisableProcessIsolation`
  (DWORD 1) under its CLSID makes attaching easier. It is for development
  only and never ships.
- A rebuild hits a file lock until that `dllhost.exe` exits or Explorer
  restarts.

## Verification corpus

- MonoGame's test textures:
  `git clone --depth 1 --filter=blob:none --no-checkout https://github.com/MonoGame/MonoGame.git`,
  then `git sparse-checkout set Tests/Assets/Textures` and `git checkout`.
- Build test XNBs in memory, and never commit game assets.
- The PowerShell XNB generator and the native `DllGetClassObject` host used on
  2026-08-10 were never committed. Rewrite them into the test project, which
  isn't written yet.
