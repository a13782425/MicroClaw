namespace MicroClaw.Configuration;
internal sealed record MicroClawConfigTypeDescriptor(Type YamlConfigType, string SectionKey, string? FileName, string? DirectoryPath, string? HeaderComment);