using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Insidash.BLL.Services
{
    /// <summary>
    /// Calls the Groq Cloud API (OpenAI-compatible) with llama-3.3-70b-versatile.
    /// Uses a single static HttpClient to avoid socket exhaustion across requests.
    /// </summary>
    public class GroqAIService : IAIService
    {
        private const string GroqEndpoint = "https://api.groq.com/openai/v1/chat/completions";
        private const string DefaultModel = "llama-3.3-70b-versatile";
        private const int DefaultMaxTokens = 1024;

        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _apiKey;

        public string ProviderName
        {
            get { return "Groq"; }
        }

        public GroqAIService(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentNullException("apiKey", "Groq API key cannot be null or empty.");

            _apiKey = apiKey;
        }

        public async Task<AIServiceResult> ChatAsync(string systemPrompt, string userMessage)
        {
            try
            {
                // Build the OpenAI-compatible request body
                var requestBody = new
                {
                    model = DefaultModel,
                    max_tokens = DefaultMaxTokens,
                    temperature = 0.3,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user",   content = userMessage }
                    }
                };

                string jsonPayload = JsonConvert.SerializeObject(requestBody);

                using (var request = new HttpRequestMessage(HttpMethod.Post, GroqEndpoint))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        return new AIServiceResult
                        {
                            Success = false,
                            ErrorMessage = string.Format("Groq API returned {0}: {1}",
                                (int)response.StatusCode, responseBody),
                            ProviderUsed = ProviderName
                        };
                    }

                    // Parse the response — Groq follows the OpenAI schema:
                    // { "choices": [{ "message": { "content": "..." } }] }
                    JObject parsed = JObject.Parse(responseBody);
                    JToken choicesToken = parsed["choices"];

                    if (choicesToken == null || !choicesToken.HasValues)
                    {
                        return new AIServiceResult
                        {
                            Success = false,
                            ErrorMessage = "Groq response contained no choices.",
                            ProviderUsed = ProviderName
                        };
                    }

                    string content = (string)choicesToken[0]["message"]["content"];

                    return new AIServiceResult
                    {
                        Success = true,
                        Content = content,
                        ProviderUsed = ProviderName
                    };
                }
            }
            catch (HttpRequestException httpEx)
            {
                return new AIServiceResult
                {
                    Success = false,
                    ErrorMessage = string.Format("Groq HTTP error: {0}", httpEx.Message),
                    ProviderUsed = ProviderName
                };
            }
            catch (TaskCanceledException)
            {
                return new AIServiceResult
                {
                    Success = false,
                    ErrorMessage = "Groq request timed out.",
                    ProviderUsed = ProviderName
                };
            }
            catch (Exception ex)
            {
                return new AIServiceResult
                {
                    Success = false,
                    ErrorMessage = string.Format("Groq unexpected error: {0}", ex.Message),
                    ProviderUsed = ProviderName
                };
            }
        }
    }
}
