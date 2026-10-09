namespace MMDb.Domain.Tests;

internal static class ValidationAssertions
{
    public static ValidationError ShouldHaveSingleError(this IEnumerable<ValidationError> errors, string field)
    {
        var list = errors.ToList();
        list.Count.ShouldBe(1, $"expected one error for {field}, got: {string.Join("; ", list)}");
        list[0].Field.ShouldBe(field);
        return list[0];
    }
}
