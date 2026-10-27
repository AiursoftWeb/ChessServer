using System.Diagnostics.CodeAnalysis;
using Aiursoft.ChessServer.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.ChessServer.MySql;

[ExcludeFromCodeCoverage]

public class MySqlContext(DbContextOptions<MySqlContext> options) : TemplateDbContext(options);
