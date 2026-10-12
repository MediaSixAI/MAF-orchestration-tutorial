using Xunit;

namespace Maf.Tests
{
    /// <summary>
    /// Serializes tests that change process-wide environment variables. Ours, not the course's.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class EnvironmentCollection
    {
        /// <summary>
        /// The collection name shared by every environment-mutating test class.
        /// </summary>
        public const string Name = "Environment";
    }
}
