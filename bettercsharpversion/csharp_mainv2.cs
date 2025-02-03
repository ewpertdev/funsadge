using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Collections.Generic;

class Program
{
    static async Task Main(string[] args)
    {
        DatabaseManager db = null;
        try
        {
            // Create DatabaseManager (it will read from app.config automatically)
            db = new DatabaseManager();

            // Test connection
            Console.WriteLine("Testing database connection...");
            bool isConnected = await db.CheckConnectionAsync();
            if (!isConnected)
            {
                throw new Exception("Failed to connect to database.");
            }
            Console.WriteLine($"Connection test result: {isConnected}");

            // Test 2: Execute a parameterized query
            Console.WriteLine("\nTesting parameterized query...");
            var parameters = new[]
            {
                new SqlParameter("@Count", 5),
                new SqlParameter("@Status", "Active")
            };
            
            string selectQuery = @"
                SELECT TOP (@Count) *
                FROM Users
                WHERE Status = @Status";
                
            DataTable result = await db.ExecuteQueryAsync(selectQuery, parameters);
            await PrintDataTableAsync(result);

            // Test 3: Execute a stored procedure that returns data
            Console.WriteLine("\nTesting stored procedure query...");
            var spParameters = new[]
            {
                new SqlParameter("@UserId", 1),
                new SqlParameter("@StartDate", DateTime.Now.AddDays(-30))
            };
            
            DataTable spResult = await db.ExecuteStoredProcedureQueryAsync("GetUserActivity", spParameters);
            await PrintDataTableAsync(spResult);

            // Test 4: Execute a stored procedure that updates data
            Console.WriteLine("\nTesting stored procedure update...");
            var updateParameters = new[]
            {
                new SqlParameter("@UserId", 1),
                new SqlParameter("@NewStatus", "Inactive")
            };
            
            bool success = await db.ExecuteStoredProcedureNonQueryAsync("UpdateUserStatus", updateParameters);
            Console.WriteLine($"Update operation success: {success}");

            // Test 5: Test bulk copy
            Console.WriteLine("\nTesting bulk copy...");
            var testData = new DataTable();
            testData.Columns.AddRange(new[]
            {
                new DataColumn("Id", typeof(int)),
                new DataColumn("Name", typeof(string)),
                new DataColumn("CreatedDate", typeof(DateTime))
            });

            // Add some test rows
            for (int i = 1; i <= 1000; i++)
            {
                testData.Rows.Add(i, $"Test{i}", DateTime.Now);
            }

            bool bulkCopySuccess = await db.BulkCopyAsync("TestTable", testData);
            Console.WriteLine($"Bulk copy operation success: {bulkCopySuccess}");

            // Test 6: Test transaction
            Console.WriteLine("\nTesting transaction...");
            bool transactionSuccess = await db.ExecuteTransactionAsync(
                "INSERT INTO Users (Name) VALUES ('User1')",
                "UPDATE Users SET Status = 'Active' WHERE Name = 'User1'",
                "INSERT INTO UserLog (UserId, Action) SELECT Id, 'Created' FROM Users WHERE Name = 'User1'"
            );
            Console.WriteLine($"Transaction operation success: {transactionSuccess}");

            // Alternative using collection
            var sqlStatements = new List<string>
            {
                "INSERT INTO Users (Name) VALUES ('User2')",
                "UPDATE Users SET Status = 'Active' WHERE Name = 'User2'"
            };
            bool collectionTransactionSuccess = await db.ExecuteTransactionAsync(sqlStatements);
            Console.WriteLine($"Collection transaction operation success: {collectionTransactionSuccess}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            db?.Dispose();
        }
    }

    static async Task PrintDataTableAsync(DataTable dt)
    {
        await Task.Run(() =>
        {
            if (dt == null || dt.Rows.Count == 0)
            {
                Console.WriteLine("No data found.");
                return;
            }

            // Calculate column widths
            var columnWidths = new int[dt.Columns.Count];
            for (int i = 0; i < dt.Columns.Count; i++)
            {
                columnWidths[i] = dt.Columns[i].ColumnName.Length;
                foreach (DataRow row in dt.Rows)
                {
                    int length = row[i]?.ToString()?.Length ?? 4;
                    if (length > columnWidths[i])
                        columnWidths[i] = length;
                }
            }

            // Print headers
            for (int i = 0; i < dt.Columns.Count; i++)
            {
                Console.Write($"| {dt.Columns[i].ColumnName.PadRight(columnWidths[i])} ");
            }
            Console.WriteLine("|");

            // Print separator
            Console.WriteLine(new string('-', dt.Columns.Count * 3 + columnWidths.Sum() + 1));

            // Print rows
            foreach (DataRow row in dt.Rows)
            {
                for (int i = 0; i < dt.Columns.Count; i++)
                {
                    string value = row[i]?.ToString() ?? "NULL";
                    Console.Write($"| {value.PadRight(columnWidths[i])} ");
                }
                Console.WriteLine("|");
            }
        });
    }
}
