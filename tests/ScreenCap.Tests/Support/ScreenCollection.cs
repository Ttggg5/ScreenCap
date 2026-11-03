namespace ScreenCap.Tests.Support;

/// <summary>
/// Tests that read the real screen. They run on their own, after the others: tests running alongside show
/// windows of their own (topmost overlays), which would cover what these expect to see.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ScreenCollection
{
    public const string Name = "Screen";
}
