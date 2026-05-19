using System;

namespace ShadUI.Demo;

public sealed class PageChangedMessage
{
    public required Type PageType { get; init; }
}