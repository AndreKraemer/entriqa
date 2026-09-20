using Entriqa.Data.Entities;
using Entriqa.Domain.Forms;

namespace Entriqa.Data.Mapping;

/// <summary>
/// The customs post between the appointment domain record and its row (#6). Hand written on purpose: few,
/// typed fields. The slug is the partition and the id is the row key - that is what keeps appointments
/// master data of a form rather than part of a version (AC 2). Skeleton: bodies throw until #6 is implemented.
/// </summary>
internal static class AppointmentMapper
{
    public static AppointmentEntity ToEntity(Appointment a)
    {
        throw new NotImplementedException();
    }

    public static Appointment ToDomain(AppointmentEntity e)
    {
        throw new NotImplementedException();
    }
}
