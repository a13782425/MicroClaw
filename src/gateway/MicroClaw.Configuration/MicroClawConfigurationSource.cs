using Microsoft.Extensions.Configuration;

namespace MicroClaw.Configuration;

public sealed class MicroClawConfigurationSource : IConfigurationSource
{
    private readonly string _filePath;

    public MicroClawConfigurationSource(string filePath)
    {
        _filePath = filePath;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => new MicroClawConfigurationProvider(_filePath);
}
