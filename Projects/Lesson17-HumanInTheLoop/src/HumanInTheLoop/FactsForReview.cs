namespace HumanInTheLoop
{
    /// <summary>
    /// The request a human reviewer receives from the workflow's "FactReview" request port: the
    /// topic, the facts ResearchAgent produced for it, and which research round they came from.
    /// The reviewer answers by calling <see cref="Approve"/> or <see cref="Revise"/>, so the
    /// response always carries everything the next executor needs.
    /// </summary>
    /// <param name="Topic">The topic the workflow was started with.</param>
    /// <param name="Facts">The facts ResearchAgent produced, as a short bullet list.</param>
    /// <param name="Round">1 for the first research pass, incremented on every revision.</param>
    internal sealed record FactsForReview(string Topic, string Facts, int Round)
    {
        /// <summary>
        /// Builds the decision that releases these facts to WriterAgent.
        /// </summary>
        internal ReviewDecision Approve()
        {
            return new ReviewDecision(this.Topic, this.Facts, this.Round, Approved: true, Feedback: null);
        }

        /// <summary>
        /// Builds the decision that sends these facts back to ResearchAgent, together with the
        /// reviewer's feedback explaining what to change.
        /// </summary>
        internal ReviewDecision Revise(string feedback)
        {
            return new ReviewDecision(this.Topic, this.Facts, this.Round, Approved: false, Feedback: feedback);
        }
    }
}
