namespace TenderAI.Api.Services.Uzex
{
    // "Uzex" section of appsettings.json
    public class UzexOptions
    {
        public const string Section = "Uzex";

        public string ApiBaseUrl { get; set; } = "https://apietender.uzex.uz/";

        // Public page of a lot; {0} is the lot ID
        public string LotUrl { get; set; } = "https://etender.uzex.uz/lot/{0}";

        public int TimeoutSeconds { get; set; } = 30;
    }
}
