using System.ComponentModel;

namespace FunctionTools
{
    /// <summary>
    /// Local C# functions exposed to the agent as function tools. Each public method here is
    /// wrapped with <c>AIFunctionFactory.Create</c> in Program.cs to become something the model
    /// can choose to call. The <see cref="DescriptionAttribute"/> on each method and parameter is
    /// not decoration - it is sent to the model as part of the tool's schema and is how the model
    /// decides when and how to call it.
    /// </summary>
    internal static class TravelTools
    {
        private static readonly Dictionary<string, double> CityUtcOffsetHours = new(StringComparer.OrdinalIgnoreCase)
        {
            ["London"] = 0,
            ["Tokyo"] = 9,
            ["New York"] = -4,
            ["Sydney"] = 10,
            ["Nairobi"] = 3,
        };

        private static readonly Dictionary<string, double> UsdExchangeRates = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 1.0,
            ["EUR"] = 0.92,
            ["GBP"] = 0.79,
            ["JPY"] = 149.5,
            ["KES"] = 129.0,
        };

        /// <summary>
        /// Returns the current local time for one of a small set of known cities, computed from
        /// a fixed UTC offset. Unknown cities produce a clear error string instead of a guess.
        /// </summary>
        /// <param name="city">The city to look up the local time for.</param>
        [Description("Gets the current local time for a known city.")]
        internal static string GetCurrentTimeInCity(
            [Description("The city name, e.g. 'Tokyo' or 'New York'.")] string city)
        {
            if (!CityUtcOffsetHours.TryGetValue(city, out double offsetHours))
            {
                return $"No time zone data is available for '{city}'.";
            }

            DateTime localTime = DateTime.UtcNow.AddHours(offsetHours);
            return $"The current local time in {city} is {localTime:yyyy-MM-dd HH:mm} (UTC{offsetHours:+0;-0}).";
        }

        /// <summary>
        /// Converts an amount between two currencies using a small fixed table of exchange rates
        /// against USD. Unknown currency codes produce a clear error string instead of a guess.
        /// </summary>
        /// <param name="amount">The amount to convert.</param>
        /// <param name="fromCurrencyCode">The three-letter currency code to convert from.</param>
        /// <param name="toCurrencyCode">The three-letter currency code to convert to.</param>
        [Description("Converts an amount of money from one currency to another.")]
        internal static string ConvertCurrency(
            [Description("The amount of money to convert.")] double amount,
            [Description("The three-letter currency code to convert from, e.g. 'USD'.")] string fromCurrencyCode,
            [Description("The three-letter currency code to convert to, e.g. 'EUR'.")] string toCurrencyCode)
        {
            if (!UsdExchangeRates.TryGetValue(fromCurrencyCode, out double fromRate) ||
                !UsdExchangeRates.TryGetValue(toCurrencyCode, out double toRate))
            {
                return $"No exchange rate data is available for '{fromCurrencyCode}' or '{toCurrencyCode}'.";
            }

            double amountInUsd = amount / fromRate;
            double converted = amountInUsd * toRate;
            return $"{amount:0.##} {fromCurrencyCode} is approximately {converted:0.##} {toCurrencyCode}.";
        }
    }
}
