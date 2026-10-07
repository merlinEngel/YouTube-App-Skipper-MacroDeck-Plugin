using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace YouTubeAdSkipper;

/// <summary>
/// The plugin's one integration: the "Skip YouTube Ad" action and the <c>youtube_ad_skippable</c>
/// variable, which the host refreshes every two seconds.
/// </summary>
public sealed class PluginIntegration : IPluginIntegration, IVariableProvider, IDisposable
{
	internal const string AdSkippableVariableId = "ad-skippable";

	private readonly ILogger _logger;
	private readonly SafariYouTubeClient _safari = new();

	private SafariFailure? _lastReportedFailure;

	// Built by DI, so anything the container knows can be taken here: IHttpClientFactory, IOptions<T>,
	// PluginMetadata, IPluginCatalogNotifier.
	public PluginIntegration(ILogger logger)
	{
		_logger = logger.ForContext<PluginIntegration>();
		Actions = [new SkipYouTubeAdAction(_safari)];
	}

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public IReadOnlyList<VariableDefinition> Variables { get; } =
	[
		VariableDefinition.Eager("youtube_ad_skippable", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(2))
			with { Id = AdSkippableVariableId }
	];

	/// <summary>
	/// Runs once the session is established, and again after a non-resume reconnect or a configuration
	/// change, so it has to be safe to run repeatedly against an already-initialized process.
	/// </summary>
	public Task InitializeAsync(IIntegrationContext context)
	{
		_logger.Information("Initialized.");
		return Task.CompletedTask;
	}

	public Task ShutdownAsync() => Task.CompletedTask;

	public void Dispose() => _safari.Dispose();

	/// <summary>False while Safari is closed or nothing skippable is showing. Unavailable only when Safari
	/// cannot be read at all, so a widget shows an empty value instead of a misleading false.</summary>
	public async ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		if (localId != AdSkippableVariableId)
		{
			return VariableReading.Unavailable;
		}

		try
		{
			var probe = await _safari.ProbeAsync(cancellationToken);
			_lastReportedFailure = null;
			return VariableReading.Of(probe == AdProbe.Skippable);
		}
		catch (SafariException exception)
		{
			// Polled every two seconds, so a persistent failure is logged once per change of cause.
			if (_lastReportedFailure != exception.Failure)
			{
				_lastReportedFailure = exception.Failure;
				_logger.Warning("Reading Safari failed ({Failure}): {Message}", exception.Failure, exception.Message);
			}

			return VariableReading.Unavailable;
		}
	}
}
