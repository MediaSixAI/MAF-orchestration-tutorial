// Lesson 22 - Capstone Design
// Designs the Lesson 22-24 capstone - one graph workflow: Researcher -> Analyst -> human-approval
// request port -> Writer, checkpointed, and later hosted over the Responses protocol - and tests
// its riskiest seam before Lesson 23 builds the graph. The seam test runs the Researcher and the
// Analyst against a live model, parses the Analyst's reply into the AnalysisResult contract, and
// builds the approval request the graph's approval port will carry. It stops there: the pause
// itself and the Writer belong to the graph.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CapstoneDesign
{
    /// <summary>
    /// Entry point that prints the capstone's graph, then runs the Researcher-to-Analyst seam
    /// test and shows the approval request its result produces.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Prints the design, runs the Researcher and Analyst on the configured topic, parses the
        /// analysis, and prints the approval request built from it.
        /// </summary>
        private static async Task Main(string[] args)
        {
            PipelineConfig config = PipelineConfig.Load();

            Console.WriteLine("=== Capstone graph ===");
            Console.WriteLine("research (Researcher + MCP tools) -> analysis (Analyst -> AnalysisResult)");
            Console.WriteLine("  -> report-approval (request port: ToolApprovalRequestContent / ToolApprovalResponseContent)");
            Console.WriteLine("  -> writer (Writer -> final report)        checkpointed at every step");
            Console.WriteLine($"Research topic: {config.ResearchTopic}");
            Console.WriteLine();

            IChatClient chatClient = ChatClientFactory.Create();
            ChatClientAgent researcher = AgentFactory.CreateAgent(
                chatClient, AgentInstructions.ResearcherName, AgentInstructions.ResearcherPersona);
            ChatClientAgent analyst = AgentFactory.CreateAgent(
                chatClient, AgentInstructions.AnalystName, AgentInstructions.AnalystPersona);

            Console.WriteLine("=== Seam test: Researcher -> Analyst -> AnalysisResult ===");
            AgentResponse notes = await researcher.RunAsync(config.ResearchTopic);
            Console.WriteLine("Researcher notes:");
            Console.WriteLine(notes.Text);
            Console.WriteLine();

            AgentResponse reply = await analyst.RunAsync(
                $"Topic: {config.ResearchTopic}\n\nResearch notes:\n{notes.Text}");
            AnalysisResult analysis = AnalysisParser.Parse(reply.Text);
            Console.WriteLine($"Parsed AnalysisResult with {analysis.KeyFindings.Count} key finding(s).");
            Console.WriteLine();

            ToolApprovalRequestContent request = ReportApprovalRequest.Create(analysis);
            Console.WriteLine("=== What the approval port will carry ===");
            Console.WriteLine($"Action: {ReportApprovalRequest.ActionName}");
            Console.WriteLine(ReportApprovalRequest.Describe(request));
        }
    }
}
