## Context

ArcadeFrontend currently switches themes by loading a Godot `.pck` and instantiating `theme.tscn`. The menu data model assumes that every theme is a native Godot artifact via `ThemePck` and `ThemeFile`, and the background host only knows how to load that packaged scene path.

The target change is narrower than general HyperSpin compatibility. The first supported scope is an extracted HyperTheme-style folder containing `Theme.xml`, `Background.png`, `Artwork1.png` through `Artwork4.png`, and an optional `video.ogv`. Theme coordinates are treated as centered positions in a 1024x768 authored space that is stretched non-uniformly to the current viewport to match HyperSpin's widescreen behavior. Flash and SWF content remain unsupported.

## Goals / Non-Goals

**Goals:**
- Allow a menu item or menu section to reference a HyperSpin runtime theme directly from `config.json`.
- Parse a constrained subset of `Theme.xml` into an internal runtime theme model.
- Render imported HyperSpin themes through a generic Godot host scene rather than requiring conversion into a `.pck`.
- Support basic artwork and video placement plus a limited set of intro effects that degrade gracefully when unsupported.
- Keep native Godot theme loading working without changing existing packaged theme behavior.

**Non-Goals:**
- Full HyperSpin compatibility across arbitrary community themes.
- Flash, SWF, or timeline/script-driven animation support.
- A permanent conversion pipeline from HyperSpin themes to Godot scenes or packages.
- Perfect visual fidelity for every effect in the first implementation.

## Decisions

### Use an explicit runtime theme type in config

The config model should distinguish native Godot themes from imported HyperSpin themes instead of inferring behavior from `ThemePck` and `ThemeFile`. A nested shape such as `Theme.Type` plus `Theme.Path` keeps the branch explicit and leaves room for future theme sources.

Alternative considered:
- Add a top-level `HyperspinTheme` field. This would work for the first pass but makes the config model grow sideways and leaves branching logic implicit.

### Render HyperSpin themes through a generic host

Imported themes should be rendered by a generic runtime host that builds Godot nodes from parsed XML and assets at selection time. This keeps the first implementation fast to iterate on and avoids committing to a conversion format before the supported subset is stable.

Alternative considered:
- Convert extracted HyperSpin themes into generated Godot scenes or `.pck` files. This would produce a cleaner runtime path but adds more tooling and makes it harder to learn from real themes early.

### Define a narrow compatibility profile

The first implementation should only support a documented subset of `Theme.xml` attributes and theme assets:
- `Background.png`
- `Artwork1.png` through `Artwork4.png`
- optional `video.ogv`
- centered `x` and `y`
- `w` and `h` for video
- `r`
- `time`
- `delay`
- `start`
- `type`
- limited layering behavior needed to place video and artwork predictably

Unsupported attributes and features should not crash the theme load. They should be ignored or downgraded to simpler rendering when possible.

Alternative considered:
- Attempt broad XML support from the start. This increases ambiguity, weakens testing, and raises the chance of claiming compatibility that the runtime does not actually provide.

### Stretch imported themes from 1024x768 to the active viewport

Imported themes should use a 1024x768 logical canvas and map coordinates and sizes to the viewport using independent X and Y scale factors. This intentionally reproduces HyperSpin's stretched widescreen presentation rather than preserving 4:3 proportions.

Alternative considered:
- Uniform scaling with pillarboxing. This would be visually cleaner for new UI but would not match the stated compatibility goal.

### Treat `video.ogv` as the initial supported video source

The runtime should assume that theme video is supplied as `video.ogv` in the same folder as `Theme.xml`. This keeps the first implementation aligned with Godot's native support and avoids format detection or transcoding concerns.

Alternative considered:
- Support `mp4`, `avi`, `mkv`, or arbitrary video filenames. This is attractive but expands surface area before the basic runtime path is proven.

## Risks / Trade-offs

- [Real-world HyperSpin themes are inconsistent] -> Publish a compatibility profile and treat unsupported features as partial compatibility rather than silent success.
- [Blur and animation effects may not match HyperSpin exactly] -> Start with a minimal effect set and allow graceful degradation when a visual effect cannot be reproduced cleanly.
- [A second theme pipeline adds maintenance cost] -> Keep the HyperSpin runtime path isolated behind an explicit theme type and a generic host.
- [Config changes can complicate migration] -> Preserve existing `ThemePck` and `ThemeFile` behavior for native themes while adding the new shape incrementally.
- [Viewport stretch may surprise future theme authors] -> Restrict the stretch behavior to imported HyperSpin themes and document it clearly.

## Migration Plan

1. Add the new config shape while keeping native Godot theme fields functioning for existing setups.
2. Implement runtime HyperSpin theme loading behind the new theme type.
3. Update sample configuration to include at least one HyperSpin theme reference.
4. Validate the sample theme against the supported compatibility profile.
5. Defer any conversion pipeline until runtime behavior and compatibility boundaries are proven.

Rollback is straightforward because the new path is additive. Removing the HyperSpin theme type and its host would return the application to packaged Godot themes only.

## Open Questions

- Whether native Godot themes should eventually move into the same nested `Theme` object for consistency.
- Whether the runtime should expose compatibility warnings to users or keep them to logs only.
- How much of HyperSpin's `type="blur"` behavior is worth emulating versus intentionally ignoring in the first pass.
