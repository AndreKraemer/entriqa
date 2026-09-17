namespace Entriqa.Domain.Submissions;

/// <summary>
/// Where a submission's regular deadline (CreatedAt + RetentionDays) meets an admin's override (#15):
/// <see cref="Submission.RetainUntil"/> null means no override, <see cref="DateTimeOffset.MaxValue"/>
/// means retained permanently, any other value is an explicit deadline that replaces the regular one.
/// The single place both RunHousekeepingUseCase and the list/detail projections ask "does this survive".
/// </summary>
public static class SubmissionRetention
{
    /// <summary>The date housekeeping would delete on. <see cref="DateTimeOffset.MaxValue"/> means never.</summary>
    public static DateTimeOffset EffectiveExpiry(DateTimeOffset createdAt, DateTimeOffset? retainUntil, int retentionDays) =>
        throw new NotImplementedException(); // #15: retainUntil ?? createdAt.AddDays(retentionDays)

    /// <summary>True while an override still shields the submission from housekeeping at <paramref name="now"/>.</summary>
    public static bool IsProtected(DateTimeOffset createdAt, DateTimeOffset? retainUntil, int retentionDays, DateTimeOffset now) =>
        throw new NotImplementedException(); // #15: EffectiveExpiry(...) > now
}
