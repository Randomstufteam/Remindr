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


## Unicode CLDR plural rules (language plural categories)

`tools/cldr/plurals.xml` is an unmodified copy of `common/supplemental/plurals.xml` from Unicode CLDR 46.1. `tools/gen-plurals.ps1` turns it into `src/Remindr/Services/PluralRules.Generated.cs`. CLDR data is Copyright (c) 1991-2024 Unicode, Inc. and is distributed under the Unicode License v3 (SPDX: Unicode-3.0, https://www.unicode.org/license.html).
