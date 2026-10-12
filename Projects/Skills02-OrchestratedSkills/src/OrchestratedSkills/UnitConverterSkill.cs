using System.ComponentModel;
using System.Text.Json;
using Microsoft.Agents.AI;

namespace OrchestratedSkills
{
    /// <summary>
    /// A class-based Agent Skill that converts between common units using a lookup table of
    /// multiplication factors. Packaging it as a class - rather than a SKILL.md folder - shows
    /// how a skill can ship inside a normal C# assembly, and eventually a NuGet package, with its
    /// resource and script bundled alongside it as attributed members.
    /// </summary>
    internal sealed class UnitConverterSkill : AgentClassSkill<UnitConverterSkill>
    {
        /// <summary>
        /// The name and description advertised to the agent before the skill is loaded.
        /// </summary>
        public override AgentSkillFrontmatter Frontmatter { get; } = new(
            "unit-converter",
            "Convert between common units (miles, kilometers, pounds, kilograms) using a " +
                "multiplication factor. Use when asked to convert distances or weights between these units.");

        /// <summary>
        /// The step-by-step instructions the agent receives after calling load_skill for this skill.
        /// </summary>
        protected override string Instructions => """
            Use this skill when the user asks to convert between miles, kilometers, pounds, or kilograms.

            1. Review the conversion-table resource to find the correct factor for the requested units.
            2. Call the convert script, passing the value and factor from the table.
            3. Present the result clearly, showing both the original and converted values with their units.
            """;

        /// <summary>
        /// Lookup table of multiplication factors for the unit pairs this skill supports.
        /// </summary>
        [AgentSkillResource("conversion-table")]
        [Description("Lookup table of multiplication factors for common unit conversions.")]
        public string ConversionTable => """
            # Conversion Table
            Formula: result = value x factor

            | From       | To         | Factor   |
            |------------|------------|----------|
            | miles      | kilometers | 1.60934  |
            | kilometers | miles      | 0.621371 |
            | pounds     | kilograms  | 0.453592 |
            | kilograms  | pounds     | 2.20462  |
            """;

        /// <summary>
        /// Multiplies a value by a conversion factor and returns the result as JSON. Runs
        /// in-process as a direct delegate call - no external script runner is needed.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <param name="factor">The multiplication factor from the conversion table.</param>
        [AgentSkillScript("convert")]
        [Description("Multiplies a value by a conversion factor and returns the result as JSON.")]
        private static string Convert(double value, double factor)
        {
            double result = Math.Round(value * factor, 4);
            return JsonSerializer.Serialize(new { value, factor, result });
        }
    }
}
