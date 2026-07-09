using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HotelBooking.IntegrationTests.Api;

public sealed class AvailabilityBarrierInterceptor : DbCommandInterceptor
{
    private readonly TaskCompletionSource _bothQueriesCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _completedQueryCount;
    private bool _armed;

    public void Arm()
    {
        _armed = true;
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (!_armed || !IsAvailabilityQuery(command.CommandText))
        {
            return result;
        }

        if (Interlocked.Increment(ref _completedQueryCount) == 2)
        {
            _armed = false;
            _bothQueriesCompleted.TrySetResult();
        }

        await _bothQueriesCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            cancellationToken);

        return result;
    }

    private static bool IsAvailabilityQuery(string commandText)
    {
        return commandText.Contains("FROM [Rooms] AS [r]", StringComparison.Ordinal)
            && commandText.Contains("NOT EXISTS", StringComparison.Ordinal);
    }
}
