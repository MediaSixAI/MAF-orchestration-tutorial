using CapstoneDeployAndReview;
using Microsoft.Extensions.AI;
using Xunit;

namespace Maf.Tests
{
    /// <summary>
    /// Verifies ChatClientFactory's provider selection without calling any model. Every lesson's
    /// copy is identical apart from its namespace, so testing Lesson 24's copy covers them all.
    /// Ours, not the course's.
    /// </summary>
    [Collection(EnvironmentCollection.Name)]
    public sealed class ChatClientFactoryTests : IDisposable
    {
        private static readonly string[] Variables =
        [
            "MAF_LLM", "MAF_MODEL", "OPENAI_API_KEY", "AZURE_OPENAI_ENDPOINT", "AZURE_OPENAI_API_KEY", "GEMINI_API_KEY",
        ];

        private readonly Dictionary<string, string?> saved = Variables.ToDictionary(v => v, Environment.GetEnvironmentVariable);

        /// <summary>
        /// Clears every provider variable so each test starts from a known environment.
        /// </summary>
        public ChatClientFactoryTests()
        {
            foreach (string variable in Variables)
            {
                Environment.SetEnvironmentVariable(variable, null);
            }
        }

        /// <summary>
        /// Restores the environment the test run started with.
        /// </summary>
        public void Dispose()
        {
            foreach ((string variable, string? value) in saved)
            {
                Environment.SetEnvironmentVariable(variable, value);
            }
        }

        /// <summary>
        /// OPENAI targets api.openai.com with the model named by MAF_MODEL.
        /// </summary>
        [Fact]
        public void OpenAIUsesMafModel()
        {
            Environment.SetEnvironmentVariable("MAF_LLM", "OPENAI");
            Environment.SetEnvironmentVariable("MAF_MODEL", "gpt-4.1-mini");
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

            ChatClientMetadata metadata = Metadata(ChatClientFactory.Create());

            Assert.Equal("gpt-4.1-mini", metadata.DefaultModelId);
            Assert.Equal("api.openai.com", metadata.ProviderUri?.Host);
        }

        /// <summary>
        /// MAF_LLM has no default: a missing provider is named in the error.
        /// </summary>
        [Fact]
        public void MissingProviderIsNamed()
        {
            Environment.SetEnvironmentVariable("MAF_MODEL", "gpt-4.1-mini");
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(ChatClientFactory.Create);
            Assert.Contains("MAF_LLM is not set", error.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// MAF_MODEL has no default: a missing model is named in the error.
        /// </summary>
        [Fact]
        public void MissingModelIsNamed()
        {
            Environment.SetEnvironmentVariable("MAF_LLM", "OPENAI");
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(ChatClientFactory.Create);
            Assert.Contains("MAF_MODEL is not set", error.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// The selected provider's missing key is named rather than failing inside the SDK.
        /// </summary>
        [Fact]
        public void MissingKeyIsNamed()
        {
            Environment.SetEnvironmentVariable("MAF_LLM", "OPENAI");
            Environment.SetEnvironmentVariable("MAF_MODEL", "gpt-4.1-mini");

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(ChatClientFactory.Create);
            Assert.Contains("OPENAI_API_KEY is not set", error.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// An unrecognised provider is refused with the list of valid values.
        /// </summary>
        [Fact]
        public void UnknownProviderIsRefused()
        {
            Environment.SetEnvironmentVariable("MAF_LLM", "CLAUDE");
            Environment.SetEnvironmentVariable("MAF_MODEL", "some-model");

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(ChatClientFactory.Create);
            Assert.Contains("OPENAI, AZURE, GEMINI, OLLAMA or LMSTUDIO", error.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// Ollama needs no key, matches MAF_LLM case-insensitively and uses the model named by MAF_MODEL.
        /// </summary>
        [Fact]
        public void OllamaNeedsNoKeyAndUsesMafModel()
        {
            Environment.SetEnvironmentVariable("MAF_LLM", "ollama");
            Environment.SetEnvironmentVariable("MAF_MODEL", "qwen3:8b");

            ChatClientMetadata metadata = Metadata(ChatClientFactory.Create());

            Assert.Equal("qwen3:8b", metadata.DefaultModelId);
            Assert.Equal(11434, metadata.ProviderUri?.Port);
        }

        /// <summary>
        /// LM Studio needs no key, targets its local server on port 1234 and uses the model named by MAF_MODEL.
        /// </summary>
        [Fact]
        public void LmStudioNeedsNoKeyAndUsesMafModel()
        {
            Environment.SetEnvironmentVariable("MAF_LLM", "LMSTUDIO");
            Environment.SetEnvironmentVariable("MAF_MODEL", "qwen/qwen3-8b");

            ChatClientMetadata metadata = Metadata(ChatClientFactory.Create());

            Assert.Equal("qwen/qwen3-8b", metadata.DefaultModelId);
            Assert.Equal("localhost", metadata.ProviderUri?.Host);
            Assert.Equal(1234, metadata.ProviderUri?.Port);
        }

        /// <summary>
        /// A Gemini 3 model is refused before the key is read, and the error names gemini-2.5-flash.
        /// </summary>
        [Fact]
        public void GeminiThreeModelIsRefused()
        {
            Environment.SetEnvironmentVariable("MAF_LLM", "GEMINI");
            Environment.SetEnvironmentVariable("MAF_MODEL", "gemini-3.1-flash-lite");

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(ChatClientFactory.Create);
            Assert.Contains("gemini-3.1-flash-lite", error.Message, StringComparison.Ordinal);
            Assert.Contains("gemini-2.5-flash", error.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// Gemini 2.5 with a key targets Google's OpenAI-compatible endpoint.
        /// </summary>
        [Fact]
        public void GeminiTwoPointFiveUsesGoogleEndpoint()
        {
            Environment.SetEnvironmentVariable("MAF_LLM", "GEMINI");
            Environment.SetEnvironmentVariable("MAF_MODEL", "gemini-2.5-flash");
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "test-key");

            ChatClientMetadata metadata = Metadata(ChatClientFactory.Create());

            Assert.Equal("gemini-2.5-flash", metadata.DefaultModelId);
            Assert.Equal("generativelanguage.googleapis.com", metadata.ProviderUri?.Host);
        }

        /// <summary>
        /// Azure builds its v1 endpoint from AZURE_OPENAI_ENDPOINT and refuses to run without it.
        /// </summary>
        [Fact]
        public void AzureRequiresAndUsesItsEndpoint()
        {
            Environment.SetEnvironmentVariable("MAF_LLM", "AZURE");
            Environment.SetEnvironmentVariable("MAF_MODEL", "gpt-4.1-mini");
            Environment.SetEnvironmentVariable("AZURE_OPENAI_API_KEY", "test-key");
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(ChatClientFactory.Create);
            Assert.Contains("AZURE_OPENAI_ENDPOINT is not set", error.Message, StringComparison.Ordinal);

            Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", "https://example.openai.azure.com/");
            ChatClientMetadata metadata = Metadata(ChatClientFactory.Create());

            Assert.Equal("/openai/v1/", metadata.ProviderUri?.AbsolutePath);
            Assert.Equal("gpt-4.1-mini", metadata.DefaultModelId);
        }

        private static ChatClientMetadata Metadata(IChatClient client) =>
            client.GetService<ChatClientMetadata>() ?? throw new InvalidOperationException("No metadata.");
    }
}
