// Lesson 20 - Observability & Evaluation
// Runs one OpenAI-backed ChatClientAgent through two different lenses. Part 1 (system health)
// instruments the chat client and the agent with OpenTelemetry and prints the resulting gen_ai.*
// spans and metrics straight to the console - no Aspire Dashboard or Azure Monitor required to
// follow along. Part 2 (outcome quality) takes that same response and scores it with two
// Microsoft.Extensions.AI.Evaluation.Quality evaluators, using a second OpenAI client as the
// LLM judge. The two halves answer two different questions: is the system behaving (health), and
// is the answer actually good (quality) - neither one substitutes for the other.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace ObservabilityAndEvaluation
{
    /// <summary>
    /// Entry point that runs an instrumented agent, then evaluates the quality of its response.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// The OpenTelemetry ActivitySource/Meter name Agent Framework's instrumentation is told
        /// to emit under, and the name this lesson's TracerProvider/MeterProvider subscribe to.
        /// </summary>
        private const string SourceName = "ObservabilityAndEvaluation";

        /// <summary>
        /// The OpenTelemetry resource service.name attached to every span and metric this lesson emits.
        /// </summary>
        private const string ServiceName = "Lesson20ObservabilityAndEvaluation";

        /// <summary>
        /// The question asked of the instrumented agent in Part 1, then re-used as the query
        /// evaluated for quality in Part 2.
        /// </summary>
        private const string Question = "What is the capital of France, in one sentence?";

        /// <summary>
        /// Runs Part 1 (instrumented agent call, system health) followed by Part 2 (quality
        /// evaluation of that call's response).
        /// </summary>
        private static async Task Main(string[] args)
        {
            using TracerProvider tracerProvider = TelemetryFactory.BuildTracerProvider(SourceName, ServiceName);
            using MeterProvider meterProvider = TelemetryFactory.BuildMeterProvider(SourceName, ServiceName);

            Console.WriteLine("=== Part 1: system health (OpenTelemetry traces + metrics) ===");
            Console.WriteLine();
            string answer = await RunInstrumentedAgentAsync();

            // Force the console exporters to flush now, so Part 1's spans/metrics print before
            // Part 2 starts rather than interleaving with it or appearing only at process exit.
            tracerProvider.ForceFlush();
            meterProvider.ForceFlush();

            Console.WriteLine();
            Console.WriteLine("=== Part 2: outcome quality (Microsoft.Extensions.AI.Evaluation) ===");
            Console.WriteLine();
            await EvaluateResponseAsync(answer);
        }

        /// <summary>
        /// Builds an OpenAI chat client and agent with OpenTelemetry instrumentation attached to
        /// both, runs one question through the agent, and returns its answer text. The chat
        /// client and the agent are instrumented separately and both include sensitive data
        /// (prompts and responses) so the console output below is fully readable - see this
        /// lesson's Setup section for why that flag is development-only.
        /// </summary>
        private static async Task<string> RunInstrumentedAgentAsync()
        {
            IChatClient chatClient = ChatClientFactory.Create();

            IChatClient instrumentedChatClient = chatClient
                .AsBuilder()
                .UseOpenTelemetry(sourceName: SourceName, configure: cfg => cfg.EnableSensitiveData = true)
                .Build();

            ChatClientAgent baseAgent = AgentFactory.CreateAgent(
                instrumentedChatClient,
                name: "ObservabilityDemoAgent",
                instructions: "You are a concise assistant.");

            AIAgent instrumentedAgent = baseAgent
                .AsBuilder()
                .UseOpenTelemetry(sourceName: SourceName, configure: cfg => cfg.EnableSensitiveData = true)
                .Build();

            AgentResponse response = await instrumentedAgent.RunAsync(Question);
            Console.WriteLine($"Agent response: {response.Text}");

            return response.Text;
        }

        /// <summary>
        /// Scores the given answer for Coherence and Relevance against this lesson's fixed
        /// question, using a second, independent OpenAI chat client as the LLM judge, and prints
        /// each metric's score, rating, and (for Coherence) the judge's reasoning.
        /// </summary>
        private static async Task EvaluateResponseAsync(string answer)
        {
            IChatClient judgeClient = new MaxOutputTokensChatClient(
                ChatClientFactory.Create(), minimumMaxOutputTokens: 8000);
            var chatConfiguration = new ChatConfiguration(judgeClient);

            var messages = new List<ChatMessage> { new(ChatRole.User, Question) };
            var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, answer));

            IEvaluator coherenceEvaluator = new CoherenceEvaluator();
            EvaluationResult coherenceResult = await coherenceEvaluator.EvaluateAsync(messages, chatResponse, chatConfiguration);
            NumericMetric coherence = coherenceResult.Get<NumericMetric>(CoherenceEvaluator.CoherenceMetricName);
            Console.WriteLine($"Coherence: {coherence.Value} ({coherence.Interpretation?.Rating}) - {coherence.Reason}");

            IEvaluator relevanceEvaluator = new RelevanceEvaluator();
            EvaluationResult relevanceResult = await relevanceEvaluator.EvaluateAsync(messages, chatResponse, chatConfiguration);
            NumericMetric relevance = relevanceResult.Get<NumericMetric>(RelevanceEvaluator.RelevanceMetricName);
            Console.WriteLine($"Relevance: {relevance.Value} ({relevance.Interpretation?.Rating})");
        }
    }
}
