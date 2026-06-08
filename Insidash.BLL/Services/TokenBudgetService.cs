using System;

namespace Insidash.BLL.Services
{
    /// <summary>
    /// Protects AI token boundaries by evaluating and safely truncating
    /// the JsonContent from TallySnapshot before it is sent to the LLM.
    /// 
    /// Heuristic: 1 token ≈ 4 characters.
    /// Budget:    ~3,000 tokens → 12,000 characters for context data.
    /// </summary>
    public class TokenBudgetService
    {
        // ~3,000 tokens at 4 chars/token
        private const int MaxContextChars = 12000;

        /// <summary>
        /// Returns the JSON context string, truncated to fit within the token budget.
        /// If the full JSON exceeds the limit, it is cut at the last valid JSON object
        /// boundary to prevent malformed JSON from crashing the AI provider.
        /// </summary>
        /// <param name="fullJson">The complete serialized JSON from TallySnapshot.JsonContent.</param>
        /// <param name="userQuestion">
        /// The user's question — reserved for future keyword-relevance filtering (Phase 7).
        /// Currently unused but kept in the signature to avoid a breaking change later.
        /// </param>
        /// <returns>A JSON string that fits within the token budget.</returns>
        public string BuildContextJson(string fullJson, string userQuestion)
        {
            // Null/empty guard
            if (string.IsNullOrWhiteSpace(fullJson))
                return "{}";

            // If the payload fits comfortably, pass it through unmodified
            if (fullJson.Length <= MaxContextChars)
                return fullJson;

            // --- Safe truncation ---
            // Take the first MaxContextChars characters
            string truncated = fullJson.Substring(0, MaxContextChars);

            // Walk backwards to find the last complete JSON object boundary "},".
            // This ensures we don't cut in the middle of a field value or key.
            int lastObjectEnd = truncated.LastIndexOf("},", StringComparison.Ordinal);

            if (lastObjectEnd > 0)
            {
                // Keep everything up to and including the closing brace, then close the array
                truncated = truncated.Substring(0, lastObjectEnd + 1) + "]";
            }
            else
            {
                // Fallback: if we can't find a clean cut point, try the last standalone '}'
                int lastBrace = truncated.LastIndexOf('}');
                if (lastBrace > 0)
                {
                    truncated = truncated.Substring(0, lastBrace + 1) + "]";
                }
                else
                {
                    // Absolute fallback — return empty context rather than broken JSON
                    truncated = "{}";
                }
            }

            return truncated;
        }
    }
}
