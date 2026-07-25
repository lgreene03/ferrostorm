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
- **`Ferrostorm.Balance/`** - the engagement matrix and counter audit (doc 12),
  run by CI. Reporting only; it does not fail the build on balance.
- **`viewer/`** and **`lookdev/`** - the HTML replay viewer and the look-dev
  harness.

## The one rule

Nothing here may import the engine, and nothing here may be the only place a
gameplay rule lives. Tools observe and generate; the sim decides.
