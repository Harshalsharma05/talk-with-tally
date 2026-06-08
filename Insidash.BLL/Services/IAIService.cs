using System.Threading.Tasks;

namespace Insidash.BLL.Services
{
    /// <summary>
    /// Abstraction for any LLM chat completion provider (Groq, Claude, etc.).
    /// Each implementation handles its own HTTP transport and auth.
    /// </summary>
    public interface IAIService
    {
        /// <summary>
        /// Send a system prompt + user message pair to the LLM and return the result.
        /// </summary>
        Task<AIServiceResult> ChatAsync(string systemPrompt, string userMessage);

        /// <summary>
        /// Human-readable provider name for logging and response metadata.
        /// </summary>
        string ProviderName { get; }
    }

    /// <summary>
    /// Unified result envelope returned by every AI provider implementation.
    /// </summary>
    public class AIServiceResult
    {
        public bool Success { get; set; }
        public string Content { get; set; }
        public string ErrorMessage { get; set; }
        public string ProviderUsed { get; set; }
    }
}
