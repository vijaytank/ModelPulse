using System.Text.Json;

namespace ModelPulse.Core.Adapters.Ollama
{
    public class OllamaParser
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static OllamaPsResponse ParsePsResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new OllamaPsResponse();
            }

            try
            {
                return JsonSerializer.Deserialize<OllamaPsResponse>(json, Options) ?? new OllamaPsResponse();
            }
            catch (JsonException)
            {
                return new OllamaPsResponse();
            }
        }
    }
}
