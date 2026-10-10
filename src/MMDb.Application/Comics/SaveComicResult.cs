using MMDb.Domain;
using MMDb.Domain.Comics;

namespace MMDb.Application.Comics;

/// <summary>Outcome of adding or updating a comic.</summary>
public abstract record SaveComicResult
{
    private SaveComicResult()
    {
    }

    public sealed record Saved(Comic Comic) : SaveComicResult;

    public sealed record Invalid(IReadOnlyList<ValidationError> Errors) : SaveComicResult;

    public sealed record NotFound : SaveComicResult;

    /// <summary>This very copy (see <see cref="Comic.IsSameCopyAs"/>) is in the collection already.</summary>
    public sealed record AlreadyOwned(Comic Comic) : SaveComicResult;

    /// <summary>Someone else changed the comic since the caller loaded it.</summary>
    public sealed record Conflict : SaveComicResult;
}
