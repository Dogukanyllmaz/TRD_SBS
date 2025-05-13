using System;
using System.Collections.Generic;
using System.Timers;
using System.Threading.Tasks;
using Telegram.Bot;
using Serilog;

public class CryptoBot
{
    private readonly BinanceClient binanceClient;
    private readonly DatabaseManager dbManager;
    private readonly TelegramBotClient botClient;
    private readonly long chatId;
    private readonly System.Timers.Timer timer;
    private readonly List<string> symbols = new List<string> { "BTCUSDT", "ETHUSDT", "XRPUSDT", "BNBUSDT", "CHZUSDT" };

    // Saatlik ve günlük veriler için ayrı yapılar
    private readonly Dictionary<string, List<decimal>> hourlyPriceHistories = new Dictionary<string, List<decimal>>();
    private readonly Dictionary<string, List<decimal>> hourlyVolumeHistories = new Dictionary<string, List<decimal>>();
    private readonly Dictionary<string, List<decimal>> dailyPriceHistories = new Dictionary<string, List<decimal>>();
    private readonly Dictionary<string, GaussianChannelCalculator> hourlyGaussianChannels = new Dictionary<string, GaussianChannelCalculator>();
    private readonly Dictionary<string, GaussianChannelCalculator> dailyGaussianChannels = new Dictionary<string, GaussianChannelCalculator>();

    // Pozisyon yönetimi ve önceki kararlar
    private readonly Dictionary<string, string> previousDecisions = new Dictionary<string, string>();
    private readonly Dictionary<string, bool> openPositions = new Dictionary<string, bool>(); // true: pozisyon açık, false: kapalı

    public CryptoBot()
    {
        binanceClient = new BinanceClient();
        dbManager = new DatabaseManager();
        botClient = new TelegramBotClient(Config.TelegramBotToken);
        chatId = Config.ChatId;

        foreach (var symbol in symbols)
        {
            hourlyPriceHistories[symbol] = new List<decimal>();
            hourlyVolumeHistories[symbol] = new List<decimal>();
            dailyPriceHistories[symbol] = new List<decimal>();
            hourlyGaussianChannels[symbol] = new GaussianChannelCalculator(N: 20, mult: 1.5); // Optimize edilmiş parametreler
            dailyGaussianChannels[symbol] = new GaussianChannelCalculator(N: 50, mult: 2.0); // Günlük için daha uzun vadeli
            openPositions[symbol] = false; // Başlangıçta pozisyon kapalı
        }

        timer = new System.Timers.Timer(3600000); // Her saat başı
        timer.Elapsed += async (s, e) => await FetchAndAnalyze("1h");
        timer.AutoReset = true;
    }

    public async Task StartAsync()
    {
        foreach (var symbol in symbols)
        {
            await LoadHistoricalData(symbol, "1h", 1000); // Saatlik veri
            await LoadHistoricalData(symbol, "1d", 100);  // Günlük veri
        }
        await FetchAndAnalyze("1h");
        timer.Start();
    }

    private async Task LoadHistoricalData(string symbol, string interval, int limit)
    {
        var candles = await binanceClient.GetKlinesAsync(symbol, interval, limit);
        string tableName = $"CandleData_{symbol}_{interval}";
        await dbManager.EnsureTableExists(tableName);

        var priceHistory = interval == "1h" ? hourlyPriceHistories[symbol] : dailyPriceHistories[symbol];
        var volumeHistory = interval == "1h" ? hourlyVolumeHistories[symbol] : null;

        foreach (var candle in candles)
        {
            priceHistory.Add(candle.ClosePrice);
            if (volumeHistory != null) volumeHistory.Add(candle.Volume);
            await dbManager.SaveCandleData(tableName, symbol, candle.OpenPrice, candle.HighPrice, candle.LowPrice, candle.ClosePrice, candle.Volume, candle.CandleTime);
        }

        Log.Information($"✅ {symbol} için {interval} veriler yüklendi ve SQL'e kaydedildi.");
    }

    private async Task FetchAndAnalyze(string interval)
    {
        foreach (string symbol in symbols)
        {
            // Saatlik veri
            var hourlyCandle = await binanceClient.GetLatestCandleAsync(symbol, "1h");
            hourlyPriceHistories[symbol].Add(hourlyCandle.ClosePrice);
            hourlyVolumeHistories[symbol].Add(hourlyCandle.Volume);
            if (hourlyPriceHistories[symbol].Count > 1000) hourlyPriceHistories[symbol].RemoveAt(0);
            if (hourlyVolumeHistories[symbol].Count > 1000) hourlyVolumeHistories[symbol].RemoveAt(0);

            // Günlük veri (her 24 saatte bir güncellenir, burada saatlik kontrolde yaklaşık kontrol yapılır)
            if (DateTime.UtcNow.Hour == 0 || dailyPriceHistories[symbol].Count == 0)
            {
                var dailyCandle = await binanceClient.GetLatestCandleAsync(symbol, "1d");
                dailyPriceHistories[symbol].Add(dailyCandle.ClosePrice);
                if (dailyPriceHistories[symbol].Count > 100) dailyPriceHistories[symbol].RemoveAt(0);
            }

            // Saatlik analiz
            decimal rsi = CalculateRSI(hourlyPriceHistories[symbol], 14);
            double hourlyCurrentPrice = (double)hourlyCandle.ClosePrice;
            double hourlyTrueRange = CalculateTrueRange((double)hourlyCandle.HighPrice, (double)hourlyCandle.LowPrice, hourlyPriceHistories[symbol].Count > 1 ? (double)hourlyPriceHistories[symbol][^2] : hourlyCurrentPrice);
            var (hourlyFilt, hourlyHband, hourlyLband) = hourlyGaussianChannels[symbol].Update(hourlyCurrentPrice, hourlyTrueRange);
            double hourlyPrevFilt = hourlyGaussianChannels[symbol].PreviousFilt;

            // Günlük analiz
            double dailyCurrentPrice = (double)dailyPriceHistories[symbol][^1];
            double dailyTrueRange = dailyPriceHistories[symbol].Count > 1 ? CalculateTrueRange((double)dailyPriceHistories[symbol][^1], (double)dailyPriceHistories[symbol][^1], (double)dailyPriceHistories[symbol][^2]) : 0;
            var (dailyFilt, dailyHband, dailyLband) = dailyGaussianChannels[symbol].Update(dailyCurrentPrice, dailyTrueRange);

            // Hacim analizi
            decimal avgVolume = CalculateAverageVolume(hourlyVolumeHistories[symbol], 20);
            bool highVolume = hourlyCandle.Volume > avgVolume;

            // Alım ve satım koşulları
            bool longCondition = !openPositions[symbol] && // Pozisyon kapalıysa
                                 hourlyPriceHistories[symbol].Count > 1 &&
                                 hourlyPriceHistories[symbol][^2] < (decimal)hourlyHband &&
                                 hourlyCurrentPrice >= hourlyHband &&
                                 hourlyFilt > hourlyPrevFilt &&
                                 highVolume && // Hacim doğrulaması
                                 dailyCurrentPrice > dailyFilt; // Günlük trend yükselişteyse

            bool closeCondition = openPositions[symbol] && // Pozisyon açıksa
                                  (hourlyCurrentPrice < hourlyLband || hourlyFilt < hourlyPrevFilt || // Alt bant veya filtre düşüşü
                                   dailyCurrentPrice < dailyLband); // Günlük trend düşüşteyse

            string decision = longCondition ? "AL" : closeCondition ? "SAT" : "BEKLE";

            if (!previousDecisions.ContainsKey(symbol) || previousDecisions[symbol] != decision)
            {
                if (decision == "AL") openPositions[symbol] = true;
                else if (decision == "SAT") openPositions[symbol] = false;

                string message = $"🔹 {symbol}: {hourlyCandle.ClosePrice} USDT\n" +
                                 $"📊 RSI: {rsi:F2}\n" +
                                 $"🌀 Saatlik Filter: {hourlyFilt:F2}\n" +
                                 $"📈 Saatlik Üst Kanal: {hourlyHband:F2}\n" +
                                 $"📉 Saatlik Alt Kanal: {hourlyLband:F2}\n" +
                                 $"🌀 Günlük Filter: {dailyFilt:F2}\n" +
                                 $"📈 Günlük Üst Kanal: {dailyHband:F2}\n" +
                                 $"📉 Günlük Alt Kanal: {dailyLband:F2}\n" +
                                 $"📦 Hacim: {hourlyCandle.Volume:F2} (Ort: {avgVolume:F2})\n" +
                                 $"🛒 Karar: {decision}";
                Log.Information(message);
                await botClient.SendTextMessageAsync(chatId, message);
                previousDecisions[symbol] = decision;
            }
        }
    }

    private static double CalculateTrueRange(double high, double low, double previousClose)
    {
        return Math.Max(high - low, Math.Max(Math.Abs(high - previousClose), Math.Abs(low - previousClose)));
    }

    private static decimal CalculateRSI(List<decimal> prices, int period)
    {
        if (prices.Count < period + 1) return 50;
        decimal gain = 0, loss = 0;
        for (int i = prices.Count - period; i < prices.Count; i++)
        {
            decimal change = prices[i] - prices[i - 1];
            if (change > 0) gain += change;
            else loss -= change;
        }
        decimal rs = gain / (loss == 0 ? 1 : loss);
        return 100 - (100 / (1 + rs));
    }

    private static decimal CalculateAverageVolume(List<decimal> volumes, int period)
    {
        if (volumes.Count < period) return volumes.Count > 0 ? volumes.Sum() / volumes.Count : 0;
        return volumes.GetRange(volumes.Count - period, period).Sum() / period;
    }
}