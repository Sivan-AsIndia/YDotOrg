namespace YDots.DON.Domain.Enums;

/// <summary>
/// SCR-DON-006 workload band. Derived from the owner's open-work count so the manager
/// can balance ownership without reading raw numbers.
/// </summary>
public enum WorkloadBand
{
    Light = 1,
    Balanced = 2,
    Heavy = 3,
    Overloaded = 4
}
