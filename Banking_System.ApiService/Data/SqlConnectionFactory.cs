using Microsoft.Data.SqlClient;

namespace Banking_System.ApiService.Data;

public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly IConfiguration _configuration;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public SqlConnection CreateConnection()
    {
        var connectionString =
            _configuration.GetConnectionString("IdentityDb");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "IdentityDb connection string is not configured.");
        }

        return new SqlConnection(connectionString);
    }
}