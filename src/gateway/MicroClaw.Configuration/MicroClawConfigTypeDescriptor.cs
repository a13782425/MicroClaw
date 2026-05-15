namespace MicroClaw.Configuration;

internal sealed record MicroClawConfigTypeDescriptor(
    Type OptionsType,
    string SectionKey,
    string? FileName,
    bool IsWritable);