using System.Text.Json;
using System.Text.Json.Serialization;

namespace TenderAI.Api.Services.Uzex
{
    // Typed HttpClient for the UZEX e-tender API. The base address is set in Program.cs.
    public class UzexClient(HttpClient http)
    {
        internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        // Active tender lots, ordered by end date. from and to are 1-based row numbers.
        public async Task<List<UzexTradeListItem>> GetTradeListAsync(int from, int to, CancellationToken ct)
        {
            // The field names must stay exactly as UZEX expects them
            var body = new Dictionary<string, object?>
            {
                ["from"] = from,
                ["to"] = to,
                ["is_agent"] = null,
                ["Is_Scientific"] = null,
                ["System_Id"] = 0,
                // 2 = tenders
                ["typeid"] = 2
            };

            using var response = await http.PostAsJsonAsync("api/common/TradeList", body, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<UzexTradeListItem>>(Json, ct) ?? [];
        }

        public async Task<UzexTrade> GetTradeAsync(long id, CancellationToken ct) =>
            await http.GetFromJsonAsync<UzexTrade>($"api/common/GetTrade/{id}/0", Json, ct)
            ?? throw new InvalidOperationException($"UZEX returned no details for lot {id}.");

        // Reads a field that is either a JSON array or a string that contains a JSON array
        internal static List<T> ReadList<T>(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Array => element.Deserialize<List<T>>(Json) ?? [],
                JsonValueKind.String when element.GetString() is { Length: > 0 } s => JsonSerializer.Deserialize<List<T>>(s, Json) ?? [],
                _ => []
            };
        }
    }
}
