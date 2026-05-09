using System;
using System.Threading.Tasks;
using Godot;

namespace ArcadeFrontend;

public partial class ConfigurationOverlay : CanvasLayer
{
    private readonly FrontendRuntimeStore _runtimeStore;
    private readonly RuntimeLibraryBuilder _libraryBuilder;
    private readonly string _masterDatabasePath;

    private CheckBox _enabledCheck = null!;
    private LineEdit _romRootInput = null!;
    private LineEdit _emulatorInput = null!;
    private LineEdit _regionInput = null!;
    private LineEdit _languageInput = null!;
    private Label _statusLabel = null!;
    private ProgressBar _progressBar = null!;
    private Button _saveButton = null!;
    private Button _scanButton = null!;
    private Button _exportButton = null!;
    private Button _backButton = null!;
    private bool _scanInProgress;
    private bool _closed;

    [Signal] public delegate void ClosedEventHandler();
    [Signal] public delegate void LibraryChangedEventHandler();

    public ConfigurationOverlay(
        FrontendRuntimeStore runtimeStore,
        RuntimeLibraryBuilder libraryBuilder,
        string masterDatabasePath)
    {
        _runtimeStore = runtimeStore;
        _libraryBuilder = libraryBuilder;
        _masterDatabasePath = masterDatabasePath;
    }

    public override void _Ready()
    {
        Layer = 60;

        var dim = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.82f),
            AnchorRight = 1,
            AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        AddChild(dim);

        var panel = new PanelContainer
        {
            AnchorLeft = 0.12f,
            AnchorTop = 0.08f,
            AnchorRight = 0.88f,
            AnchorBottom = 0.92f,
        };
        AddChild(panel);

        var scroll = new ScrollContainer
        {
            AnchorRight = 1,
            AnchorBottom = 1,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        panel.AddChild(scroll);

        var layout = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        layout.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(layout);

        var title = new Label
        {
            Text = "Configuration",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 30);
        layout.AddChild(title);

        layout.AddChild(BuildSectionLabel("Setup Systems"));
        _enabledCheck = new CheckBox { Text = "Enable SNES runtime library" };
        layout.AddChild(_enabledCheck);

        _romRootInput = BuildLineEdit("SNES ROM root folder");
        layout.AddChild(_romRootInput);

        _emulatorInput = BuildLineEdit("System emulator command (use {romPath})");
        layout.AddChild(_emulatorInput);

        layout.AddChild(BuildSectionLabel("Preferences"));
        _regionInput = BuildLineEdit("Preferred region code");
        layout.AddChild(_regionInput);

        _languageInput = BuildLineEdit("Preferred language code");
        layout.AddChild(_languageInput);

        var buttonRow = new HBoxContainer();
        buttonRow.AddThemeConstantOverride("separation", 12);
        layout.AddChild(buttonRow);

        _saveButton = new Button { Text = "Save" };
        _saveButton.Pressed += SaveConfiguration;
        buttonRow.AddChild(_saveButton);

        _scanButton = new Button { Text = "Scan SNES Library" };
        _scanButton.Pressed += ScanLibrary;
        buttonRow.AddChild(_scanButton);

        _exportButton = new Button { Text = "Export Config Snapshot" };
        _exportButton.Pressed += ExportConfiguration;
        buttonRow.AddChild(_exportButton);

        _backButton = new Button { Text = "Back" };
        _backButton.Pressed += Close;
        buttonRow.AddChild(_backButton);

        _progressBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Value = 0,
            ShowPercentage = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        layout.AddChild(_progressBar);

        _statusLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        layout.AddChild(_statusLabel);

        LoadCurrentValues();
        _romRootInput.GrabFocus();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel") && !_scanInProgress)
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void LoadCurrentValues()
    {
        foreach (var system in _runtimeStore.LoadSettings().Systems)
        {
            if (!string.Equals(system.SystemId, "snes", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _enabledCheck.ButtonPressed = system.IsEnabled;
            _romRootInput.Text = system.RomRootPath ?? string.Empty;
            _emulatorInput.Text = system.DefaultEmulatorCommand ?? string.Empty;
            _regionInput.Text = system.PreferredRegionCode ?? "USA";
            _languageInput.Text = system.PreferredLanguageCode ?? "EN";
            break;
        }
    }

    private void SaveConfiguration()
    {
        _runtimeStore.UpdateSystemConfiguration(
            systemId: "snes",
            romRootPath: _romRootInput.Text,
            defaultEmulatorCommand: _emulatorInput.Text,
            preferredRegionCode: _regionInput.Text,
            preferredLanguageCode: _languageInput.Text,
            isEnabled: _enabledCheck.ButtonPressed);
        _statusLabel.Text = "Configuration saved.";
        EmitSignal(SignalName.LibraryChanged);
    }

    private async void ScanLibrary()
    {
        if (_scanInProgress)
        {
            return;
        }

        SaveConfiguration();

        if (string.IsNullOrWhiteSpace(_romRootInput.Text))
        {
            _statusLabel.Text = "ROM root path is required before scanning.";
            return;
        }

        try
        {
            SetScanState(true);
            var scanner = new UnifiedSnesLibraryScanner(_runtimeStore, _masterDatabasePath);
            await Task.Run(() =>
                scanner.Scan(
                    systemId: "snes",
                    romRootPath: _romRootInput.Text,
                    onProgress: progress => CallDeferred(MethodName.ApplyProgressUpdate, BuildProgressText(progress), CalculateProgressValue(progress), CalculateProgressMax(progress))));
            _statusLabel.Text = "Scan complete.";
            _progressBar.MaxValue = 1;
            _progressBar.Value = 1;
            EmitSignal(SignalName.LibraryChanged);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Scan failed: {ex.Message}";
        }
        finally
        {
            SetScanState(false);
        }
    }

    private void ExportConfiguration()
    {
        try
        {
            var exporter = new RuntimeConfigExporter(_libraryBuilder, _runtimeStore);
            var exportPath = exporter.Export("snes");
            _statusLabel.Text = $"Exported configuration to {exportPath}";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Export failed: {ex.Message}";
        }
    }

    private void Close()
    {
        if (_scanInProgress)
        {
            return;
        }

        if (_closed)
        {
            return;
        }

        _closed = true;
        EmitSignal(SignalName.Closed);
        QueueFree();
    }

    private static Label BuildSectionLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 24);
        return label;
    }

    private static LineEdit BuildLineEdit(string placeholder)
    {
        return new LineEdit
        {
            PlaceholderText = placeholder,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
    }

    private void ApplyProgressUpdate(string statusText, double progressValue, int progressMax)
    {
        _statusLabel.Text = statusText;
        _progressBar.MaxValue = Math.Max(1, progressMax);
        _progressBar.Value = Math.Clamp(progressValue, 0, _progressBar.MaxValue);
    }

    private void SetScanState(bool scanning)
    {
        _scanInProgress = scanning;
        _saveButton.Disabled = scanning;
        _scanButton.Disabled = scanning;
        _exportButton.Disabled = scanning;
        _backButton.Disabled = scanning;
        _romRootInput.Editable = !scanning;
        _emulatorInput.Editable = !scanning;
        _regionInput.Editable = !scanning;
        _languageInput.Editable = !scanning;
        _enabledCheck.Disabled = scanning;

        if (scanning)
        {
            _progressBar.MaxValue = 1;
            _progressBar.Value = 0;
            _statusLabel.Text = "Starting scan...";
        }
    }

    private static string BuildProgressText(LibraryScanProgress progress)
    {
        return progress.Phase switch
        {
            LibraryScanPhase.Discovery => $"Discovery: {progress.Message}",
            LibraryScanPhase.Matching => $"Matching: {progress.ProcessedCandidates}/{progress.TotalCandidates} matched {progress.MatchedCandidates}. {progress.Message}",
            LibraryScanPhase.AssetFetch => $"Assets: {progress.ProcessedCandidates}/{Math.Max(1, progress.TotalCandidates)}. {progress.Message}",
            LibraryScanPhase.Complete => progress.Message ?? "Scan complete.",
            LibraryScanPhase.Failed => progress.Message ?? "Scan failed.",
            _ => progress.Message ?? progress.Phase.ToString(),
        };
    }

    private static double CalculateProgressValue(LibraryScanProgress progress)
    {
        if (progress.TotalCandidates <= 0)
        {
            return 0;
        }

        return progress.Phase switch
        {
            LibraryScanPhase.Discovery => 0,
            LibraryScanPhase.Matching => progress.ProcessedCandidates,
            LibraryScanPhase.AssetFetch => progress.ProcessedCandidates,
            LibraryScanPhase.Complete => progress.TotalCandidates,
            _ => 0,
        };
    }

    private static int CalculateProgressMax(LibraryScanProgress progress)
    {
        return progress.Phase switch
        {
            LibraryScanPhase.AssetFetch => Math.Max(1, progress.TotalCandidates),
            _ => Math.Max(1, progress.TotalCandidates),
        };
    }
}
