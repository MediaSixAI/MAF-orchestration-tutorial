// Lesson 24 - Capstone Deploy & Review (client)
// Drives the hosted capstone over plain HTTP, the way any Responses protocol client can. The first
// POST /responses starts a run on a topic; the reply shows each graph step that ran and ends with
// an mcp_approval_request. After the decision is read from the console, a second POST quotes the
// first reply's id as previous_response_id and answers with an mcp_approval_response, and the same
// run continues to the Writer. Start CapstoneDeployAndReview (and build its companion
// ResearchMcpServer) first; this client fails to connect if the host is not running.

using System.Text;
using System.Text.Json;

namespace CapstoneDeployAndReviewClient
{
    /// <summary>
    /// Entry point that starts a hosted run, shows the approval request, sends the console
    /// decision back, and prints the final report.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Base address of the running CapstoneDeployAndReview host.
        /// </summary>
        private const string HostUrl = "http://localhost:5200/";

        /// <summary>
        /// The topic researched when none is given on the command line.
        /// </summary>
        private const string DefaultTopic = "the deep sea anglerfish";

        /// <summary>
        /// Starts a run, prints the approval request it pauses on, asks for the decision, sends
        /// it back in a follow-up request, and prints the result.
        /// </summary>
        private static async Task Main(string[] args)
        {
            string topic = args.Length > 0 ? string.Join(" ", args) : DefaultTopic;

            // A run calls the model several times, so allow more than HttpClient's 100-second default.
            using HttpClient http = new() { BaseAddress = new Uri(HostUrl), Timeout = TimeSpan.FromMinutes(5) };

            Console.WriteLine($"=== POST /responses: research '{topic}' ===");
            using JsonDocument first = await PostAsync(http, ResponsesRequests.CreateStart(topic));
            PrintSteps(first.RootElement);

            ApprovalRequestItem? request = ResponsesReader.FindApprovalRequest(first.RootElement);
            if (request is null)
            {
                Console.WriteLine("The run did not ask for approval. Output:");
                Console.WriteLine(ResponsesReader.ReadOutputText(first.RootElement));
                return;
            }

            Console.WriteLine();
            Console.WriteLine("=== Paused for approval ===");
            Console.WriteLine(ResponsesReader.Describe(request));
            Console.WriteLine();
            Console.WriteLine("The pending run is checkpointed by the host: you can stop and restart the");
            Console.WriteLine("host now, and this answer still resumes the same run.");
            Console.Write("Approve this analysis for the final report? [y/N] ");
            bool approve = string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);

            Console.WriteLine();
            Console.WriteLine($"=== POST /responses: answer the approval request ({(approve ? "approve" : "reject")}) ===");
            string reply = ResponsesRequests.CreateApprovalReply(
                ResponsesReader.ReadResponseId(first.RootElement), request.Id, approve);
            using JsonDocument second = await PostAsync(http, reply);
            PrintSteps(second.RootElement);

            Console.WriteLine();
            Console.WriteLine("=== Result ===");
            Console.WriteLine(ResponsesReader.ReadOutputText(second.RootElement));
        }

        /// <summary>
        /// Posts a JSON body to /responses and returns the parsed reply. Throws with the host's
        /// error text if the request fails.
        /// </summary>
        private static async Task<JsonDocument> PostAsync(HttpClient http, string body)
        {
            using StringContent content = new(body, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await http.PostAsync("responses", content);
            string json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"POST /responses returned {(int)response.StatusCode}: {json}");
            }

            return JsonDocument.Parse(json);
        }

        /// <summary>
        /// Prints the graph steps that completed in a reply, in order.
        /// </summary>
        private static void PrintSteps(JsonElement response)
        {
            Console.WriteLine($"Steps completed: {string.Join(" -> ", ResponsesReader.ReadCompletedSteps(response))}");
        }
    }
}
