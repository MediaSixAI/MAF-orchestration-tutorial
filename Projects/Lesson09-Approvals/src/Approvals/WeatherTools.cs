using System.ComponentModel;

namespace Approvals
{
    /// <summary>
    /// A local C# function tool exposed to the agent. This lesson wraps it in an
    /// <see cref="Microsoft.Extensions.AI.ApprovalRequiredAIFunction"/> so every call to it
    /// pauses for approval before it runs - the tool itself has no idea it is gated.
    /// </summary>
    internal static class WeatherTools
    {
        private static readonly Dictionary<string, string> ConditionsByCity = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Amsterdam"] = "cloudy with a high of 15C",
            ["Tokyo"] = "clear with a high of 28C",
        };

        /// <summary>
        /// Gets a fixed, fake weather condition for a known city. Unknown cities produce a
        /// clear error string instead of a guess.
        /// </summary>
        /// <param name="location">The city to get the weather for.</param>
        [Description("Gets the weather for a given location.")]
        internal static string GetWeather(
            [Description("The location to get the weather for.")] string location)
        {
            if (!ConditionsByCity.TryGetValue(location, out string? condition))
            {
                return $"No weather data is available for '{location}'.";
            }

            return $"The weather in {location} is {condition}.";
        }
    }
}
