using System;
using System.Collections.Generic;

namespace RecipieApp.Shared.Domains.MedApp;

public class Schedule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Medicine this schedule belongs to
    public Guid MedicineId { get; set; }

    // Weekdays on which the medicine should be taken
    public List<DayOfWeek> WeekDays { get; set; } = new List<DayOfWeek>();

    // Start date for schedule
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    // Optional end date
    public DateOnly? EndDate { get; set; }
}
