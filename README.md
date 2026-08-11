# tinyxnb

Native Windows Explorer thumbnails for `.xnb` files, plus a right-click **Save as PNG**
verb. Roughly what [xnb-js](https://lybell-art.github.io/xnb-js/) does in a browser tab, but
in the shell.

`.xnb` is the compiled-content format from Microsoft XNA's Content Pipeline, still used by
MonoGame and FNA — Stardew Valley, Terraria, Dust: An Elysian Tail and many others ship
their art as `.xnb`.

## Installing

**Scoop is the canonical way to install tinyxnb.** It handles the download, the hash check,
registration, updates and clean removal, and it needs no administrator rights.

```powershell
scoop bucket add regulad https://github.com/regulad/scoop-regulad
scoop install regulad/tinyxnb
```

Explorer is restarted as part of the install, so thumbnails appear immediately.

### Prerequisite: the .NET 8 runtime

The only thing Scoop cannot install for you — no bucket carries a runtime-only .NET package.
Any **one** of these is sufficient, since all three place `Microsoft.NETCore.App` **8.x**
into the same `shared` folder that the COM host's runtime resolver reads:

```powershell
winget install Microsoft.DotNet.Runtime.8          # smallest, and all that is needed
winget install Microsoft.DotNet.DesktopRuntime.8   # superset, also fine
winget install Microsoft.DotNet.SDK.8              # superset, also fine
```

It must be **8.x specifically**: the handler asks for `Microsoft.NETCore.App 8.0.0`, and
hostfxr's roll-forward does not cross major versions, so a 9.x or 10.x runtime will not
satisfy it. Note it asks for the *base* framework, not `WindowsDesktop.App` — which is why
the plain runtime is enough.

Those installers need admin. If you want to avoid elevation entirely, install .NET into your
profile instead:

```powershell
./dotnet-install.ps1 -Runtime dotnet -Channel 8.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"
setx DOTNET_ROOT "$env:LOCALAPPDATA\Microsoft\dotnet"
```

`DOTNET_ROOT` is the first location both the COM host and `install.ps1` consult, so a
per-user runtime works — but Explorer must be restarted afterwards to inherit the variable.

### Manual install

For working from a source tree or a release zip without Scoop. Per-user and unelevated:
`install.ps1` writes only under `HKCU\Software\Classes`, and `uninstall.ps1` removes every
key it wrote.

```powershell
.\install.ps1 -RestartExplorer          # copies to %LOCALAPPDATA%\Programs\tinyxnb
.\uninstall.ps1 -RemoveFiles -RestartExplorer
```

Explorer must restart before the handler is loaded — `SHChangeNotify` is enough to make the
verbs appear, but not to make Explorer load a newly-registered handler DLL. Run both from
**64-bit** PowerShell: a 32-bit host's `HKCU\Software\Classes\CLSID` writes are redirected
into `Wow6432Node`, where 64-bit Explorer will never look, and the symptom is simply that
nothing happens.

`install.ps1 -ClearThumbnailCache` is a testing aid. Once Explorer has cached a thumbnail it
will not call the handler again for that file — not after a rebuild, and not after a rename
or move — so a stale thumbnail is indistinguishable from a broken handler.

### Packaging notes

Releases are versioned by date as `yy.M.d`, unpadded — today's would be `26.8.11` — and are
built and published by `.github/workflows/release.yml` when a `v*` tag is pushed. Each
release carries a `.sha256` sidecar next to the zip so a bucket's autoupdate can pick the
hash up without a human.

`-InPlace` registers the binaries where they already sit, leaving Scoop in charge of the
files:

```json
{
    "version": "26.8.11",
    "architecture": {
        "64bit": { "url": "...tinyxnb-26.8.11-win-x64.zip", "hash": "..." }
    },
    "bin": "xnb2png.exe",
    "post_install": [
        "$target = Join-Path (Split-Path $dir -Parent) 'current'",
        "& \"$dir\\install.ps1\" -InPlace -SourceDir $target -RestartExplorer"
    ],
    "pre_uninstall": [ "& \"$dir\\uninstall.ps1\" -RestartExplorer" ]
}
```

Three things to get right:

- **Register the `current` junction, not the versioned directory**, or every update orphans
  the registration — `InprocServer32` would point into a deleted path. The `post_install`
  expression above resolves to the junction whether Scoop hands you `…\tinyxnb\current` or
  `…\tinyxnb\26.8.11` as `$dir`, so it is correct either way and needs no assumption about
  Scoop's internals.
- **`pre_uninstall`, not `post_uninstall`.** Explorer holds `XnbThumbnail.comhost.dll` open
  while loaded, so `scoop update` and `scoop uninstall` fail with a file lock unless the
  handler is unregistered and Explorer restarted first.
- **Check for .NET 8 in `pre_install`, not `depends`.** No bucket carries a runtime-only
  .NET package — only SDKs, which are far heavier and install into Scoop's own tree rather
  than where the COM host looks. A `depends` on the wrong package breaks installation
  outright, so the manifest probes for `Microsoft.NETCore.App` 8.x directly and aborts with
  instructions before anything is registered.

`scoop install -g` installs machine-wide; pass `-Scope Machine` to both scripts for that,
from an elevated shell.

## Why it exists

There is no existing thumbnail provider, preview handler, or WIC codec for `.xnb` — not on
GitHub, not in SageThumbs / XnView / PIFShellExtensions, not in the Microsoft Store. Every
tool in the ecosystem (xnbcli, StardewXnbHack, XnbExtract, XnaConvert) is a batch CLI that
dumps a folder to disk. Nothing integrates with the shell.

The gap exists because `.xnb` is not an image container. It is a serialised-object stream
that *sometimes* holds a texture, so a thumbnailer has to parse a type-reader manifest,
possibly decompress LZX or LZ4, and decode BCn — all before it knows whether there is an
image in there at all. That is a real decoder, not a thin wrapper.

## Goals

- A texture in an `.xnb` should have a thumbnail in Explorer, with correct transparency.
- Getting a PNG out should be one right-click, not a command line and a scratch folder.
- Non-texture `.xnb` files must fail *cleanly* — the provider returns `WINCODEC_ERR_BADHEADER`
  so Explorer falls back to the generic icon rather than rendering something broken.
- No runtime surprises inside `dllhost.exe`: no GDI+, no image libraries, no NuGet
  dependencies in the decoder.

## Scope

**In scope:** `.xnb` files whose root object is a `Texture2D`. Read-only.

**Explicitly out of scope** — please don't add these without discussion:

- Repacking or writing `.xnb`
- `SpriteFont`, `SoundEffect`, `Song`, `Model`, `Effect`, `TextureCube`, `Texture3D`,
  dictionaries and other data readers
- Mip levels beyond level 0
- A GUI application
- A full WIC codec. It is the architecturally correct answer — one codec would give
  Explorer thumbnails, the preview pane and Photos for free — but it is far more work in
  C++/Rust, and since most `.xnb` files aren't images it would spend most of its life
  returning failure. Revisit only once the thumbnail provider is shipping and stable.

## Layout

```
src/XnbCore/        net8.0, portable, zero NuGet dependencies
                    container parsing, BCn + uncompressed surface decoding,
                    a ~150-line PNG encoder, and a box/nearest resampler
  Vendor/           NOT source — fetched from MonoGame at build time, see below
src/Xnb2Png/        self-contained CLI, xnb2png.exe — backs the context-menu verb
src/XnbThumbnail/   COM IThumbnailProvider — XnbThumbnail.comhost.dll
```

The repo/product is `tinyxnb`; the binaries deliberately keep descriptive names
(`xnb2png.exe`, `XnbThumbnail.comhost.dll`).

## Status

| Component | State |
|---|---|
| `XnbCore` — parsing, decoding, PNG, resampling | Done |
| `Xnb2Png` — CLI | Done |
| `XnbThumbnail` — COM thumbnail provider | Builds and activates; whole COM path verified without registration |
| `install.ps1` / `uninstall.ps1` | Done; install/uninstall round trip verified, no leftovers |
| Test project | Not written |

The handler has not yet been exercised by Explorer itself — only by a native stand-in host
that activates it through `DllGetClassObject`.

## What has actually been tested

Every surface format below is *implemented*, but only some have ever been fed real or
synthetic data. This table is deliberately conservative: "untested" means untested, not
"probably fine".

| Format | Container | Tested how | Verdict |
|---|---|---|---|
| `Color` (RGBA8) | uncompressed | 21 real OneShot: World Machine Edition wallpapers, 352×415 to 1920×1080; two inspected by eye | **works** |
| `Color` (RGBA8) | uncompressed | synthetic 4×4, driven through the full COM path | **pixel-exact** |
| `Dxt1` / BC1 | uncompressed | decoded against Pillow's DDS decoder | **bit-exact, maxdiff 0** |
| `Dxt3` / BC2 | uncompressed | decoded against Pillow | **bit-exact, maxdiff 0** |
| `Dxt5` / BC3 | uncompressed | decoded against Pillow | **bit-exact in RGB and alpha** |
| `Bgra4444` | uncompressed | part of MonoGame's 19-file texture corpus; dimensions and format match | decodes |
| any | LZ4 | hand-built literals-only block wrapping a known payload | plumbing only |
| any | LZX | hand-built stream of *uncompressed* blocks | plumbing only |
| `Bgr565`, `Bgra5551`, `Alpha8`, `Bgr32`, `Bgra32`, sRGB variants, `Dxt1a` | — | never exercised | **untested** |

Rejection paths are verified: a `SpriteFontReader` XNB and a file that isn't an XNB at all
both return `WINCODEC_ERR_BADHEADER`, so Explorer falls back to the generic icon.

Two gaps worth knowing about:

- **No real compressed `.xnb` has ever been decoded.** Both the LZX and LZ4 results above
  come from synthetic streams that exercise the framing and the wiring but never touch the
  Huffman path. The decoders themselves are MonoGame's and are battle-tested; the wiring
  around them is ours. Any Stardew Valley or Terraria `Content/` folder is a good corpus.
- The BC1/BC2/BC3 and `Bgra4444` results were produced on Linux during development and have
  not been re-run on Windows.

## Building

Requires the .NET 8 SDK. The first build downloads three files from MonoGame into
`src/XnbCore/Vendor/`, so it needs network access once; to build offline, drop those files
in by hand and the download is skipped.

```
dotnet publish src/Xnb2Png/Xnb2Png.csproj      -c Release
dotnet publish src/XnbThumbnail/XnbThumbnail.csproj -c Release
```

**Runtime requirement:** `xnb2png.exe` is self-contained and needs nothing installed. The
thumbnail handler **does** require the .NET 8 desktop runtime on the target machine — COM
hosting starts the runtime through `hostfxr_initialize_for_runtime_config`, which cannot
load a self-contained `runtimeconfig.json`. Both binaries must be x64; 64-bit Explorer will
silently never call a 32-bit build.

## Licensing

This project's own source is **MIT** (see [LICENSE](LICENSE)).

Every binary produced here also contains three files fetched from MonoGame `v3.8.5` at build
time, which are under the **Microsoft Public License (Ms-PL)** — LZX and LZ4 decompression is
not reimplemented here and should not be. Consequences worth knowing before you ship
anything:

- `LzxDecoder.cs` is dual-licensed LGPL-2.1 **or** Ms-PL. **This project elects Ms-PL**,
  which avoids LGPL §6 relinking obligations on a statically-linked shell extension.
- Ms-PL requires that attribution notices survive — which is why `Vendor/` must never be
  hand-edited.
- Binary releases must carry [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and
  [licenses/MonoGame-v3.8.5-LICENSE.txt](licenses/MonoGame-v3.8.5-LICENSE.txt).
- **Ms-PL is not GPL-compatible**, so these binaries cannot be combined into a GPL work.

Because `Vendor/` is `.gitignore`d and fetched rather than committed, this *repository*
distributes no Ms-PL source — only the compiled artifacts do. Full detail in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
