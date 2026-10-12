using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Middleware
{
    /// <summary>
    /// Function-calling middleware that logs the name of every tool the agent decides to
    /// invoke, along with the result it gets back. This sits inside the agent's function
    /// invocation loop, so it only fires when a tool call actually happens.
    /// </summary>
    internal static class FunctionCallLoggingMiddleware
    {
        /// <summary>
        /// Logs the function name before invoking it via <paramref name="next"/>, then logs the
        /// result before returning it back up the chain.
        /// </summary>
        internal static async ValueTask<object?> LogFunctionCallAsync(
            AIAgent agent,
            FunctionInvocationContext context,
            Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
            CancellationToken cancellationToken)
        {
            Console.WriteLine($"  [Tool] -> calling '{context.Function.Name}'.");
            object? result = await next(context, cancellationToken);
            Console.WriteLine($"  [Tool] <- '{context.Function.Name}' returned: {result}");
            return result;
        }
    }
}
