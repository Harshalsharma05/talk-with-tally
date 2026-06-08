using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Insidash.BLL.Services
{
    /// <summary>
    /// Core AI runtime manager for TalkWithTally.
    /// 
    /// Responsibilities:
    ///   1. Read provider API keys from Web.config / App.config appSettings.
    ///   2. Build an ordered fallback chain of IAIService providers.
    ///   3. Compile the system instruction prompt with token-budgeted context.
    ///   4. Dispatch the user's question to the first available provider,
    ///      falling through to the next on failure.
    /// </summary>
    public class AIManager
    {
        private readonly List<IAIService> _providers;
        private readonly TokenBudgetService _tokenBudget;

        public AIManager()
        {
            _providers = new List<IAIService>();
            _tokenBudget = new TokenBudgetService();

            // --- Provider registration (order = priority) ---

            // Slot 1: Claude (reserved for future — populate CLAUDE_API_KEY in appSettings to activate)
            string claudeKey = ConfigurationManager.AppSettings["CLAUDE_API_KEY"];
            if (!string.IsNullOrWhiteSpace(claudeKey)
                && !claudeKey.StartsWith("PUT_", StringComparison.OrdinalIgnoreCase))
            {
                // ClaudeAIService will be implemented in a future phase.
                // When ready, uncomment:
                // _providers.Add(new ClaudeAIService(claudeKey));
                Debug.WriteLine("[AIManager] Claude key detected but ClaudeAIService not yet implemented — skipping.");
            }

            // Slot 2: Groq (active)
            string groqKey = ConfigurationManager.AppSettings["GROQ_API_KEY"];
            if (!string.IsNullOrWhiteSpace(groqKey))
            {
                _providers.Add(new GroqAIService(groqKey));
                Debug.WriteLine("[AIManager] Groq provider registered.");
            }

            if (_providers.Count == 0)
            {
                throw new InvalidOperationException(
                    "No AI provider API keys are configured. " +
                    "Add GROQ_API_KEY (and optionally CLAUDE_API_KEY) to the <appSettings> section of Web.config or App.config.");
            }
        }

        /// <summary>
        /// Process a user chat message against the Tally snapshot context.
        /// Tries each provider in priority order until one succeeds.
        /// </summary>
        /// <param name="userMessage">The natural language question from the user.</param>
        /// <param name="fullContextJson">Raw JsonContent from the TallySnapshot table.</param>
        /// <param name="companyName">Display name of the tenant company for the system prompt.</param>
        /// <returns>The AI response result, including which provider handled it.</returns>
        public async Task<AIServiceResult> ProcessChatAsync(
            string userMessage, string fullContextJson, string companyName)
        {
            // Apply token budget constraints to the snapshot data
            string optimizedContext = _tokenBudget.BuildContextJson(fullContextJson, userMessage);

            // Compile the full system instruction
            string systemPrompt = BuildSystemPrompt(companyName, optimizedContext);

            // Walk the fallback chain
            List<string> errors = new List<string>();

            foreach (IAIService provider in _providers)
            {
                Debug.WriteLine(string.Format("[AIManager] Trying provider: {0}", provider.ProviderName));

                AIServiceResult result = await provider.ChatAsync(systemPrompt, userMessage)
                    .ConfigureAwait(false);

                if (result.Success)
                {
                    Debug.WriteLine(string.Format("[AIManager] Success via {0}", provider.ProviderName));
                    return result;
                }

                // Log and continue to next provider
                string errorEntry = string.Format("{0}: {1}", provider.ProviderName, result.ErrorMessage);
                errors.Add(errorEntry);
                Debug.WriteLine(string.Format("[AIManager] Provider {0} failed — {1}", provider.ProviderName, result.ErrorMessage));
            }

            // All providers exhausted
            return new AIServiceResult
            {
                Success = false,
                ErrorMessage = string.Format("All AI providers failed. Details: {0}",
                    string.Join(" | ", errors)),
                Content = "I'm unable to process your request right now. Please try again later.",
                ProviderUsed = "None"
            };
        }

        /// <summary>
        /// Constructs the system instruction that defines TalkWithTally's persona,
        /// behavioral guardrails, and the current snapshot data context.
        /// </summary>
        private string BuildSystemPrompt(string companyName, string contextJson)
        {
            return string.Format(
@"You are TalkWithTally, an AI financial assistant for {0}.
You help users understand their Tally Prime accounting data by answering
questions about ledgers, vouchers, trial balances, and other financial records.

RULES:
1. Answer questions based STRICTLY on the data provided below.
2. If the answer is not present in the data, say so clearly — do NOT guess or fabricate numbers.
3. Be concise and professional.
4. Use INR (₹) for all currency values.
5. When listing items, use numbered lists or tables for clarity.
6. If the user asks about data types not present in the context, explain which
   data is currently available and suggest they sync the missing data type.

TALLY DATA CONTEXT:
{1}", companyName, contextJson);
        }
    }
}
