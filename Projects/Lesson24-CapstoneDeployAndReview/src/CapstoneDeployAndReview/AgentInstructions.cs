namespace CapstoneDeployAndReview
{
    /// <summary>
    /// Shared persona names and system instructions for the capstone's three agent roles -
    /// Researcher, Analyst, Writer. Lesson 22 defines them; Lessons 23 and 24 add the
    /// Researcher's instruction to use its MCP research tool.
    /// </summary>
    internal static class AgentInstructions
    {
        /// <summary>
        /// Display name for the Researcher role.
        /// </summary>
        internal const string ResearcherName = "ResearcherAgent";

        /// <summary>
        /// Researcher persona: gathers factual notes about a topic using the MCP research tool
        /// it is given, and reports only what that tool returned.
        /// </summary>
        internal const string ResearcherPersona =
            "You are a meticulous researcher. Given a topic, use the SearchResearchNotes tool to " +
            "look up factual notes about it - never invent facts yourself. Report back two or " +
            "three concise notes, each with its source, based only on what the tool returned.";

        /// <summary>
        /// Display name for the Analyst role.
        /// </summary>
        internal const string AnalystName = "AnalystAgent";

        /// <summary>
        /// Analyst persona: extracts the Researcher's raw notes into strict JSON matching the
        /// <see cref="AnalysisResult"/> shape - the prompt contract AnalysisParser reads. The
        /// wording forbids markdown fences, and JsonFenceStripper removes one if a model adds it.
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
