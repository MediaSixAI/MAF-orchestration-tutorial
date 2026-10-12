namespace CapstoneBuild
{
    /// <summary>
    /// The message the research step sends along the graph's first edge: the topic that was
    /// researched and the Researcher agent's raw, unstructured notes about it.
    /// </summary>
    internal sealed record ResearchNotes(string Topic, string Notes);
}
