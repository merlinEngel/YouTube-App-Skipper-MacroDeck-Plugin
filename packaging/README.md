# Packaging

`MacroDeck.Plugin.Templates.csproj` packs the repository root as a `dotnet new` template package. It is
deliberately outside `MacroDeck.PluginTemplate.slnx`: `dotnet build` at the root builds the plugin, not
the package that ships it.

The repository root *is* the template content. What `dotnet new macrodeck-plugin` writes out is exactly
what a clone of this repository contains, minus what `.template.config/template.json` excludes
(`packaging/`, `.github/`, `local-feed/*.nupkg`, build output). So a change to the template is an
ordinary change to the plugin in `src/`.

The one exception is `.template.config/content/`, which holds conditional variants of `manifest.json`
and `macrodeck-build.json`. Platform selection and the omission of an unsupplied `homepage` need
`//#if` markers, and a file carrying those is not valid JSON - which would break
`macrodeck-plugin validate` and `build` and the manifest reader for anyone who *clones* the repository
instead of generating from it. So the files under `src/` stay valid JSON, `template.json` excludes them
from the first source and maps the variants over them from a second one.

That is a real second copy. Change one and you must change the other: rendering the template with
default parameters has to reproduce the `src/` files byte for byte, which the CI job below checks.

## Releasing

The release version comes from a Git tag. A release is:

1. Merge the release commit to `main`.
2. Create and push a semantic-version tag such as `v3.0.0-preview.3`.

`.github/workflows/publish.yml` builds, tests, packs and pushes to nuget.org for tags beginning with `v`
and a digit. It removes the leading `v`, validates the remaining semantic version and passes it to
`dotnet pack`, so `v3.0.0-preview.3` publishes package version `3.0.0-preview.3`. The workflow is
authenticated through [trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
rather than a stored API key - the `NUGET_USER` secret is the nuget.org account that owns the policy, and
the policy names `publish.yml`. The push uses `--skip-duplicate` to make retrying a release safe.

Moving the `NuGet/login` step into another workflow file would silently need a policy of its own; keep
it here.

## Building the package locally

```bash
dotnet pack packaging/MacroDeck.Plugin.Templates.csproj -p:Version=3.0.0-preview.3 -o ./artifacts
```

Without `-p:Version`, local and non-release CI builds use `0.0.0-local`.

```bash
dotnet new install ./artifacts/MacroDeck.Plugin.Templates.<version>.nupkg
```

```bash
dotnet new macrodeck-plugin -n Acme.LightControl --pluginId com.acme.light-control --pluginName "Acme Light Control"
```

`dotnet new uninstall MacroDeck.Plugin.Templates` removes it again. Reinstalling after a repack needs
the uninstall first - the same version installed twice is not refreshed in place.

## Keeping the template a template

Anything added to the repository root ships to everyone who runs `dotnet new macrodeck-plugin`, so:

- **Keep it minimal.** A capability added "to show how" is a capability every generated plugin then has
  to delete. Demonstrations belong in the
  [sample plugins repository](https://github.com/Macro-Deck-App/Macro-Deck-Sample-Plugins).
- **Exclude repository-only files.** A new root-level file that is about maintaining the template rather
  than writing a plugin needs an entry in `.template.config/template.json`.
- **Exclude local credentials twice.** `.macrodeck-dev-state/` must stay out of both the package glob in
  `MacroDeck.Plugin.Templates.csproj` and generated output in `.template.config/template.json`; Gitignore
  alone does not stop an untracked credential from entering the `.nupkg`.
- **Keep renaming working.** `sourceName` is `MacroDeck.PluginTemplate`, `pluginId` replaces
  `app.macro-deck.template` and `pluginName` replaces `Macro Deck Plugin Template`. Anything that
  hardcodes one of those strings has to keep matching. The same goes for the metadata symbols:
  `publisher` replaces `Example Publisher`, `description` replaces `A minimal Macro Deck 3 plugin.`,
  `license` replaces `MIT`, `repository` replaces `https://github.com/example/my-plugin` and `homepage`
  replaces `https://example.com/my-plugin`.
- **Watch what `replaces` sweeps up.** A `replaces` value is plain text matched across every processed
  file, not a JSON path. `MIT` is why `LICENSE` is `copyOnly` - otherwise `--license Apache-2.0` would
  rewrite the licence text itself. Before adding a symbol, grep the repository for its placeholder and
  confirm every hit should change.
- **Keep the resx `copyOnly`.** `Localization/*.resx` carries no placeholder and must not be run through
  conditional processing.
- **Do not link to repository-only files by relative path** from `README.md` or `AGENTS.md`: both ship
  into generated projects, where such a link is dead. Link to the file on GitHub instead.

## What to check after changing the template

`dotnet new` renaming is driven by `sourceName` (`MacroDeck.PluginTemplate`) plus the `pluginId` and
`pluginName` parameters, so anything that hardcodes those strings has to keep matching. After a change,
generate a project and confirm:

- the project, test project, solution file and namespaces all carry the new name,
- `manifest.json` carries the new `id`, `name`, `publisher.name`, `license` and per-platform
  `executable` values and `repository`, and omits `homepage` when it was not supplied,
- `macrodeck-build.json` has a target for exactly the platforms `--platforms` selected, matching
  `entrypoints`,
- generating with default parameters reproduces `src/MacroDeck.PluginTemplate/manifest.json` and
  `macrodeck-build.json` byte for byte,
- `Properties/launchSettings.json` exists with only the secret-free **Macro Deck - Real Host** profile,
- no `.run/` directory or `.macrodeck-dev-state/` content was emitted,
- `dotnet build` and `dotnet test` pass in the generated project,
- `macrodeck-plugin build` publishes every declared runtime identifier and packs an artifact that
  `validate` accepts and `run --stub-host` can start,
- no repository-only file (`packaging/`, `.github/`, packed SDK packages) leaked into the output.

CI does all of this on every pull request, so the list is a description of the gate rather than a manual
checklist. The last item is the one worth understanding: `entrypoints` and `macrodeck-build.json` are
two files that have to agree, and nothing but an actual `build` proves they do. A template that emits a
target for a platform the manifest does not declare - or an entrypoint path that does not match what the
publish step writes - fails there and nowhere earlier.
