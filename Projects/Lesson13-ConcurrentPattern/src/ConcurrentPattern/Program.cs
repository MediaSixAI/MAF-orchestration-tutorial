// Lesson 13 - Concurrent Pattern
// Runs three independent specialist agents - SEO, legal risk, and tone - against the same
// marketing headline at the same time via AgentWorkflowBuilder.BuildConcurrent, then merges
// their responses with a custom aggregator. A sequential baseline using the same three agents
// is timed first, so the concurrent run's speedup is measured, not just asserted.

using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace ConcurrentPattern
{
    /// <summary>
    /// Entry point that times a sequential baseline against a concurrent workflow running the
    /// same three review agents on the same input.
    /// </summary>
    internal sealed class Program
    {
        private const string Headline = "Buy Now, Regret Never!";

        /// <summary>
        /// Builds three independent review agents, runs them sequentially to establish a
        /// timing baseline, then runs the same agents concurrently and prints both durations
        /// alongside the merged result.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            ChatClientAgent seoAgent = AgentFactory.CreateAgent(
                chatClient, "SeoAgent", "Give one short SEO-focused suggestion for the given headline.");
            ChatClientAgent legalAgent = AgentFactory.CreateAgent(
                chatClient, "LegalAgent", "Give one short legal-risk note for the given headline.");
            ChatClientAgent toneAgent = AgentFactory.CreateAgent(
                chatClient, "ToneAgent", "Give one short tone-and-voice suggestion for the given headline.");

            Console.WriteLine("=== Sequential baseline (same three agents, one after another) ===");
            Stopwatch sequentialTimer = Stopwatch.StartNew();
            await seoAgent.RunAsync(Headline);
            await legalAgent.RunAsync(Headline);
            await toneAgent.RunAsync(Headline);
            sequentialTimer.Stop();
            Console.WriteLine($"Sequential elapsed: {sequentialTimer.ElapsedMilliseconds}ms");

            Console.WriteLine();
            Console.WriteLine("=== Concurrent workflow (BuildConcurrent) ===");
            Workflow workflow = AgentWorkflowBuilder.BuildConcurrent(
                [seoAgent, legalAgent, toneAgent], ReviewAggregator.Combine);

            Stopwatch concurrentTimer = Stopwatch.StartNew();
            Run result = await InProcessExecution.RunAsync(workflow, new ChatMessage(ChatRole.User, Headline));
            concurrentTimer.Stop();
            Console.WriteLine($"Concurrent elapsed: {concurrentTimer.ElapsedMilliseconds}ms");

            foreach (WorkflowEvent evt in result.NewEvents)
            {
                if (evt is WorkflowOutputEvent outputEvent && outputEvent.Data is List<ChatMessage> reviews)
                {
                    Console.WriteLine($"Merged {reviews.Count} independent review(s):");
                    foreach (ChatMessage review in reviews)
                    {
                        Console.WriteLine($"  [{review.AuthorName}] {review.Text}");
                    }
                }
            }
        }
    }
}
