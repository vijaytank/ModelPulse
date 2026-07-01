using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ModelPulse.Core.Adapters.Ollama
{
    public class OllamaPsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaRunningModel> Models { get; set; } = new();
    }

    public class OllamaRunningModel
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("digest")]
        public string Digest { get; set; } = string.Empty;

        [JsonPropertyName("details")]
        public OllamaModelDetails Details { get; set; } = new();

        [JsonPropertyName("expires_at")]
        public DateTime? ExpiresAt { get; set; }

        [JsonPropertyName("size_vram")]
        public long? SizeVram { get; set; }
    }

    public class OllamaModelDetails
    {
        [JsonPropertyName("parent_model")]
        public string ParentModel { get; set; } = string.Empty;

        [JsonPropertyName("format")]
        public string Format { get; set; } = string.Empty;

        [JsonPropertyName("family")]
        public string Family { get; set; } = string.Empty;

        [JsonPropertyName("families")]
        public List<string> Families { get; set; } = new();

        [JsonPropertyName("parameter_size")]
        public string ParameterSize { get; set; } = string.Empty;

        [JsonPropertyName("quantization_level")]
        public string QuantizationLevel { get; set; } = string.Empty;

        /// <summary>Context window length. Present in /api/tags response details.</summary>
        [JsonPropertyName("context_length")]
        public long? ContextLength { get; set; }

        /// <summary>Embedding vector length. Present in /api/tags response details.</summary>
        [JsonPropertyName("embedding_length")]
        public long? EmbeddingLength { get; set; }
    }
}
