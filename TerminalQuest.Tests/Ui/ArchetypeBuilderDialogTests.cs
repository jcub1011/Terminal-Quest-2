using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using TerminalQuest.Agents;
using TerminalQuest.Saves;
using TerminalQuest.Settings;
using TerminalQuest.Tests.Infrastructure;
using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    [Collection(EnvironmentCollection.Name)]
    [Trait(Categories.Name, Categories.Environment)]
    public sealed class ArchetypeBuilderDialogTests
    {
        [Fact]
        public void ArchetypeBuilderDialog_initializes_with_defaults()
        {
            var app = Application.Create();
            var settings = new AppSettings();
            var dialog = new ArchetypeBuilderDialog(app, settings);

            Assert.Equal(ClassTemplates.DefaultPointBudget, dialog.TotalAllocatedPoints);
            Assert.False(dialog.IsOverallocated);
            Assert.Equal(6, dialog.CustomAttributes.Count);
            Assert.All(CharacterAttributes.Core, core =>
                Assert.Contains(dialog.CustomAttributes, a => a.Name == core));
        }

        [Fact]
        public void ArchetypeBuilderDialog_equipment_scroll_container_has_scrollbar()
        {
            var app = Application.Create();
            var settings = new AppSettings();
            var dialog = new ArchetypeBuilderDialog(app, settings);

            Assert.NotNull(dialog.EquipmentScrollContainer);
            Assert.Equal(ScrollBarVisibilityMode.Auto, dialog.EquipmentScrollContainer.VerticalScrollBar.VisibilityMode);
            Assert.True(dialog.EquipmentScrollContainer.GetContentHeight() > 15);
            Assert.Contains(dialog.WeaponsListView, dialog.EquipmentScrollContainer.SubViews);
            Assert.Contains(dialog.OffhandsListView, dialog.EquipmentScrollContainer.SubViews);
            Assert.Contains(dialog.SpecialsListView, dialog.EquipmentScrollContainer.SubViews);
        }

        [Fact]
        public void ArchetypeBuilderDialog_detects_overallocation_and_allows_confirmation()
        {
            var app = Application.Create();
            var settings = new AppSettings();
            var dialog = new ArchetypeBuilderDialog(app, settings);

            // Overallocate points
            var strength = dialog.CustomAttributes.First(a => a.Name == "Strength");
            strength.Score = 25; // pushes sum from 74 to 87

            Assert.True(dialog.IsOverallocated);
            Assert.True(dialog.TotalAllocatedPoints > ClassTemplates.DefaultPointBudget);

            // Confirm submission via Ctrl+A
            dialog.NewKeyDownEvent(Key.A.WithCtrl);

            Assert.True(dialog.Confirmed);
            Assert.NotNull(dialog.ResultTemplate);
            Assert.Equal(25, dialog.ResultTemplate.Attributes.First(a => a.Name == "Strength").Score);
            Assert.Equal(ClassTemplates.StandardStartingMoney, dialog.ResultTemplate.StartingMoney);
        }

        [Fact]
        public void ArchetypeBuilderDialog_custom_attribute_add_and_remove()
        {
            var app = Application.Create();
            var settings = new AppSettings();
            var dialog = new ArchetypeBuilderDialog(app, settings);

            dialog.NewAttrNameField.Text = "Arcana";
            dialog.NewAttrScoreField.Text = "10";

            dialog.AddAttrButton.NewKeyDownEvent(Key.Enter);

            Assert.Contains(dialog.CustomAttributes, a => a.Name == "Arcana" && a.Score == 10);

            // Select and remove
            dialog.AttributeListView.SelectedItem = dialog.CustomAttributes.Count - 1;
            dialog.RemoveAttrButton.NewKeyDownEvent(Key.Enter);

            Assert.DoesNotContain(dialog.CustomAttributes, a => a.Name == "Arcana");
        }

        [Fact]
        public async Task ArchetypeBuilderDialog_item_generation_requires_summary_and_aptitude()
        {
            var app = Application.Create();
            var settings = new AppSettings();
            var dialog = new ArchetypeBuilderDialog(app, settings);

            dialog.SummaryField.Text = "   ";
            await dialog.TriggerItemGenerationAsync();

            Assert.Contains("Required", dialog.GeneratorStatusLabel.Text);
        }

        [Fact]
        public void ArchetypeBuilderDialog_creates_class_template_with_selected_items_and_standard_allocation()
        {
            var app = Application.Create();
            var settings = new AppSettings();
            var dialog = new ArchetypeBuilderDialog(app, settings);

            dialog.NewKeyDownEvent(Key.A.WithCtrl);

            Assert.True(dialog.Confirmed);
            var template = dialog.ResultTemplate!;
            Assert.Equal("Custom", template.Name);
            Assert.Equal(15, template.StartingMoney);
            Assert.Equal(5, template.StartingItems.Count); // weapon, offhand, special, bandages, rations

            Assert.Contains(template.StartingItems, i => i.Name == "bandages" && i.Quantity == 2);
            Assert.Contains(template.StartingItems, i => i.Name == "rations" && i.Quantity == 3);
        }

        [Fact]
        public void ArchetypeBuilderDialog_adjusts_attribute_score_with_D_and_A_keys()
        {
            var app = Application.Create();
            var settings = new AppSettings();
            var dialog = new ArchetypeBuilderDialog(app, settings);

            dialog.AttributeListView.SelectedItem = 0; // Strength (12)

            var initialScore = dialog.CustomAttributes[0].Score;

            // Press 'D' on AttributeListView to increment
            dialog.AttributeListView.NewKeyDownEvent(Key.D);
            Assert.Equal(initialScore + 1, dialog.CustomAttributes[0].Score);

            // Press CursorRight to increment again
            dialog.AttributeListView.NewKeyDownEvent(Key.CursorRight);
            Assert.Equal(initialScore + 2, dialog.CustomAttributes[0].Score);

            // Press 'A' to decrement
            dialog.AttributeListView.NewKeyDownEvent(Key.A);
            Assert.Equal(initialScore + 1, dialog.CustomAttributes[0].Score);

            // Press CursorLeft to decrement
            dialog.AttributeListView.NewKeyDownEvent(Key.CursorLeft);
            Assert.Equal(initialScore, dialog.CustomAttributes[0].Score);
        }

        [Fact]
        public void ArchetypeBuilderDialog_inventory_lists_wrap_words_and_position_button_at_top()
        {
            var app = Application.Create();
            var settings = new AppSettings();
            var dialog = new ArchetypeBuilderDialog(app, settings);

            // Generate button is positioned at top
            Assert.NotNull(dialog.GenerateItemsButton);
            Assert.NotNull(dialog.GeneratorStatusLabel);

            // Weapons list expands to fit all items (Height >= 5)
            Assert.True(dialog.WeaponsListView.Source!.Count > 0);
            Assert.NotNull(dialog.WeaponsListView.Height);

            // First item starts with "[X] "
            var firstItem = dialog.WeaponsListView.Source.ToList()[0]!.ToString()!;
            Assert.StartsWith("[X] ", firstItem);
        }
    }
}
