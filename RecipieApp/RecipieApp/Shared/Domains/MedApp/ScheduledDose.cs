using System;

namespace RecipieApp.Shared.Domains.MedApp;

public class ScheduledDose
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Date only for the scheduled dose
    public DateOnly Date { get; set; }

    public Guid MedicineId { get; set; }
    public string MedicineName { get; set; } = string.Empty;

    // If injection: which site
    public Guid? InjectionSiteId { get; set; }
    public string? InjectionSiteName { get; set; }

    public DoseStatus Status { get; set; } = DoseStatus.Pending;
}
