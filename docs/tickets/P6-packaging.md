# Packaging: the first build that runs without the toolchain

The README said "the game is playable from source; there are no packaged builds
yet", which meant exactly one person on one machine could ever play it. This is
the packaging path, and the five real defects that had to be fixed to get one.

`tools/package.sh [macos|linux|windows|all]` produces a build plus its data
folder under `build/<platform>/`. All three platforms work.

## The first three defects, in the order they bit

**1. An exported build could never have found /data.** ADR-006 made /data the
runtime source, and `GameFiles.RepoRoot` resolved it as the parent of `res://`.
That is true from source (res:// is `game/`, so its parent is the repo root) and
false in a package, where `res://` lives inside the .app. Worse, the sim's
loaders take REAL OS PATHS (`CatalogueFiles.RegisterAll` walks directories,
`MapData.Load` opens a file) and files inside an exported .pck are not real OS
files, so /data cannot simply be imported into res:// either.

The fix is that /data ships as a LOOSE FOLDER beside the game, and `RepoRoot`
became an ordered search: the parent of res:// (source), res:// itself, the
executable's own directory (the shipped layout), and three levels up from the
executable (macOS, where the binary sits at `Game.app/Contents/MacOS/`). Resolved
once and cached. If nothing holds a /data it returns the first candidate
unchanged, so the existing readable failure still fires (the catrefuse gate pins
that the loader fails readably on a missing directory), with a warning naming
every path it searched. Shipping data loose also keeps it moddable.

`MainMenu` had open-coded the same res://-parent idiom for the map list instead
of using `GameFiles.RepoRoot`, so a packaged build would have shown an EMPTY
theatre picker even after the above. It now goes through RepoRoot.

**2. The project had no solution file, so the export packed no C# at all.**
Godot's .NET export refuses without one: "This project contains C# files but no
solution file was found at game/Ferrostorm.Game.sln". The first exports therefore
produced a bundle with ZERO managed assemblies, which built and signed happily
and then segfaulted the instant it booted. Fixed by adding
`game/Ferrostorm.Game.sln` covering the game project and the three sim projects.
With it, the export log gains the `dotnet_publish_project` step that was silently
missing before.

**3. Godot signs macOS bundles ad-hoc AND with the hardened runtime, which
macOS refuses to launch.** Codesign flags read `0x10002 (adhoc,runtime)`, and the
process was SIGKILLed at exec with no output whatsoever, which reads like a
corrupt build rather than a signing policy (`--version` died the same way, which
is what proved it was exec-level rather than a game bug). The script re-signs
plain ad-hoc afterwards, clearing the runtime flag, and the bundle launches.

The export also refuses for arm64 or universal unless ETC2 ASTC texture import is
enabled, so `project.godot` gains
`textures/vram_compression/import_etc2_astc=true`. That is an import-time texture
option: it changes the gitignored import cache and the exported package, never
the renderer's behaviour and never the sim.

## Using it

    tools/package.sh            # macOS release (the default)
    tools/package.sh all        # all three platforms
    tools/package.sh linux      # one platform
    tools/package.sh windows --debug

Requires the Godot 4.7 **mono** export templates, which are NOT installed by
default; they go in
`~/Library/Application Support/Godot/export_templates/4.7.stable.mono/` and are a
1.2 GB download from the Godot release page.

`export_presets.cfg` is gitignored by convention, because that is where a real
signing identity would end up. The committed `export_presets.template.cfg` is the
working default and the script seeds the ignored file from it on first run, so a
fresh clone can package without hand-authoring a preset.

## The load-bearing rule

**The .app and the data folder travel together.** Move the .app on its own and
the game starts and then refuses every match with a catalogue error, because
/data is gone. `package.sh` rsyncs data with `--delete` so a re-package never
leaves a stale map behind, which would make the packaged build disagree with the
repo.

## Two more defects, found only by exporting for real (2026-07-25)

The first packaging pass shipped a macOS build that LAUNCHED but could not run
the game, and said so as though it were finished. Adding the Linux and Windows
presets exposed both reasons, because comparing three packages made the hole
obvious.

**4. `dotnet/embed_build_outputs=false` shipped packages with NO managed code.**
Every package contained zero game assemblies. `dotnet_publish_project` reported
DONE and nothing arrived, so the .NET runtime had nothing to load. The macOS
build survived `--version` only because that is the engine binary answering
before any C# loads, which is precisely why `--version` was NOT sufficient
evidence and should not have been reported as if it were. Setting
`embed_build_outputs=true` fixes all three: Linux and Windows carry the
assemblies inside the .pck, macOS in
`Contents/Resources/data_Ferrostorm.Game_macos_arm64/` (346 files).

**5. `GlobalizePath("res://")` returns an EMPTY STRING in a packaged build**, and
`Path.GetFullPath("")` throws. The candidate search in `GameFiles` therefore
threw ArgumentException on its second candidate, before it ever reached the
executable-directory candidate a package actually needs, and the game died in
`MainMenu._Ready`. Guarded now. This bug was introduced by the very change that
made packaging possible and could ONLY be caught by running a real export, which
is the whole argument for doing so rather than reasoning about it.

The lesson worth keeping: a build that starts is not a build that runs, and on
macOS a release export prints nothing, so "it launched" is the weakest possible
evidence. The real signal is a headless run that exits 0 with no exception in the
log, which is what is now recorded below.

## What is proven, and what is not

Proven, on all three platforms: `tools/package.sh [macos|linux|windows|all]`
runs clean end to end from a wiped `build/`; every package carries its managed
assemblies; `/data` stages beside each with all four maps; the sim is untouched
(`git diff sim/ data/` empty), all 24 goldens are byte-identical and the full
battery exits 0.

Proven on macOS specifically, and this is the check that was missing before: the
packaged build runs headless to **exit 0 with ZERO exceptions in the log**.
Because `MainMenu._Ready` enumerates the map list through `GameFiles.RepoRoot`,
a clean boot is positive evidence that the package found its data folder. Before
the two fixes above the same run died at exec or threw in `_Ready`.

Binary shapes verified: macOS a universal .app signed `adhoc`; Linux an
`ELF 64-bit LSB x86-64`; Windows a `PE32+ GUI x86-64`. Linux and Windows carry
an identical 196 MB .pck, as they should.

NOT proven, and honestly owed to a human: that any package shows its menu on
screen and plays a match. A headless run cannot judge that, and I cannot see a
window. **Double-click `build/macos/Ferrostorm.app`.** Gatekeeper refuses an
ad-hoc bundle on first launch: right-click and Open, or clear it with
`xattr -dr com.apple.quarantine build/macos/Ferrostorm.app`. The Linux and
Windows builds are structurally verified only; nobody has run them on their own
operating system.

## Owed next

- The human launch check above, which is the only remaining verification on
  macOS, and a run on a real Linux box and a real Windows box for those two.
- Linux and Windows presets are DONE (2026-07-25). Neither has macOS's signing
  problem: the Linux binary just needs its executable bit, which the script
  sets, and Windows is unsigned by design here.
- A real Developer ID signature and notarisation before this is given to anyone
  else; the hardened runtime returns at that point, since notarisation requires
  it, and the re-sign step in the script must then be dropped rather than kept.
- An icon: `application/icon` is empty, so the .app carries Godot's default.
