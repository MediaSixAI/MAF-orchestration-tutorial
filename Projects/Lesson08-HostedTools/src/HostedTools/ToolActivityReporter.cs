using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HostedTools
{
    /// <summary>
    /// Prints the evidence of tool use that an agent run leaves in its response messages: local
    /// function calls, hosted web searches, hosted code executions and the sources a web search
    /// cited. An answer alone cannot prove a tool ran - the model could have answered from memory -
    /// so every demo in this lesson prints this report under the answer.
    /// </summary>
    internal static class ToolActivityReporter
    {
        /// <summary>
        /// Writes one line per tool call and per cited source found in the response, or a single
        /// line saying no tool was used.
        /// </summary>
        internal static void Print(AgentResponse response)
        {
            int found = 0;
            foreach (AIContent content in response.Messages.SelectMany(message => message.Contents))
            {
                string? activity = content switch
                {
                    FunctionCallContent call => $"local function call: {call.Name}",
                    WebSearchToolCallContent search => $"hosted web search: {string.Join(", ", search.Queries ?? [])}",
                    CodeInterpreterToolCallContent code => $"hosted code execution:{Environment.NewLine}{DescribeInputs(code.Inputs)}",
                    _ => null,
                };

                if (activity is not null)
                {
                    Console.WriteLine($"  [tool] {activity}");
                    found++;
                }

                foreach (CitationAnnotation citation in content.Annotations?.OfType<CitationAnnotation>() ?? [])
                {
                    Console.WriteLine($"  [source] {citation.Title} - {citation.Url}");
                    found++;
                }
            }

            if (found == 0)
            {
                Console.WriteLine("  [tool] none - the model answered on its own");
            }
        }

        /// <summary>
        /// Renders the code a hosted code execution ran, which arrives as text or as a text-typed
        /// data payload depending on the provider.
        /// </summary>
        private static string DescribeInputs(IList<AIContent>? inputs)
        {
            IEnumerable<string> parts = (inputs ?? []).Select(input => input switch
            {
                TextContent text => text.Text,
                DataContent data when data.MediaType.StartsWith("text/", StringComparison.Ordinal)
                    => Encoding.UTF8.GetString(data.Data.Span),
                _ => $"({input.GetType().Name})",
            });

            return string.Join(Environment.NewLine, parts);
        }
    }
}
