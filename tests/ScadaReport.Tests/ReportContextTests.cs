using Scada;
using Scada.Client;
using Scada.Data.Entities;
using Scada.Data.Models;
using Scada.Data.Tables;
using Scada.Lang;
using Scada.Report;
using System.Collections.Generic;
using System.Globalization;
using Xunit;

namespace ScadaReport.Tests;

public class ReportContextTests
{
    [Fact]
    public void Context_Properties_Are_Settable_Via_Init()
    {
        // Arrange
        var configDb = new ConfigDataset();
        var connectionString = "Server=localhost;Database=scada;";
        var culture = CultureInfo.GetCultureInfo("en-US");
        var timeZone = TimeZoneInfo.Utc;
        var username = "operator1";
        var configDir = "/opt/scada/config";
        var templateDir = "/opt/scada/templates";
        var cssPath = "/opt/scada/css";

        // Act
        IReportContext context = new ReportContext
        {
            ConfigDatabase = configDb,
            ScadaClient = null!,
            ConnectionString = connectionString,
            Culture = culture,
            TimeZone = timeZone,
            Username = username,
            ConfigDir = configDir,
            TemplateDir = templateDir,
            CssPath = cssPath
        };

        // Assert
        Assert.Same(configDb, context.ConfigDatabase);
        Assert.Equal(connectionString, context.ConnectionString);
        Assert.Same(culture, context.Culture);
        Assert.Same(timeZone, context.TimeZone);
        Assert.Equal(username, context.Username);
        Assert.Equal(configDir, context.ConfigDir);
        Assert.Equal(templateDir, context.TemplateDir);
        Assert.Equal(cssPath, context.CssPath);
    }

    [Fact]
    public void FormatDateTime_Converts_Utc_To_Local_Time()
    {
        // Arrange
        IReportContext context = new ReportContext
        {
            ConfigDatabase = new ConfigDataset(),
            ScadaClient = null!,
            ConnectionString = "",
            Culture = CultureInfo.InvariantCulture,
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById("UTC"),
            Username = "test",
            ConfigDir = "",
            TemplateDir = "",
            CssPath = ""
        };
        var utcTime = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var result = context.FormatDateTime(utcTime);

        // Assert
        Assert.Equal("06/15/2026 12:00:00", result);
    }

    [Fact]
    public void ConvertTimeFromUtc_Converts_To_Specified_TimeZone()
    {
        // Arrange
        IReportContext context = new ReportContext
        {
            ConfigDatabase = new ConfigDataset(),
            ScadaClient = null!,
            ConnectionString = "",
            Culture = CultureInfo.InvariantCulture,
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById("UTC"),
            Username = "test",
            ConfigDir = "",
            TemplateDir = "",
            CssPath = ""
        };
        var utcTime = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var result = context.ConvertTimeFromUtc(utcTime);

        // Assert
        Assert.Equal(utcTime, result);
    }

    [Fact]
    public void FindArchive_Throws_When_Archive_Not_Found()
    {
        // Arrange
        IReportContext context = new ReportContext
        {
            ConfigDatabase = new ConfigDataset(),
            ScadaClient = null!,
            ConnectionString = "",
            Culture = CultureInfo.InvariantCulture,
            TimeZone = TimeZoneInfo.Utc,
            Username = "test",
            ConfigDir = "",
            TemplateDir = "",
            CssPath = ""
        };

        // Act & Assert
        var ex = Assert.Throws<ScadaException>(() => context.FindArchive("NonExistent"));
        Assert.True(ex.MessageIsPublic);
        Assert.Contains("Archive not found", ex.Message);
    }

    [Fact]
    public void FindChannels_Returns_Empty_List_For_Null_Input()
    {
        // Arrange
        IReportContext context = new ReportContext
        {
            ConfigDatabase = new ConfigDataset(),
            ScadaClient = null!,
            ConnectionString = "",
            Culture = CultureInfo.InvariantCulture,
            TimeZone = TimeZoneInfo.Utc,
            Username = "test",
            ConfigDir = "",
            TemplateDir = "",
            CssPath = ""
        };

        // Act
        var result = context.FindChannels(null);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void FindChannels_Returns_Empty_Channels_For_Missing_CnlNums()
    {
        // Arrange
        IReportContext context = new ReportContext
        {
            ConfigDatabase = new ConfigDataset(),
            ScadaClient = null!,
            ConnectionString = "",
            Culture = CultureInfo.InvariantCulture,
            TimeZone = TimeZoneInfo.Utc,
            Username = "test",
            ConfigDir = "",
            TemplateDir = "",
            CssPath = ""
        };

        // Act
        var result = context.FindChannels(new List<int> { 999, 1000 });

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(999, result[0].CnlNum);
        Assert.Equal(string.Empty, result[0].Name);
        Assert.Equal(1000, result[1].CnlNum);
        Assert.Equal(string.Empty, result[1].Name);
    }
}