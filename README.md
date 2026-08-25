# Brawlhalalla

A small tool that edits **text** inside Brawlhalla's game files: it removes Legend lore and renames
some Legends and maps. Everything else is left exactly as it was.

It is cosmetic and client-side only. It changes no gameplay values, no hitboxes, nothing that could
be an advantage.

> **Online play: use `--only languages`.**
> The full patch breaks online — Brawlhalla validates its archives and rejects a modified
> install. Running with `--only languages` removes all the lore and most of the renames
> without touching those archives, and online play worked in testing.
> See [Online play](#online-play).

**[⬇ Download the latest release](https://github.com/akhihaani/brawlhalalla/releases/latest)** —
Windows, Mac and Linux. No installation required.

---

## For people who just want to run it

1. Go to the **[latest release](https://github.com/akhihaani/brawlhalalla/releases/latest)** and
   download the file for your computer:

   | Your computer | File |
   |---|---|
   | Windows | `Brawlhalalla-Windows.zip` |
   | Mac (M1/M2/M3/M4) | `Brawlhalalla-Mac-AppleSilicon.zip` |
   | Mac (older, Intel) | `Brawlhalalla-Mac-Intel.zip` |
   | Linux | `Brawlhalalla-Linux.zip` |

2. Unzip it. Inside is the program, plus `brawlhalalla-config.json` if you want to change the names.
3. **Close Brawlhalla** if it is running.
4. Double-click the program. It finds your game, backs up the originals, makes the changes, and
   tells you what it did. Press Enter to close the window.

If it cannot find your game, drag your Brawlhalla folder onto the program instead. In Steam:
right-click Brawlhalla → Manage → Browse local files.

**On a Mac that folder looks empty** — you will only see `Brawlhalla.app`. That is normal: macOS
hides the contents. Right-click `Brawlhalla.app` → **Show Package Contents** → `Contents` →
`Resources`, and the game files are in there. You do not normally need to do this; the tool looks
inside the app bundle by itself.

### The one-time warning your computer will show

Because this program is not signed by a big company, each OS complains once:

- **Windows** — "Windows protected your PC" → click **More info** → **Run anyway**.
- **Mac** — right-click the file → **Open** → **Open**. If macOS still refuses, open Terminal and run:
  ```
  xattr -dr com.apple.quarantine ~/Downloads/Brawlhalalla-Mac-AppleSilicon.command
  ```
- **Linux** — mark it executable first:
  ```
  chmod +x Brawlhalalla-Linux && ./Brawlhalalla-Linux
  ```

### Run it again after every Brawlhalla update

Game updates replace the files this tool edits, so your changes will disappear. Just run it again —
it is safe to run as many times as you like, and it will tell you what was already done.

### If something goes wrong

Run it again with `--restore` and it puts the original files back from the `swz_backup` folder it
made inside your game directory (18 files: the four archives, the `.swf`, and all 13 language files). Failing that, Steam → right-click Brawlhalla → Properties →
Installed Files → **Verify integrity of game files**.

---

## What actually changes

| Legend | Becomes | | Map | Becomes |
|---|---|---|---|---|
| Thor | Tony | | Demon Island | Damon's Island |
| Loki | Larry | | Western Air Temple | Western Air Apartment |
| Artemis | Aaminah | | Spirit Realm Showdown | Kung-Fu Panda Showdown |
| Orion | 'Umar | | Lich's Tomb | Skeleton Tomb |
| Cross | Hologram Man | | | |
| Imugi | Big Turtle | | | |
| Munin | Raven | | | |

Every Legend's lore — the bio, quote, attribution and "also known as" text — is emptied, for all 71
Legends across all 13 languages the game ships. Display names stay, so the character select screen
still works normally.

Map name variants are included too, so `Demon Island CTF` becomes `Damon's Island CTF` rather than
being left half-renamed.

Legends are also renamed inside the ~2,100 cosmetic labels named after them — `Thor Winter Holiday`
becomes `Tony Winter Holiday`, in every language. Matching is on whole words only, so Brawlhalla's
`Thorn Queen`, `Repeating Crossbows`, `Lacrosse Check` and `Star-Crossed Nightmare` are left alone.
Turn it off with `advanced.renameInCosmeticNames`.

Names follow the casing the game already uses. Brawlhalla stores a Legend's name twice — shouted on
the roster (`THOR`) and normal on the bio screen (`Thor`) — so `"Thor": "Tony"` becomes `TONY` on
the roster and `Tony` on the bio screen from that one line. Set `advanced.matchStoredCasing` to
`false` if you would rather your spelling be used exactly as written everywhere.

To change any of this, edit `brawlhalalla-config.json` next to the program. Delete that file and the
program falls back to identical built-in defaults.

---

## Online play

**Confirmed by testing:** with the **full** patch applied, Brawlhalla reports "you are on an old
version" and refuses online play. Verifying the game files through Steam restores online
immediately; re-applying the full patch breaks it again. It reproduces every time.

With **`--only languages`**, online play worked.

This is Brawlhalla doing its job. It is a lockstep fighting game — every player must simulate
identical frames — so the client's data files have to match what the server expects. Modified
archives fail that check.

**This tool will not try to defeat that check.** No patching the validation out of the game, no
faking the values it reports. That is circumventing anti-cheat and a good way to lose an account.

### The online-safe mode

The lore is **not in the archives**. It lives in `languages/language.N.bin`, which are separate
files holding pure display text that cannot affect the simulation — and in testing they are not
validated.

```
Brawlhalalla --only languages
```

That gives you:

| | `--only languages` | Full patch |
|---|---|---|
| Legend lore removed | ✅ all of it, 13 languages | ✅ |
| Legend names in store / costume text | ✅ | ✅ |
| Skin, colour and avatar labels renamed | ✅ ~2,100 of them | ✅ |
| Legend names on the roster and bio screen | ❌ | ✅ |
| Map names | ❌ | ✅ |
| **Online play** | ✅ worked in testing | ❌ "old version" |

So the main feature — removing every Legend's lore — works online, and so does renaming the Legend
almost everywhere else their name is shown. Only the roster/bio name field and the map names live
inside the archives, and touching those is what breaks online.

**Every archive is checked.** `--only languages,Init.swz` (map names only) was tested and also
breaks online, so there is no partial archive patch that survives. It is language files, or offline.

This has been tested on one install and one game version. If online behaves differently for you,
please open an issue and say which combination you used.

### Either way

Modifying game files may breach Brawlhalla's terms of service regardless of whether it works.
That risk is yours to weigh.

## Before the first run on a new game version (recommended)

The risky part of a tool like this is not the text editing — it is re-encrypting the archives
afterwards. You can prove that works on your own install, without changing a single word of text:

```
Brawlhalalla --verify-codec
```

This decrypts and re-encrypts your archives with **no edits at all**, then asks you to launch
Brawlhalla. If the game starts normally, the encryption is proven on your version and it is safe to
run the real patch. If it does not, run `--restore` and stop.

This is worth doing once after a big game update. It is not required, but it turns "hope this works"
into "this is known to work".

---

## Command line options

```
Brawlhalalla [install-folder] [options]

  --dry-run          Apply and verify every edit in memory, write nothing.
  --verify-codec     Re-encrypt with no text changes, to prove the codec works (see above).
  --dump [folder]    Decrypt all four archives to a folder and exit.
  --only <targets>   Patch only these, comma separated: languages, Init.swz, Game.swz,
                     Dynamic.swz, Engine.swz. See "Online play" above.
  --restore          Put the originals back from swz_backup/ and exit.
  --config <file>    Use a specific config file.
  --key <number>     Supply the archive key manually instead of reading it from the .swf.
  --no-pause         Don't wait for Enter at the end. For scripts.
  -h, --help         Show the options.
```

The install folder is found in this order: the argument you pass → the `BH_DIR` environment
variable → a scan of the usual Steam and Ubisoft locations for your OS.

---

## How it works, and why it is built this way

Brawlhalla keeps its data in four encrypted archives — `Init.swz`, `Game.swz`, `Dynamic.swz`,
`Engine.swz`. Each holds plain XML and CSV entries, zlib-compressed and XOR-encrypted with a stream
from a WELL512 generator. The 32-bit key lives inside `BrawlhallaAir.swf` and changes on most
patches, so it is read fresh from the game's ActionScript bytecode every run.

The text is split across two very different places, verified against a live install:

| What | Where |
|---|---|
| Legend names | `HeroTypes.xml` in `Game.swz` — `HeroDisplayName` (stored uppercase, `THOR`) and `BioName` (`Thor`) |
| Map names | `LevelTypes.xml` in `Init.swz` — `DisplayName` |
| Legend lore | **not in the archives at all** — `languages/language.N.bin`, 13 files, keyed `HeroType_Thor_BioText` and similar |

The archives only store *lookup keys* for lore; the sentences live in the language files, which are
a separate format (a length header, a zlib stream, then length-prefixed key/value pairs). Blanking
the keys would break the lookup, so the tool empties the values in the language files instead.

Three things make this safer than a find-and-replace:

**Edits are anchored to specific XML elements, not to text.** Renaming `Cross` matches the complete
value of a display-name element. It cannot touch `crossover`, `across`, or a costume asset key that
merely contains the word.

**Internal identifiers are protected.** In `LegendTypes.xml`, Thor's internal `<LegendName>` and his
shown `<BioName>` both contain the text "Thor" — but only one of them is safe to change. Rewriting
the internal one breaks the character. Those elements are on a protected list and are never written
to, even when their value matches.

**Nothing is written until it has been read back.** Every re-encrypted archive is decrypted again in
memory and compared entry-by-entry against what was intended before it reaches disk, and each edited
XML file is re-parsed with the same Haxe parser Brawlhalla itself uses, then structurally compared
against the original — same elements, same attributes, same order, differing only in the exact text
values that were meant to change. A mismatch aborts the run instead of writing.

Matching is also deliberately forgiving in the find direction, because a missed match changes nothing
*silently*, which is the worst outcome. A config entry of `Lich's Tomb` matches a stored `Lich’s
Tomb` (curly apostrophe), `Munin` matches a stored `Múnin`, and casing does not matter. The
replacement is always written exactly as configured. Anything that does not match is reported as a
loud `MISS` at the end, never passed over quietly.

Backups are taken before the first write and never overwritten by a later run, so repeatedly running
the tool cannot destroy your originals. Two cases get special handling, both learned the hard way:

- **After a game update**, a backup holds files from the *previous* version. Restoring it would
  genuinely downgrade the install. The tool compares the backed-up `BrawlhallaAir.swf` — which it
  reads but never writes — against the installed one; a difference means the game was patched.
  `--restore` then refuses and points at Steam's file verification, and a patch run retires the old
  backup to `swz_backup-old-<timestamp>/` before taking a fresh one.
- **If the files are already modified and no usable backup exists**, the tool refuses to run at all,
  rather than saving the modified state as though it were the originals and leaving no way back.

Only CSV files that look like localized string tables are edited. Any other CSV that happens to
contain a matching cell is reported to you and left alone, so a data table that uses a legend name
as a code reference cannot be damaged.

---

## Building from source

Requires the .NET 10 SDK.

```
dotnet run --project tests/Brawlhalalla.SelfTest   # 23 self-tests, no game needed
dotnet run --project src/Brawlhalalla -- <install-path>
./publish.sh                                       # all four platform binaries into dist/
```

The self-tests build real encrypted archives and real language files in memory and check the
round-trips, the internal-ID protection, the `Cross` substring case, apostrophe and accent matching,
CSV key-column safety, rejection of corrupt language files, and that re-running reports "already
done" rather than "not found".

You can also exercise the whole flow without owning the game, using a throwaway install folder of
genuinely encrypted archives:

```
dotnet run --project tests/Brawlhalalla.SelfTest -- --make-fixture /tmp/fake-install 305419896
dotnet run --project src/Brawlhalalla -- /tmp/fake-install --key 305419896
```

### Repository layout

```
src/Brawlhalalla/      the tool
  Swz.cs               archive codec facade: find key, read, write, verify
  LanguageFile.cs      languages/language.N.bin codec, where the lore text lives
  XmlEdit.cs           element-anchored XML rewriting + structural validation
  CsvEdit.cs           whole-cell CSV rewriting
  Passes.cs            lore strip, legend rename, map rename
  Install.cs           finding the game, backup, restore
  Program.cs           command line and reporting
tests/                 self-tests and the fixture generator
vendor/                the two MIT libraries doing the crypto and bytecode parsing
```

---

## Credits

The hard parts are other people's work, vendored under MIT with licenses intact — see
[vendor/README.md](vendor/README.md):

- [BrawlhallaSwz](https://github.com/allhailcheese/BrawlhallaSwz) by AllHailCheese — the SWZ codec,
  and the port of Brawlhalla's Haxe XML parser.
- [AbcDisassembler](https://github.com/moffel1020/AbcDisassembler) by moffel1020 — ActionScript
  bytecode parsing, used to recover the key.
- Key-extraction logic adapted from [BrawlhallaSwz.CLI](https://github.com/moffel1020/BrawlhallaSwz.CLI).

## Licence

MIT — see [LICENSE](LICENSE).

Not affiliated with, endorsed by, or connected to Blue Mammoth Games or Ubisoft. Brawlhalla is their
trademark. Use it on your own copy, at your own risk.
