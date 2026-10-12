// Lesson 17 - Human-in-the-Loop
// Puts a human approval checkpoint inside a running multi-agent workflow. ResearchAgent gathers
// facts, a RequestPort pauses the graph and hands those facts to a human, and only an approved
// decision reaches WriterAgent; rejected facts go back to ResearchAgent with the reviewer's
// feedback. Lesson 9 gated one tool call inside one agent's turn; this checkpoint gates the flow
// of work between agents.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace HumanInTheLoop
{
    /// <summary>
    /// Entry point that builds the research-review-write workflow, runs it until it pauses for
    /// review, and keeps answering the pending request from console input until the reviewer
    /// approves and WriterAgent produces the final paragraph.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Builds ResearchAgent and WriterAgent, wires them into the review workflow, and drives
        /// the pause, review and resume loop: while the run reports
        /// <see cref="RunStatus.PendingRequests"/>, it shows the pending facts, reads the
        /// reviewer's decision and resumes the same run with it.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            ChatClientAgent researchAgent = AgentFactory.CreateAgent(
                chatClient, "ResearchAgent",
                "You are a trivia researcher. Given a topic, list exactly two little-known, " +
                    "verifiable facts about it as a short bullet list. Never write prose " +
                    "paragraphs, only bullets.");

            ChatClientAgent writerAgent = AgentFactory.CreateAgent(
                chatClient, "WriterAgent",
                "You are a copywriter. Given a topic and a list of approved facts, weave every " +
                    "fact into exactly one short, engaging prose paragraph. Never produce bullet " +
                    "lists, only flowing prose.");

            Workflow workflow = ReviewWorkflowFactory.Build(researchAgent, writerAgent);

            Console.WriteLine("=== Human-in-the-Loop: review the researcher's facts before the writer runs ===");
            const string topic = "the planet Saturn";
            Console.WriteLine($"Topic: {topic}");
            Console.WriteLine();

            await using Run run = await InProcessExecution.RunAsync(workflow, topic);
            List<WorkflowEvent> events = run.NewEvents.ToList();

            while (await run.GetStatusAsync() == RunStatus.PendingRequests)
            {
                ExternalRequest request = GetPendingRequest(events);
                if (!request.TryGetDataAs(out FactsForReview? review))
                {
                    throw new InvalidOperationException($"Unexpected request on port '{request.PortInfo.PortId}'.");
                }

                Console.WriteLine($"--- Run status: {RunStatus.PendingRequests}. Round {review.Round} facts from ResearchAgent ---");
                Console.WriteLine(review.Facts);
                Console.WriteLine();
                Console.Write("Type 'y' to approve, or type feedback to send the facts back: ");
                string input = ReadNonEmptyLine();
                Console.WriteLine();

                ReviewDecision decision = input.Equals("y", StringComparison.OrdinalIgnoreCase)
                    ? review.Approve()
                    : review.Revise(input);

                await run.ResumeAsync([request.CreateResponse(decision)]);
                events = run.NewEvents.ToList();
            }

            PrintResult(events);
            Console.WriteLine();
            Console.WriteLine($"Final run status: {await run.GetStatusAsync()}");
        }

        /// <summary>
        /// Returns the most recent request the workflow raised in this batch of events. Throws if
        /// there is none, because the caller only asks while the run reports pending requests.
        /// </summary>
        private static ExternalRequest GetPendingRequest(IEnumerable<WorkflowEvent> events)
        {
            RequestInfoEvent? requestEvent = events.OfType<RequestInfoEvent>().LastOrDefault();
            return requestEvent?.Request
                ?? throw new InvalidOperationException("The run reports pending requests, but no RequestInfoEvent was raised.");
        }

        /// <summary>
        /// Reads a trimmed line from the console, prompting again until the reviewer types
        /// something.
        /// </summary>
        private static string ReadNonEmptyLine()
        {
            while (true)
            {
                string input = (Console.ReadLine() ?? string.Empty).Trim();
                if (input.Length > 0)
                {
                    return input;
                }

                Console.Write("Please type 'y' or your feedback: ");
            }
        }

        /// <summary>
        /// Prints the workflow's final output, or the failure that stopped it, from the last
        /// batch of events.
        /// </summary>
        private static void PrintResult(IEnumerable<WorkflowEvent> events)
        {
            foreach (WorkflowEvent evt in events)
            {
                switch (evt)
                {
                    case WorkflowOutputEvent output:
                        Console.WriteLine($"--- Approved. Output from {output.ExecutorId} ---");
                        Console.WriteLine(output.Data);
                        break;
                    case ExecutorFailedEvent failed:
                        Console.WriteLine($"--- {failed.ExecutorId} failed: {failed.Data} ---");
                        break;
                    case WorkflowErrorEvent error:
                        Console.WriteLine($"--- Workflow error: {error.Exception?.Message} ---");
                        break;
                }
            }
        }
    }
}
