using ShareX.HelpersLib;
using System;
using System.Drawing;

namespace ShareX
{
    public sealed class DailyNewsSettings
    {
        private const string ApiKeyEntropy = "xiaomangtongxue.daily-news";

        public bool Enabled = false;
        public string EncryptedApiKey = "";
        public string ApiBase = "https://api.deepseek.com";
        public string Model = "deepseek-chat";
        public int MaxPerSource = 20;
        public int MaxTotal = 60;
        public int RecentHours = 24;
        public int MaxSummaryChars = 500;
        public int Concurrency = 6;
        public int TimeoutSeconds = 20;
        public bool ProxyEnabled;
        public string ProxyUrl = "http://127.0.0.1:7897";
        public string DailyHotApi = "https://api-hot.imsyy.top";
        public bool EnableDigest = true;
        public bool EnableTopicGroup = true;
        public bool AutoRun = true;
        public int AutoRunHour = 8;
        public DateTime? LastDomesticSuccess;
        public DateTime? LastInternationalSuccess;
        public Size DomesticWindowSize = new Size(1200, 800);
        public Size InternationalWindowSize = new Size(1200, 800);
        public int WindowSizeVersion;

        public string GetApiKey()
        {
            if (string.IsNullOrWhiteSpace(EncryptedApiKey)) return "";
            try
            {
                return DPAPI.Decrypt(EncryptedApiKey, ApiKeyEntropy);
            }
            catch
            {
                return "";
            }
        }

        public void SetApiKey(string apiKey)
        {
            string value = apiKey?.Trim() ?? "";
            EncryptedApiKey = value.Length == 0 ? "" : DPAPI.Encrypt(value, ApiKeyEntropy);
        }

        public void Normalize()
        {
            ApiBase = string.IsNullOrWhiteSpace(ApiBase) ? "https://api.deepseek.com" : ApiBase.Trim().TrimEnd('/');
            Model = string.IsNullOrWhiteSpace(Model) ? "deepseek-chat" : Model.Trim();
            DailyHotApi = string.IsNullOrWhiteSpace(DailyHotApi) ? "https://api-hot.imsyy.top" : DailyHotApi.Trim().TrimEnd('/');
            ProxyUrl = ProxyUrl?.Trim() ?? "";
            MaxPerSource = Math.Clamp(MaxPerSource, 1, 50);
            MaxTotal = Math.Clamp(MaxTotal, 1, 200);
            RecentHours = Math.Clamp(RecentHours, 1, 168);
            MaxSummaryChars = Math.Clamp(MaxSummaryChars, 100, 2000);
            Concurrency = Math.Clamp(Concurrency, 1, 10);
            TimeoutSeconds = Math.Clamp(TimeoutSeconds, 5, 120);
            AutoRunHour = Math.Clamp(AutoRunHour, 0, 23);
            if (WindowSizeVersion < 2)
            {
                // Reset the earlier oversized default once. Later user resizing is remembered.
                DomesticWindowSize = new Size(1200, 800);
                InternationalWindowSize = new Size(1200, 800);
                WindowSizeVersion = 2;
            }
            DomesticWindowSize = NormalizeWindowSize(DomesticWindowSize);
            InternationalWindowSize = NormalizeWindowSize(InternationalWindowSize);
        }

        private static Size NormalizeWindowSize(Size size)
        {
            int width = size.Width <= 0 ? 1200 : Math.Clamp(size.Width, 800, 3840);
            int height = size.Height <= 0 ? 800 : Math.Clamp(size.Height, 600, 2160);
            return new Size(width, height);
        }
    }
}
