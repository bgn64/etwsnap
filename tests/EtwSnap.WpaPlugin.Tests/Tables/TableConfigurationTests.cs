using EtwSnap.WpaPlugin.Tables;
using Microsoft.Performance.SDK.Processing;

namespace EtwSnap.WpaPlugin.Tests.Tables;

public sealed class TableConfigurationTests
{
    [Theory]
    [InlineData("All Frames")]
    [InlineData("Saved Screenshots")]
    public void ScreenshotsArePointsWithDescribedColumns(string name)
    {
        var configuration = ScreenshotsTable.CreateConfiguration(name);

        Assert.Equal(ChartType.PointInTime, configuration.ChartType);
        Assert.Single(configuration.ColumnRoles);
        Assert.True(configuration.ColumnRoles.ContainsKey(ColumnRole.StartTime));
        Assert.DoesNotContain(configuration.Columns, column => column.Metadata.Name == "Duration");
        Assert.Equal(["Start Time"], PlottedColumns(configuration));
        AssertColumnDescriptions(configuration);
    }

    [Fact]
    public void SessionsPlotStartAndEndWithDescribedColumns()
    {
        var configuration = SessionsTable.CreateConfiguration();

        Assert.Equal(ChartType.Line, configuration.ChartType);
        Assert.True(configuration.ColumnRoles.ContainsKey(ColumnRole.StartTime));
        Assert.True(configuration.ColumnRoles.ContainsKey(ColumnRole.EndTime));
        Assert.True(configuration.ColumnRoles.ContainsKey(ColumnRole.Duration));
        Assert.Equal(["Start Time", "End Time"], PlottedColumns(configuration));
        AssertColumnDescriptions(configuration);
    }

    private static IEnumerable<string> PlottedColumns(TableConfiguration configuration) => configuration.Columns
        .SkipWhile(column => column != TableConfiguration.GraphColumn)
        .Skip(1)
        .Select(column => column.Metadata.Name);

    private static void AssertColumnDescriptions(TableConfiguration configuration) => Assert.All(
        configuration.Columns.Where(column => column != TableConfiguration.GraphColumn && column != TableConfiguration.PivotColumn),
        column =>
        {
            Assert.False(string.IsNullOrWhiteSpace(column.Metadata.Description), $"Missing description: {column.Metadata.Name}");
            Assert.Equal(column.Metadata.Description, column.Metadata.ShortDescription);
        });
}