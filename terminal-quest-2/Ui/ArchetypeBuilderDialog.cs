using System.Collections.ObjectModel;
using System.Text;

using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using TerminalQuest.Agents;
using TerminalQuest.Saves;
using TerminalQuest.Settings;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Popup dialog allowing the player to customize archetype identity, distribute attribute points,
    /// invent custom attributes, and select starting inventory items generated via LLM.
    /// </summary>
    internal sealed class ArchetypeBuilderDialog : Dialog
    {
        private const int MaxNameLength = 40;
        private readonly IApplication? _app;
        private readonly AppSettings _settings;
        private readonly ExternalEditor? _editor;

        // Custom template state
        private string _archetypeName;
        private string _archetypeSummary;
        private string _archetypeAptitude;
        private int _maxHealth = 24;

        private readonly List<CharacterAttribute> _attributes = [];
        private GeneratedItemSets _itemSets;

        private int _selectedWeaponIndex;
        private int _selectedOffhandIndex;
        private int _selectedSpecialIndex;

        // UI Controls
        private readonly Tabs _tabs;
        private readonly View _identityTabView;
        private readonly View _equipmentTabView;

        // Identity tab controls
        private readonly TextField _nameField;
        private readonly TextField _maxHealthField;
        private readonly TextField _summaryField;
        private readonly TextField _aptitudeField;

        private readonly Label _pointsLabel;
        private readonly Label _overallocatedLabel;
        private readonly ListView _attributeListView;
        private readonly Button _incrementButton;
        private readonly Button _decrementButton;

        private readonly TextField _newAttrNameField;
        private readonly TextField _newAttrScoreField;
        private readonly Button _addAttrButton;
        private readonly Button _removeAttrButton;

        // Equipment tab controls
        private readonly View _equipmentScrollContainer;
        private readonly ListView _weaponsListView;
        private readonly ListView _offhandsListView;
        private readonly ListView _specialsListView;
        private readonly Button _generateItemsButton;
        private readonly Label _generatorStatusLabel;

        private int _weaponListHeight = 6;
        private int _offhandListHeight = 6;
        private int _specialListHeight = 6;

        // Bottom action buttons
        private readonly Button _acceptButton;
        private readonly Button _cancelButton;

        private bool _isGenerating;

        public ArchetypeBuilderDialog(
            IApplication? app,
            AppSettings settings,
            ClassTemplate? initialTemplate = null,
            ExternalEditor? editor = null)
        {
            _app = app;
            _settings = settings;
            _editor = editor;

            Title = "Archetype Builder - Custom Character";
            BorderStyle = LineStyle.Rounded;
            SetScheme(Theme.CreateScheme());

            var cols = app?.Driver?.Cols ?? 100;
            var rows = app?.Driver?.Rows ?? 40;
            Width = Math.Clamp(cols - 4, 70, 100);
            Height = Math.Clamp(rows - 2, 24, 36);

            var initial = initialTemplate ?? ClassTemplates.CreateDefaultCustom();
            _archetypeName = initial.Name;
            _archetypeSummary = initial.Summary;
            _archetypeAptitude = initial.Aptitude;
            _maxHealth = initial.MaxHealth;

            foreach (var attr in initial.Attributes)
            {
                _attributes.Add(new CharacterAttribute { Name = attr.Name, Score = attr.Score });
            }

            // Ensure core attributes exist
            foreach (var core in CharacterAttributes.Core)
            {
                if (!_attributes.Any(a => string.Equals(a.Name, core, StringComparison.OrdinalIgnoreCase)))
                {
                    _attributes.Add(new CharacterAttribute { Name = core, Score = CharacterAttributes.Neutral });
                }
            }

            _itemSets = LlmItemGenerator.GetDefaultItems();

            _tabs = new Tabs
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(2),
                CanFocus = true,
            };
            _tabs.SetScheme(Theme.CreateScheme());

            // ==========================================
            // TAB 1: IDENTITY & ATTRIBUTES
            // ==========================================
            _identityTabView = new View
            {
                Title = "_Identity & Stats",
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };
            _identityTabView.SetScheme(Theme.CreateScheme());

            var nameLabel = new Label { Text = "Archetype Name:", X = 1, Y = 0 };
            _nameField = new TextField { Text = _archetypeName, X = 18, Y = 0, Width = 30 };
            nameLabel.SetScheme(Theme.CreateScheme());
            _nameField.SetScheme(Theme.CreateScheme());

            var hpLabel = new Label { Text = "Max HP:", X = 50, Y = 0 };
            _maxHealthField = new TextField { Text = _maxHealth.ToString(), X = 59, Y = 0, Width = 8 };
            hpLabel.SetScheme(Theme.CreateScheme());
            _maxHealthField.SetScheme(Theme.CreateScheme());

            var summaryLabel = new Label { Text = "Summary:", X = 1, Y = 2 };
            _summaryField = new TextField { Text = _archetypeSummary, X = 18, Y = 2, Width = Dim.Fill(2) };
            summaryLabel.SetScheme(Theme.CreateScheme());
            _summaryField.SetScheme(Theme.CreateScheme());

            var aptitudeLabel = new Label { Text = "Aptitude:", X = 1, Y = 4 };
            _aptitudeField = new TextField { Text = _archetypeAptitude, X = 18, Y = 4, Width = Dim.Fill(2) };
            aptitudeLabel.SetScheme(Theme.CreateScheme());
            _aptitudeField.SetScheme(Theme.CreateScheme());

            // Stats & Point allocation frame
            var statsFrame = new FrameView
            {
                Title = "Attributes & Point Pool",
                X = 1,
                Y = 6,
                Width = Dim.Fill(2),
                Height = Dim.Fill(1),
                BorderStyle = LineStyle.Rounded,
            };
            statsFrame.SetScheme(Theme.CreateScheme());

            _pointsLabel = new Label { X = 1, Y = 0, Width = 32 };
            _pointsLabel.SetScheme(Theme.CreateScheme());

            _overallocatedLabel = new Label { Text = string.Empty, X = Pos.Right(_pointsLabel) + 2, Y = 0, Width = 28 };
            _overallocatedLabel.SetScheme(Theme.CreateScheme());

            _attributeListView = new AttributeListControl
            {
                X = 1,
                Y = 2,
                Width = 36,
                Height = Dim.Fill(3),
                OnAdjust = AdjustSelectedAttribute,
            };
            _attributeListView.SetScheme(Theme.CreateScheme());

            _incrementButton = new Button { Text = "+1 (D)", X = Pos.Right(_attributeListView) + 2, Y = 3 };
            _decrementButton = new Button { Text = "-1 (A)", X = Pos.Right(_incrementButton) + 2, Y = 3 };
            _incrementButton.SetScheme(Theme.CreateScheme());
            _decrementButton.SetScheme(Theme.CreateScheme());

            _incrementButton.Accepting += (_, _) => AdjustSelectedAttribute(1);
            _decrementButton.Accepting += (_, _) => AdjustSelectedAttribute(-1);

            var newAttrLabel = new Label { Text = "Invent New Attribute:", X = Pos.Right(_attributeListView) + 2, Y = 6 };
            newAttrLabel.SetScheme(Theme.CreateScheme());

            var newNameLbl = new Label { Text = "Name:", X = Pos.Right(_attributeListView) + 2, Y = 8 };
            _newAttrNameField = new TextField { X = Pos.Right(_attributeListView) + 9, Y = 8, Width = 18 };
            var newScoreLbl = new Label { Text = "Score:", X = Pos.Right(_newAttrNameField) + 2, Y = 8 };
            _newAttrScoreField = new TextField { Text = "10", X = Pos.Right(_newAttrNameField) + 9, Y = 8, Width = 6 };

            _addAttrButton = new Button { Text = "Add Attribute", X = Pos.Right(_attributeListView) + 2, Y = 10 };
            _removeAttrButton = new Button { Text = "Remove Selected", X = Pos.Right(_addAttrButton) + 2, Y = 10 };

            newNameLbl.SetScheme(Theme.CreateScheme());
            _newAttrNameField.SetScheme(Theme.CreateScheme());
            newScoreLbl.SetScheme(Theme.CreateScheme());
            _newAttrScoreField.SetScheme(Theme.CreateScheme());
            _addAttrButton.SetScheme(Theme.CreateScheme());
            _removeAttrButton.SetScheme(Theme.CreateScheme());

            _addAttrButton.Accepting += (_, _) => AddCustomAttribute();
            _removeAttrButton.Accepting += (_, _) => RemoveCustomAttribute();

            statsFrame.Add(
                _pointsLabel,
                _overallocatedLabel,
                _attributeListView,
                _incrementButton,
                _decrementButton,
                newAttrLabel,
                newNameLbl,
                _newAttrNameField,
                newScoreLbl,
                _newAttrScoreField,
                _addAttrButton,
                _removeAttrButton);

            _identityTabView.Add(
                nameLabel,
                _nameField,
                hpLabel,
                _maxHealthField,
                summaryLabel,
                _summaryField,
                aptitudeLabel,
                _aptitudeField,
                statsFrame);

            // ==========================================
            // TAB 2: EQUIPMENT SELECTION & LLM GENERATOR
            // ==========================================
            _equipmentTabView = new View
            {
                Title = "_Starting Gear",
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };
            _equipmentTabView.SetScheme(Theme.CreateScheme());

            _generateItemsButton = new Button
            {
                Text = "Generate Items with LLM (Ctrl+G)",
                X = 1,
                Y = 0,
            };
            _generateItemsButton.SetScheme(Theme.CreateScheme());

            _generatorStatusLabel = new Label
            {
                Text = "Summary and Aptitude on Identity tab are required before generating.",
                X = Pos.Right(_generateItemsButton) + 2,
                Y = 0,
                Width = Dim.Fill(2),
            };
            _generatorStatusLabel.SetScheme(Theme.CreateScheme());

            _generateItemsButton.Accepting += async (_, _) => await TriggerItemGenerationAsync();

            var defaultAllocationInfo = new Label
            {
                Text = "Fixed Allocation (All Archetypes): 2x Bandages (Healing) | 3x Rations | 15 Gold Pieces",
                X = 1,
                Y = 2,
                Width = Dim.Fill(2),
            };
            defaultAllocationInfo.SetScheme(Theme.CreateScheme());

            _equipmentScrollContainer = new View
            {
                X = 0,
                Y = 4,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };
            _equipmentScrollContainer.VerticalScrollBar.VisibilityMode = ScrollBarVisibilityMode.Auto;
            _equipmentScrollContainer.SetScheme(Theme.CreateScheme());

            var weaponsLabel = new Label { Text = "1. Choose Weapon (Select with Space/Enter):", X = 1, Y = 0 };
            _weaponsListView = new ListView
            {
                X = 1,
                Y = Pos.Bottom(weaponsLabel),
                Width = Dim.Fill(2),
                Height = 6,
                BorderStyle = LineStyle.Rounded,
                CanFocus = true,
            };
            weaponsLabel.SetScheme(Theme.CreateScheme());
            _weaponsListView.SetScheme(Theme.CreateScheme());

            var offhandsLabel = new Label { Text = "2. Choose Offhand Item:", X = 1, Y = Pos.Bottom(_weaponsListView) + 1 };
            _offhandsListView = new ListView
            {
                X = 1,
                Y = Pos.Bottom(offhandsLabel),
                Width = Dim.Fill(2),
                Height = 6,
                BorderStyle = LineStyle.Rounded,
                CanFocus = true,
            };
            offhandsLabel.SetScheme(Theme.CreateScheme());
            _offhandsListView.SetScheme(Theme.CreateScheme());

            var specialsLabel = new Label { Text = "3. Choose Special Item:", X = 1, Y = Pos.Bottom(_offhandsListView) + 1 };
            _specialsListView = new ListView
            {
                X = 1,
                Y = Pos.Bottom(specialsLabel),
                Width = Dim.Fill(2),
                Height = 6,
                BorderStyle = LineStyle.Rounded,
                CanFocus = true,
            };
            specialsLabel.SetScheme(Theme.CreateScheme());
            _specialsListView.SetScheme(Theme.CreateScheme());

            _weaponsListView.HasFocusChanged += (_, _) =>
            {
                if (_weaponsListView.HasFocus && _equipmentScrollContainer.Viewport.Y > 0)
                {
                    _equipmentScrollContainer.ScrollVertical(-_equipmentScrollContainer.Viewport.Y);
                }
            };

            _offhandsListView.HasFocusChanged += (_, _) =>
            {
                if (_offhandsListView.HasFocus)
                {
                    var offhandTargetY = 1 + _weaponListHeight;
                    if (offhandTargetY < _equipmentScrollContainer.Viewport.Y ||
                        offhandTargetY > _equipmentScrollContainer.Viewport.Y + Math.Max(1, _equipmentScrollContainer.Viewport.Height - 6))
                    {
                        _equipmentScrollContainer.ScrollVertical(offhandTargetY - _equipmentScrollContainer.Viewport.Y);
                    }
                }
            };

            _specialsListView.HasFocusChanged += (_, _) =>
            {
                if (_specialsListView.HasFocus)
                {
                    var maxScroll = Math.Max(0, _equipmentScrollContainer.GetContentHeight() - _equipmentScrollContainer.Viewport.Height);
                    if (_equipmentScrollContainer.Viewport.Y < maxScroll)
                    {
                        _equipmentScrollContainer.ScrollVertical(maxScroll - _equipmentScrollContainer.Viewport.Y);
                    }
                }
            };

            _weaponsListView.ValueChanged += (_, _) =>
            {
                if (_isUpdatingSources) return;
                var lineIdx = _weaponsListView.SelectedItem ?? 0;
                if (lineIdx >= 0 && lineIdx < _weaponMappings.Count)
                {
                    var itemIdx = _weaponMappings[lineIdx].ItemIndex;
                    if (itemIdx != _selectedWeaponIndex)
                    {
                        _selectedWeaponIndex = itemIdx;
                        RefreshItemSources();
                    }
                }
            };
            _weaponsListView.Accepting += (_, _) =>
            {
                if (_isUpdatingSources) return;
                var lineIdx = _weaponsListView.SelectedItem ?? 0;
                if (lineIdx >= 0 && lineIdx < _weaponMappings.Count)
                {
                    var itemIdx = _weaponMappings[lineIdx].ItemIndex;
                    if (itemIdx != _selectedWeaponIndex)
                    {
                        _selectedWeaponIndex = itemIdx;
                        RefreshItemSources();
                    }
                }
            };

            _offhandsListView.ValueChanged += (_, _) =>
            {
                if (_isUpdatingSources) return;
                var lineIdx = _offhandsListView.SelectedItem ?? 0;
                if (lineIdx >= 0 && lineIdx < _offhandMappings.Count)
                {
                    var itemIdx = _offhandMappings[lineIdx].ItemIndex;
                    if (itemIdx != _selectedOffhandIndex)
                    {
                        _selectedOffhandIndex = itemIdx;
                        RefreshItemSources();
                    }
                }
            };
            _offhandsListView.Accepting += (_, _) =>
            {
                if (_isUpdatingSources) return;
                var lineIdx = _offhandsListView.SelectedItem ?? 0;
                if (lineIdx >= 0 && lineIdx < _offhandMappings.Count)
                {
                    var itemIdx = _offhandMappings[lineIdx].ItemIndex;
                    if (itemIdx != _selectedOffhandIndex)
                    {
                        _selectedOffhandIndex = itemIdx;
                        RefreshItemSources();
                    }
                }
            };

            _specialsListView.ValueChanged += (_, _) =>
            {
                if (_isUpdatingSources) return;
                var lineIdx = _specialsListView.SelectedItem ?? 0;
                if (lineIdx >= 0 && lineIdx < _specialMappings.Count)
                {
                    var itemIdx = _specialMappings[lineIdx].ItemIndex;
                    if (itemIdx != _selectedSpecialIndex)
                    {
                        _selectedSpecialIndex = itemIdx;
                        RefreshItemSources();
                    }
                }
            };
            _specialsListView.Accepting += (_, _) =>
            {
                if (_isUpdatingSources) return;
                var lineIdx = _specialsListView.SelectedItem ?? 0;
                if (lineIdx >= 0 && lineIdx < _specialMappings.Count)
                {
                    var itemIdx = _specialMappings[lineIdx].ItemIndex;
                    if (itemIdx != _selectedSpecialIndex)
                    {
                        _selectedSpecialIndex = itemIdx;
                        RefreshItemSources();
                    }
                }
            };

            _equipmentScrollContainer.Add(
                weaponsLabel,
                _weaponsListView,
                offhandsLabel,
                _offhandsListView,
                specialsLabel,
                _specialsListView);

            _equipmentTabView.Add(
                _generateItemsButton,
                _generatorStatusLabel,
                defaultAllocationInfo,
                _equipmentScrollContainer);

            _tabs.Add(_identityTabView, _equipmentTabView);
            _tabs.Value = _identityTabView;

            // ==========================================
            // BOTTOM ACTION BUTTONS
            // ==========================================
            _acceptButton = new Button { Text = "Accept Archetype (Ctrl+A)", X = 2, Y = Pos.AnchorEnd(1) };
            _cancelButton = new Button { Text = "Cancel (Esc)", X = Pos.Right(_acceptButton) + 2, Y = Pos.AnchorEnd(1) };

            _acceptButton.SetScheme(Theme.CreateScheme());
            _cancelButton.SetScheme(Theme.CreateScheme());

            _acceptButton.Accepting += (_, _) => ConfirmArchetype();
            _cancelButton.Accepting += (_, _) => CloseDialog();

            Add(_tabs, _acceptButton, _cancelButton);

            RefreshAttributesList();
            RefreshItemSources();

            Initialized += (_, _) => _nameField.SetFocus();
        }

        public bool Confirmed { get; private set; }

        public ClassTemplate? ResultTemplate { get; private set; }

        public ClassTemplate CustomTemplate =>
            ClassTemplates.BuildCustom(
                _archetypeName,
                _archetypeSummary,
                _archetypeAptitude,
                _maxHealth,
                _attributes.ToList(),
                SelectedWeapon,
                SelectedOffhand,
                SelectedSpecial);

        public int TotalAllocatedPoints => _attributes.Sum(a => a.Score);

        public bool IsOverallocated => TotalAllocatedPoints > ClassTemplates.DefaultPointBudget;

        public IReadOnlyList<CharacterAttribute> CustomAttributes => _attributes;

        public TextField NameField => _nameField;

        public TextField SummaryField => _summaryField;

        public TextField AptitudeField => _aptitudeField;

        public TextField MaxHealthField => _maxHealthField;

        public TextField NewAttrNameField => _newAttrNameField;

        public TextField NewAttrScoreField => _newAttrScoreField;

        public Button AddAttrButton => _addAttrButton;

        public Button RemoveAttrButton => _removeAttrButton;

        public Button AcceptButton => _acceptButton;

        public Button CancelButton => _cancelButton;

        public Button GenerateItemsButton => _generateItemsButton;

        public Label GeneratorStatusLabel => _generatorStatusLabel;

        public ListView AttributeListView => _attributeListView;

        public View EquipmentScrollContainer => _equipmentScrollContainer;

        public ListView WeaponsListView => _weaponsListView;

        public ListView OffhandsListView => _offhandsListView;

        public ListView SpecialsListView => _specialsListView;

        public event Action? Done;

        public event Action? Cancelled;

        public Item SelectedWeapon =>
            _itemSets.Weapons.Count > 0
                ? _itemSets.Weapons[Math.Clamp(_selectedWeaponIndex, 0, _itemSets.Weapons.Count - 1)]
                : new Item { Name = "iron broadsword", Quantity = 1, Description = "Standard steel sword." };

        public Item SelectedOffhand =>
            _itemSets.Offhands.Count > 0
                ? _itemSets.Offhands[Math.Clamp(_selectedOffhandIndex, 0, _itemSets.Offhands.Count - 1)]
                : new Item { Name = "wooden roundshield", Quantity = 1, Description = "Standard wooden shield." };

        public Item SelectedSpecial =>
            _itemSets.Specials.Count > 0
                ? _itemSets.Specials[Math.Clamp(_selectedSpecialIndex, 0, _itemSets.Specials.Count - 1)]
                : new Item { Name = "traveler's charm", Quantity = 1, Description = "A protective keepsake." };

        private void AdjustSelectedAttribute(int delta)
        {
            var idx = _attributeListView.SelectedItem ?? -1;
            if (idx >= 0 && idx < _attributes.Count)
            {
                var attr = _attributes[idx];
                attr.Score = Math.Clamp(attr.Score + delta, CharacterAttributes.MinScore, CharacterAttributes.MaxScore);
                RefreshAttributesList(preserveIndex: idx);
            }
        }

        private void AddCustomAttribute()
        {
            var name = _newAttrNameField.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (_attributes.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var score = 10;
            if (int.TryParse(_newAttrScoreField.Text?.Trim(), out var parsedScore))
            {
                score = Math.Clamp(parsedScore, CharacterAttributes.MinScore, CharacterAttributes.MaxScore);
            }

            _attributes.Add(new CharacterAttribute { Name = name, Score = score });
            _newAttrNameField.Text = string.Empty;
            _newAttrScoreField.Text = "10";

            RefreshAttributesList(preserveIndex: _attributes.Count - 1);
        }

        private void RemoveCustomAttribute()
        {
            var idx = _attributeListView.SelectedItem ?? -1;
            if (idx >= 0 && idx < _attributes.Count)
            {
                var attr = _attributes[idx];
                if (!CharacterAttributes.IsCore(attr.Name))
                {
                    _attributes.RemoveAt(idx);
                    RefreshAttributesList(preserveIndex: Math.Max(0, idx - 1));
                }
            }
        }

        private void RefreshAttributesList(int preserveIndex = -1)
        {
            var items = _attributes.Select(a =>
            {
                var mod = CharacterAttributes.Modifier(a.Score);
                var sign = CharacterAttributes.Sign(mod);
                var coreTag = CharacterAttributes.IsCore(a.Name) ? string.Empty : " *";
                return $"{a.Name.PadRight(15)}{a.Score,2} ({sign}){coreTag}";
            }).ToList();

            _attributeListView.SetSource(new ObservableCollection<string>(items));
            if (preserveIndex >= 0 && preserveIndex < items.Count)
            {
                _attributeListView.SelectedItem = preserveIndex;
            }
            else if (_attributeListView.SelectedItem is null && items.Count > 0)
            {
                _attributeListView.SelectedItem = 0;
            }

            var total = TotalAllocatedPoints;
            _pointsLabel.Text = $"Points: {total} / {ClassTemplates.DefaultPointBudget}";

            if (total > ClassTemplates.DefaultPointBudget)
            {
                _overallocatedLabel.Text = "Attributes Overallocated";
            }
            else
            {
                _overallocatedLabel.Text = string.Empty;
            }
        }

        private sealed record ItemLineMapping(int ItemIndex, string LineText, bool IsFirstLine);

        private List<ItemLineMapping> _weaponMappings = [];
        private List<ItemLineMapping> _offhandMappings = [];
        private List<ItemLineMapping> _specialMappings = [];

        private bool _isUpdatingSources;

        private void RefreshItemSources()
        {
            if (_isUpdatingSources)
            {
                return;
            }

            _isUpdatingSources = true;
            try
            {
                var cols = _app?.Driver?.Cols ?? 100;
                var wrapWidth = Math.Clamp(cols - 16, 42, 92);

                var weaponLines = BuildCategoryLines(_itemSets.Weapons, _selectedWeaponIndex, wrapWidth, out _weaponMappings);
                _weaponListHeight = Math.Max(3, weaponLines.Count + 2);
                _weaponsListView.SetSource(new ObservableCollection<string>(weaponLines));
                _weaponsListView.Height = _weaponListHeight;
                var targetWeaponLine = _weaponMappings.FindIndex(m => m.ItemIndex == _selectedWeaponIndex && m.IsFirstLine);
                if (targetWeaponLine >= 0)
                {
                    _weaponsListView.SelectedItem = targetWeaponLine;
                }

                var offhandLines = BuildCategoryLines(_itemSets.Offhands, _selectedOffhandIndex, wrapWidth, out _offhandMappings);
                _offhandListHeight = Math.Max(3, offhandLines.Count + 2);
                _offhandsListView.SetSource(new ObservableCollection<string>(offhandLines));
                _offhandsListView.Height = _offhandListHeight;
                var targetOffhandLine = _offhandMappings.FindIndex(m => m.ItemIndex == _selectedOffhandIndex && m.IsFirstLine);
                if (targetOffhandLine >= 0)
                {
                    _offhandsListView.SelectedItem = targetOffhandLine;
                }

                var specialLines = BuildCategoryLines(_itemSets.Specials, _selectedSpecialIndex, wrapWidth, out _specialMappings);
                _specialListHeight = Math.Max(3, specialLines.Count + 2);
                _specialsListView.SetSource(new ObservableCollection<string>(specialLines));
                _specialsListView.Height = _specialListHeight;
                var targetSpecialLine = _specialMappings.FindIndex(m => m.ItemIndex == _selectedSpecialIndex && m.IsFirstLine);
                if (targetSpecialLine >= 0)
                {
                    _specialsListView.SelectedItem = targetSpecialLine;
                }

                var totalContentHeight = 1 + _weaponListHeight + 1 + _offhandListHeight + 1 + _specialListHeight + 2;
                _equipmentScrollContainer.SetContentHeight(totalContentHeight);
            }
            finally
            {
                _isUpdatingSources = false;
            }
        }

        private static List<string> BuildCategoryLines(
            IReadOnlyList<Item> items,
            int selectedIndex,
            int maxWidth,
            out List<ItemLineMapping> mappings)
        {
            mappings = [];
            var lines = new List<string>();

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var isSelected = i == selectedIndex;
                var wrapped = WrapItemEntry(item, isSelected, maxWidth);

                for (var lineIdx = 0; lineIdx < wrapped.Count; lineIdx++)
                {
                    var text = wrapped[lineIdx];
                    lines.Add(text);
                    mappings.Add(new ItemLineMapping(i, text, lineIdx == 0));
                }
            }

            return lines;
        }

        private static List<string> WrapItemEntry(Item item, bool isSelected, int maxWidth)
        {
            var prefix = isSelected ? "[X] " : "[ ] ";
            var qty = item.Quantity > 1 ? $" ({item.Quantity}x)" : string.Empty;
            var title = $"{item.Name}{qty}";
            var full = string.IsNullOrWhiteSpace(item.Description)
                ? $"{prefix}{title}"
                : $"{prefix}{title} - {item.Description}";

            var wrapLimit = Math.Max(35, maxWidth);
            if (full.Length <= wrapLimit)
            {
                return [full];
            }

            var words = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var result = new List<string>();
            var current = new StringBuilder();
            var indent = "    ";

            foreach (var word in words)
            {
                if (current.Length == 0)
                {
                    current.Append(word);
                }
                else if (current.Length + 1 + word.Length <= wrapLimit)
                {
                    current.Append(' ').Append(word);
                }
                else
                {
                    result.Add(current.ToString());
                    current.Clear();
                    current.Append(indent).Append(word);
                }
            }

            if (current.Length > 0)
            {
                result.Add(current.ToString());
            }

            return result.Count > 0 ? result : [full];
        }

        public async Task TriggerItemGenerationAsync()
        {
            if (_isGenerating)
            {
                return;
            }

            var summary = (_editor?.Resolve(_summaryField) ?? _summaryField.Text ?? string.Empty).Trim();
            var aptitude = (_editor?.Resolve(_aptitudeField) ?? _aptitudeField.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(summary) || string.IsNullOrWhiteSpace(aptitude))
            {
                _generatorStatusLabel.Text = "Required: Fill Summary & Aptitude on Identity tab first!";
                return;
            }

            _isGenerating = true;
            _generateItemsButton.Enabled = false;
            _generatorStatusLabel.Text = "Generating items via LLM...";

            try
            {
                var generated = await Task.Run(async () =>
                    await LlmItemGenerator.GenerateAsync(_settings, summary, aptitude));

                _itemSets = generated;
                _selectedWeaponIndex = 0;
                _selectedOffhandIndex = 0;
                _selectedSpecialIndex = 0;

                _app?.Invoke(() =>
                {
                    RefreshItemSources();
                    _generatorStatusLabel.Text = "Generated new items successfully!";
                });
            }
            catch (Exception ex)
            {
                _app?.Invoke(() =>
                {
                    _generatorStatusLabel.Text = $"Generation failed ({ex.Message}). Standard items active.";
                });
            }
            finally
            {
                _isGenerating = false;
                _generateItemsButton.Enabled = true;
            }
        }

        private void ConfirmArchetype()
        {
            var name = (_editor?.Resolve(_nameField) ?? _nameField.Text ?? "Custom").Trim();
            if (name.Length == 0)
            {
                name = "Custom";
            }

            if (name.Length > MaxNameLength)
            {
                name = name[..MaxNameLength];
            }

            var summary = (_editor?.Resolve(_summaryField) ?? _summaryField.Text ?? string.Empty).Trim();
            var aptitude = (_editor?.Resolve(_aptitudeField) ?? _aptitudeField.Text ?? string.Empty).Trim();

            var hp = 24;
            if (int.TryParse(_maxHealthField.Text?.Trim(), out var parsedHp))
            {
                hp = Math.Max(1, parsedHp);
            }

            _archetypeName = name;
            _archetypeSummary = summary;
            _archetypeAptitude = aptitude;
            _maxHealth = hp;

            ResultTemplate = ClassTemplates.BuildCustom(
                _archetypeName,
                _archetypeSummary,
                _archetypeAptitude,
                _maxHealth,
                _attributes.ToList(),
                SelectedWeapon,
                SelectedOffhand,
                SelectedSpecial);

            Confirmed = true;
            Done?.Invoke();
            CloseDialog();
        }

        private void CloseDialog()
        {
            if (!Confirmed)
            {
                Cancelled?.Invoke();
            }

            if (_app is not null)
            {
                _app.RequestStop(this);
            }
        }

        protected override bool OnKeyDown(Key key)
        {
            if (key == Key.Esc)
            {
                CloseDialog();
                return true;
            }

            if (key == Key.A.WithCtrl || key == Key.Enter.WithCtrl)
            {
                ConfirmArchetype();
                return true;
            }

            if (key == Key.G.WithCtrl)
            {
                _ = TriggerItemGenerationAsync();
                return true;
            }

            if (MostFocused == _attributeListView || _attributeListView.HasFocus)
            {
                if (key == Key.CursorRight || key == Key.D || key.AsRune.Value == 'd' || key.AsRune.Value == 'D')
                {
                    AdjustSelectedAttribute(1);
                    return true;
                }

                if (key == Key.CursorLeft || key == Key.A || key.AsRune.Value == 'a' || key.AsRune.Value == 'A')
                {
                    AdjustSelectedAttribute(-1);
                    return true;
                }
            }

            return base.OnKeyDown(key);
        }

        private sealed class AttributeListControl : ListView
        {
            public AttributeListControl()
            {
                CanFocus = true;
                BorderStyle = LineStyle.Rounded;
            }

            public Action<int>? OnAdjust { get; set; }

            protected override bool OnKeyDown(Key key)
            {
                if (key == Key.CursorRight || key == Key.D || (char)key.KeyCode == 'd' || (char)key.KeyCode == 'D' || key.AsRune.Value == 'd' || key.AsRune.Value == 'D')
                {
                    OnAdjust?.Invoke(1);
                    return true;
                }

                if (key == Key.CursorLeft || key == Key.A || (char)key.KeyCode == 'a' || (char)key.KeyCode == 'A' || key.AsRune.Value == 'a' || key.AsRune.Value == 'A')
                {
                    OnAdjust?.Invoke(-1);
                    return true;
                }

                return base.OnKeyDown(key);
            }
        }
    }
}
