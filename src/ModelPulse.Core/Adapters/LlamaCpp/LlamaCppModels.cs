using System.Text.Json.Serialization;

namespace ModelPulse.Core.Adapters.LlamaCpp
{
    public class LlamaCppHealthResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("slots_idle")]
        public int? SlotsIdle { get; set; }

        [JsonPropertyName("slots_processing")]
        public int? SlotsProcessing { get; set; }
    }

    public class LlamaCppMetrics
    {
        public double PromptTokensTotal { get; set; }
        public double TokensPredictedTotal { get; set; }
        public int ActiveSlots { get; set; }
    }
}
