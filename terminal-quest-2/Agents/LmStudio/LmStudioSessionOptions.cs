namespace TerminalQuest.Agents.LmStudio
{
    /// <summary>
    /// Configuration for a <see cref="LmStudioSession"/>.
    /// </summary>
    internal sealed record LmStudioSessionOptions
    {
        /// <summary>
        /// Root of the OpenAI-compatible API, with no trailing slash and no endpoint on the end -
        /// <c>/chat/completions</c> and <c>/models</c> are appended to it.
        /// <para>
        /// LM Studio serves this on port 1234 by default. Anything else speaking the same shape
        /// works here too: Ollama, llama.cpp's server, vLLM, Jan.
        /// </para>
        /// </summary>
        public string BaseUrl { get; init; } = "http://localhost:1234/v1";

        /// <summary>
        /// The model to run, exactly as the server names it in <c>GET /models</c>. Null asks the
        /// server for whatever it already has loaded.
        /// </summary>
        public required string? Model { get; init; }

        /// <summary>The system message, sent as the first entry of every request.</summary>
        public string SystemPrompt { get; init; } = "You are a helpful assistant. Answer concisely.";

        /// <summary>
        /// Sent as a bearer token.
        /// <para>
        /// LM Studio ignores it until authentication is switched on in its developer settings, at
        /// which point every request without the right token is refused with a 401 - so this is not
        /// the formality the placeholder default makes it look like. Other servers on this same API
        /// shape have their own opinions about it.
        /// </para>
        /// </summary>
        public string ApiKey { get; init; } = "lm-studio";

        /// <summary>Sampling temperature. Null leaves the server on its own default.</summary>
        public double? Temperature { get; init; } = 0.8;

        /// <summary>Cap on generated tokens per request. Null means no cap is sent.</summary>
        public int? MaxOutputTokens { get; init; }

        /// <summary>
        /// How many times one turn may go round the call-tools-and-ask-again loop before it is
        /// abandoned.
        /// <para>
        /// This is a backstop against a model that calls tools forever without ever settling into
        /// prose - a real failure mode on smaller local models, and one that would otherwise hang
        /// the turn until <see cref="TurnTimeout"/>. An opening turn legitimately uses several
        /// iterations: read the state, create what is missing, then narrate.
        /// </para>
        /// </summary>
        public int MaxToolIterations { get; init; } = 12;

        /// <summary>
        /// Strips <c>&lt;think&gt;...&lt;/think&gt;</c> spans out of the response text.
        /// <para>
        /// Reasoning models served locally split roughly two ways: some report their reasoning in a
        /// separate <c>reasoning_content</c> delta, which is dropped unconditionally, and some emit
        /// it inline in the content as tagged text, which is indistinguishable from prose without
        /// this. Off is the right setting only for a model known not to do the latter.
        /// </para>
        /// </summary>
        public bool StripThinkTags { get; init; } = true;

        /// <summary>
        /// How long a single turn may run - the whole turn, including every tool round-trip, not
        /// one HTTP request.
        /// </summary>
        public TimeSpan TurnTimeout { get; init; } = TimeSpan.FromMinutes(5);

        /// <summary>How long <see cref="LmStudioSession.StartAsync"/> waits for the server to answer.</summary>
        public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(10);
    }
}
