my fix

public async Task<bool> ExecuteTransactionAsync(IEnumerable<string> sqlStatements)
{
    if (sqlStatements == null || !sqlStatements.Any())
        throw new ArgumentException("At least one SQL statement is required.", nameof(sqlStatements));

    using var connection = CreateConnection();
    await connection.OpenAsync();

    using var transaction = connection.BeginTransaction(); // Begin transaction

    try
    {
        foreach (var sql in sqlStatements)
        {
            ValidateSQL(sql);

            using var command = new SqlCommand(sql, connection, transaction) // Attach transaction
            {
                CommandTimeout = _commandTimeout
            };

            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync(); // Commit if all succeed
        return true;
    }
    catch (Exception)
    {
        await transaction.RollbackAsync(); // Rollback on error
        throw;
    }
}








using System;
using System.Data;
using System.Data.SqlClient;
using System.Security;
using System.Threading.Tasks;
using System.Transactions;
using System.Linq;
using System.Collections.Generic;
using System.Configuration;

public sealed class DatabaseManager : IDisposable
{
    private readonly string _connectionString;
    private readonly int _commandTimeout;
    private readonly string _username;
    private readonly SecureString _password;
    private readonly bool _useIntegratedSecurity;
    private bool _disposed;

    public DatabaseManager()
    {
        // Try to get connection string first
        var connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"]?.ConnectionString;

        // If no connection string, build it from settings
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = ConfigurationManager.AppSettings["Database:DataSource"],
                InitialCatalog = ConfigurationManager.AppSettings["Database:InitialCatalog"],
                IntegratedSecurity = bool.Parse(ConfigurationManager.AppSettings["Database:IntegratedSecurity"] ?? "true"),
                PersistSecurityInfo = bool.Parse(ConfigurationManager.AppSettings["Database:PersistSecurityInfo"] ?? "false"),
                TrustServerCertificate = bool.Parse(ConfigurationManager.AppSettings["Database:TrustServerCertificate"] ?? "true")
            };

            _connectionString = builder.ConnectionString;
            _username = ConfigurationManager.AppSettings["Database:Username"];
            _password = ConvertToSecureString(ConfigurationManager.AppSettings["Database:Password"]);
        }
        else
        {
            _connectionString = connectionString;
            var builder = new SqlConnectionStringBuilder(connectionString);
            _username = builder.UserID;
            _password = ConvertToSecureString(builder.Password);
        }

        _commandTimeout = int.Parse(ConfigurationManager.AppSettings["CommandTimeout"] ?? "30");
        _useIntegratedSecurity = new SqlConnectionStringBuilder(_connectionString).IntegratedSecurity;
    }

    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> ExecuteNonQueryAsync(string sql, params SqlParameter[] parameters)
    {
        ValidateSQL(sql);

        using var connection = CreateConnection();
        using var command = CreateCommand(connection, sql, CommandType.Text, parameters);
        
        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<DataTable> ExecuteQueryAsync(string sql, params SqlParameter[] parameters)
    {
        ValidateSQL(sql);

        using var connection = CreateConnection();
        using var command = CreateCommand(connection, sql, CommandType.Text, parameters);
        
        var dataTable = new DataTable();
        await connection.OpenAsync();
        
        using var reader = await command.ExecuteReaderAsync();
        dataTable.Load(reader);
        return dataTable;
    }

    public async Task<bool> ExecuteStoredProcedureNonQueryAsync(string procedureName, params SqlParameter[] parameters)
    {
        ValidateStoredProcedureName(procedureName);

        using var connection = CreateConnection();
        using var command = CreateCommand(connection, procedureName, CommandType.StoredProcedure, parameters);
        
        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<DataTable> ExecuteStoredProcedureQueryAsync(string procedureName, params SqlParameter[] parameters)
    {
        ValidateStoredProcedureName(procedureName);

        using var connection = CreateConnection();
        using var command = CreateCommand(connection, procedureName, CommandType.StoredProcedure, parameters);
        
        var dataTable = new DataTable();
        await connection.OpenAsync();
        
        using var reader = await command.ExecuteReaderAsync();
        dataTable.Load(reader);
        return dataTable;
    }

    public async Task<bool> BulkCopyAsync(string tableName, DataTable dataTable)
    {
        ValidateTableName(tableName);
        if (dataTable == null || dataTable.Rows.Count == 0)
            throw new ArgumentException("DataTable cannot be null or empty.", nameof(dataTable));

        try
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();

            using var bulkCopy = new SqlBulkCopy(connection)
            {
                DestinationTableName = tableName,
                BatchSize = 1000,
                BulkCopyTimeout = _commandTimeout
            };

            // Map columns automatically
            foreach (DataColumn column in dataTable.Columns)
            {
                bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }

            await bulkCopy.WriteToServerAsync(dataTable);
            return true;
        }
        catch (SqlException ex)
        {
            throw new Exception($"Database error: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new Exception($"General error: {ex.Message}", ex);
        }
    }

    public async Task<bool> ExecuteTransactionAsync(string sql1, string sql2 = "", string sql3 = "", string sql4 = "")
    {
        var queries = new[] { sql1, sql2, sql3, sql4 }
            .Where(sql => !string.IsNullOrWhiteSpace(sql))
            .ToList();

        ValidateSQL(sql1); // At least first query is required

        using var transactionScope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions 
            { 
                IsolationLevel = IsolationLevel.Serializable 
            },
            TransactionScopeAsyncFlowOption.Enabled);

        try
        {
            using var connection = CreateConnection();
            await connection.OpenAsync();

            foreach (var sql in queries)
            {
                using var command = CreateCommand(connection, sql, CommandType.Text, Array.Empty<SqlParameter>());
                await command.ExecuteNonQueryAsync();
            }

            transactionScope.Complete();
            return true;
        }
        catch (SqlException ex)
        {
            throw new Exception($"Database error: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new Exception($"General error: {ex.Message}", ex);
        }
    }

    // Optional: Overload that accepts a collection of SQL statements
    public async Task<bool> ExecuteTransactionAsync(IEnumerable<string> sqlStatements)
    {
        if (sqlStatements == null || !sqlStatements.Any())
            throw new ArgumentException("At least one SQL statement is required.", nameof(sqlStatements));

        return await ExecuteTransactionAsync(
            sqlStatements.First(),
            sqlStatements.Skip(1).FirstOrDefault() ?? "",
            sqlStatements.Skip(2).FirstOrDefault() ?? "",
            sqlStatements.Skip(3).FirstOrDefault() ?? ""
        );
    }

    private SqlConnection CreateConnection()
    {
        try
        {
            if (_useIntegratedSecurity)
            {
                return new SqlConnection(_connectionString);
            }
            else if (_username != null && _password != null)
            {
                var credential = new SqlCredential(_username, _password);
                return new SqlConnection(_connectionString, credential);
            }
            else
            {
                // Fallback to connection string settings
                return new SqlConnection(_connectionString);
            }
        }
        catch (Exception ex)
        {
            throw new Exception("Failed to create database connection.", ex);
        }
    }

    private SqlCommand CreateCommand(SqlConnection connection, string commandText, CommandType commandType, SqlParameter[] parameters)
    {
        var command = new SqlCommand(commandText, connection)
        {
            CommandType = commandType,
            CommandTimeout = _commandTimeout
        };

        if (parameters != null && parameters.Length > 0)
        {
            command.Parameters.AddRange(parameters);
        }

        return command;
    }

    private static void ValidateSQL(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException("SQL query cannot be null or empty.", nameof(sql));
    }

    private static void ValidateStoredProcedureName(string procedureName)
    {
        if (string.IsNullOrWhiteSpace(procedureName))
            throw new ArgumentException("Stored procedure name cannot be null or empty.", nameof(procedureName));
    }

    private static void ValidateTableName(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name cannot be null or empty.", nameof(tableName));
    }

    private static SecureString ConvertToSecureString(string password)
    {
        if (string.IsNullOrEmpty(password))
            return null;

        var secure = new SecureString();
        foreach (char c in password)
        {
            secure.AppendChar(c);
        }
        secure.MakeReadOnly();
        return secure;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_password != null)
            {
                _password.Dispose();
            }
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
