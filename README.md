# Brawlhalalla

A small tool that edits **text** inside Brawlhalla: it removes every Legend's lore and renames some
Legends and maps. Nothing else is touched — no gameplay values, no hitboxes, nothing that could be
an advantage.

**[⬇ Download the latest release](https://github.com/akhihaani/brawlhalalla/releases/latest)** —
Windows, Mac and Linux. Nothing to install.

---

## Choose how you want to run it

This is the one thing worth reading before you start.

| | What you get | Online play |
|---|---|---|
| **`--only languages`** | All lore removed. Legends renamed in the store, costumes, and ~2,200 skin/colour/avatar labels. | ✅ **works** |
| **`--only languages --strip-glyphs I,O`** | The above, plus roster and map names can no longer be spelled. Letters vanish from menus too. | ✅ **works** |
| **Just double-click it** | Everything, including proper roster names and map names. | ❌ **broken** |

Brawlhalla checks its data files. Change them and it refuses to let you online, saying you are on an
old version. The language files are *not* checked, which is why the first two modes work.

**If you want to play with friends online, use `--only languages`.** Double-clicking gives you the
full patch, which is for offline and local play only.

---

## Running it

1. Download the file for your computer from the [latest release](https://github.com/akhihaani/brawlhalalla/releases/latest):

   | Your computer | File |
   |---|---|
   | Windows | `Brawlhalalla-Windows.zip` |
   | Mac (M1/M2/M3/M4) | `Brawlhalalla-Mac-AppleSilicon.zip` |
   | Mac (older, Intel) | `Brawlhalalla-Mac-Intel.zip` |
   | Linux | `Brawlhalalla-Linux.zip` |

2. Unzip it. Inside is the program plus `brawlhalalla-config.json`, if you want to change the names.
3. **Close Brawlhalla.**
4. Run it. It finds your game, backs up the originals, makes the changes, and prints what it did.

Double-clicking applies the full offline patch. For the online-safe version you need to type a
command — on Mac, open Terminal and run:

```
cd ~/Downloads/mac-apple-silicon
./Brawlhalalla-Mac-AppleSilicon.command --only languages
```

### Your computer will warn you once

The program isn't signed by a big company, so each OS complains the first time:

- **Windows** — "Windows protected your PC" → **More info** → **Run anyway**
- **Mac** — right-click the file → **Open** → **Open**. If macOS still refuses:
  `xattr -dr com.apple.quarantine Brawlhalalla-Mac-AppleSilicon.command`
- **Linux** — `chmod +x Brawlhalalla-Linux && ./Brawlhalalla-Linux`

### Can't find your game?

Drag your Brawlhalla folder onto the program. In Steam: right-click Brawlhalla → Manage → Browse
local files.

**On a Mac that folder looks empty** — you only see `Brawlhalla.app`. That's normal; macOS hides the
contents. Right-click it → **Show Package Contents** → `Contents` → `Resources`. You don't normally
need to do this — the tool looks inside the app by itself.

### Run it again after every Brawlhalla update

Updates replace the files this tool edits, so your changes disappear. Just run it again. It's safe to
run as often as you like and will tell you what was already done.

---

## What changes

| Legend | Becomes | | Map | Becomes |
|---|---|---|---|---|
| Thor | Tony | | Demon Island | Damon's Island |
| Loki | Larry | | Western Air Temple | Western Air Apartment |
| Artemis | Aaminah | | Spirit Realm Showdown | Kung-Fu Panda Showdown |
| Orion | 'Umar | | Lich's Tomb | Skeleton Tomb |
| Cross | Hologram Man | | | |
| Imugi | Big Turtle | | | |
| Munin | Raven | | | |

**Lore** — every Legend's bio, quote, attribution and "also known as" text is emptied. That's 5,460
entries across all 13 languages the game ships. Names stay, so character select still works.

**Cosmetic labels** — Legends are renamed inside the ~2,200 skins, colours and avatars named after
them, so `Thor Winter Holiday` becomes `Tony Winter Holiday`, in every language including Chinese
and Korean. Whole words only: Brawlhalla's `Thorn Queen`, `Repeating Crossbows`, `Lacrosse Check`
and `Star-Crossed Nightmare` are left alone.

**Map variants** are included, so `Demon Island CTF` becomes `Damon's Island CTF` rather than being
left half-renamed.

**Casing follows the game.** A Legend's name is stored twice — shouted on the roster (`THOR`) and
normal on the bio screen (`Thor`) — so one config line, `"Thor": "Tony"`, produces `TONY` and `Tony`
in the right places.

To change any of this, edit `brawlhalalla-config.json` next to the program. Delete it and the
built-in defaults are used instead.

---

## Online play

**Tested and confirmed:** the full patch makes Brawlhalla report "you are on an old version" and
refuse online play. Verifying your files through Steam fixes it; re-applying the full patch breaks it
again, every time.

With `--only languages`, online play worked.

This is Brawlhalla doing its job. It's a lockstep fighting game — every player simulates identical
frames — so the data files have to match what the server expects.

**This tool will not try to defeat that check.** No patching the validation out of the game, no
faking what it reports. That's circumventing anti-cheat and a good way to lose an account.

### What's safe and what isn't

The lore isn't in the checked archives at all — it lives in `languages/language.N.bin`, which hold
pure display text and aren't validated. The Legend roster name and map names *are* in the archives,
which is the whole limitation.

**Every archive is checked.** `--only languages,Init.swz` (map names only) was tested and also breaks
online, so there's no partial archive patch that survives. It's language files, or offline.

Tested on one install and one game version. If online behaves differently for you, please open an
issue and say which combination you used.

Modifying game files may breach Brawlhalla's terms of service regardless of whether it works. That
risk is yours to weigh.

---

## Blanking letters (`--strip-glyphs`)

The roster and map names can't be renamed online — but the **fonts** can be edited, because they sit
outside the checked archives.

```
Brawlhalalla --only languages --strip-glyphs I,O
```

This blanks `I` and `O` so those names can't be written: `ORION` → `RN`, `THOR` → `THR`,
`LOKI` → `LK`.

**Read this before using it.** A font is global, so those letters vanish from everything:

| Was | Becomes |
|---|---|
| `Options` | `ptns` |
| `Inventory` | `nventry` |
| `SPECIAL OFFER!` | `SPECAL FFER!` |

Roughly 17% of the game's text is affected blanking capitals only, ~82% blanking both cases. Menus
stay usable — buttons don't move — but they read badly.

It's also not a clean removal. `THR` and `CRSS` are still recognisable, and map names stored in
Title Case (`Western Air Temple`, `Lich's Tomb`) have no capital `I` or `O`, so they're unaffected.
`ORION` is why common letters are unavoidable — its letters are O, R, I, N, and there's no rare one
that breaks it.

**To limit the damage**, restrict it to the font the roster name uses, leaving body text and
descriptions readable:

```json
"advanced": { "glyphStripFonts": [ "BMG Bespoke Sans Extrabold" ] }
```

---

## If something goes wrong

```
Brawlhalalla --restore
```

Puts everything back from the `swz_backup` folder inside your game directory — 24 files: the four
archives, the `.swf`, 13 language files and 6 fonts.

If that doesn't help: Steam → right-click Brawlhalla → Properties → Installed Files →
**Verify integrity of game files**. That always works, and it's the right fix if the backup is ever
refused.

### Worth doing once after a big game update

```
Brawlhalalla --verify-codec
```

This re-encrypts your archives with **no text changes at all**, then asks you to launch the game. If
it starts normally, the risky machinery is proven on your version. If it doesn't, run `--restore`
and stop. Not required, but it turns "hope this works" into "this is known to work".

---

## Command line options

```
Brawlhalalla [install-folder] [options]

  --only <targets>   Patch only these, comma separated: languages, Init.swz, Game.swz,
                     Dynamic.swz, Engine.swz. Use "languages" for online-safe.
  --strip-glyphs <letters>
                     Blank these letters in the game's fonts, e.g. I,O. Works online,
                     but affects menus too.
  --dry-run          Apply and verify every edit in memory, write nothing.
  --restore          Put the originals back and exit.
  --verify-codec     Re-encrypt with no text changes, to prove the codec works.
  --dump [folder]    Decrypt everything to a folder and exit.
  --config <file>    Use a specific config file.
  --key <number>     Supply the archive key manually instead of reading it from the .swf.
  --no-pause         Don't wait for Enter at the end. For scripts.
  -h, --help         Show the options.
```

The install folder is found in this order: the argument you pass → the `BH_DIR` environment
variable → a scan of the usual Steam and Ubisoft locations for your OS.

---

## How it works

Brawlhalla keeps its data in four encrypted archives — `Init.swz`, `Game.swz`, `Dynamic.swz`,
`Engine.swz` — holding XML and CSV entries, zlib-compressed and XOR-encrypted with a WELL512 stream.
The 32-bit key lives inside `BrawlhallaAir.swf` and changes on most patches, so it's read fresh from
the game's ActionScript bytecode every run.

The text lives in three places, all verified against a real install:

| What | Where |
|---|---|
| Legend names | `HeroTypes.xml` in `Game.swz` — `HeroDisplayName` (`THOR`) and `BioName` (`Thor`) |
| Map names | `LevelTypes.xml` in `Init.swz` — `DisplayName` |
| Legend lore, store and cosmetic names | **not in the archives** — `languages/language.N.bin`, 13 files |

The archives only hold *lookup keys* for lore, like `HeroType_Thor_BioText`; the sentences live in
the language files, which use their own format (a length header, a zlib stream, then length-prefixed
key/value pairs). Blanking a key would break the lookup, so the tool empties the values instead.

Four things make this safer than find-and-replace:

**Edits are anchored to specific elements, not to text.** Renaming `Cross` matches a complete
display-name value. It can't touch `crossover`, `across`, or an asset key containing the word.

**Internal identifiers are never written to.** A Legend's internal name has no relation to their
displayed one — Cross is `Mobster`, Artemis is `Spacehunter`, Orion is `Valkyrie`, Munin is
`BirdBard`. Those live in an XML *attribute* the tool never edits, and asset, sound and reward fields
are on an explicit protected list.

**Nothing is written until it's been read back.** Every re-encrypted archive is decrypted again in
memory and compared entry by entry. Every edited XML file is re-parsed with the same Haxe parser
Brawlhalla itself uses and structurally compared against the original — same elements, attributes and
order, differing only where intended. Every edited font is re-parsed to confirm only the requested
letters changed. Any mismatch aborts before writing.

**Matching is forgiving in the find direction**, because a missed match changes nothing *silently*,
which is the worst outcome. `Lich's Tomb` matches a stored `Lich’s Tomb` (curly apostrophe), `Munin`
matches `Múnin`, casing doesn't matter, and CJK labels like `灰燼警衛Thor` are handled. Replacements
are written exactly as configured, and anything unmatched is reported as a loud `MISS`.

### Backups

Taken before the first write and never overwritten by a later run. Two cases get special handling,
both learned the hard way:

- **After a game update**, a backup holds the *previous* version's files, and restoring it would
  genuinely downgrade your install. The tool compares the backed-up `BrawlhallaAir.swf` — which it
  reads but never writes — against the installed one. If they differ, `--restore` refuses and points
  at Steam's verification, and a patch run retires the old backup to `swz_backup-old-<timestamp>/`.
- **If the files are already modified and no usable backup exists**, the tool refuses to run rather
  than saving the modified state as though it were the originals.

Only CSVs that look like localized string tables are edited. Any other CSV containing a matching cell
is reported and left alone, so a data table using a Legend name as a code reference can't be damaged.

---

## Building from source

Requires the .NET 10 SDK.

```
dotnet run --project tests/Brawlhalalla.SelfTest   # 40 self-tests, no game needed
dotnet run --project src/Brawlhalalla -- <install-path>
./publish.sh                                       # all four platform binaries into dist/
```

The self-tests build real encrypted archives, language files and fonts in memory and check the
round-trips, internal-ID protection, the `Cross` substring case, apostrophe/accent/CJK matching, CSV
key-column safety, font editing, backup staleness, and that re-running reports "already done" rather
than "not found". `publish.sh` refuses to build if any of them fail.

You can exercise the whole flow without owning the game:

```
dotnet run --project tests/Brawlhalalla.SelfTest -- --make-fixture /tmp/fake-install 305419896
dotnet run --project src/Brawlhalalla -- /tmp/fake-install --key 305419896
```

### Repository layout

```
src/Brawlhalalla/
  Swz.cs               archive codec facade: find key, read, write, verify
  LanguageFile.cs      languages/language.N.bin codec — where the lore lives
  FontFile.cs          embedded-font editing for --strip-glyphs
  XmlEdit.cs           element-anchored XML rewriting + structural validation
  CsvEdit.cs           whole-cell CSV rewriting
  Passes.cs            lore strip, legend rename, map rename
  Install.cs           finding the game, backup, restore
  Program.cs           command line and reporting
tests/                 self-tests and the fixture generator
vendor/                two MIT libraries doing the crypto and bytecode parsing
```

---

## Credits

The hard parts are other people's work, vendored under MIT with licences intact — see
[vendor/README.md](vendor/README.md):

- [BrawlhallaSwz](https://github.com/allhailcheese/BrawlhallaSwz) by AllHailCheese — the SWZ codec,
  and the port of Brawlhalla's Haxe XML parser.
- [AbcDisassembler](https://github.com/moffel1020/AbcDisassembler) by moffel1020 — ActionScript
  bytecode parsing, used to recover the key.
- Key extraction adapted from [BrawlhallaSwz.CLI](https://github.com/moffel1020/BrawlhallaSwz.CLI).

## Licence

MIT — see [LICENSE](LICENSE).

Not affiliated with, endorsed by, or connected to Blue Mammoth Games or Ubisoft. Brawlhalla is their
trademark. Use it on your own copy, at your own risk.
