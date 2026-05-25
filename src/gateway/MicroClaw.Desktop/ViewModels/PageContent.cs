namespace MicroClaw.Desktop.ViewModels;

public sealed record StaticMetric(string Label, string Value, string Caption);

public sealed record StaticEntry(string Title, string Description, string Status, string Meta);
