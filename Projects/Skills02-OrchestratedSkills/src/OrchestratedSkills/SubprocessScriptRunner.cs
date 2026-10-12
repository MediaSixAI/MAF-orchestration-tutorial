using System.Diagnostics;
using System.Text.Json;
using Microsoft.Agents.AI;

namespace OrchestratedSkills
{
    /// <summary>
    /// Runs a file-based skill's scripts (the files in its <c>scripts/</c> folder) as local
    /// subprocesses. The script's extension picks the interpreter, the JSON array of arguments the
    /// model sends becomes positional command-line arguments, and the script's standard output,
    /// standard error and exit code come back as the tool result. Class-based and inline skill
    /// scripts are C# delegates and never pass through this runner.
    /// </summary>
    internal static class SubprocessScriptRunner
    {
        /// <summary>
        /// Starts the script in its own folder with the interpreter that matches its extension
        /// (.py python3, .js node, .sh bash, .ps1 pwsh; anything else is started directly), waits
        /// for it to exit, and returns its standard output followed by any standard error and a
        /// non-zero exit code. Matches the <see cref="AgentFileSkillScriptRunner"/> delegate so it
        /// can be passed to <c>UseFileScriptRunner</c>.
        /// </summary>
        public static async Task<object?> RunAsync(
            AgentFileSkill skill,
            AgentFileSkillScript script,
            JsonElement? arguments,
            IServiceProvider? serviceProvider,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(script.FullPath))
            {
                return $"Error: Script file not found: {script.FullPath}";
            }

            string? interpreter = Path.GetExtension(script.FullPath) switch
            {
                ".py" => "python3",
                ".js" => "node",
                ".sh" => "bash",
                ".ps1" => "pwsh",
                _ => null,
            };

            ProcessStartInfo startInfo = new()
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(script.FullPath) ?? ".",
            };

            if (interpreter is not null)
            {
                startInfo.FileName = interpreter;
                startInfo.ArgumentList.Add(script.FullPath);
            }
            else
            {
                startInfo.FileName = script.FullPath;
            }

            if (arguments is { ValueKind: JsonValueKind.Array } argumentArray)
            {
                foreach (JsonElement element in argumentArray.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.String)
                    {
                        throw new InvalidOperationException(
                            $"Script arguments must be JSON strings, but received a {element.ValueKind}.");
                    }

                    startInfo.ArgumentList.Add(element.GetString()!);
                }
            }
            else if (arguments is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } other)
            {
                throw new InvalidOperationException(
                    $"Script arguments must be a JSON array of strings, but received a {other.ValueKind}.");
            }

            Process? process = null;
            try
            {
                process = Process.Start(startInfo);
                if (process is null)
                {
                    return $"Error: Failed to start a process for script '{script.Name}'.";
                }

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                string output = await outputTask.ConfigureAwait(false);
                string error = await errorTask.ConfigureAwait(false);

                if (!string.IsNullOrEmpty(error))
                {
                    output += $"\nStderr:\n{error}";
                }

                if (process.ExitCode != 0)
                {
                    output += $"\nScript exited with code {process.ExitCode}";
                }

                return string.IsNullOrEmpty(output) ? "(no output)" : output.Trim();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                process?.Kill(entireProcessTree: true);
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return $"Error: Failed to run script '{script.Name}': {ex.Message}";
            }
            finally
            {
                process?.Dispose();
            }
        }
    }
}
