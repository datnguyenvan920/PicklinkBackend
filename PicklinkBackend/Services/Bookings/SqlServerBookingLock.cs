using System.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace PicklinkBackend.Services.Bookings;

public static class SqlServerBookingLock
{
    public static async Task<bool> AcquireAsync(
        IDbContextTransaction transaction,
        string resource,
        CancellationToken cancellationToken)
    {
        var dbTransaction = transaction.GetDbTransaction();
        var connection = dbTransaction.Connection
            ?? throw new InvalidOperationException("Transaction connection is null.");

        await using var command = connection.CreateCommand();
        command.Transaction = dbTransaction;
        command.CommandText = """
            DECLARE @lockResult int;
            EXEC @lockResult = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 5000;
            SELECT @lockResult;
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.DbType = DbType.String;
        parameter.Value = resource;
        command.Parameters.Add(parameter);

        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return result >= 0;
    }

    public static Task<bool> AcquireAsync(
        object dbContext,
        IDbContextTransaction transaction,
        string resource,
        CancellationToken cancellationToken) => AcquireAsync(transaction, resource, cancellationToken);
}
