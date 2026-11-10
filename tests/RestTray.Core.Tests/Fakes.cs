using RestTray.Core.Abstractions;

namespace RestTray.Core.Tests;

/// <summary>虚拟时钟:测试手动推进时间,不依赖真实等待。</summary>
internal sealed class FakeClock : IClock
{
    public FakeClock(DateTime start) => Now = start;

    public DateTime Now { get; set; }

    public void Advance(TimeSpan delta) => Now += delta;
}

/// <summary>虚拟空闲源:测试直接设定系统空闲时长。</summary>
internal sealed class FakeIdleSource : IIdleTimeSource
{
    public string Name => "Fake";

    public TimeSpan Idle { get; set; } = TimeSpan.Zero;

    public TimeSpan GetIdleTime() => Idle;
}
