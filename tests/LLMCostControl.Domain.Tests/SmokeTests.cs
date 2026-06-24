namespace LLMCostControl.Domain.Tests;

public class SmokeTests
{
    [Fact]
    public void Solution_is_wired_and_tests_run()
    {
        const int expected = 2;
        var actual = 1 + 1;

        actual.Should().Be(expected);
    }
}
