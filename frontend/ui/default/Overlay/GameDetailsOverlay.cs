using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;

namespace ArcadeFrontend;

public partial class GameDetailsOverlay : CanvasLayer
{
    private const int RelatedSeriesSectionIndex = 3;
    private const int RelatedPublisherSectionIndex = 4;
    private const int RelatedDeveloperSectionIndex = 5;

    private readonly FrontendRuntimeStore _runtimeStore;
    private readonly RuntimeLibraryBuilder _libraryBuilder;
    private MenuItemData _menuItem;

    private readonly List<PanelContainer> _sections = [];
    private readonly Dictionary<string, List<MenuItemData>> _relatedContent = new();
    private readonly Dictionary<string, RelatedStripSection> _relatedStripSections = new();
    private readonly Dictionary<string, int> _relatedSelectionIndices = new();

    private int _sectionIndex;
    private int _actionIndex;
    private int _releaseIndex;
    private int _screenshotIndex;
    private bool _releaseEditMode;
    private bool _closed;

    private Label _titleLabel = null!;
    private TextureRect _logoTexture = null!;
    private TextureRect _posterTexture = null!;
    private Label _descriptionLabel = null!;
    private Label _metadataLabel = null!;
    private Label _actionsLabel = null!;
    private Label _releaseListLabel = null!;
    private TextureRect _screenshotTexture = null!;
    private Label _screenshotLabel = null!;
    private ScrollContainer _scroll = null!;
    private string? _statusMessage;

    [Signal] public delegate void ClosedEventHandler();

    public GameDetailsOverlay(FrontendRuntimeStore runtimeStore, RuntimeLibraryBuilder libraryBuilder, MenuItemData menuItem)
    {
        _runtimeStore = runtimeStore;
        _libraryBuilder = libraryBuilder;
        _menuItem = menuItem;
    }

    public override void _Ready()
    {
        Layer = 70;

        var dim = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.7f),
            AnchorRight = 1,
            AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        AddChild(dim);

        var panel = new PanelContainer
        {
            AnchorLeft = 0.1f,
            AnchorTop = 0.05f,
            AnchorRight = 0.94f,
            AnchorBottom = 0.95f,
        };
        AddChild(panel);

        _scroll = new ScrollContainer
        {
            AnchorRight = 1,
            AnchorBottom = 1,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        panel.AddChild(_scroll);

        var content = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddThemeConstantOverride("separation", 16);
        _scroll.AddChild(content);

        content.AddChild(BuildHeroSection());
        _releaseListLabel = AddTextSection(content, "Owned Releases");
        BuildScreenshotSection(content);
        BuildRelatedSection(content, "series", "Games In Series");
        BuildRelatedSection(content, "publisher", "More From Publisher");
        BuildRelatedSection(content, "developer", "More From Developer");

        LoadRelatedContent();
        RefreshDisplay();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            if (_releaseEditMode)
            {
                _releaseEditMode = false;
                RefreshDisplay();
            }
            else
            {
                Close();
            }

            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("ui_up"))
        {
            if (_releaseEditMode)
            {
                _releaseIndex = Math.Max(0, _releaseIndex - 1);
            }
            else
            {
                _sectionIndex = Math.Max(0, _sectionIndex - 1);
            }

            RefreshDisplay();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("ui_down"))
        {
            if (_releaseEditMode)
            {
                _releaseIndex = Math.Min(Math.Max(0, _menuItem.ItemInformation.Versions.Count - 1), _releaseIndex + 1);
            }
            else
            {
                _sectionIndex = Math.Min(_sections.Count - 1, _sectionIndex + 1);
            }

            RefreshDisplay();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("ui_left"))
        {
            HandleHorizontalMove(-1);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("ui_right"))
        {
            HandleHorizontalMove(1);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed("ui_accept"))
        {
            HandleAccept();
            GetViewport().SetInputAsHandled();
        }
    }

    private PanelContainer BuildHeroSection()
    {
        var panel = new PanelContainer();
        var root = new HBoxContainer();
        root.AddThemeConstantOverride("separation", 20);
        panel.AddChild(root);

        _posterTexture = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(280, 420),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        root.AddChild(_posterTexture);

        var right = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        right.AddThemeConstantOverride("separation", 10);
        root.AddChild(right);

        _titleLabel = new Label();
        _titleLabel.AddThemeFontSizeOverride("font_size", 30);
        right.AddChild(_titleLabel);

        _logoTexture = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(360, 110),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        right.AddChild(_logoTexture);

        _metadataLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        right.AddChild(_metadataLabel);

        _descriptionLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        right.AddChild(_descriptionLabel);

        _actionsLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        right.AddChild(_actionsLabel);

        _sections.Add(panel);
        return panel;
    }

    private PanelContainer BuildScreenshotSection(VBoxContainer parent)
    {
        var panel = new PanelContainer();
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 10);
        panel.AddChild(content);

        _screenshotTexture = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(720, 405),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        content.AddChild(_screenshotTexture);

        _screenshotLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _screenshotLabel.AddThemeFontSizeOverride("font_size", 20);
        content.AddChild(_screenshotLabel);

        parent.AddChild(panel);
        _sections.Add(panel);
        return panel;
    }

    private void HandleHorizontalMove(int direction)
    {
        if (_sectionIndex == 0)
        {
            _actionIndex = Mathf.Clamp(_actionIndex + direction, 0, 1);
        }
        else if (_sectionIndex == 2 && _menuItem.ItemInformation.Screenshots.Count > 0)
        {
            _screenshotIndex = WrapIndex(_screenshotIndex + direction, _menuItem.ItemInformation.Screenshots.Count);
        }
        else if (TryGetRelationForSection(_sectionIndex, out var relation) &&
                 _relatedContent.TryGetValue(relation, out var items) &&
                 items.Count > 0)
        {
            var current = _relatedSelectionIndices.TryGetValue(relation, out var selectedIndex) ? selectedIndex : 0;
            _relatedSelectionIndices[relation] = WrapIndex(current + direction, items.Count);
        }

        RefreshDisplay();
    }

    private void HandleAccept()
    {
        switch (_sectionIndex)
        {
            case 0:
                if (_actionIndex == 0)
                {
                    PlayCurrentVersion();
                }
                else
                {
                    ToggleFavorite();
                }
                break;
            case 1:
                if (!_releaseEditMode)
                {
                    _releaseEditMode = true;
                }
                else
                {
                    SavePreferredRelease();
                    _releaseEditMode = false;
                }
                break;
            case 2:
                OpenScreenshotViewer();
                break;
            case RelatedSeriesSectionIndex:
                OpenRelated("series");
                break;
            case RelatedPublisherSectionIndex:
                OpenRelated("publisher");
                break;
            case RelatedDeveloperSectionIndex:
                OpenRelated("developer");
                break;
        }

        RefreshDisplay();
    }

    private void PlayCurrentVersion()
    {
        var version = GetSelectedVersion();
        if (string.IsNullOrWhiteSpace(version?.LaunchCommand))
        {
            _statusMessage = "No emulator command is configured for the selected release.";
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(version.LaunchCommand);
            psi.Environment["XAUTHORITY"] = System.Environment.GetEnvironmentVariable("XAUTHORITY");

            var process = Process.Start(psi);
            _statusMessage = process == null
                ? "Launch failed to start a process."
                : $"Launching {(_menuItem.Name ?? "game")}.\n{version.LaunchCommand}";
        }
        catch (Exception ex)
        {
            _statusMessage = $"Launch failed: {ex.Message}";
        }
    }

    private void ToggleFavorite()
    {
        if (string.IsNullOrWhiteSpace(_menuItem.SystemId) || string.IsNullOrWhiteSpace(_menuItem.GameKey))
        {
            return;
        }

        var nextValue = !_runtimeStore.IsGameFavorite(_menuItem.SystemId, _menuItem.GameKey);
        _runtimeStore.SetGameFavorite(_menuItem.SystemId, _menuItem.GameKey, nextValue);
        LoadRelatedContent();
        _statusMessage = nextValue ? "Marked as favorite." : "Removed from favorites.";
    }

    private void SavePreferredRelease()
    {
        var version = GetSelectedVersion();
        if (version?.ReleaseKey == null || _menuItem.SystemId == null || _menuItem.GameKey == null)
        {
            return;
        }

        _runtimeStore.SetGamePreferredReleaseKey(_menuItem.SystemId, _menuItem.GameKey, version.ReleaseKey);
        for (var index = 0; index < _menuItem.ItemInformation.Versions.Count; index++)
        {
            _menuItem.ItemInformation.Versions[index].Default = index == _releaseIndex;
        }
        _statusMessage = "Saved default release.";
    }

    private void OpenScreenshotViewer()
    {
        if (_menuItem.ItemInformation.Screenshots.Count == 0)
        {
            return;
        }

        var viewer = new ScreenshotViewerOverlay(_menuItem.ItemInformation.Screenshots, _screenshotIndex);
        viewer.Closed += index =>
        {
            _screenshotIndex = index;
            RefreshDisplay();
        };
        AddChild(viewer);
    }

    private void OpenRelated(string relation)
    {
        if (!_relatedContent.TryGetValue(relation, out var items) || items.Count == 0)
        {
            return;
        }

        var selectedIndex = _relatedSelectionIndices.TryGetValue(relation, out var index) ? index : 0;
        selectedIndex = Mathf.Clamp(selectedIndex, 0, items.Count - 1);
        var selectedItem = items[selectedIndex];
        var related = _libraryBuilder.BuildCanonicalGameDetails(selectedItem.SystemId!, selectedItem.GameKey!);
        var overlay = new GameDetailsOverlay(_runtimeStore, _libraryBuilder, related);
        AddChild(overlay);
    }

    private void LoadRelatedContent()
    {
        if (string.IsNullOrWhiteSpace(_menuItem.SystemId) || string.IsNullOrWhiteSpace(_menuItem.GameKey))
        {
            return;
        }

        _relatedContent["series"] = _libraryBuilder.LoadRelatedGames(_menuItem.SystemId, _menuItem.GameKey, "series");
        _relatedContent["publisher"] = _libraryBuilder.LoadRelatedGames(_menuItem.SystemId, _menuItem.GameKey, "publisher");
        _relatedContent["developer"] = _libraryBuilder.LoadRelatedGames(_menuItem.SystemId, _menuItem.GameKey, "developer");
    }

    private void RefreshDisplay()
    {
        _titleLabel.Text = _menuItem.Name ?? string.Empty;
        _descriptionLabel.Text = _menuItem.ItemInformation?.Description ?? string.Empty;
        _metadataLabel.Text = BuildMetadata();
        _actionsLabel.Text = BuildActionsText();
        _releaseListLabel.Text = BuildReleaseListText();
        RefreshArtwork();
        RefreshRelatedStrips();

        for (var index = 0; index < _sections.Count; index++)
        {
            _sections[index].Modulate = index == _sectionIndex
                ? new Color(1f, 0.95f, 0.65f, 1f)
                : Colors.White;
        }

        CallDeferred(nameof(EnsureSectionVisible));
    }

    private void RefreshArtwork()
    {
        _logoTexture.Texture = LoadTexture(_menuItem.ItemInformation?.LogoLocation);
        _posterTexture.Texture = LoadTexture(_menuItem.ItemInformation?.Poster);

        var screenshots = _menuItem.ItemInformation?.Screenshots ?? [];
        if (screenshots.Count == 0)
        {
            _screenshotTexture.Texture = null;
            _screenshotLabel.Text = "No screenshots cached yet.";
            return;
        }

        _screenshotIndex = Mathf.Clamp(_screenshotIndex, 0, screenshots.Count - 1);
        _screenshotTexture.Texture = LoadTexture(screenshots[_screenshotIndex]);
        _screenshotLabel.Text = $"Screenshots\n{_screenshotIndex + 1} / {screenshots.Count}\nLeft/Right browse. Accept opens fullscreen.";
    }

    private static Texture2D? LoadTexture(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : Utils.LoadExternalImage(path);
    }

    private string BuildMetadata()
    {
        var info = _menuItem.ItemInformation;
        var players = info?.Players?.ToString() ?? "?";
        var coop = info?.Coop == true ? "Co-op" : "Solo/Unknown";
        var publishers = info?.Publishers == null ? string.Empty : string.Join(", ", info.Publishers);
        var developers = info?.Developers == null ? string.Empty : string.Join(", ", info.Developers);
        return $"Year: {info?.ReleaseData ?? "Unknown"}\nPlayers: {players}\nMode: {coop}\nPublisher: {publishers}\nDeveloper: {developers}";
    }

    private string BuildActionsText()
    {
        var favorite = (_menuItem.SystemId != null && _menuItem.GameKey != null && _runtimeStore.IsGameFavorite(_menuItem.SystemId, _menuItem.GameKey))
            ? "Unfavorite"
            : "Favorite";
        var playLabel = _actionIndex == 0 ? "[Play]" : "Play";
        var favoriteLabel = _actionIndex == 1 ? $"[{favorite}]" : favorite;
        var selectedVersion = GetSelectedVersion();
        var defaultReleaseSummary = selectedVersion == null
            ? "No owned release available."
            : $"Default Release: {string.Join("/", selectedVersion.Regions ?? [])} {selectedVersion.Revision}".Trim();
        var launchSummary = string.IsNullOrWhiteSpace(selectedVersion?.LaunchCommand)
            ? "Launch: configure a system/game/release emulator command first."
            : "Launch: ready";
        var status = string.IsNullOrWhiteSpace(_statusMessage) ? string.Empty : $"\n{_statusMessage}";
        return $"Actions\n{playLabel}    {favoriteLabel}\n{defaultReleaseSummary}\n{launchSummary}{status}";
    }

    private string BuildReleaseListText()
    {
        var lines = new List<string>();
        for (var index = 0; index < _menuItem.ItemInformation.Versions.Count; index++)
        {
            var version = _menuItem.ItemInformation.Versions[index];
            var isCursor = _releaseEditMode && index == _releaseIndex;
            var marker = version.Default ? "✓" : " ";
            var cursor = isCursor ? ">" : " ";
            var regions = version.Regions == null ? string.Empty : string.Join("/", version.Regions);
            var languages = version.Languages == null ? string.Empty : string.Join("/", version.Languages);
            lines.Add($"{cursor} {marker} {regions} {languages} {version.Revision}".TrimEnd());
        }

        lines.Add(_releaseEditMode
            ? "Accept saves default. Cancel exits release selection."
            : "Accept enters release selection.");

        return string.Join('\n', lines);
    }

    private void RefreshRelatedStrips()
    {
        foreach (var pair in _relatedStripSections)
        {
            var relation = pair.Key;
            var section = pair.Value;

            if (!_relatedContent.TryGetValue(relation, out var items) || items.Count == 0)
            {
                section.EmptyLabel.Text = "No related owned games found.";
                section.EmptyLabel.Visible = true;
                section.CardRow.Visible = false;
                continue;
            }

            section.EmptyLabel.Visible = false;
            section.CardRow.Visible = true;

            var selectedIndex = _relatedSelectionIndices.TryGetValue(relation, out var index)
                ? WrapIndex(index, items.Count)
                : 0;
            _relatedSelectionIndices[relation] = selectedIndex;

            for (var cardIndex = 0; cardIndex < section.Cards.Count; cardIndex++)
            {
                var itemIndex = cardIndex < items.Count ? WrapIndex(selectedIndex + cardIndex, items.Count) : -1;
                var card = section.Cards[cardIndex];
                if (cardIndex >= items.Count || itemIndex < 0)
                {
                    card.Root.Visible = false;
                    continue;
                }

                var item = items[itemIndex];
                card.Root.Visible = true;
                var isSelected = cardIndex == 0;
                card.Root.Modulate = isSelected
                    ? new Color(1f, 0.97f, 0.72f, 1f)
                    : new Color(0.92f, 0.92f, 0.92f, 1f);
                card.Root.SelfModulate = isSelected
                    ? Colors.White
                    : new Color(0.86f, 0.86f, 0.86f, 1f);
                card.Root.AddThemeStyleboxOverride("panel", BuildRelatedCardStyle(isSelected));
                card.Poster.Texture = LoadTexture(item.Poster ?? item.ItemInformation?.Poster);
                card.Title.Text = item.Name ?? string.Empty;
                card.Meta.Text = string.IsNullOrWhiteSpace(item.ItemInformation?.ReleaseData)
                    ? string.Empty
                    : item.ItemInformation.ReleaseData;
            }
        }
    }

    private Version? GetSelectedVersion()
    {
        if (_menuItem.ItemInformation?.Versions == null || _menuItem.ItemInformation.Versions.Count == 0)
        {
            return null;
        }

        if (_releaseEditMode)
        {
            return _menuItem.ItemInformation.Versions[_releaseIndex];
        }

        return _menuItem.ItemInformation.Versions.FirstOrDefault(version => version.Default)
               ?? _menuItem.ItemInformation.Versions[0];
    }

    private Label AddTextSection(VBoxContainer parent, string title)
    {
        var panel = new PanelContainer();
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 6);
        panel.AddChild(content);

        var titleLabel = new Label
        {
            Text = title,
        };
        titleLabel.AddThemeFontSizeOverride("font_size", 18);
        content.AddChild(titleLabel);

        var label = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        content.AddChild(label);
        parent.AddChild(panel);
        _sections.Add(panel);
        return label;
    }

    private void BuildRelatedSection(VBoxContainer parent, string relation, string title)
    {
        var panel = new PanelContainer();
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 10);
        panel.AddChild(content);

        var titleLabel = new Label
        {
            Text = title,
        };
        titleLabel.AddThemeFontSizeOverride("font_size", 18);
        content.AddChild(titleLabel);

        var emptyLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        content.AddChild(emptyLabel);

        var cardRow = new HBoxContainer();
        cardRow.AddThemeConstantOverride("separation", 12);
        content.AddChild(cardRow);

        var cards = new List<RelatedCard>();
        for (var index = 0; index < 3; index++)
        {
            var cardPanel = new PanelContainer
            {
                CustomMinimumSize = new Vector2(190, 320),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };

            var cardContent = new VBoxContainer();
            cardContent.AddThemeConstantOverride("separation", 8);
            cardPanel.AddChild(cardContent);

            var poster = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(170, 250),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            cardContent.AddChild(poster);

            var cardTitle = new Label
            {
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            cardContent.AddChild(cardTitle);

            var cardMeta = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            cardMeta.AddThemeFontSizeOverride("font_size", 14);
            cardContent.AddChild(cardMeta);

            cardRow.AddChild(cardPanel);
            cards.Add(new RelatedCard(cardPanel, poster, cardTitle, cardMeta));
        }

        parent.AddChild(panel);
        _sections.Add(panel);
        _relatedStripSections[relation] = new RelatedStripSection(panel, emptyLabel, cardRow, cards);
    }

    private static bool TryGetRelationForSection(int sectionIndex, out string relation)
    {
        relation = sectionIndex switch
        {
            RelatedSeriesSectionIndex => "series",
            RelatedPublisherSectionIndex => "publisher",
            RelatedDeveloperSectionIndex => "developer",
            _ => string.Empty,
        };

        return !string.IsNullOrEmpty(relation);
    }

    private static StyleBoxFlat BuildRelatedCardStyle(bool selected)
    {
        var style = new StyleBoxFlat
        {
            BgColor = selected ? new Color(0.14f, 0.14f, 0.14f, 0.96f) : new Color(0.09f, 0.09f, 0.09f, 0.92f),
            BorderColor = selected ? new Color(1f, 0.87f, 0.38f, 1f) : new Color(0.32f, 0.32f, 0.32f, 0.85f),
            BorderWidthTop = selected ? 5 : 2,
            BorderWidthRight = selected ? 5 : 2,
            BorderWidthBottom = selected ? 5 : 2,
            BorderWidthLeft = selected ? 5 : 2,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomRight = 8,
            CornerRadiusBottomLeft = 8,
        };
        style.ContentMarginLeft = 8;
        style.ContentMarginTop = 8;
        style.ContentMarginRight = 8;
        style.ContentMarginBottom = 8;
        return style;
    }

    private void EnsureSectionVisible()
    {
        if (_sectionIndex < 0 || _sectionIndex >= _sections.Count)
        {
            return;
        }

        var sectionRect = _sections[_sectionIndex].GetGlobalRect();
        var scrollRect = _scroll.GetGlobalRect();
        var target = _scroll.ScrollVertical + sectionRect.Position.Y - scrollRect.Position.Y - 40;
        _scroll.ScrollVertical = (int)Math.Max(0, target);
    }

    private static int WrapIndex(int value, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        return ((value % count) + count) % count;
    }

    private void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        EmitSignal(SignalName.Closed);
        QueueFree();
    }

    private sealed partial class ScreenshotViewerOverlay : CanvasLayer
    {
        private readonly IReadOnlyList<string> _screenshots;
        private int _index;
        private TextureRect _texture = null!;

        public event Action<int>? Closed;

        public ScreenshotViewerOverlay(IReadOnlyList<string> screenshots, int startIndex)
        {
            _screenshots = screenshots;
            _index = Mathf.Clamp(startIndex, 0, Math.Max(0, screenshots.Count - 1));
        }

        public override void _Ready()
        {
            Layer = 90;

            var dim = new ColorRect
            {
                Color = new Color(0, 0, 0, 0.92f),
                AnchorRight = 1,
                AnchorBottom = 1,
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            AddChild(dim);

            _texture = new TextureRect
            {
                AnchorLeft = 0.08f,
                AnchorTop = 0.08f,
                AnchorRight = 0.92f,
                AnchorBottom = 0.92f,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            };
            AddChild(_texture);
            Refresh();
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event.IsActionPressed("ui_cancel"))
            {
                Closed?.Invoke(_index);
                QueueFree();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (@event.IsActionPressed("ui_left"))
            {
                _index = WrapIndex(_index - 1, _screenshots.Count);
                Refresh();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (@event.IsActionPressed("ui_right"))
            {
                _index = WrapIndex(_index + 1, _screenshots.Count);
                Refresh();
                GetViewport().SetInputAsHandled();
            }
        }

        private void Refresh()
        {
            _texture.Texture = _screenshots.Count == 0 ? null : Utils.LoadExternalImage(_screenshots[_index]);
        }
    }

    private sealed record RelatedCard(PanelContainer Root, TextureRect Poster, Label Title, Label Meta);

    private sealed record RelatedStripSection(
        PanelContainer Root,
        Label EmptyLabel,
        HBoxContainer CardRow,
        List<RelatedCard> Cards);
}
