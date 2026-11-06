# Third-party notices

Remindr bundles the following third-party material.

## Fluent UI Emoji (profile and reminder pictures)

The PNG files in `src/Remindr/Assets/Pictures` (3D style) are from Microsoft's Fluent UI Emoji set. The same text is kept next to the images in `LICENSE-fluentui-emoji.txt`.

```text
MIT License

    Copyright (c) Microsoft Corporation.

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE
```

## Libraries

Remindr is built on these open-source packages. Each is used under its own license, linked below.

| Package | License |
|---|---|
| [WPF-UI](https://github.com/lepoco/wpfui) | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT |
| [Dapper](https://github.com/DapperLib/Dapper) | Apache-2.0 |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) and [SQLite](https://sqlite.org/copyright.html) | MIT / public domain |
| [Microsoft.Extensions.Hosting](https://github.com/dotnet/runtime) | MIT |
| [.NET runtime](https://github.com/dotnet/runtime) (bundled in the installer) | MIT |
| [LLamaSharp](https://github.com/SciSharp/LLamaSharp) (with its CPU and Vulkan backend packages) | MIT |
| [llama.cpp / ggml](https://github.com/ggml-org/llama.cpp) (the native files inside the LLamaSharp backends, and the `llama-server` build b11960 the voice downloads) | MIT |
| [NAudio](https://github.com/naudio/NAudio) (microphone capture and audio playback) | MIT |


## Unicode CLDR plural rules (language plural categories)

`tools/cldr/plurals.xml` is an unmodified copy of `common/supplemental/plurals.xml` from Unicode CLDR 46.1. `tools/gen-plurals.ps1` turns it into `src/Remindr/Services/PluralRules.Generated.cs`. CLDR data is Copyright (c) 1991-2024 Unicode, Inc. and is distributed under the Unicode License v3 (SPDX: Unicode-3.0, https://www.unicode.org/license.html).


## LFM2.5-1.2B-Instruct (optional AI assistant model, downloaded on request)

The AI assistant is off by default. When it is turned on it uses a language model that is **not** part of the installer: it is downloaded to your PC (or already there) and runs fully offline.

- Model: LFM2.5-1.2B-Instruct (GGUF files), Hugging Face repository `LiquidAI/LFM2.5-1.2B-Instruct-GGUF`.
- License: LFM Open License v1.0, Copyright Liquid AI, Inc. The full text is in `LICENSE-LFM-Open-License-v1.0.txt`, installed next to this file.
- In plain words: it is free to use, including in a product, as long as the yearly revenue of whoever uses or distributes it stays under US$10 million (Section 5 of the license); qualifying non-profits are exempt for non-commercial and research use. Above that limit a separate license from Liquid AI is needed. Read the license for the exact terms.
- "Liquid AI" and "LFM" name where the model comes from. They are not Remindr's branding.


## LFM3-Audio-2B (optional voice model, downloaded on request)

Voice is off by default. When it is turned on it uses a speech model that hears you and reads replies aloud. It is **not** part of the installer: it is downloaded to your PC and runs fully offline. Audio is processed in memory and never saved. Until it is downloaded, spoken replies use a Windows voice.

- Model: LFM3-Audio-2B (`LFM3-Audio-2B-Q4_K_M.gguf` and its audio projector `mmproj-LFM3-Audio-2B-Q8_0.gguf`), Hugging Face repository `LiquidAI/LFM3-Audio-2B-GGUF`.
- License: LFM Open License v1.0, Copyright Liquid AI, Inc., the same license and US$10 million revenue limit as the assistant model above. The full text is in `LICENSE-LFM-Open-License-v1.0.txt`.
- It runs in llama.cpp's `llama-server` (Windows x64 Vulkan build b11960, MIT, from https://github.com/ggml-org/llama.cpp/releases/tag/b11960), downloaded with the model and started only on this PC, at 127.0.0.1.
