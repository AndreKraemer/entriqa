using Entriqa.Data.Entities;
using Entriqa.Domain.Forms;

namespace Entriqa.Data.Mapping;

/// <summary>
/// The customs post between the appointment domain record and its row (#6). Hand written on purpose: few,
/// typed fields. The slug is the partition and the id is the row key - that is what keeps appointments
/// master data of a form rather than part of a version (AC 2).
/// </summary>
internal static class AppointmentMapper
{
    public static AppointmentEntity ToEntity(Appointment a) => new()
    {
        PartitionKey = a.Slug,
        RowKey = a.Id,
        Start = a.Start,
        End = a.End,
        Capacity = a.Capacity,
        Title = a.Title,
        WaitlistEnabled = a.WaitlistEnabled,
        Active = a.Active,
    };

    public static Appointment ToDomain(AppointmentEntity e) => new()
    {
        Slug = e.PartitionKey,
        Id = e.RowKey,
        Start = e.Start,
        End = e.End,
        Capacity = e.Capacity,
        Title = e.Title,
        WaitlistEnabled = e.WaitlistEnabled,
        Active = e.Active,
    };
}
