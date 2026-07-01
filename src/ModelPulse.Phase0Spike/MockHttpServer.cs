using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ModelPulse.Phase0Spike
{
    public class MockHttpServer : IDisposable
    {
        private readonly HttpListener _listener;
        private bool _running;

        public MockHttpServer(string prefix)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
        }

        public void Start()
        {
            _listener.Start();
            _running = true;
            Task.Run(ListenLoop);
        }

        private async Task ListenLoop()
        {
            while (_running && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
                catch (HttpListenerException)
                {
                    // Occurs when listener is stopped
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Mock Server Error] {ex.Message}");
                }
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;
                var rawPath = request.RawUrl ?? "";

                if (rawPath.Equals("/health", StringComparison.OrdinalIgnoreCase))
                {
                    var json = "{\"status\": \"ok\", \"slots_idle\": 4, \"slots_processing\": 1}";
                    byte[] buffer = Encoding.UTF8.GetBytes(json);
                    response.ContentType = "application/json";
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
                else if (rawPath.Equals("/metrics", StringComparison.OrdinalIgnoreCase))
                {
                    var metrics = @"# HELP llamacpp:prompt_tokens_total Number of prompt tokens processed.
# TYPE llamacpp:prompt_tokens_total counter
llamacpp:prompt_tokens_total 4827
# HELP llamacpp:tokens_predicted_total Number of tokens predicted.
# TYPE llamacpp:tokens_predicted_total counter
llamacpp:tokens_predicted_total 9272
# HELP llamacpp:slots_active Number of active slots.
# TYPE llamacpp:slots_active gauge
llamacpp:slots_active 2";

                    byte[] buffer = Encoding.UTF8.GetBytes(metrics);
                    response.ContentType = "text/plain; charset=utf-8";
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                }

                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Mock Handler Error] {ex.Message}");
            }
        }

        public void Dispose()
        {
            _running = false;
            if (_listener.IsListening)
            {
                _listener.Stop();
            }
            _listener.Close();
        }
    }
}
