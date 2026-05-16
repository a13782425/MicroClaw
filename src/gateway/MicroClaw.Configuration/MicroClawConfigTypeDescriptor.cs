namespace MicroClaw.Configuration;
internal sealed record MicroClawConfigTypeDescriptor(Type OptionsType, string SectionKey, string? FileName, string? DirectoryPath, string? HeaderComment);