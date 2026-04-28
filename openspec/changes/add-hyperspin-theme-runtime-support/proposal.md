## Why

ArcadeFrontend currently requires themes to be authored as native Godot scenes and packages, which makes theme creation expensive and slow. Supporting a constrained subset of extracted HyperSpin themes at runtime would let the frontend reuse existing theme assets without taking on Flash support or full HyperSpin compatibility.

## What Changes

- Add runtime support for loading an extracted HyperSpin-style theme from a `Theme.xml` file and sibling assets in the same folder.
- Introduce a config shape that distinguishes native Godot themes from imported HyperSpin themes instead of overloading `ThemePck` and `ThemeFile`.
- Define a compatibility profile for supported HyperSpin features: centered coordinates in a 1024x768 authored space, non-uniform stretch to the current viewport, static artwork layers, `video.ogv`, and a limited set of entry animation effects.
- Define explicit non-goals and fallback behavior for unsupported features such as Flash, SWF content, and unsupported XML attributes or effects.

## Capabilities

### New Capabilities
- `hyperspin-theme-runtime`: Load and display a constrained subset of extracted HyperSpin themes directly at runtime without pre-converting them to Godot packages.

### Modified Capabilities

None.

## Impact

- Affects menu theme configuration and theme selection behavior in `config.json`.
- Affects runtime theme loading in the theme manager and background/theme host flow.
- Introduces XML parsing, asset discovery, viewport scaling, and runtime scene construction for imported themes.
- Establishes a compatibility contract for HyperSpin theme support that future conversion tooling can target.
