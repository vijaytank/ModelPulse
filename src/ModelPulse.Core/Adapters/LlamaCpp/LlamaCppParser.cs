using System;
using System.Text.Json;

namespace ModelPulse.Core.Adapters.LlamaCpp
{
    public class LlamaCppParser
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static LlamaCppHealthResponse ParseHealthResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new LlamaCppHealthResponse();
            }

            try
            {
                return JsonSerializer.Deserialize<LlamaCppHealthResponse>(json, Options) ?? new LlamaCppHealthResponse();
            }
            catch (JsonException)
            {
                return new LlamaCppHealthResponse();
            }
        }

        public static LlamaCppMetrics ParseMetrics(string rawMetrics)
        {
            var metrics = new LlamaCppMetrics();
            if (string.IsNullOrWhiteSpace(rawMetrics))
            {
                return metrics;
            }

            var lines = rawMetrics.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("#"))
                {
                    continue;
                }

                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    continue;
                }

                var name = parts[0];
                var valueStr = parts[1];

                if (name == "llamacpp:prompt_tokens_total" && double.TryParse(valueStr, out var promptTokens))
                {
                    metrics.PromptTokensTotal = promptTokens;
                }
                else if (name == "llamacpp:tokens_predicted_total" && double.TryParse(valueStr, out var tokensPredicted))
                {
                    metrics.TokensPredictedTotal = tokensPredicted;
                }
                else if (name == "llamacpp:slots_active" && int.TryParse(valueStr, out var activeSlots))
                {
                    metrics.ActiveSlots = activeSlots;
                }
            }

            return metrics;
        }
    }
}
