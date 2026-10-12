using System.ComponentModel;
using ModelContextProtocol.Server;

namespace ResearchMcpServer
{
    /// <summary>
    /// MCP tools exposed by this server. Every method here is discovered automatically by
    /// <c>WithToolsFromAssembly</c> via reflection and advertised to any connecting MCP client -
    /// the class and its methods must be public for that reflection-based scan to find them.
    /// </summary>
    [McpServerToolType]
    public static class ResearchTools
    {
        /// <summary>
        /// A small, fixed knowledge base of demo topics, keyed by a lowercase substring the
        /// caller's topic is matched against. A real capstone deployment would replace this with
        /// a call to an actual search index or document store, behind this exact same tool
        /// signature.
        /// </summary>
        private static readonly Dictionary<string, string[]> Notes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["anglerfish"] =
            [
                "Female deep-sea anglerfish use a bioluminescent lure called an esca, powered by symbiotic light-producing bacteria housed in a specialized organ, to attract prey in the pitch-black deep sea. (Source: Encyclopedia of Marine Biology)",
                "In many anglerfish species, the much smaller male permanently fuses to the female's body after mating, becoming a parasitic sperm donor sustained by her bloodstream for the rest of his life. (Source: Encyclopedia of Marine Biology)",
            ],
            ["octopus"] =
            [
                "Octopuses have three hearts: two pump blood through the gills, while the third circulates it to the rest of the body. (Source: Cephalopod Biology Handbook)",
                "An octopus's arms contain roughly two-thirds of its neurons, allowing each arm to process sensory information and react semi-independently of the brain. (Source: Cephalopod Biology Handbook)",
            ],
            ["narwhal"] =
            [
                "A narwhal's tusk is an elongated left canine tooth that can grow up to 3 meters long and is believed to serve sensory as well as display functions. (Source: Arctic Marine Mammal Survey)",
                "Narwhals can dive to depths beyond 1,500 meters, among the deepest of any marine mammal, in search of squid and fish. (Source: Arctic Marine Mammal Survey)",
            ],
        };

        /// <summary>
        /// Searches the internal knowledge base for factual notes matching the given topic. If
        /// no dedicated notes exist for that topic, returns a message telling the caller so,
        /// rather than fabricating an answer.
        /// </summary>
        [McpServerTool, Description("Searches a small internal research knowledge base for factual notes about a topic.")]
        public static string SearchResearchNotes(
            [Description("The topic to search for, e.g. 'anglerfish'.")] string topic)
        {
            foreach ((string key, string[] notes) in Notes)
            {
                if (topic.Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Join("\n", notes);
                }
            }

            return $"No dedicated notes on file for '{topic}'. Draw on general knowledge instead, " +
                "and clearly note that these facts are not independently sourced.";
        }
    }
}
