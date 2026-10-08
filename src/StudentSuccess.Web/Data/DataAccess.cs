using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;

namespace StudentSuccess.Web.Data;

/// <summary>Creates database connections. Keeps SqlClient out of the rest of the app.</summary>
public interface IDbConnectionFactory
{
    DbConnection Create();
}

public sealed class SqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    // ADO.NET pools connections, so creating one per call is cheap
    public DbConnection Create() => new SqlConnection(connectionString);
}

/// <summary>An expected error with a matching HTTP status code.</summary>
public abstract class DomainException(string message, int statusCode, string title) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Title { get; } = title;
}

public sealed class InvalidInputException(string message)
    : DomainException(message, StatusCodes.Status400BadRequest, "Invalid input");

public sealed class RecordNotFoundException(string message)
    : DomainException(message, StatusCodes.Status404NotFound, "Not found");

public sealed class ConflictException(string message)
    : DomainException(message, StatusCodes.Status409Conflict, "Conflict");

/// <summary>
/// Turns SQL Server error numbers into domain exceptions. 50400/50404/50409 are
/// raised with THROW in the stored procedures; 2601/2627 are unique-key violations.
/// Anything else is unexpected and becomes a generic 500.
/// </summary>
public static class SqlErrorMapper
{
    public const int InvalidInput = 50400;
    public const int NotFound = 50404;
    public const int Conflict = 50409;
    public const int UniqueIndexViolation = 2601;
    public const int UniqueConstraintViolation = 2627;

    public static DomainException? Map(int errorNumber, string message) => errorNumber switch
    {
        InvalidInput => new InvalidInputException(message),
        NotFound => new RecordNotFoundException(message),
        Conflict => new ConflictException(message),
        UniqueIndexViolation or UniqueConstraintViolation =>
            new ConflictException("A record with the same key already exists."),
        _ => null,
    };

    public static bool TryMap(DbException exception, [NotNullWhen(true)] out DomainException? mapped)
    {
        mapped = exception is SqlException sql ? Map(sql.Number, sql.Message) : null;
        return mapped is not null;
    }
}

/// <summary>Small helpers that keep the repository readable.</summary>
internal static class DbExtensions
{
    /// <summary>
    /// Adds a typed parameter. Values are always sent as parameters, never pasted
    /// into SQL text, which is what prevents SQL injection.
    /// </summary>
    public static DbParameter Add(
        this DbCommand command, string name, DbType type, object? value,
        int size = 0, ParameterDirection direction = ParameterDirection.Input)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.DbType = type;
        p.Direction = direction;
        p.Value = value ?? DBNull.Value;
        if (size != 0)
        {
            p.Size = size;
        }
        if (type == DbType.Decimal)
        {
            p.Precision = 5;
            p.Scale = 2;
        }
        command.Parameters.Add(p);
        return p;
    }

    public static string Str(this DbDataReader r, string column) => r.GetString(r.GetOrdinal(column));

    public static string? StrOrNull(this DbDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }

    /// <summary>Reads INT, SMALLINT or TINYINT as int.</summary>
    public static int Int(this DbDataReader r, string column) => Convert.ToInt32(r.GetValue(r.GetOrdinal(column)));

    public static int? IntOrNull(this DbDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    }

    public static decimal Dec(this DbDataReader r, string column) => Convert.ToDecimal(r.GetValue(r.GetOrdinal(column)));

    public static decimal? DecOrNull(this DbDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));
    }

    public static bool Bool(this DbDataReader r, string column) => r.GetBoolean(r.GetOrdinal(column));

    public static DateTime UtcDate(this DbDataReader r, string column) =>
        DateTime.SpecifyKind(r.GetDateTime(r.GetOrdinal(column)), DateTimeKind.Utc);

    public static byte[] Bytes(this DbDataReader r, string column) => (byte[])r.GetValue(r.GetOrdinal(column));

    public static async Task<List<T>> ReadAllAsync<T>(this DbDataReader r, Func<DbDataReader, T> map, CancellationToken ct)
    {
        var list = new List<T>();
        while (await r.ReadAsync(ct))
        {
            list.Add(map(r));
        }
        return list;
    }
}
