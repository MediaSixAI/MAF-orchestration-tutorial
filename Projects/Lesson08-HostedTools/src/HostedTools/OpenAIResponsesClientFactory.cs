using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace HostedTools
{
    /// <summary>
    /// Builds the <see cref="IChatClient"/> for this lesson over OpenAI's Responses API, wrapped by
    /// the Microsoft.Extensions.AI.OpenAI package's AsIChatClient() adapter. Every other lesson uses
    /// ChatClientFactory, which talks to the Chat Completions API and can switch provider via
    /// MAF_LLM. Hosted tools are different: web search and code execution are run by OpenAI itself,
    /// and the Responses API is where OpenAI runs them, so this lesson is OpenAI-only.
    /// </summary>
    internal static class OpenAIResponsesClientFactory
    {
        /// <summary>
        /// Creates the Responses API chat client for the model named by MAF_MODEL. Throws if MAF_LLM,
        /// MAF_MODEL or OPENAI_API_KEY is missing or empty, or if MAF_LLM is not OPENAI.
        /// </summary>
        internal static IChatClient Create()
        {
            Func<string, string> requiredVariable = name =>
                Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                    ? value
                    : throw new InvalidOperationException($"{name} is not set. Set it before running this lesson.");

            string provider = requiredVariable("MAF_LLM");
            if (!provider.Equals("OPENAI", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"MAF_LLM is '{provider}', but hosted tools run only on OpenAI's Responses API. " +
                    "Set MAF_LLM to OPENAI for this lesson.");
            }

            string modelId = requiredVariable("MAF_MODEL");
            string apiKey = requiredVariable("OPENAI_API_KEY");

            // The Responses client and its IChatClient adapter are experimental OpenAI APIs; this opts in.
#pragma warning disable OPENAI001
            return new OpenAIClient(new ApiKeyCredential(apiKey))
                .GetResponsesClient()
                .AsIChatClient(modelId);
#pragma warning restore OPENAI001
        }
    }
}
