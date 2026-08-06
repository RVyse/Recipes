using System;
using System.Collections.Generic;

namespace RecipieApp.Shared.Domains.MedApp;

public class Medicine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool IsInjection { get; set; }

    // Ordered injection sites for this medicine (if IsInjection)
    public List<InjectionSite> InjectionSites { get; set; } = new List<InjectionSite>();

    // Index into InjectionSites for the next pick. Advances when a dose is marked Taken.
    public int NextSiteIndex { get; set; }

    // Optional: starting site Id to initialize NextSiteIndex
    public Guid? StartSiteId { get; set; }
}
