namespace MMDb.Contracts;

/// <summary>Answer to <c>GET /api/v1/health</c>: the PC server is up and reachable.</summary>
public sealed record HealthResponse(string Status);
