using System.Diagnostics.CodeAnalysis;
using Aiursoft.ChessServer.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.ChessServer.Sqlite;

[ExcludeFromCodeCoverage]

public class SqliteContext(DbContextOptions<SqliteContext> options) : TemplateDbContext(options)
{
    public override Task<bool> CanConnectAsync()
    {
        return Task.FromResult(true);
    }
}
