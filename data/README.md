# /data
All gameplay numbers live here as YAML, schema-validated (schema.unit.json and siblings to come for buildings, weapons, AI doctrines). Hot-reloadable in dev builds. This is the modding surface: formats here are versioned and carry a published deprecation policy before Workshop opens (TDD s5).
Conventions: lower_snake_case keys; ids prefixed dir_/sod_/com_; every file traces to a GDD section in its notes field. Stat changes >15% need Balance + Game Designer co-sign.
