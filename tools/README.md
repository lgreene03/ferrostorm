# /tools

Development tooling. Engine-free, like everything outside `/game` (ADR-004): CI
greps for `using Godot` here and fails on it.

- **`verify-client.sh`** - the headless client harness. Boots the real battle
  scene from the joiner's seat, asserts, exits nonzero on failure. Run it for
  any `/game` change; CI runs it too. On a fresh clone it does one Godot asset
  import pass first, because `.godot/` is gitignored and a project with no
  imported assets does not fail loudly - the models are simply absent and every
  check that reads the view fails against an empty-looking scene.
- **`package.sh [macos|linux|windows|all]`** - packaged builds. Seeds
  `export_presets.cfg` from a committed template, re-signs macOS ad-hoc, and
  rsyncs `/data` beside the binary (the sim loads real OS paths, so `/data`
  cannot live inside the .pck).
- **`mapgen.py`** and `gen_skirmish_0*.py` - map generation and validation. The
  fairness invariants (180-degree rotation symmetry, reachability, Chebyshev
  distance profiles) are checked here, not trusted. See docs/design/26-map-design.md.
- **`Ferrostorm.Balance/ [full|quick] [--report PATH]`** - the balance
  simulator (doc 12, rebuilt by P8-14): the engagement matrix at four budgets,
  the sieges, and the faction war, all on /data. CI runs `full`, the default.
  It fails the build only on a failed self-check (fielded credits, mirror
  annihilation, reproducible tempo) or one of the gates it has always carried
  (the DEF-17 siege, the wall's hp per credit, the tempo floor); F10 and F11
  are reported until P8-32 and P8-33 flip their switches. A `--report` path
  inside the repository is refused.
- **`viewer/`** and **`lookdev/`** - the HTML replay viewer and the look-dev
  harness.

## The one rule

Nothing here may import the engine, and nothing here may be the only place a
gameplay rule lives. Tools observe and generate; the sim decides.
