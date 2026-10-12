namespace HumanInTheLoop
{
    /// <summary>
    /// The response a human reviewer sends back through the workflow's "FactReview" request port.
    /// The workflow's conditional edges read <see cref="Approved"/> to decide where it goes next:
    /// to WriterExecutor when approved, or back to ResearchExecutor for another round.
    /// </summary>
    /// <param name="Topic">The topic the workflow was started with.</param>
    /// <param name="Facts">The facts that were reviewed.</param>
    /// <param name="Round">The research round the reviewed facts came from.</param>
    /// <param name="Approved">True to release the facts to WriterAgent; false to send them back.</param>
    /// <param name="Feedback">The reviewer's revision feedback, or null when approved.</param>
    internal sealed record ReviewDecision(string Topic, string Facts, int Round, bool Approved, string? Feedback);
}
