using NSubstitute;
using Entriqa.Application.Ports;
using Entriqa.Application.UseCases;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.UseCases;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #6, slice 1 of the registration epic (#5): an operator maintains the dates of a form. Appointments
/// are master data of a form <em>slug</em>, not part of the immutable <see cref="FormVersion"/> - so a change
/// is readable at once, without a new version and without a republish (AC 3). This slice is the operator-facing
/// data only: create (AC 1), edit keyed by a stable id (AC 2 + AC 3), deactivate keeping the row (AC 4),
/// delete (AC 5, the explicit confirmation is the admin's job), and the end-after-start invariant (AC 6).
/// Seat counting, the visitor view and registrations are #7/#8.
///
/// The store is modelled, not scripted: a real dictionary keyed by (slug, id), so a save is observable through
/// a later list exactly as Table Storage would show it (test-conventions: "a double answers the request").
/// </summary>
public class AppointmentAdminUseCaseTests
{
    private const string Slug = "webinar";

    private readonly FakeAppointmentStore _store = new();
    private readonly IDeleteAppointmentCommand _delete = Substitute.For<IDeleteAppointmentCommand>();

    private ISaveAppointmentUseCase Save => new SaveAppointmentUseCase(_store);
    private IListAppointmentsUseCase List => new ListAppointmentsUseCase(_store);
    private IDeactivateAppointmentUseCase Deactivate => new DeactivateAppointmentUseCase(_store, _store);
    private IDeleteAppointmentUseCase Delete => new DeleteAppointmentUseCase(_delete);

    private static Appointment New(DateTimeOffset start, int capacity = 10) =>
        new() { Slug = Slug, Start = start, Capacity = capacity };

    // ---- AC 1: a created appointment carries its data and a distinct id ------------------------------

    [Fact]
    public async Task GivenAStartAndCapacityAndNoOptionalFields_WhenAnAppointmentIsCreated_ThenItIsStoredWithADistinctIdUnderItsSlug()
    {
        var start = TestData.Time.GetUtcNow().AddDays(7);

        var saved = await Save.ExecuteAsync(New(start, capacity: 25));

        Assert.False(string.IsNullOrWhiteSpace(saved.Id));                 // an id was assigned
        var stored = Assert.Single(await List.ExecuteAsync(Slug));
        Assert.Equal(saved.Id, stored.Id);
        Assert.Equal(Slug, stored.Slug);
        Assert.Equal(start, stored.Start);
        Assert.Equal(25, stored.Capacity);
        Assert.Null(stored.End);                                           // optional, left unset
        Assert.Null(stored.Title);
        Assert.False(stored.WaitlistEnabled);
        Assert.True(stored.Active);                                        // created active
    }

    // ---- AC 2 + AC 3: an edit keeps the id, replaces the one row, and is readable at once ------------

    [Fact]
    public async Task GivenAnExistingAppointment_WhenItIsEdited_ThenItKeepsItsIdAndTheChangedStateReplacesTheSameRow()
    {
        var created = await Save.ExecuteAsync(New(TestData.Time.GetUtcNow().AddDays(7), capacity: 10));

        await Save.ExecuteAsync(created with { Capacity = 20, Title = "Morning cohort" });

        var stored = Assert.Single(await List.ExecuteAsync(Slug));         // still one row, not appended
        Assert.Equal(created.Id, stored.Id);                               // same identity
        Assert.Equal(20, stored.Capacity);                                 // change is readable at once
        Assert.Equal("Morning cohort", stored.Title);
    }

    // ---- AC 4: deactivating keeps the row in the admin and keeps its identity -----------------------

    [Fact]
    public async Task GivenAnActiveAppointment_WhenItIsDeactivated_ThenItStaysInTheAdminListAsInactiveWithItsIdentityUnchanged()
    {
        var created = await Save.ExecuteAsync(New(TestData.Time.GetUtcNow().AddDays(7)));

        await Deactivate.ExecuteAsync(Slug, created.Id);

        var stored = Assert.Single(await List.ExecuteAsync(Slug));         // still visible in the admin
        Assert.False(stored.Active);
        Assert.Equal(created.Id, stored.Id);                               // same id - stays assigned to its registrations
        Assert.Equal(Slug, stored.Slug);
    }

    [Fact]
    public async Task GivenNoAppointmentUnderThatId_WhenItIsDeactivated_ThenItIsReportedAsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Deactivate.ExecuteAsync(Slug, "does-not-exist"));
    }

    // ---- AC 5: delete removes that one row (the explicit confirmation is the admin's job) -----------

    [Fact]
    public async Task GivenAnAppointment_WhenItIsDeleted_ThenThatRowIsRemovedUnderItsSlug()
    {
        await Delete.ExecuteAsync(Slug, "a1");

        await _delete.Received(1).ExecuteAsync(Slug, "a1", Arg.Any<CancellationToken>());
    }

    // ---- AC 6: a set end is always after its start --------------------------------------------------

    [Theory]
    [InlineData(0)]        // end == start
    [InlineData(-60)]      // end before start
    public async Task GivenAnEndNotAfterItsStart_WhenTheAppointmentIsSaved_ThenItIsRejectedAndNothingIsStored(int endOffsetMinutes)
    {
        var start = TestData.Time.GetUtcNow().AddDays(7);
        var appointment = New(start) with { End = start.AddMinutes(endOffsetMinutes) };

        var ex = await Assert.ThrowsAsync<AppException>(() => Save.ExecuteAsync(appointment));

        Assert.Equal(ErrorCodes.Validation, ex.ErrorCode);
        Assert.Empty(await List.ExecuteAsync(Slug));
    }

    [Fact]
    public async Task GivenAnEndAfterItsStart_WhenTheAppointmentIsSaved_ThenItIsAccepted()
    {
        var start = TestData.Time.GetUtcNow().AddDays(7);

        var saved = await Save.ExecuteAsync(New(start) with { End = start.AddHours(2) });

        Assert.Equal(start.AddHours(2), saved.End);
        Assert.Equal(start.AddHours(2), Assert.Single(await List.ExecuteAsync(Slug)).End);
    }

    /// <summary>
    /// The appointment table as this slice sees it: a real dictionary keyed by (slug, id). A save is a replace,
    /// so editing the same id keeps one row; a list returns exactly what was saved, which is how AC 3's
    /// "readable at once" is observable without a running Table Storage.
    /// </summary>
    private sealed class FakeAppointmentStore : IListAppointmentsQuery, ITryGetAppointmentQuery, ISaveAppointmentCommand
    {
        private readonly Dictionary<(string Slug, string Id), Appointment> _rows = new();

        public Task<IReadOnlyList<Appointment>> ExecuteAsync(string slug, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Appointment>>(_rows.Values.Where(a => a.Slug == slug).ToList());

        public Task<Appointment?> ExecuteAsync(string slug, string id, CancellationToken ct = default) =>
            Task.FromResult(_rows.TryGetValue((slug, id), out var a) ? a : null);

        public Task ExecuteAsync(Appointment appointment, CancellationToken ct = default)
        {
            _rows[(appointment.Slug, appointment.Id)] = appointment;
            return Task.CompletedTask;
        }
    }
}
