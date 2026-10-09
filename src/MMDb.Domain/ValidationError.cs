namespace MMDb.Domain;

/// <summary>A rule broken by user input. <see cref="Field"/> names the input (see <see cref="Movies.MovieFields"/>).</summary>
public sealed record ValidationError(string Field, string Message);

/// <summary>Thrown when a domain object is asked to accept input that fails validation.</summary>
public sealed class DomainValidationException(IReadOnlyList<ValidationError> errors)
    : Exception("Invalid input: " + string.Join("; ", errors.Select(e => $"{e.Field}: {e.Message}")))
{
    public IReadOnlyList<ValidationError> Errors { get; } = errors;
}
