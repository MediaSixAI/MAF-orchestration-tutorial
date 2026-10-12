using System.ComponentModel;

namespace Middleware
{
    /// <summary>
    /// A single local C# function tool exposed to the agent. It exists in this lesson to give
    /// the function-calling middleware something real to intercept - the middleware itself is
    /// the point of the lesson, not this lookup logic.
    /// </summary>
    internal static class OrderTools
    {
        private static readonly Dictionary<string, string> OrderStatusesById = new(StringComparer.OrdinalIgnoreCase)
        {
            ["A1001"] = "Shipped, arriving in 2 days.",
            ["A1002"] = "Processing, has not yet shipped.",
            ["A1003"] = "Delivered.",
        };

        /// <summary>
        /// Looks up the shipping status for a known order ID. Unknown order IDs produce a clear
        /// error string instead of a guess.
        /// </summary>
        /// <param name="orderId">The order ID to look up, e.g. 'A1001'.</param>
        [Description("Gets the current shipping status for a given order ID.")]
        internal static string GetOrderStatus(
            [Description("The order ID to look up, e.g. 'A1001'.")] string orderId)
        {
            if (!OrderStatusesById.TryGetValue(orderId, out string? status))
            {
                return $"No order found with ID '{orderId}'.";
            }

            return status;
        }
    }
}
