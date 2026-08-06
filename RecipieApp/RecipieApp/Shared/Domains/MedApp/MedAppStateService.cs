using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace RecipieApp.Shared.Domains.MedApp;

public class MedAppStateService
{
    private const string StorageKey = "medapp_state_v1";
    private readonly IJSRuntime _js;

    private readonly List<Medicine> _medicines = new();
    private readonly List<Schedule> _schedules = new();

    // Recorded (marked) doses persisted: key is combination of medicineId + date
    private readonly List<RecordedDose> _recorded = new();
    private Settings _settings = new Settings { RetentionDays = 365, GenerateDays = 30 };

    public MedAppStateService(IJSRuntime js)
    {
        _js = js;
    }

    public Task ResetAsync()
    {
        _medicines.Clear();
        _schedules.Clear();
        _recorded.Clear();
        return SaveAsync();
    }

    public IReadOnlyList<Medicine> Medicines => _medicines;
    public IReadOnlyList<Schedule> Schedules => _schedules;

    public async Task InitializeAsync()
    {
        try
        {
            var raw = await _js.InvokeAsync<string>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrEmpty(raw))
            {
                var dto = JsonSerializer.Deserialize<PersistDto>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (dto != null)
                {
                    _medicines.Clear();
                    _medicines.AddRange(dto.Medicines ?? new List<Medicine>());

                    _schedules.Clear();
                    _schedules.AddRange(dto.Schedules ?? new List<Schedule>());

                    _recorded.Clear();
                    _recorded.AddRange(dto.Recorded ?? new List<RecordedDose>());
                    if (dto.Settings != null) _settings = dto.Settings;
                }
            }
        }
        catch
        {
            // ignore and start fresh
        }
    }

    private Task SaveAsync()
    {
        var dto = new PersistDto
        {
            Medicines = _medicines,
            Schedules = _schedules,
            Recorded = _recorded,
            Settings = _settings
        };
        var raw = JsonSerializer.Serialize(dto);
        return _js.InvokeVoidAsync("localStorage.setItem", StorageKey, raw).AsTask();
    }

    public Task<string> ExportStateAsync()
    {
        var dto = new PersistDto { Medicines = _medicines, Schedules = _schedules, Recorded = _recorded, Settings = _settings };
        var raw = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
        return Task.FromResult(raw);
    }

    public async Task ImportStateAsync(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        var dto = JsonSerializer.Deserialize<PersistDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (dto == null) return;

        _medicines.Clear();
        _medicines.AddRange(dto.Medicines ?? new List<Medicine>());

        _schedules.Clear();
        _schedules.AddRange(dto.Schedules ?? new List<Schedule>());

        _recorded.Clear();
        _recorded.AddRange(dto.Recorded ?? new List<RecordedDose>());

        if (dto.Settings != null) _settings = dto.Settings;

        // After import, regenerate forward to bring system up-to-date
        await GenerateForwardForAllAsync();
        await SaveAsync();
    }

    public Task SetRetentionDaysAsync(int days)
    {
        _settings.RetentionDays = days;
        return SaveAsync();
    }

    public Task SetGenerateDaysAsync(int days)
    {
        _settings.GenerateDays = days;
        return SaveAsync();
    }

    public Task PurgeOldRecordsAsync()
    {
        var cutoff = DateOnly.FromDateTime(DateTime.Today.AddDays(-_settings.RetentionDays));
        _recorded.RemoveAll(r => r.Date < cutoff);
        return SaveAsync();
    }

    public async Task GenerateForwardForAllAsync()
    {
        var to = DateOnly.FromDateTime(DateTime.Today).AddDays(_settings.GenerateDays);
        foreach (var med in _medicines)
        {
            var schedules = _schedules.Where(s => s.MedicineId == med.Id).ToList();
            if (!schedules.Any()) continue;

            // find last recorded date for this medicine
            DateOnly lastRec = schedules.Min(s => s.StartDate);
            var recs = _recorded.Where(r => r.MedicineId == med.Id).ToList();
            if (recs.Any()) lastRec = recs.Max(r => r.Date);

            foreach (var sched in schedules)
            {
                // simulate from lastRec+1 to 'to'
                var d = lastRec.AddDays(1);
                while (d <= to)
                {
                    if (sched.EndDate.HasValue && d > sched.EndDate.Value) break;
                    if (sched.WeekDays.Contains(d.DayOfWeek))
                    {
                        // if there is a recorded entry, apply its effect
                        var existing = _recorded.FirstOrDefault(r => r.MedicineId == med.Id && r.Date == d);
                        if (existing != null)
                        {
                            if (existing.Status == DoseStatus.Taken && med.InjectionSites.Any())
                            {
                                var siteIdx = med.InjectionSites.FindIndex(s => s.Id == existing.InjectionSiteId);
                                if (siteIdx >= 0) med.NextSiteIndex = (siteIdx + 1) % med.InjectionSites.Count;
                            }
                            else if (existing.Status == DoseStatus.Missed && med.InjectionSites.Any())
                            {
                                var siteIdx = med.InjectionSites.FindIndex(s => s.Id == existing.InjectionSiteId);
                                if (siteIdx >= 0) med.NextSiteIndex = siteIdx; // do not advance
                            }
                        }
                        else
                        {
                            // no record: assume sequence advances (simulate taken)
                            if (med.InjectionSites.Any()) med.NextSiteIndex = (med.NextSiteIndex + 1) % med.InjectionSites.Count;
                        }
                    }
                    d = d.AddDays(1);
                }
            }
        }

        await SaveAsync();
    }

    public Task AddMedicineAsync(Medicine med)
    {
        _medicines.Add(med);
        return SaveAsync();
    }

    public Task UpdateMedicineAsync(Medicine med)
    {
        var idx = _medicines.FindIndex(m => m.Id == med.Id);
        if (idx >= 0)
            _medicines[idx] = med;
        return SaveAsync();
    }

    public Task AddScheduleAsync(Schedule schedule)
    {
        _schedules.Add(schedule);
        return SaveAsync();
    }

    public Task RemoveScheduleAsync(Guid scheduleId)
    {
        var idx = _schedules.FindIndex(s => s.Id == scheduleId);
        if (idx >= 0)
        {
            _schedules.RemoveAt(idx);
        }
        return SaveAsync();
    }

    public Task RemoveMedicineAsync(Guid medicineId)
    {
        var idx = _medicines.FindIndex(m => m.Id == medicineId);
        if (idx >= 0)
        {
            _medicines.RemoveAt(idx);
            // remove related schedules and recorded doses
            _schedules.RemoveAll(s => s.MedicineId == medicineId);
            _recorded.RemoveAll(r => r.MedicineId == medicineId);
        }
        return SaveAsync();
    }

    public IEnumerable<Schedule> GetSchedulesForMedicine(Guid medicineId)
    {
        return _schedules.Where(s => s.MedicineId == medicineId).ToList();
    }

    public IEnumerable<ScheduledDose> GetUpcomingDoses(DateOnly from, DateOnly to)
    {
        var list = new List<ScheduledDose>();

        foreach (var sched in _schedules)
        {
            var med = _medicines.FirstOrDefault(m => m.Id == sched.MedicineId);
            if (med == null) continue;

            var start = DateOnly.FromDateTime(from.ToDateTime(TimeOnly.MinValue));
            var end = DateOnly.FromDateTime(to.ToDateTime(TimeOnly.MinValue));

            // ensure schedule start
            var iterStart = start < sched.StartDate ? sched.StartDate : start;

            // collect dates
            var dates = new List<DateOnly>();
            for (var d = iterStart; d <= end; d = d.AddDays(1))
            {
                if (sched.EndDate.HasValue && d > sched.EndDate.Value) break;
                if (sched.WeekDays.Contains(d.DayOfWeek)) dates.Add(d);
            }

            // Determine nextSiteIndex snapshot
            var siteCount = med.InjectionSites?.Count ?? 0;
            var nextIndex = med.NextSiteIndex;

            var offset = 0;
            foreach (var date in dates)
            {
                // check if user recorded this dose
                var rec = _recorded.FirstOrDefault(r => r.MedicineId == med.Id && r.Date == date);
                if (rec != null)
                {
                    list.Add(new ScheduledDose
                    {
                        Id = Guid.NewGuid(),
                        Date = date,
                        MedicineId = med.Id,
                        MedicineName = med.Name,
                        InjectionSiteId = rec.InjectionSiteId,
                        InjectionSiteName = rec.InjectionSiteName,
                        Status = rec.Status
                    });

                    // If recorded as Taken, advance nextIndex accordingly
                    if (rec.Status == DoseStatus.Taken && siteCount > 0)
                    {
                        // find index of injection site
                        var siteIdx = med.InjectionSites.FindIndex(s => s.Id == rec.InjectionSiteId);
                        if (siteIdx >= 0) nextIndex = (siteIdx + 1) % siteCount;
                    }

                    continue;
                }

                // not recorded: assign site by simulation
                Guid? siteId = null;
                string? siteName = null;
                if (med.IsInjection && siteCount > 0)
                {
                    var idx = (nextIndex + offset) % siteCount;
                    var site = med.InjectionSites[idx];
                    siteId = site.Id;
                    siteName = site.Name;
                    offset++; // assume sequence advances for display of future events
                }

                list.Add(new ScheduledDose
                {
                    Id = Guid.NewGuid(),
                    Date = date,
                    MedicineId = med.Id,
                    MedicineName = med.Name,
                    InjectionSiteId = siteId,
                    InjectionSiteName = siteName,
                    Status = DoseStatus.Pending
                });
            }
        }

        return list.OrderBy(d => d.Date).ToList();
    }

    public (int Taken, int Missed, int Pending) GetStatistics(DateOnly from, DateOnly to)
    {
        var taken = _recorded.Count(r => r.Status == DoseStatus.Taken && r.Date >= from && r.Date <= to);
        var missed = _recorded.Count(r => r.Status == DoseStatus.Missed && r.Date >= from && r.Date <= to);

        // pending: scheduled in range but not recorded
        var scheduledCount = GetUpcomingDoses(from, to).Count();
        var pending = scheduledCount - taken - missed;

        return (taken, missed, Math.Max(0, pending));
    }

    public async Task MarkDoseTakenAsync(Guid medicineId, DateOnly date)
    {
        var med = _medicines.FirstOrDefault(m => m.Id == medicineId);
        if (med == null) return;

        // determine site used for this date (recompute same as GetUpcomingDoses would)
        var schedule = _schedules.FirstOrDefault(s => s.MedicineId == medicineId && s.WeekDays.Contains(date.DayOfWeek) && date >= s.StartDate && (!s.EndDate.HasValue || date <= s.EndDate.Value));

        Guid? siteId = null;
        string? siteName = null;
        if (med.IsInjection && med.InjectionSites.Count > 0 && schedule != null)
        {
            // compute how many occurrences between schedule.StartDate and this date to figure index
            var occurrences = 0;
            for (var d = schedule.StartDate; d <= date; d = d.AddDays(1))
            {
                if (schedule.WeekDays.Contains(d.DayOfWeek)) occurrences++;
            }

            var siteCount = med.InjectionSites.Count;
            var idx = (med.NextSiteIndex + (occurrences - 1)) % siteCount;
            var site = med.InjectionSites[idx];
            siteId = site.Id;
            siteName = site.Name;

            // advance NextSiteIndex to the next after this taken dose
            med.NextSiteIndex = (idx + 1) % siteCount;
        }

        // store recorded
        var rec = _recorded.FirstOrDefault(r => r.MedicineId == medicineId && r.Date == date);
        if (rec == null)
        {
            _recorded.Add(new RecordedDose { MedicineId = medicineId, Date = date, InjectionSiteId = siteId, InjectionSiteName = siteName, Status = DoseStatus.Taken });
        }
        else
        {
            rec.Status = DoseStatus.Taken;
            rec.InjectionSiteId = siteId;
            rec.InjectionSiteName = siteName;
        }

        await SaveAsync();
    }

    public async Task MarkDoseMissedAsync(Guid medicineId, DateOnly date)
    {
        var med = _medicines.FirstOrDefault(m => m.Id == medicineId);
        if (med == null) return;

        // determine site for this date similar to above
        var schedule = _schedules.FirstOrDefault(s => s.MedicineId == medicineId && s.WeekDays.Contains(date.DayOfWeek) && date >= s.StartDate && (!s.EndDate.HasValue || date <= s.EndDate.Value));

        Guid? siteId = null;
        string? siteName = null;
        if (med.IsInjection && med.InjectionSites.Count > 0 && schedule != null)
        {
            var occurrences = 0;
            for (var d = schedule.StartDate; d <= date; d = d.AddDays(1))
            {
                if (schedule.WeekDays.Contains(d.DayOfWeek)) occurrences++;
            }
            var siteCount = med.InjectionSites.Count;
            var idx = (med.NextSiteIndex + (occurrences - 1)) % siteCount;
            var site = med.InjectionSites[idx];
            siteId = site.Id;
            siteName = site.Name;

            // Do not advance NextSiteIndex for missed dose (so next uses this site)
            med.NextSiteIndex = idx;
        }

        var rec = _recorded.FirstOrDefault(r => r.MedicineId == medicineId && r.Date == date);
        if (rec == null)
        {
            _recorded.Add(new RecordedDose { MedicineId = medicineId, Date = date, InjectionSiteId = siteId, InjectionSiteName = siteName, Status = DoseStatus.Missed });
        }
        else
        {
            rec.Status = DoseStatus.Missed;
            rec.InjectionSiteId = siteId;
            rec.InjectionSiteName = siteName;
        }

        await SaveAsync();
    }

    private class PersistDto
    {
        public List<Medicine>? Medicines { get; set; }
        public List<Schedule>? Schedules { get; set; }
        public List<RecordedDose>? Recorded { get; set; }
        public Settings? Settings { get; set; }
    }

    private class RecordedDose
    {
        public Guid MedicineId { get; set; }
        public DateOnly Date { get; set; }
        public Guid? InjectionSiteId { get; set; }
        public string? InjectionSiteName { get; set; }
        public DoseStatus Status { get; set; }
    }

    private class Settings
    {
        public int RetentionDays { get; set; } = 365;
        public int GenerateDays { get; set; } = 30;
    }
}
