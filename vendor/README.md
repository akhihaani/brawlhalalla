# Vendored dependencies

Neither library is published on NuGet, so both are vendored here from source. Both are MIT
licensed and their `LICENSE` files are kept alongside the code.

| Directory | Upstream | Commit | License |
|---|---|---|---|
| `BrawlhallaSwz/` | https://github.com/allhailcheese/BrawlhallaSwz | `c8d4c85d4293be8de908e8234282c5cc786e8ed9` | MIT © AllHailCheese |
| `AbcDisassembler/` | https://github.com/moffel1020/AbcDisassembler | `43563abf4eae4bb7772537de5d4a9f0657582408` | MIT © moffel1020 |

`BrawlhallaSwz` provides the SWZ codec (WELL512-driven XOR cipher + zlib entries) and, importantly,
`BrawlhallaSwz.Xml.BhXmlParser` — a port of the Haxe XML parser that Brawlhalla itself uses to read
these files. Brawlhalalla validates every edit against that parser, so "does the game accept this
XML" is checked with the game's own parsing rules rather than .NET's stricter one.

`AbcDisassembler` parses the ActionScript bytecode inside `BrawlhallaAir.swf`, which is how the
decryption key is recovered automatically after each patch.

## Local modifications

- `BrawlhallaSwz/BrawlhallaSwz/Sample.cs` deleted (it was excluded from compilation upstream anyway).
- Upstream unit tests and sample projects not copied.
- No source changes. Both projects still target `net8.0`; the app targets `net10.0` and references
  them directly, which is a supported combination.

The key-extraction logic in `src/Brawlhalalla/Swz.cs` (`FindKey`) is adapted from
`moffel1020/BrawlhallaSwz.CLI` (`Utils.cs`), MIT © moffel1020.
