using System.Text;
using TerminalQuest.Claude;

namespace TerminalQuest
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            await using var claude = new ClaudeSession(new ClaudeSessionOptions
            {
                Model = "claude-haiku-4-5-20251001",
                SystemPrompt = "You are the narrator of a terminal adventure game. "
                             + "Answer in at most two sentences.",
            });

            claude.OnTextDelta += Console.Write;

            Console.Write("Starting Claude... ");
            await claude.StartAsync();
            Console.WriteLine("ready.");
            Console.WriteLine("Type a message. Press Enter on an empty line to quit.");

            while (true)
            {
                Console.Write("\n> ");
                var line = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                {
                    break;
                }

                var turn = await claude.SendAsync(line);
                Console.WriteLine();

                if (turn.IsError)
                {
                    Console.WriteLine($"[error] {turn.Text}");
                }

                Console.WriteLine(
                    $"[session {claude.SessionId} | cache read {turn.CacheReadTokens}, "
                  + $"written {turn.CacheCreationTokens} | ${turn.CostUsd:F4} | {turn.DurationMs}ms]");
            }
        }
    }
}
