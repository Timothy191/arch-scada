using Scada.Report;
using Xunit;

namespace ScadaReport.Tests;

public class OutputFormatTests
{
    [Fact]
    public void Enum_Contains_Expected_Values()
    {
        // Assert
        Assert.Equal(OutputFormat.Default, OutputFormat.Default);
        Assert.Equal(OutputFormat.Pdf, OutputFormat.Pdf);
        Assert.Equal(OutputFormat.Xml2003, OutputFormat.Xml2003);
        Assert.Equal(OutputFormat.Xlsx, OutputFormat.Xlsx);
        Assert.Equal(OutputFormat.Html, OutputFormat.Html);
    }

    [Fact]
    public void Enum_Has_Five_Distinct_Values()
    {
        // Act
        var values = Enum.GetValues<OutputFormat>();

        // Assert
        Assert.Equal(5, values.Length);
    }
}