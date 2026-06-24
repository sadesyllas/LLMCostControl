namespace LLMCostControl.Admin.App.Tests;

public class SmokeTests
{
    [Fact]
    public void Project_is_wired_and_tests_run()
    {
        const int expected = 2;
        var actual = 1 + 1;

        actual.Should().Be(expected);
    }
}
