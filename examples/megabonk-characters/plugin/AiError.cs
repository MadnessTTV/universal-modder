using System;
using System.Text.Json;

namespace UmMegabonkStudio
{
    // Same mapping as um.megabonk_ai_error. The provider message is never copied.
    static class AiError
    {
        public static string Text;
        public static int PauseSeconds;

        public static void Explain(int status, string body, string retryAfter)
        {
            Text = "";
            PauseSeconds = 0;
            if (status == 401)
            {
                Text = "The model server did not accept the request (401). Nothing was saved. Your prompt is still here.";
                return;
            }
            string code;
            string typ;
            Tokens(body, out code, out typ);
            if (IsQuota(code))
            {
                Quota(code);
                return;
            }
            if (IsRate(code))
            {
                Rate(code, retryAfter);
                return;
            }
            if (IsOverload(code))
            {
                Overload(code, retryAfter);
                return;
            }
            if (IsQuota(typ))
            {
                Quota(typ);
                return;
            }
            if (IsRate(typ))
            {
                Rate(typ, retryAfter);
                return;
            }
            if (IsOverload(typ))
            {
                Overload(typ, retryAfter);
                return;
            }
            if (status == 429)
                Text = "The model server returned 429. No recognized limit code was included, so this is not treated as a billing limit. Nothing was saved. Your prompt is still here.";
            else
                Text = "The model server returned " + status + ". Nothing was saved. Your prompt is still here.";
        }

        static void Quota(string ident)
        {
            Text = "The account limit " + ident + " was reached. Sending again will not fix it. Your prompt is still here. Nothing was saved.";
        }

        static void Rate(string ident, string retryAfter)
        {
            var wait = RetrySeconds(retryAfter);
            if (wait == 0) wait = 15;
            PauseSeconds = wait;
            Text = "The model service is rate limiting requests (" + ident + "). Wait " + wait + " seconds, then send again if you want. Your prompt is still here. Nothing was saved.";
        }

        static void Overload(string ident, string retryAfter)
        {
            var wait = RetrySeconds(retryAfter);
            if (wait == 0) wait = 15;
            PauseSeconds = wait;
            Text = "The model is temporarily overloaded (" + ident + "). Wait " + wait + " seconds, then send again if you want. Your prompt is still here. Nothing was saved.";
        }

        static void Tokens(string body, out string code, out string typ)
        {
            code = null;
            typ = null;
            if (string.IsNullOrEmpty(body) || body.Length > 4096) return;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
                if (!doc.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object) return;
                if (error.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.String)
                    code = Allow(codeEl.GetString());
                if (error.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
                    typ = Allow(typeEl.GetString());
            }
            catch (JsonException)
            {
            }
        }

        static string Allow(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64) return null;
            for (int i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (!((c >= 'a' && c <= 'z') || c == '_')) return null;
            }
            if (IsQuota(value) || IsRate(value) || IsOverload(value)) return value;
            return null;
        }

        static bool IsQuota(string value)
        {
            return value == "insufficient_quota"
                || value == "credit_balance_exhausted"
                || value == "organization_spend_limit_exceeded"
                || value == "project_spend_limit_exceeded"
                || value == "organization_usage_limit_exceeded";
        }

        static bool IsRate(string value)
        {
            return value == "rate_limit_exceeded" || value == "slow_down" || value == "rate_limit_error";
        }

        static bool IsOverload(string value)
        {
            return value == "server_is_overloaded" || value == "service_unavailable_error";
        }

        static int RetrySeconds(string header)
        {
            if (string.IsNullOrEmpty(header)) return 0;
            header = header.Trim();
            if (header.Length < 1 || header.Length > 5) return 0;
            int seconds = 0;
            for (int i = 0; i < header.Length; i++)
            {
                if (header[i] < '0' || header[i] > '9') return 0;
                seconds = seconds * 10 + (header[i] - '0');
            }
            if (seconds < 1 || seconds > 3600) return 0;
            return seconds;
        }
    }
}
