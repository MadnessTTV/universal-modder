using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace UmMegabonkStudio
{
    // Talks to an OpenAI-compatible /v1/chat/completions server only after the user
    // fills ai.json. No address is built in. No key is written or logged.
    static class AiClient
    {
        static readonly object Gate = new object();
        static string _pending;
        static bool _busy;
        public static bool Busy;
        public static string Status = "No model is configured. Nothing is sent.";
        public static List<string> Questions = new List<string>();
        public static StudioCharacter Pending;
        static readonly List<string[]> Transcript = new List<string[]>();

        public static string ConfigPath()
        {
            return Path.Combine(Path.GetDirectoryName(Plugin.JsonPath) ?? "", "ai.json");
        }

        public static bool TryConfig(out string baseUrl, out string model, out string keyEnv, out string error)
        {
            baseUrl = "";
            model = "";
            keyEnv = "MEGABONK_AI_KEY";
            error = "";
            var path = ConfigPath();
            if (!File.Exists(path))
            {
                error = "ai.json is missing next to characters.json. No request will be sent.";
                return false;
            }
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                if (root.TryGetProperty("base_url", out var url) && url.ValueKind == JsonValueKind.String)
                    baseUrl = (url.GetString() ?? "").Trim();
                if (root.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String)
                    model = (modelEl.GetString() ?? "").Trim();
                if (root.TryGetProperty("api_key_env", out var env) && env.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(env.GetString()))
                    keyEnv = env.GetString().Trim();
            }
            catch (Exception ex)
            {
                error = "ai.json could not be read (" + ex.GetType().Name + "). No request will be sent.";
                return false;
            }
            if (baseUrl.Length == 0 || model.Length == 0)
            {
                error = "Set base_url and model in ai.json. No request will be sent until both are set.";
                return false;
            }
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                error = "base_url must be an http or https address.";
                return false;
            }
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                error = "Put a key in the environment variable, not in base_url.";
                return false;
            }
            return true;
        }

        public static void Ask(string userText, string selectedJson)
        {
            if (_busy) return;
            if (!TryConfig(out var baseUrl, out var model, out var keyEnv, out var error))
            {
                Status = error;
                return;
            }
            LastUser = userText ?? "";
            _busy = true;
            Busy = true;
            Status = "Sending to the configured model.";
            var prior = new List<string[]>(Transcript);
            Task.Run(() => Send(baseUrl, model, keyEnv, userText, selectedJson, prior));
        }

        static void Send(string baseUrl, string model, string keyEnv, string userText, string selectedJson, List<string[]> prior)
        {
            string body = null;
            string failure = null;
            try
            {
                var key = Environment.GetEnvironmentVariable(keyEnv);
                if (string.IsNullOrWhiteSpace(key))
                {
                    failure = keyEnv + " is not set in this game process. Nothing was sent.";
                }
                else
                {
                    var messages = new List<object>
                    {
                        new { role = "system", content = AiProtocol.CatalogPrompt() },
                    };
                    foreach (var turn in prior)
                        messages.Add(new { role = turn[0], content = turn[1] });
                    var content = userText ?? "";
                    if (!string.IsNullOrWhiteSpace(selectedJson))
                        content += "\n\nCurrent character, for context only. Do not copy its id.\n" + selectedJson;
                    messages.Add(new { role = "user", content = content });
                    var payload = JsonSerializer.Serialize(new { model, messages });
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(90);
                    var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/chat/completions");
                    request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
                    var response = client.Send(request);
                    var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (!response.IsSuccessStatusCode)
                        failure = "The model server returned " + (int)response.StatusCode + ".";
                    else
                        body = ReadContent(text);
                }
            }
            catch (Exception ex)
            {
                failure = "The model request failed: " + ex.GetType().Name;
            }
            lock (Gate)
            {
                if (failure != null) _pending = "error\n" + failure;
                else _pending = "ok\n" + (body ?? "");
            }
        }

        static string ReadContent(string responseJson)
        {
            using var doc = JsonDocument.Parse(responseJson);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }

        public static void Poll()
        {
            string incoming = null;
            lock (Gate)
            {
                incoming = _pending;
                _pending = null;
            }
            if (incoming == null) return;
            _busy = false;
            Busy = false;
            if (incoming.StartsWith("error\n", StringComparison.Ordinal))
            {
                Status = incoming.Substring(6);
                Plugin.Warn(Status);
                return;
            }
            var reply = incoming.Substring(3);
            if (!AiProtocol.TryInterpret(reply, out var kind, out var questions, out var draft, out var error))
            {
                Status = "The model reply was not applied. " + error;
                Pending = null;
                Questions = new List<string>();
                return;
            }
            Transcript.Add(new[] { "user", LastUser });
            Transcript.Add(new[] { "assistant", reply });
            if (Transcript.Count > 16)
                Transcript.RemoveRange(0, Transcript.Count - 16);
            if (kind == "questions")
            {
                Questions = questions;
                Pending = null;
                Status = "The model asked " + questions.Count + " questions. Answer, then send again. Nothing was saved.";
                return;
            }
            Questions = new List<string>();
            Pending = draft;
            Status = "Draft ready: " + draft.Name + ", look " + draft.Appearance + ". It is not in the character list until you apply it.";
        }

        public static string LastUser = "";

        public static void NoteUser(string text) { LastUser = text ?? ""; }

        public static void ClearPending()
        {
            Pending = null;
            Questions = new List<string>();
            Status = "Draft discarded. The character list was not changed.";
        }
    }
}
