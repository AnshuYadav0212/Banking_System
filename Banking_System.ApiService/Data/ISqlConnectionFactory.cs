using Microsoft.Data.SqlClient;

namespace Banking_System.ApiService.Data;

public interface ISqlConnectionFactory
{
    SqlConnection CreateConnection();
}