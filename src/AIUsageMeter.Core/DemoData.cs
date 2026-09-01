namespace AIUsageMeter.Core;

public static class DemoData
{
    public static IReadOnlyList<ProviderSnapshot> Snapshots(IEnumerable<ProviderId> ids)
    {
        var now = DateTimeOffset.UtcNow;
        return ids.Select((id, index) => new ProviderSnapshot(id,
        [
            new("primary", index % 2 == 0 ? "Current session" : "Monthly", 24 + index * 13 % 78, 100, now.AddHours(2 + index)),
            new("secondary", "Weekly", 41 + index * 7 % 55, 100, now.AddDays(3))
        ], Source: DataSourceKind.Demo, Message: "DEMO DATA", UpdatedAt: now)).ToList();
    }
}
