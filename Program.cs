using System;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;

class Program
{
    static async Task Main()
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("logs/crypto_bot.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        Log.Information("🚀 Bot başlatıldı...");

        var bot = new CryptoBot();
        await bot.StartAsync();

        // Süresiz çalışması için
        while (true) { await Task.Delay(1000); }
    }
}