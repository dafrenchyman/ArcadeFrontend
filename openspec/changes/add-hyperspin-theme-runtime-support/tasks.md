## 1. Config And Theme Selection

- [x] 1.1 Define the config model for distinguishing packaged Godot themes from HyperSpin runtime themes.
- [x] 1.2 Update menu/theme selection logic to branch between native Godot theme loading and HyperSpin runtime theme loading.
- [x] 1.3 Preserve backward compatibility for existing `ThemePck` and `ThemeFile` configurations.

## 2. HyperSpin Theme Parsing

- [x] 2.1 Create a runtime data model for the supported HyperSpin theme subset.
- [x] 2.2 Parse `Theme.xml` into that model, including supported artwork, video, timing, and animation attributes.
- [x] 2.3 Resolve theme assets relative to the directory containing `Theme.xml`.
- [x] 2.4 Define fallback behavior for missing optional assets and unsupported XML attributes.

## 3. Runtime Rendering Host

- [x] 3.1 Create a generic Godot host for rendering imported HyperSpin themes at runtime.
- [x] 3.2 Implement 1024x768 logical canvas mapping with non-uniform stretch to the viewport.
- [x] 3.3 Render background, artwork layers, and `video.ogv` using centered placement rules.
- [x] 3.4 Implement the first-pass supported animation behaviors and degrade unsupported effects safely.

## 4. Integration And Validation

- [x] 4.1 Add sample configuration for the extracted `themes/MetalSlugX/Theme.xml` theme.
- [x] 4.2 Validate that the sample theme loads without packaged Godot theme artifacts.
- [x] 4.3 Validate that existing packaged Godot themes continue to load unchanged.
- [x] 4.4 Document the supported HyperSpin compatibility profile and explicit non-goals.
