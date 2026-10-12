// Lesson 18 - Checkpointing & Durability
// Runs the Magentic-One workflow from Lesson 16 with checkpointing enabled and plan sign-off turned
// on, pauses at its plan-review request (the RequestInfoEvent pause Lesson 17 teaches), then
// simulates a full process restart: the workflow, its agents, and the paused Run object are all
// discarded and rebuilt from nothing. Only a session id (a plain string) and a checkpoint store on
// disk survive into the "new process." The restored run still has the pending plan-review request
// waiting for it, proving the pause is durable rather than an in-memory artifact of the process
// that started it.

using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Agents.AI.Workflows.Specialized.Magentic;
using Microsoft.Extensions.AI;

namespace CheckpointingAndDurability
{
    /// <summary>
    /// Entry point that starts a checkpointed Magentic-One run, lets it pause for plan review,
    /// then simulates a process restart before restoring and completing the run.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// The durable session identifier a real application would persist somewhere (a database
        /// row, a queue message) rather than hold in memory - this and the checkpoint store on
        /// disk are the only things that survive the simulated restart in this lesson.
        /// </summary>
        private const string SessionId = "saturn-report-session";

        /// <summary>
        /// The user request this lesson runs, identical to Lesson 16's request so the only new
        /// variable this lesson introduces is the restart itself.
        /// </summary>
        private const string Request = "Get two little-known facts about the planet Saturn from " +
            "the researcher, then have the writer turn them into one short engaging paragraph.";

        /// <summary>
        /// Clears any checkpoints from a prior run of this lesson, starts a fresh checkpointed
        /// run through the pause point, simulates a process restart, then restores and completes
        /// the run from disk alone.
        /// </summary>
        private static async Task Main(string[] args)
        {
            DirectoryInfo checkpointDirectory = new(Path.Combine(AppContext.BaseDirectory, "checkpoints"));
            if (checkpointDirectory.Exists)
            {
                checkpointDirectory.Delete(recursive: true);
            }
            checkpointDirectory.Create();

            CheckpointManager checkpointManager = CheckpointManager.CreateJson(
                new FileSystemJsonCheckpointStore(checkpointDirectory), customOptions: null);

            Console.WriteLine("=== Phase 1: start a checkpointed run, let it pause for plan review ===");
            await RunUntilPausedAsync(checkpointManager);

            Console.WriteLine();
            Console.WriteLine("=== Simulating a process restart ===");
            Console.WriteLine("(the Run, Workflow, and every agent object from Phase 1 are now gone;");
            Console.WriteLine($" only checkpointManager and the session id \"{SessionId}\" remain)");
            Console.WriteLine();

            Console.WriteLine("=== Phase 2: restore from disk and complete the run ===");
            await RestoreAndCompleteAsync(checkpointManager);
        }

        /// <summary>
        /// Builds a fresh Magentic-One workflow, runs it with checkpointing enabled, and reports
        /// the checkpoint that was automatically captured when the run paused for plan review.
        /// </summary>
        private static async Task RunUntilPausedAsync(CheckpointManager checkpointManager)
        {
            IChatClient chatClient = ChatClientFactory.Create();
            Workflow workflow = MagenticWorkflowFactory.Build(chatClient);

            Run run = await InProcessExecution.RunAsync(
                workflow, new ChatMessage(ChatRole.User, Request), checkpointManager, SessionId);

            Console.WriteLine($"IsCheckpointingEnabled: {run.IsCheckpointingEnabled}");
            Console.WriteLine($"Checkpoints captured so far: {run.Checkpoints.Count}");
            Console.WriteLine($"Latest checkpoint: {run.LastCheckpoint}");

            List<WorkflowEvent> events = run.NewEvents.ToList();
            RequestInfoEvent? pending = TryGetPendingRequest(events);
            if (pending is not null && pending.Request.TryGetDataAs(out MagenticPlanReviewRequest? planReview))
            {
                Console.WriteLine();
                Console.WriteLine("--- Paused, awaiting plan review ---");
                Console.WriteLine(planReview!.Plan.Text);
            }
        }

        /// <summary>
        /// Looks up the latest checkpoint for this lesson's session id, rebuilds the workflow
        /// from scratch, resumes from that checkpoint, and approves the still-pending plan review
        /// to let the run complete.
        /// </summary>
        private static async Task RestoreAndCompleteAsync(CheckpointManager checkpointManager)
        {
            CheckpointInfo? latestCheckpoint = await checkpointManager.GetLatestCheckpointAsync(SessionId);
            if (latestCheckpoint is null)
            {
                Console.WriteLine($"No checkpoint found for session \"{SessionId}\".");
                return;
            }
            Console.WriteLine($"GetLatestCheckpointAsync(\"{SessionId}\") -> {latestCheckpoint}");

            IChatClient chatClient = ChatClientFactory.Create();
            Workflow workflow = MagenticWorkflowFactory.Build(chatClient);

            Run run = await InProcessExecution.ResumeAsync(workflow, latestCheckpoint, checkpointManager);

            List<WorkflowEvent> restoredEvents = run.NewEvents.ToList();
            RequestInfoEvent? pending = TryGetPendingRequest(restoredEvents);
            if (pending is null || !pending.Request.TryGetDataAs(out MagenticPlanReviewRequest? planReview))
            {
                Console.WriteLine("Expected a restored plan-review request but found none.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("--- Restored plan-review request, identical to Phase 1's pause ---");
            Console.WriteLine(planReview!.Plan.Text);
            Console.WriteLine();
            Console.WriteLine("Approving and resuming...");

            ExternalResponse response = pending.Request.CreateResponse(planReview.Approve());
            await run.ResumeAsync([response]);

            Console.WriteLine();
            Console.WriteLine("--- Final answer ---");
            foreach (WorkflowEvent evt in run.NewEvents.ToList())
            {
                if (evt is WorkflowOutputEvent outputEvent && outputEvent.Data is List<ChatMessage> transcript)
                {
                    foreach (ChatMessage message in transcript)
                    {
                        string author = string.IsNullOrEmpty(message.AuthorName) ? message.Role.ToString() : message.AuthorName;
                        Console.WriteLine($"  [{author}] {message.Text}");
                    }
                }
            }
        }

        /// <summary>
        /// Finds the most recent pending external request in a batch of workflow events, or null
        /// if the run did not pause during that batch. Callers pass an already-materialized list
        /// since Run.NewEvents is a single-pass sequence that would otherwise be exhausted by the
        /// first caller to enumerate it.
        /// </summary>
        private static RequestInfoEvent? TryGetPendingRequest(IReadOnlyList<WorkflowEvent> events)
        {
            foreach (WorkflowEvent evt in events)
            {
                if (evt is RequestInfoEvent requestEvent)
                {
                    return requestEvent;
                }
            }

            return null;
        }
    }
}
