# Selenium Automation Practice Form

A .NET 8 console app that fills First Name, Gender, and State using Selenium WebDriver and Chrome.

## Setup

- Install the .NET 8 SDK and Google Chrome.
- Run `dotnet restore` from the project directory. Selenium Manager will obtain a compatible ChromeDriver when needed.
- Configure the values and defaults in `appsettings.json`.

## Run

```powershell
dotnet run -- "https://app.cloudqa.io/home/AutomationPracticeForm"
```

URL precedence is positional CLI URL, then `PAGE_URL`, then `PageUrl` in `appsettings.json`, then the built-in URL. Headless mode defaults to true; use `--no-headless` or `--headless` to override it. `FIRSTNAME`, `GENDER`, `STATE`, and `TIMEOUTSECONDS` can override the corresponding settings. For example:

```powershell
$env:PAGE_URL = 'https://example.test/form'
$env:FIRSTNAME = 'Avery'
dotnet run -- --no-headless
```

## Resolver behavior

The resolver checks controls of the required type, then scores direct label associations (`for`, wrapping labels, and `aria-labelledby`), weak text hints in aria-label/placeholder/name/id, and proximity to matching label text. Exact normalized label text is preferred. Normalization lowercases, trims, collapses whitespace, and removes colons and asterisks. Hidden or disabled controls are excluded; radio options must also match the requested gender. Each candidate score and the selected strategy are logged. Searches recurse into iframes.

State uses Selenium `SelectElement` for native selects. For custom controls the app clicks the resolved trigger and waits for a visible option with matching text. On the current CloudQA page, the field labeled **State** contains country choices (including `United States`), while **Country** is a text box; `appsettings.json` therefore defaults State to `United States`. Change `State` to another option actually shown by that dropdown. Explicit waits verify the final text, selected radio, or selected state.

## Mutation fixtures

`tests/mutations/` contains five locally saved HTML variants. Run them with:

```powershell
.\tests\mutations\run-mutations.ps1
```

The script opens each fixture through a `file://` URL and prints PASS/FAIL per case. Fixtures are representative local examples, not captured copies of the live site.

## Limitations

No heuristic resolver can disambiguate arbitrary pages with indistinguishable labels and controls. Tweak the normalized target names or scoring rules for a site whose labels differ substantially. The live site may require consent, authentication, or extra interaction outside these three fields. Custom dropdown markup varies; the fallback recognizes common option roles and visible list items.
