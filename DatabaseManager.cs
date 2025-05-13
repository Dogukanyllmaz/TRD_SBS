using System.Data.SqlClient;
using System.Threading.Tasks;

public class DatabaseManager
{
    private readonly string connectionString = Config.ConnectionString;

    public async Task EnsureTableExists(string tableName)
    {
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            string query = $@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{tableName}')
                CREATE TABLE {tableName} (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    Symbol NVARCHAR(10),
                    OpenPrice DECIMAL(18,8),
                    HighPrice DECIMAL(18,8),
                    LowPrice DECIMAL(18,8),
                    ClosePrice DECIMAL(18,8),
                    Volume DECIMAL(18,8),
                    CandleTime DATETIME
                );";
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    public async Task SaveCandleData(string tableName, string symbol, decimal open, decimal high, decimal low, decimal close, decimal volume, DateTime candleTime)
    {
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            string query = $"INSERT INTO {tableName} (Symbol, OpenPrice, HighPrice, LowPrice, ClosePrice, Volume, CandleTime) VALUES (@symbol, @open, @high, @low, @close, @volume, @candleTime)";
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@symbol", symbol);
                command.Parameters.AddWithValue("@open", open);
                command.Parameters.AddWithValue("@high", high);
                command.Parameters.AddWithValue("@low", low);
                command.Parameters.AddWithValue("@close", close);
                command.Parameters.AddWithValue("@volume", volume);
                command.Parameters.AddWithValue("@candleTime", candleTime);
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}