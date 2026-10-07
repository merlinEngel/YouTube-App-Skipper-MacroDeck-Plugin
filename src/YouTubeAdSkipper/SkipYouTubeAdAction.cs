using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace YouTubeAdSkipper;

/// <summary>Presses the skip button of a YouTube ad playing in Safari.</summary>
public sealed class SkipYouTubeAdAction : IActionDefinition
{
	private readonly SafariYouTubeClient _safari;

	internal SkipYouTubeAdAction(SafariYouTubeClient safari) => _safari = safari;

	public string Id => "skip-youtube-ad";

	public LocalizedText Name => Strings.Actions.SkipYouTubeAd.Name();

	public LocalizedText Description => Strings.Actions.SkipYouTubeAd.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public IActionExecutor CreateExecutor() => new Executor(_safari);

	private sealed class Executor(SafariYouTubeClient safari) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				return await safari.TrySkipAsync(context.CancellationToken)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.NoSkippableAd());
			}
			catch (SafariException exception)
			{
				return SafariFailures.ToResult(exception);
			}
		}
	}
}

internal static class SafariFailures
{
	internal static ActionResult ToResult(SafariException exception) => exception.Failure switch
	{
		SafariFailure.UnsupportedPlatform => ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Errors.MacOsOnly()),
		SafariFailure.JavaScriptDisabled => ActionResult.Failed(ActionErrorCodes.PermissionDenied, Strings.Errors.JavaScriptDisabled()),
		SafariFailure.AutomationDenied => ActionResult.Failed(ActionErrorCodes.PermissionDenied, Strings.Errors.AutomationDenied()),
		_ => ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Errors.SafariFailed())
	};
}
