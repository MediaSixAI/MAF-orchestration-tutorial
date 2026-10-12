namespace CapstoneDeployAndReview
{
    /// <summary>
    /// The Analyst agent's structured extraction of the Researcher agent's raw notes - the
    /// contract the human-approval request presents for review and the Writer agent consumes once
    /// it is approved. Defined in Lesson 22's design and used unchanged by every capstone lesson.
    /// </summary>
    internal sealed record AnalysisResult(string Topic, string Summary, List<ResearchFinding> KeyFindings);
}
