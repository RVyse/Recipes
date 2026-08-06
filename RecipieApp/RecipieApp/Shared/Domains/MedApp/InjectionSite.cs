using System;

namespace RecipieApp.Shared.Domains.MedApp;

public class InjectionSite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int Sequence { get; set; }
}
