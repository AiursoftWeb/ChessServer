namespace Aiursoft.ChessServer.Models;

public record CommandReceipt(Guid Id, string Fingerprint);
public record GameCommand(Guid Id, string Kind, int ExpectedPly = -1, string? Move = null);
