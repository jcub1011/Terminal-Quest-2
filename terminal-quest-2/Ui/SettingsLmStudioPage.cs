using TerminalQuest.Settings;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// How to reach the model running on this machine, and which one to ask for.
    /// </summary>
    internal sealed class SettingsLmStudioPage : SettingsPage
    {
        /// <summary>Rows the window asks about by name rather than by number.</summary>
        public const int AddressRow = 0;

        /// <summary>The row that goes and asks the server what it has.</summary>
        public const int ModelRow = 1;

        /// <summary>The row whose value is nobody else's business.</summary>
        public const int ApiKeyRow = 2;

        /// <summary>Past the longest label, so the three values line up as a column.</summary>
        private const int ValuesAt = 14;

        private const string Unset = "(whichever is loaded)";

        public SettingsLmStudioPage(AppSettings draft)
            : base(draft)
        {
        }

        public override string Title => "LM Studio";

        public override string Hint =>
            "Enter edits an entry, or lists the server's models.  Left goes back.  Ctrl+Enter saves.";

        public override int ValueColumn => ValuesAt;

        public override IReadOnlyList<MenuRow> Rows =>
        [
            new("Address", Draft.LmStudioBaseUrl, false),
            new("Model", Draft.LmStudioModel is { Length: > 0 } model ? model : Unset, false),
            new("API Key", Mask(Draft.LmStudioApiKey), false),
        ];

        // Asking the server what it is serving beats spelling a model id from memory, so the
        // model row opens a list rather than an editor. The window falls back to the editor when
        // the server cannot be reached - see SettingsWindow.ProbeAsync.
        public override bool NeedsProbe(int index) => index == ModelRow;

        public override bool IsSecret(int index) => index == ApiKeyRow;

        public override bool TryBeginEdit(int index, out string text)
        {
            text = index switch
            {
                AddressRow => Draft.LmStudioBaseUrl,
                ModelRow => Draft.LmStudioModel,
                ApiKeyRow => Draft.LmStudioApiKey,
                _ => string.Empty,
            };

            return index is AddressRow or ModelRow or ApiKeyRow;
        }

        public override string? Commit(int index, string text)
        {
            var typed = text?.Trim() ?? string.Empty;

            switch (index)
            {
                case AddressRow:
                    // Checked as the player leaves the field rather than only at save time, so a
                    // bad address is rejected while it is still the thing being typed.
                    if (!AppSettings.IsAddress(typed))
                    {
                        return "That needs to be a full URL, such as http://localhost:1234/v1";
                    }

                    Draft.LmStudioBaseUrl = typed;
                    return null;

                case ModelRow:
                    Draft.LmStudioModel = typed;
                    return null;

                case ApiKeyRow:
                    Draft.LmStudioApiKey = typed;
                    return null;

                default:
                    return null;
            }
        }

        /// <summary>
        /// A fixed run of dots rather than one per character, so the screen does not even give
        /// away how long the token is.
        /// </summary>
        private static string Mask(string value) =>
            value is { Length: > 0 } ? "••••••••" : string.Empty;
    }
}
