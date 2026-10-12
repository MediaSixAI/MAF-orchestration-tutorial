namespace CapstoneDesign
{
    /// <summary>
    /// Shared persona names and system instructions for the capstone's three agent roles -
    /// Researcher, Analyst, Writer. Defined once here so the Lesson 23 build and the Lesson 24
    /// deployment reuse the same personas rather than redefining them.
    /// </summary>
    internal static class AgentInstructions
    {
        /// <summary>
        /// Display name for the Researcher role.
        /// </summary>
        internal const string ResearcherName = "ResearcherAgent";

        /// <summary>
        /// Researcher persona: gathers factual notes about a topic. This lesson gives it no
        /// tools - Lesson 23 will connect it to a real MCP server following Lesson 7's pattern, so for
        /// now it draws on the model's own background knowledge, keeping this lesson's seam
        /// test fast and dependency-free.
        /// </summary>
        internal const string ResearcherPersona =
            "You are a meticulous researcher. Given a topic, produce two or three concise, " +
            "factual notes about it, each attributed to a specific, plausible-sounding source. " +
            "Keep each note to one or two sentences.";

        /// <summary>
        /// Display name for the Analyst role.
        /// </summary>
        internal const string AnalystName = "AnalystAgent";

        /// <summary>
        /// Analyst persona: extracts the Researcher's raw notes into strict JSON matching the
        /// <see cref="AnalysisResult"/> shape. This wording (one exact JSON shape, no markdown
        /// fences, no commentary) is the prompt contract AnalysisParser reads.
        /// </summary>
        internal const string AnalystPersona = """
            You are an analyst. You will receive raw research notes about a topic.
            Extract the information into STRICT JSON matching exactly this shape, with no
            markdown code fences, no commentary, and no text before or after the JSON:
            {"Topic": string, "Summary": string, "KeyFindings": [{"Fact": string, "Source": string}]}
            Include exactly two items in KeyFindings.
            """;

        /// <summary>
        /// Display name for the Writer role.
        /// </summary>
        internal const string WriterName = "WriterAgent";

        /// <summary>
        /// Writer persona: turns an approved, structured analysis into a short prose report for
        /// a general audience.
        /// </summary>
        internal const string WriterPersona =
            "You are a writer. Given an approved analysis (a topic, a summary, and key findings, " +
            "provided to you as JSON), turn it into a short, engaging report of two or three short " +
            "paragraphs for a general audience. Do not mention that your input was JSON.";
    }
}
