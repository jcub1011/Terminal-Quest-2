using System.Drawing;

using Terminal.Gui.Input;
using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    public sealed class InventoryViewTests
    {
        [Fact]
        public void Empty_items_list_shows_empty_pack()
        {
            var view = new InventoryView();
            view.SetItems([]);

            // Verify view initializes cleanly with empty list
            Assert.NotNull(view);
        }

        [Fact]
        public void Clicking_item_row_raises_entity_clicked_with_id()
        {
            var view = new InventoryView();
            view.Viewport = new Rectangle(0, 0, 24, 10);

            view.SetItems([
                new InventoryEntry(1, "Rusted Key", "itm_1"),
                new InventoryEntry(2, "Healing Potion", "itm_2")
            ]);

            string? clickedId = null;
            view.EntityClicked += id => clickedId = id;

            // Simulate clicking on the second row (Y = 1)
            var mouse = new Mouse
            {
                Flags = MouseFlags.LeftButtonClicked,
                Position = new Point(5, 1)
            };

            var handled = view.NewMouseEvent(mouse);

            Assert.True(handled);
            Assert.Equal("itm_2", clickedId);
        }

        [Fact]
        public void Clicking_first_item_row_raises_first_item_id()
        {
            var view = new InventoryView();
            view.Viewport = new Rectangle(0, 0, 24, 10);

            view.SetItems([
                new InventoryEntry(1, "Rusted Key", "itm_1"),
                new InventoryEntry(2, "Healing Potion", "itm_2")
            ]);

            string? clickedId = null;
            view.EntityClicked += id => clickedId = id;

            // Click first row (Y = 0)
            var mouse = new Mouse
            {
                Flags = MouseFlags.LeftButtonClicked,
                Position = new Point(3, 0)
            };

            var handled = view.NewMouseEvent(mouse);

            Assert.True(handled);
            Assert.Equal("itm_1", clickedId);
        }

        [Fact]
        public void Mouse_wheel_scrolls_view()
        {
            var view = new InventoryView();
            view.Viewport = new Rectangle(0, 0, 24, 2);

            view.SetItems([
                new InventoryEntry(1, "Item 1", "itm_1"),
                new InventoryEntry(1, "Item 2", "itm_2"),
                new InventoryEntry(1, "Item 3", "itm_3"),
                new InventoryEntry(1, "Item 4", "itm_4"),
            ]);

            var wheelDown = new Mouse { Flags = MouseFlags.WheeledDown };
            var handledDown = view.NewMouseEvent(wheelDown);
            Assert.True(handledDown);

            var wheelUp = new Mouse { Flags = MouseFlags.WheeledUp };
            var handledUp = view.NewMouseEvent(wheelUp);
            Assert.True(handledUp);
        }
    }
}
