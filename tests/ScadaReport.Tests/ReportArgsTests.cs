using Scada;
using Scada.Report;
using Xunit;

namespace ScadaReport.Tests;

public class ReportArgsTests
{
    [Fact]
    public void Default_Constructor_Initializes_With_Expected_Defaults()
    {
        // Arrange & Act
        var args = new ReportArgs();

        // Assert
        Assert.Equal(DateTime.MinValue, args.StartTime);
        Assert.Equal(DateTime.MinValue, args.EndTime);
        Assert.Equal(0, args.MaxPeriod);
        Assert.Equal(string.Empty, args.ArchiveCode);
        Assert.Equal(OutputFormat.Default, args.Format);
        Assert.NotNull(args.CustomArgs);
        Assert.Empty(args.CustomArgs);
    }

    [Fact]
    public void Validate_Throws_When_StartTime_After_EndTime()
    {
        // Arrange
        var args = new ReportArgs
        {
            StartTime = new DateTime(2026, 1, 2),
            EndTime = new DateTime(2026, 1, 1)
        };

        // Act & Assert
        var ex = Assert.Throws<ScadaException>(() => args.Validate());
        Assert.True(ex.MessageIsPublic);
        Assert.Contains("Invalid time range", ex.Message);
    }

    [Fact]
    public void Validate_Throws_When_Period_Exceeds_MaxPeriod()
    {
        // Arrange
        var args = new ReportArgs
        {
            StartTime = new DateTime(2026, 1, 1),
            EndTime = new DateTime(2026, 1, 10),
            MaxPeriod = 5
        };

        // Act & Assert
        var ex = Assert.Throws<ScadaException>(() => args.Validate());
        Assert.True(ex.MessageIsPublic);
        Assert.Contains("Report period exceeded", ex.Message);
    }

    [Fact]
    public void Validate_Passes_When_TimeRange_Valid_And_Within_MaxPeriod()
    {
        // Arrange
        var args = new ReportArgs
        {
            StartTime = new DateTime(2026, 1, 1),
            EndTime = new DateTime(2026, 1, 5),
            MaxPeriod = 10,
            ArchiveCode = "Events"
        };

        // Act
        args.Validate();

        // Assert - no exception thrown
        Assert.True(true);
    }

    [Fact]
    public void Validate_Passes_When_MaxPeriod_Zero_Disables_Period_Check()
    {
        // Arrange
        var args = new ReportArgs
        {
            StartTime = new DateTime(2026, 1, 1),
            EndTime = new DateTime(2026, 12, 31),
            MaxPeriod = 0
        };

        // Act
        args.Validate();

        // Assert - no exception thrown
        Assert.True(true);
    }

    [Fact]
    public void CustomArgs_Can_Be_Populated()
    {
        // Arrange
        var args = new ReportArgs();

        // Act
        args.CustomArgs["viewId"] = "overview";
        args.CustomArgs["detailLevel"] = "high";

        // Assert
        Assert.Equal("overview", args.CustomArgs["viewId"]);
        Assert.Equal("high", args.CustomArgs["detailLevel"]);
        Assert.Equal(2, args.CustomArgs.Count);
    }
}