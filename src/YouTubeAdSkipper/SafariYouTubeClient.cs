using System.Diagnostics;

namespace YouTubeAdSkipper;

internal enum AdProbe
{
	SafariClosed,
	Idle,
	Playing,
	Skippable
}

internal enum SafariFailure
{
	UnsupportedPlatform,
	JavaScriptDisabled,
	AutomationDenied,
	Other
}

internal sealed class SafariException(SafariFailure failure, string message) : Exception(message)
{
	public SafariFailure Failure { get; } = failure;
}

/// <summary>
/// Talks to Safari through <c>osascript</c>: a script walks every YouTube tab and runs a small piece of
/// JavaScript in it. Reading the DOM is more robust than clicking screen coordinates, and it leaves the
/// mouse alone.
/// </summary>
internal sealed class SafariYouTubeClient : IDisposable
{
	private const string OsascriptPath = "/usr/bin/osascript";
	private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(10);

	private const string SkipButtonSelector =
		".ytp-skip-ad-button, .ytp-ad-skip-button-modern, .ytp-ad-skip-button";

	private const string ProbeScript = $$"""
		(function () {
			var v = document.querySelector('video.html5-main-video') || document.querySelector('video');
			if (!v || v.paused || v.ended) return 'idle';
			var b = document.querySelector('{{SkipButtonSelector}}');
			return b && b.getClientRects().length > 0 ? 'skippable' : 'playing';
		})()
		""";

	private const string SkipScript = $$"""
		(function () {
			var v = document.querySelector('video.html5-main-video') || document.querySelector('video');
			if (!v || v.paused || v.ended) return 'idle';
			var b = document.querySelector('{{SkipButtonSelector}}');
			if (b && b.getClientRects().length > 0) { b.click(); return 'skipped'; }
			return 'playing';
		})()
		""";

	// "application is running" does not launch Safari, unlike addressing it with tell. The JavaScript
	// arrives as an argument so it needs no AppleScript escaping.
	private const string AppleScript = """
		on run argv
			set js to item 1 of argv
			if application "Safari" is not running then return "off"
			set out to {}
			tell application "Safari"
				repeat with w in windows
					repeat with t in tabs of w
						set tabUrl to ""
						try
							set tabUrl to URL of t
						end try
						if tabUrl contains "youtube.com" then
							set end of out to (do JavaScript js in t)
						end if
					end repeat
				end repeat
			end tell
			set AppleScript's text item delimiters to ","
			return out as text
		end run
		""";

	private readonly SemaphoreSlim _gate = new(1, 1);

	public void Dispose() => _gate.Dispose();

	public async Task<AdProbe> ProbeAsync(CancellationToken cancellationToken)
	{
		var results = await RunAsync(ProbeScript, cancellationToken);
		if (results is null)
		{
			return AdProbe.SafariClosed;
		}

		if (results.Contains("skippable"))
		{
			return AdProbe.Skippable;
		}

		return results.Contains("playing") ? AdProbe.Playing : AdProbe.Idle;
	}

	/// <summary>Clicks the skip button of every YouTube tab that currently offers one. Returns whether a
	/// click happened.</summary>
	public async Task<bool> TrySkipAsync(CancellationToken cancellationToken)
	{
		var results = await RunAsync(SkipScript, cancellationToken);
		return results is not null && results.Contains("skipped");
	}

	/// <summary>Returns the per-tab results, or null when Safari is not running.</summary>
	private async Task<IReadOnlyList<string>?> RunAsync(string javaScript, CancellationToken cancellationToken)
	{
		if (!OperatingSystem.IsMacOS())
		{
			throw new SafariException(SafariFailure.UnsupportedPlatform, "Safari automation needs macOS.");
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(ProcessTimeout);

			using var process = new Process
			{
				StartInfo = new ProcessStartInfo(OsascriptPath)
				{
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					ArgumentList = { "-e", AppleScript, javaScript }
				}
			};

			process.Start();
			var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
			var error = process.StandardError.ReadToEndAsync(timeout.Token);

			try
			{
				await process.WaitForExitAsync(timeout.Token);
			}
			catch (OperationCanceledException)
			{
				process.Kill(entireProcessTree: true);
				cancellationToken.ThrowIfCancellationRequested();
				throw new SafariException(SafariFailure.Other, "osascript timed out.");
			}

			if (process.ExitCode != 0)
			{
				throw Classify(await error);
			}

			var text = (await output).Trim();
			return text == "off" ? null : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		}
		finally
		{
			_gate.Release();
		}
	}

	private static SafariException Classify(string stderr)
	{
		if (stderr.Contains("Allow JavaScript from Apple Events", StringComparison.OrdinalIgnoreCase))
		{
			return new SafariException(SafariFailure.JavaScriptDisabled, stderr.Trim());
		}

		// -1743 is errAEEventNotPermitted, the code macOS reports when Automation access is refused.
		if (stderr.Contains("(-1743)", StringComparison.Ordinal))
		{
			return new SafariException(SafariFailure.AutomationDenied, stderr.Trim());
		}

		return new SafariException(SafariFailure.Other, stderr.Trim());
	}
}
