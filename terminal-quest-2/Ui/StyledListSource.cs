using System.Collections;
using System.Collections.Specialized;

using Terminal.Gui.Views;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Backs a <see cref="ListView"/> with rows that carry their own colours.
    /// <para>
    /// This is the seam the game's lists were hand-drawn for. A stock <c>ListView</c> paints one
    /// scheme across a row, but <see cref="IListDataSource.Render"/> hands the row back to us with
    /// the width and the selection state, so a row can be emitted span by span with a
    /// <see cref="TextRole"/> each - which is all the hand-drawn lists ever needed. Everything
    /// around the row - the scroll window, the selection, the key bindings, keeping the highlight on
    /// screen - belongs to <c>ListView</c>, which is where it should have been.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The kind of thing being listed.</typeparam>
    /// <param name="format">
    /// Builds one row. Given the item, the width available and whether it is the highlighted row;
    /// anything wider than the width is the formatter's to trim, as the columns differ per list.
    /// </param>
    internal sealed class StyledListSource<T>(Func<T, int, bool, StyledLine> format) : IListDataSource
    {
        private readonly Func<T, int, bool, StyledLine> _format = format;

        private List<T> _items = [];

        /// <summary>What to list. Setting it tells the <see cref="ListView"/> to re-read.</summary>
        public IReadOnlyList<T> Items
        {
            get => _items;

            set
            {
                _items = [.. value ?? []];

                if (!SuspendCollectionChangedEvent)
                {
                    CollectionChanged?.Invoke(
                        this,
                        new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                }
            }
        }

        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public int Count => _items.Count;

        /// <summary>
        /// Only ever asked for to size horizontal scrolling, which none of these lists do - every one
        /// of them trims to the width instead.
        /// </summary>
        public int MaxItemLength => 0;

        public bool SuspendCollectionChangedEvent { get; set; }

        /// <summary>Nothing here is markable; the lists are single-select.</summary>
        public bool IsMarked(int item) => false;

        public void SetMark(int item, bool value)
        {
            // Deliberately nothing. See IsMarked.
        }

        public IList ToList() => _items;

        /// <summary>
        /// Draws one row, span by span, and pads to the full width.
        /// </summary>
        /// <remarks>
        /// The padding is required rather than tidy: the interface asks implementations to fill the
        /// whole width, and a row that stops short leaves whatever the row above it drew standing in
        /// the columns it did not reach.
        /// </remarks>
        public void Render(
            ListView listView,
            bool selected,
            int item,
            int col,
            int row,
            int width,
            int viewportX = 0)
        {
            ArgumentNullException.ThrowIfNull(listView);

            if (width <= 0 || item < 0 || item >= _items.Count)
            {
                return;
            }

            listView.Move(col, row);

            var drawn = 0;

            foreach (var span in _format(_items[item], width, selected).Spans)
            {
                if (drawn >= width)
                {
                    break;
                }

                var text = span.Text.Length > width - drawn ? span.Text[..(width - drawn)] : span.Text;

                listView.SetAttribute(Theme.Attr(span.Role));
                listView.AddStr(text);

                drawn += text.Length;
            }

            if (drawn < width)
            {
                listView.SetAttribute(Theme.Attr(TextRole.Normal));
                listView.AddStr(new string(' ', width - drawn));
            }
        }

        /// <summary>Marks are off, so there is nothing to draw for one.</summary>
        public void RenderMark(ListView listView, int item, int col, bool isMarked, bool isSelected)
        {
            // Deliberately nothing. See IsMarked.
        }

        /// <summary>
        /// Nothing here owns an unmanaged handle or a subscription: the items are the caller's list
        /// and the only event is one this raises rather than one it listens to.
        /// </summary>
        public void Dispose() => GC.SuppressFinalize(this);
    }
}
