# Third-party notices

`src/XnbCore/Vendor/` is **not** part of this project's source. Those files are downloaded
verbatim at build time from the MonoGame repository, pinned to tag `v3.8.5` (see
`MonoGameRef` in `src/XnbCore/XnbCore.csproj`), and are compiled in unmodified. They are
`.gitignore`d, so this repository does not redistribute them in source form — but the
compiled binaries do contain them, and the notices below travel with those binaries.

| File | Upstream path | Licence |
|---|---|---|
| `LzxDecoder.cs` | `MonoGame.Framework/Content/LzxDecoder.cs` | Dual **LGPL-2.1 or Ms-PL**, at the recipient's election. **This project elects Ms-PL** (see below). Derived from Stuart Caie's libmspack (`lzxd.c`), © 2003–2004 Stuart Caie, © 2011 Ali Scissons. |
| `LzxDecoderStream.cs` | `MonoGame.Framework/Utilities/LzxStream/LzxDecoderStream.cs` | **Ms-PL**, © MonoGame Foundation, Inc. |
| `Lz4DecoderStream.cs` | `MonoGame.Framework/Utilities/Lz4Stream/Lz4DecoderStream.cs` | **Ms-PL**, © MonoGame Foundation, Inc. |

The full text as published at that tag is kept in
[`licenses/MonoGame-v3.8.5-LICENSE.txt`](licenses/MonoGame-v3.8.5-LICENSE.txt).

The LZX method was created by Jonathan Forbes and Tomi Poutanen, adapted by Microsoft
Corporation.

## The Ms-PL election for `LzxDecoder.cs`

`LzxDecoder.cs` is offered under *either* LGPL-2.1 *or* Ms-PL, and the recipient chooses.
**This project chooses Ms-PL, and that choice is deliberate.** Taking it under LGPL-2.1
instead would attach LGPL §6 obligations to a statically-linked shell extension — you would
have to ship object code or otherwise enable relinking for every binary release. Ms-PL
imposes no such duty. Do not remove or muddy this election.

The dual grant is legitimate: the file's own header records that the port "is recognized by
Stuart Caie and is authorized to adapt any changes made to lzxd.c … and will still retain
this dual licensing scheme."

## What Ms-PL requires of anyone shipping this

- **§3(C)** — retain all copyright, patent, trademark and attribution notices. This is why
  `Vendor/` must never be hand-edited: the `LzxDecoder.cs` header *is* the notice.
- **§3(D)** — any portion redistributed *in source form* must go out under Ms-PL with a
  complete copy of the licence. This repository sidesteps that entirely by fetching
  `Vendor/` at build time rather than committing it.
- **§3(D)** — any portion redistributed *in compiled form* must go out under a licence that
  complies with Ms-PL. Ship this file and `licenses/MonoGame-v3.8.5-LICENSE.txt` alongside
  binary releases.
- **§3(A)** — no trademark licence. "MonoGame", "XNA" and "Xbox" are the marks of the
  MonoGame Foundation and Microsoft respectively; use them descriptively ("reads XNA `.xnb`
  files"), never as branding.

**Ms-PL is not GPL-compatible.** Binaries built from this project therefore cannot be
combined into a GPL-licensed work. If that ever matters, the LZX path would have to be
replaced or made a separately-distributed plug-in.

## .NET runtime

`xnb2png.exe` is published self-contained and so embeds parts of the .NET runtime and
libraries, © Microsoft Corporation, licensed under the MIT License
(<https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>). Redistribution in this form is
expressly permitted. `XnbThumbnail.comhost.dll` is framework-dependent and embeds nothing.

## Test assets

MonoGame's `Tests/Assets/Textures/*.xnb` are used during development and are **not**
redistributed here. Assets extracted from commercial games (Stardew Valley, Terraria, …) are
useful for testing the LZX path but must never be committed to this repository.
