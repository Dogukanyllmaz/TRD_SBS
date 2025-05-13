using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public class BinanceClient
{
    private readonly HttpClient client = new HttpClient();

    public async Task<List<Candle>> GetKlinesAsync(string symbol, string interval, int limit)
    {
        string url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";
        HttpResponseMessage response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        string responseBody = await response.Content.ReadAsStringAsync();
        JArray json = JArray.Parse(responseBody);

        var candles = new List<Candle>();
        foreach (var candle in json)
        {
            candles.Add(new Candle
            {
                OpenPrice = Convert.ToDecimal(candle[1]),
                HighPrice = Convert.ToDecimal(candle[2]),
                LowPrice = Convert.ToDecimal(candle[3]),
                ClosePrice = Convert.ToDecimal(candle[4]),
                Volume = Convert.ToDecimal(candle[5]),
                CandleTime = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(candle[0])).UtcDateTime
            });
        }
        return candles;
    }

    public async Task<Candle> GetLatestCandleAsync(string symbol, string interval)
    {
        string url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit=1";
        HttpResponseMessage response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        string responseBody = await response.Content.ReadAsStringAsync();
        JArray json = JArray.Parse(responseBody);
        var candle = json[0];
        return new Candle
        {
            OpenPrice = Convert.ToDecimal(candle[1]),
            HighPrice = Convert.ToDecimal(candle[2]),
            LowPrice = Convert.ToDecimal(candle[3]),
            ClosePrice = Convert.ToDecimal(candle[4]),
            Volume = Convert.ToDecimal(candle[5]),
            CandleTime = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(candle[0])).UtcDateTime
        };
    }
}

public class Candle
{
    public decimal OpenPrice { get; set; }
    public decimal HighPrice { get; set; }
    public decimal LowPrice { get; set; }
    public decimal ClosePrice { get; set; }
    public decimal Volume { get; set; }
    public DateTime CandleTime { get; set; }
}