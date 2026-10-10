namespace MMDb.Domain.Movies;

/// <summary>Someone behind the camera and the job they did. Directors are kept apart, in <see cref="MovieDetails.Directors"/>.</summary>
/// <param name="Job">As credited, e.g. "Director of Photography".</param>
/// <param name="Department">The part of the crew the job belongs to, e.g. "Camera"; see <see cref="CrewCredits.Departments"/>.</param>
public sealed record CrewMember(string Name, string Job, string? Department = null);

/// <summary>The order crew credits are kept and shown in, and which jobs are the headline ones.</summary>
public static class CrewCredits
{
    public const string Screenplay = "Screenplay";
    public const string Writer = "Writer";
    public const string Story = "Story";
    public const string Novel = "Novel";
    public const string Producer = "Producer";
    public const string ExecutiveProducer = "Executive Producer";
    public const string DirectorOfPhotography = "Director of Photography";
    public const string Composer = "Original Music Composer";
    public const string Editor = "Editor";

    /// <summary>TMDB's departments, in the order a film's credits usually run. Others follow, A–Z; credits without one come last.</summary>
    public static readonly IReadOnlyList<string> Departments =
        ["Directing", "Writing", "Production", "Camera", "Editing", "Sound", "Art", "Costume & Make-Up", "Visual Effects", "Lighting", "Crew"];

    /// <summary>The jobs that lead their department, most senior first.</summary>
    public static readonly IReadOnlyList<string> HeadlineJobs =
        [Screenplay, Writer, Story, Novel, Producer, ExecutiveProducer, DirectorOfPhotography, Composer, Editor];

    public static readonly IReadOnlyList<string> WritingJobs = [Screenplay, Writer, Story, Novel];

    public static bool IsHeadline(CrewMember member) => HeadlineJobs.Contains(member.Job, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// By department, the headline jobs first within each, then the other jobs A–Z; people sharing a job stay in
    /// the order they came in.
    /// </summary>
    public static IReadOnlyList<CrewMember> InCreditOrder(IEnumerable<CrewMember> crew) =>
        [.. crew
            .OrderBy(c => c.Department is null ? 2 : Rank(Departments, c.Department) < Departments.Count ? 0 : 1)
            .ThenBy(c => c.Department is null ? 0 : Rank(Departments, c.Department))
            .ThenBy(c => c.Department, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => Rank(HeadlineJobs, c.Job))
            .ThenBy(c => c.Job, StringComparer.OrdinalIgnoreCase)];

    /// <summary>The distinct names credited with any of these jobs, in the order of the jobs given.</summary>
    public static IReadOnlyList<string> NamesFor(IEnumerable<CrewMember> crew, params IReadOnlyList<string> jobs) =>
        [.. crew
            .Where(c => jobs.Contains(c.Job, StringComparer.OrdinalIgnoreCase))
            .OrderBy(c => Rank(jobs, c.Job))
            .Select(c => c.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static int Rank(IReadOnlyList<string> order, string value)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (string.Equals(order[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return order.Count;
    }
}
