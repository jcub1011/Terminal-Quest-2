using System.Buffers;
using System.Text;
using System.Text.Json;

namespace TerminalQuest.Mcp
{
    /// <summary>
    /// Which agent roles are permitted to see and call a tool.
    /// </summary>
    [Flags]
    internal enum ToolRole
    {
        None = 0,
        Narrator = 1 << 0,
        Director = 1 << 1,
        Both = Narrator | Director,
    }

    /// <summary>One tool as advertised to the model.</summary>
    /// <param name="Name">
    /// Underscored, never spaced. The model reaches it as <c>mcp__quest__{Name}</c>.
    /// </param>
    /// <param name="Description">
    /// Written for the model: it has to say when to reach for the tool,
    /// because that judgement is the whole of the model's side of this contract.
    /// </param>
    /// <param name="InputSchema">A JSON Schema object, emitted into <c>tools/list</c>.</param>
    /// <param name="Role">Which role(s) may invoke this tool.</param>
    internal sealed record QuestTool(string Name, string Description, string InputSchema, ToolRole Role = ToolRole.Narrator)
    {
        // Declaring the property by hand stops the primary constructor assigning it, so the
        // parameter has to be consumed here instead.
        private readonly string _inputSchema = Compact(InputSchema);

        /// <summary>
        /// The schema, collapsed to a single line.
        /// <para>
        /// The transport is newline-delimited JSON, so a literal newline anywhere inside a frame
        /// splits it in two and the client sees a truncated message followed by garbage. Schemas
        /// are written as indented literals for the sake of whoever is reading this file, which
        /// means the newlines have to come back out here rather than at the point of use - a
        /// caller that forgot would produce a corrupt stream, not a compile error.
        /// </para>
        /// </summary>
        public string InputSchema
        {
            get => _inputSchema;
            init => _inputSchema = Compact(value);
        }

        /// <summary>Re-emits JSON without insignificant whitespace, validating it on the way.</summary>
        /// <exception cref="JsonException">The schema is not valid JSON.</exception>
        private static string Compact(string schema)
        {
            using var document = JsonDocument.Parse(schema);

            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                document.RootElement.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
    }
}
