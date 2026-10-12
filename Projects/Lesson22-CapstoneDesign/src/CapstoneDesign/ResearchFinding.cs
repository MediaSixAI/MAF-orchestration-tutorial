namespace CapstoneDesign
{
    /// <summary>
    /// One discrete fact the Researcher agent surfaced, as extracted and structured by the
    /// Analyst agent. Part of the shared data contract defined in Lesson 22's design.
    /// </summary>
    internal sealed record ResearchFinding(string Fact, string Source);
}
