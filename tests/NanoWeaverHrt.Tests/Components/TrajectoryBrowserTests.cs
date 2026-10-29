using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NanoWeaverHrt.Abstractions;
using NanoWeaverHrt.Analytics;
using NanoWeaverHrt.Components.Pages;
using NanoWeaverHrt.Configuration;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Pipelines;
using NanoWeaverHrt.Storage;
using Xunit;

namespace NanoWeaverHrt.Tests.Components;

/// <summary>
/// Unit and component interaction tests verifying the Trajectory Browser page,
/// interactive DuckDB analytical query drawer, preset SQL templates, and results table in <see cref="TrajectoryBrowser"/>.
/// </summary>
public sealed class TrajectoryBrowserTests : TestContext
{
    /// <summary>
    /// Mock dataset manager providing fake discovered trajectories and exports.
    /// </summary>
    private readonly Mock<IDatasetManager> _datasetManagerMock = new();

    /// <summary>
    /// Mock configuration repository supplying trajectory directory settings.
    /// </summary>
    private readonly Mock<IConfigurationRepository> _configRepoMock = new();

    /// <summary>
    /// Mock parquet trajectory writer injected into the trajectory browser.
    /// </summary>
    private readonly Mock<IParquetTrajectoryWriter> _parquetWriterMock = new();

    /// <summary>
    /// Real in-process DuckDB analytics service instance for running sample queries.
    /// </summary>
    private readonly DuckDbTrajectoryAnalyticsService _analyticsService = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TrajectoryBrowserTests"/> class.
    /// </summary>
    public TrajectoryBrowserTests()
    {
        // WHAT: Registers required services and mock dependencies for TrajectoryBrowser.razor.
        // WHY:  Ensures full DI graph resolution when rendering the interactive page component.
        // HOW:  Configures loose JSInterop and registers mock and real services in the test context.
        JSInterop.Mode = JSRuntimeMode.Loose;

        _datasetManagerMock
            .Setup(m => m.ListTrajectoriesAsync(default))
            .ReturnsAsync(new List<TrajectoryFileItem>
            {
                new()
                {
                    Name = "dna_sample.xyz",
                    Format = "XYZ",
                    SizeBytes = 1024,
                    SizeFormatted = "1.0 KB",
                    LastModifiedUtc = DateTime.UtcNow.ToString("O")
                }
            });

        _datasetManagerMock
            .Setup(m => m.ListExportsAsync(default))
            .ReturnsAsync(new List<ExportFileItem>());

        _configRepoMock
            .Setup(c => c.GetValueAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("/tmp");

        Services.AddSingleton<IDatasetManager>(_datasetManagerMock.Object);
        Services.AddSingleton<IConfigurationRepository>(_configRepoMock.Object);
        Services.AddSingleton<IParquetTrajectoryWriter>(_parquetWriterMock.Object);
        Services.AddSingleton<XyzPipeReader>();
        Services.AddSingleton<DcdPipeReader>();
        Services.AddSingleton<LammpsCustomPipeReader>();
        Services.AddSingleton<ITrajectoryAnalyticsService>(_analyticsService);
    }

    /// <summary>
    /// Releases the unmanaged resources held by the DuckDB test service.
    /// </summary>
    /// <param name="disposing">True when disposing managed resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _analyticsService.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Verifies that the analytical query drawer renders with default controls and initial query.
    /// </summary>
    [Fact]
    public void TrajectoryBrowser_RendersQueryDrawerWithControls()
    {
        // WHAT: Renders the TrajectoryBrowser page and checks presence of analytical drawer elements.
        // WHY:  Ensures researchers are immediately presented with accessible analytical SQL controls.
        // HOW:  Renders component and asserts existence of dataset select, preset select, query textarea, and run button.
        IRenderedComponent<TrajectoryBrowser> cut = RenderComponent<TrajectoryBrowser>();

        Assert.NotNull(cut.Find("#analytics-section"));
        Assert.NotNull(cut.Find("#dataset-select"));
        Assert.NotNull(cut.Find("#preset-select"));
        Assert.NotNull(cut.Find("#sql-query-input"));
        Assert.NotNull(cut.Find("#btn-run-query"));
        Assert.Contains("Distribution of Beads by Region", cut.Find("#preset-select").InnerHtml);
    }

    /// <summary>
    /// Verifies that clicking the drawer toggle collapses and expands the analytical query drawer.
    /// </summary>
    [Fact]
    public void ToggleQueryDrawer_CollapsesAndExpandsDrawer()
    {
        // WHAT: Clicks the query drawer toggle button twice.
        // WHY:  Verifies responsive collapsible drawer behavior.
        // HOW:  Finds toggle button, clicks to hide drawer, asserts absence, clicks again, asserts presence.
        IRenderedComponent<TrajectoryBrowser> cut = RenderComponent<TrajectoryBrowser>();

        Assert.NotEmpty(cut.FindAll(".query-drawer"));

        var toggleBtn = cut.Find("#analytics-section button");
        toggleBtn.Click();

        Assert.Empty(cut.FindAll(".query-drawer"));

        toggleBtn = cut.Find("#analytics-section button");
        toggleBtn.Click();

        Assert.NotEmpty(cut.FindAll(".query-drawer"));
    }

    /// <summary>
    /// Verifies that selecting preset templates populates the query input with the corresponding SQL.
    /// </summary>
    [Fact]
    public void PresetSelector_PopulatesSqlTemplates()
    {
        // WHAT: Triggers change events on the preset dropdown with different template keys.
        // WHY:  Ensures researchers can quickly select pre-built analytical query templates.
        // HOW:  Dispatches change events for bbox, density, and region, asserting textarea updates.
        IRenderedComponent<TrajectoryBrowser> cut = RenderComponent<TrajectoryBrowser>();

        var presetSelect = cut.Find("#preset-select");

        presetSelect.Change("bbox");
        var textarea = cut.Find("#sql-query-input");
        Assert.Contains("dx", textarea.GetAttribute("value") ?? textarea.TextContent);

        presetSelect = cut.Find("#preset-select");
        presetSelect.Change("density");
        textarea = cut.Find("#sql-query-input");
        Assert.Contains("frame_count", textarea.GetAttribute("value") ?? textarea.TextContent);

        presetSelect = cut.Find("#preset-select");
        presetSelect.Change("region");
        textarea = cut.Find("#sql-query-input");
        Assert.Contains("bead_count", textarea.GetAttribute("value") ?? textarea.TextContent);
    }

    /// <summary>
    /// Verifies that executing a valid query renders the tabular results with headers and rows.
    /// </summary>
    [Fact]
    public async Task ExecuteQuery_WithValidSample_RendersResultsTable()
    {
        // WHAT: Executes an analytical query against the sample dataset and inspects the result table.
        // WHY:  Validates interactive SQL execution in the Blazor UI.
        // HOW:  Sets query text, clicks run button, re-queries DOM for #analytics-results-table, asserts cell content.
        IRenderedComponent<TrajectoryBrowser> cut = RenderComponent<TrajectoryBrowser>();

        var textarea = cut.Find("#sql-query-input");
        textarea.Change("SELECT 42 AS val, 'scaffold' AS bead_type;");

        var runBtn = cut.Find("#btn-run-query");
        await cut.InvokeAsync(() => runBtn.Click());

        var table = cut.Find("#analytics-results-table");
        Assert.NotNull(table);
        Assert.Contains("val", table.InnerHtml);
        Assert.Contains("bead_type", table.InnerHtml);
        Assert.Contains("42", table.InnerHtml);
        Assert.Contains("scaffold", table.InnerHtml);
    }

    /// <summary>
    /// Verifies that submitting an empty query displays a user-facing validation error message.
    /// </summary>
    [Fact]
    public async Task ExecuteQuery_WithEmptyQuery_DisplaysErrorMessage()
    {
        // WHAT: Clears query input and triggers execution.
        // WHY:  Verifies client-side validation guard against empty queries.
        // HOW:  Sets textarea to whitespace, clicks run button, asserts presence of error message.
        IRenderedComponent<TrajectoryBrowser> cut = RenderComponent<TrajectoryBrowser>();

        var textarea = cut.Find("#sql-query-input");
        textarea.Change("   ");

        var runBtn = cut.Find("#btn-run-query");
        await cut.InvokeAsync(() => runBtn.Click());

        var errorMsg = cut.Find("#query-error-message");
        Assert.NotNull(errorMsg);
        Assert.Contains("empty", errorMsg.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that clicking Clear Results removes the displayed results table.
    /// </summary>
    [Fact]
    public async Task ClearQueryResults_ClearsResultsTable()
    {
        // WHAT: Executes a valid query, then clicks Clear Results.
        // WHY:  Ensures researchers can reset the analytical display.
        // HOW:  Runs query, asserts results box appears, clicks clear button, asserts results box disappears.
        IRenderedComponent<TrajectoryBrowser> cut = RenderComponent<TrajectoryBrowser>();

        var textarea = cut.Find("#sql-query-input");
        textarea.Change("SELECT 1 AS num;");

        var runBtn = cut.Find("#btn-run-query");
        await cut.InvokeAsync(() => runBtn.Click());

        Assert.NotNull(cut.Find("#query-results-box"));

        var clearBtn = cut.FindAll("button").First(b => b.TextContent.Contains("Clear Results"));
        await cut.InvokeAsync(() => clearBtn.Click());

        Assert.Empty(cut.FindAll("#query-results-box"));
    }
}
