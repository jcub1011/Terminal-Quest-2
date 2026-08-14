using TerminalQuest.Settings;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// What the LM Studio server said it was serving, as a list to pick from.
    /// <para>
    /// Built from a reply already in hand - the page is only opened once the server has answered,
    /// so there is no such thing as an empty or still-loading version of it.
    /// </para>
    /// </summary>
    internal sealed class SettingsLmModelsPage : SettingsPage
    {
        private const string Unset = "(whichever is loaded)";

        private readonly IReadOnlyList<string> _models;

        public SettingsLmModelsPage(AppSettings draft, IReadOnlyList<string> models)
            : base(draft)
        {
            ArgumentNullException.ThrowIfNull(models);
            _models = models;
        }

        public override string Title => "Model";

        public override string Hint => "Enter picks a model.  Ctrl+L asks again.  Left goes back.";

        public override bool CanSelect(int index) => index >= 0 && index <= _models.Count;

        public override IReadOnlyList<MenuRow> Rows
        {
            get
            {
                var stored = Draft.LmStudioModel?.Trim() ?? string.Empty;
                var rows = new MenuRow[_models.Count + 1];

                // Leaving it unset is a real answer, not an absence of one: the server narrates
                // with whatever it has loaded, which is what a single-model setup wants.
                rows[0] = new MenuRow(Unset, string.Empty, stored.Length == 0);

                for (var index = 0; index < _models.Count; index++)
                {
                    var name = _models[index];
                    rows[index + 1] = new MenuRow(
                        name,
                        string.Empty,
                        string.Equals(name, stored, StringComparison.Ordinal));
                }

                return rows;
            }
        }

        public override bool Select(int index)
        {
            if (index < 0 || index > _models.Count)
            {
                return false;
            }

            var chosen = index == 0 ? string.Empty : _models[index - 1];

            if (string.Equals(chosen, Draft.LmStudioModel, StringComparison.Ordinal))
            {
                return false;
            }

            Draft.LmStudioModel = chosen;
            return true;
        }
    }
}
