using DementiaComputerButtons.Core.Models;
using DementiaComputerButtons.Core.Services;

namespace DementiaComputerButtons.Tests;

public sealed class SystemStateServiceTests
{
    [Fact]
    public void TransitionsAndReturnsHome()
    {
        var service = new SystemStateService(new TestLog());
        var observed = new List<DadConsoleState>();
        service.StateChanged += (_, state) => observed.Add(state);
        service.TransitionTo(DadConsoleState.WatchingTV, "test");
        service.ReturnHome("stop");
        Assert.Equal([DadConsoleState.WatchingTV, DadConsoleState.Home], observed);
        Assert.Equal(DadConsoleState.Home, service.Current);
    }

    [Fact]
    public void DoesNotRaiseDuplicateTransition()
    {
        var service = new SystemStateService(new TestLog());
        var count = 0;
        service.StateChanged += (_, _) => count++;
        service.TransitionTo(DadConsoleState.Home, "already home");
        Assert.Equal(0, count);
    }
}
