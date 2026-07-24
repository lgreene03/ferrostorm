# Packaging: the first build that runs without the toolchain

The README said "the game is playable from source; there are no packaged builds
yet", which meant exactly one person on one machine could ever play it. This is
the packaging path, and the three real defects that had to be fixed to get one.

`tools/package.sh` produces `build/macos/Ferrostorm.app` plus `build/macos/data`.

## The three defects, in the order they bit

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

    tools/package.sh            # release
    tools/package.sh --debug    # debug template, for a console

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

## What is proven, and what is not

Proven: the script runs clean end to end from a wiped `build/`; the bundle is a
425 MB universal .app signed `adhoc` only; it LAUNCHES (`--version` exits 0 and
prints the engine version, where before it was SIGKILLed); data stages beside it
with all four maps; the sim is untouched (`git diff sim/ data/` empty), all 24
goldens are byte-identical and the full battery exits 0.

NOT proven, and honestly owed to a human: that the packaged game actually shows
its menu and plays a match. An exported release build writes nothing to stdout on
macOS, so a headless run cannot report success or failure, and I cannot see a
window. **Double-click `build/macos/Ferrostorm.app`.** Gatekeeper will refuse an
ad-hoc bundle on first launch: right-click and Open, or clear it with
`xattr -dr com.apple.quarantine build/macos/Ferrostorm.app`.

## Owed next

- The human launch check above, which is the only remaining verification.
- A Linux and a Windows preset. The templates for both are already installed with
  the mono template set, and neither has macOS's signing problem, so they are
  cheap; they simply need presets and a cross-check that the data folder lands
  beside the binary.
- A real Developer ID signature and notarisation before this is given to anyone
  else; the hardened runtime returns at that point, since notarisation requires
  it, and the re-sign step in the script must then be dropped rather than kept.
- An icon: `application/icon` is empty, so the .app carries Godot's default.
