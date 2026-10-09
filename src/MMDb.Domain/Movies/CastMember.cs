namespace MMDb.Domain.Movies;

/// <summary>An actor and the part they play.</summary>
public sealed record CastMember(string Name, string? Character = null);
