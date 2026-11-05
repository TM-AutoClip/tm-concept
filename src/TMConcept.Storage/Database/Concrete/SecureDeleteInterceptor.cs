using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TMConcept.Storage.Database;

/// <summary>
/// Turns on <c>PRAGMA secure_delete</c> on every connection: deleted rows are overwritten with zeros instead of staying in free pages
/// (the file is encrypted anyway; this is an extra layer for "delete permanently").
/// </summary>
public sealed class SecureDeleteInterceptor : DbConnectionInterceptor
{
    /// <summary>The shared instance.</summary>
    public static SecureDeleteInterceptor Instance { get; } = new();

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA secure_delete = ON;";
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = "PRAGMA secure_delete = ON;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
