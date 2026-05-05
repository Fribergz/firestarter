using Firestarter.Core.Extensions;

namespace Firestarter.Core.Tests.Extensions;

public class StatsExtractorTests
{
    [Fact]
    public void Extract_returns_null_when_stdout_has_no_json()
    {
        var result = StatsExtractor.Extract("hello world\nall done\n");
        Assert.Null(result);
    }

    [Fact]
    public void Extract_reads_fenced_json_block()
    {
        var stdout = """
starting work
processed 10 items
```json
{ "items": 10, "errors": 0 }
```
bye
""";
        var result = StatsExtractor.Extract(stdout);
        Assert.NotNull(result);
        Assert.Contains("\"items\"", result);
    }

    [Fact]
    public void Extract_reads_trailing_balanced_json()
    {
        var stdout = "doing stuff\n{\"count\":3,\"nested\":{\"ok\":true}}";
        var result = StatsExtractor.Extract(stdout);
        Assert.NotNull(result);
        Assert.Contains("\"nested\"", result);
    }

    [Fact]
    public void Extract_returns_null_when_trailing_json_is_invalid()
    {
        var stdout = "prefix\n{not valid json}";
        var result = StatsExtractor.Extract(stdout);
        Assert.Null(result);
    }

    [Fact]
    public void Extract_prefers_fenced_block_even_with_trailing_content()
    {
        var stdout = """
running...
```json
{ "source": "fence" }
```
tail noise
""";
        var result = StatsExtractor.Extract(stdout);
        Assert.NotNull(result);
        Assert.Contains("fence", result);
    }
}
