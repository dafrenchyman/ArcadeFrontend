## ADDED Requirements

### Requirement: Config can reference a HyperSpin runtime theme

The system SHALL allow menu entries to declare a theme source that identifies a HyperSpin runtime theme separately from native Godot packaged themes.

#### Scenario: Menu item declares a HyperSpin theme

- **WHEN** a menu item configuration declares a HyperSpin theme path
- **THEN** the system treats that path as the entry point for runtime HyperSpin theme loading instead of packaged Godot theme loading

#### Scenario: Existing Godot theme configuration remains valid

- **WHEN** a menu item configuration uses the existing packaged Godot theme fields
- **THEN** the system continues to load the native Godot theme without requiring HyperSpin theme fields

### Requirement: Runtime loader reads extracted HyperSpin theme folders

The system SHALL load an extracted HyperSpin runtime theme from a `Theme.xml` file and resolve sibling assets from the same directory.

#### Scenario: Theme folder contains expected assets

- **WHEN** the referenced HyperSpin theme path points to a folder containing `Theme.xml` and sibling asset files
- **THEN** the runtime loader resolves those assets relative to the directory containing `Theme.xml`

#### Scenario: Optional asset is missing

- **WHEN** an optional HyperSpin asset such as `video.ogv` is not present in the theme folder
- **THEN** the runtime loader skips that asset without failing the entire theme load

### Requirement: Imported HyperSpin themes use HyperSpin coordinate scaling

The system SHALL interpret imported HyperSpin themes in a 1024x768 authored space and stretch them non-uniformly to the active viewport.

#### Scenario: Center-based artwork coordinate is rendered

- **WHEN** an artwork element defines `x` and `y` coordinates in `Theme.xml`
- **THEN** the runtime places that artwork using centered coordinates mapped from the 1024x768 authored space to the active viewport

#### Scenario: Video rectangle is rendered

- **WHEN** a video element defines `x`, `y`, `w`, and `h` in `Theme.xml`
- **THEN** the runtime maps the video rectangle from the 1024x768 authored space to the active viewport using independent horizontal and vertical scale factors

### Requirement: Runtime loader supports a constrained HyperSpin theme subset

The system SHALL support a documented subset of HyperSpin theme features and degrade gracefully for unsupported features.

#### Scenario: Supported artwork and animation attributes are present

- **WHEN** `Theme.xml` contains supported attributes such as `x`, `y`, `w`, `h`, `r`, `time`, `delay`, `start`, and `type`
- **THEN** the runtime applies those attributes to background, video, and artwork rendering according to the documented compatibility profile

#### Scenario: Unsupported attribute or effect is encountered

- **WHEN** `Theme.xml` contains an unsupported attribute or effect
- **THEN** the runtime ignores or simplifies that behavior without crashing the application

#### Scenario: Theme requires Flash or SWF behavior

- **WHEN** a HyperSpin theme depends on Flash, SWF, or other explicitly unsupported behavior
- **THEN** the runtime does not attempt to execute that behavior and treats the unsupported portion as outside the compatibility profile

### Requirement: Runtime loader supports theme video as video.ogv

The system SHALL treat `video.ogv` in the same folder as `Theme.xml` as the supported theme video source for imported HyperSpin themes.

#### Scenario: Theme video exists

- **WHEN** `video.ogv` exists beside `Theme.xml`
- **THEN** the runtime uses that file for the imported theme's video region

#### Scenario: Alternate video file exists without video.ogv

- **WHEN** other video files exist in the theme folder but `video.ogv` does not
- **THEN** the runtime does not infer an alternate video source automatically
