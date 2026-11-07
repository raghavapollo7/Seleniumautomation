using System.Text.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

var options = AppOptions.Load(args);
using var driver = new ChromeDriver(ChromeOptionsFactory.Create(options.Headless));
driver.Navigate().GoToUrl(options.PageUrl);
var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(options.TimeoutSeconds));
var resolver = new ElementResolver(driver, wait);
var results = new List<(string Field, bool Passed)>();

try
{
    var firstName = resolver.Resolve("First Name", "text");
    firstName.Element.Clear();
    firstName.Element.SendKeys(options.FirstName);
    var nameOk = wait.Until(_ => firstName.Element.GetAttribute("value") == options.FirstName);
    Report("First Name", firstName, nameOk);
    results.Add(("First Name", nameOk));
}
catch (Exception ex) { ReportFailure("First Name", ex); results.Add(("First Name", false)); }

try
{
    var radios = resolver.ResolveRadio("Gender", options.Gender);
    radios.Element.Click();
    var genderOk = wait.Until(_ => radios.Element.Selected && ElementResolver.Normalize(radios.Description).Contains(ElementResolver.Normalize(options.Gender), StringComparison.Ordinal));
    Report("Gender", radios, genderOk);
    results.Add(("Gender", genderOk));
}
catch (Exception ex) { ReportFailure("Gender", ex); results.Add(("Gender", false)); }

try
{
    var state = resolver.SelectState(options.State);
    var stateOk = wait.Until(_ => SelectedText(state.Element).Equals(ElementResolver.Normalize(options.State), StringComparison.Ordinal));
    Report("State", state, stateOk);
    results.Add(("State", stateOk));
}
catch (Exception ex) { ReportFailure("State", ex); results.Add(("State", false)); }

Console.WriteLine($"\nSummary: {results.Count(x => x.Passed)}/{results.Count} passed");
Environment.ExitCode = results.All(x => x.Passed) ? 0 : 1;

static string SelectedText(IWebElement element) => element.TagName.Equals("select", StringComparison.OrdinalIgnoreCase)
    ? ElementResolver.Normalize(new SelectElement(element).SelectedOption.Text)
    : ElementResolver.Normalize(element.GetAttribute("aria-label") ?? element.Text);
static void Report(string field, Resolution result, bool passed) =>
    Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {field}: strategy={result.Strategy}, score={result.Score:0.00}, candidate={result.Description}");
static void ReportFailure(string field, Exception ex) => Console.WriteLine($"FAIL {field}: {ex.GetType().Name}: {ex.Message}");

static class ChromeOptionsFactory
{
    public static ChromeOptions Create(bool headless)
    {
        var options = new ChromeOptions();
        if (headless) options.AddArgument("--headless=new");
        options.AddArgument("--window-size=1440,1000");
        options.AddArgument("--disable-gpu");
        return options;
    }
}

sealed record AppOptions(string PageUrl, bool Headless, string FirstName, string Gender, string State, int TimeoutSeconds)
{
    public static AppOptions Load(string[] args)
    {
        var file = File.Exists("appsettings.json") ? JsonDocument.Parse(File.ReadAllText("appsettings.json")).RootElement : default;
        string Get(string key, string fallback) => Environment.GetEnvironmentVariable(key.ToUpperInvariant()) ??
            (file.ValueKind == JsonValueKind.Object && file.TryGetProperty(key, out var v) ? v.ToString() : fallback);
        bool headless = bool.TryParse(Get("Headless", "true"), out var h) && h;
        for (var i = 0; i < args.Length; i++)
            if (args[i] == "--headless") headless = true;
            else if (args[i] == "--no-headless") headless = false;
        var url = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Environment.GetEnvironmentVariable("PAGE_URL") ?? Get("PageUrl", "https://app.cloudqa.io/home/AutomationPracticeForm");
        return new(url, headless, Get("FirstName", "Taylor"), Get("Gender", "Female"), Get("State", "California"), int.TryParse(Get("TimeoutSeconds", "10"), out var seconds) ? seconds : 10);
    }
}

sealed record Resolution(IWebElement Element, string Strategy, double Score, string Description);

/// <summary>Combines semantic label association, weak attribute hints, and label proximity.</summary>
sealed class ElementResolver(IWebDriver driver, WebDriverWait wait)
{
    public static string Normalize(string? text) => System.Text.RegularExpressions.Regex.Replace((text ?? "").Trim().ToLowerInvariant(), @"\s+", " ")
        .Replace(":", "").Replace("*", "").Trim();

    public Resolution Resolve(string target, string kind) => SearchFrames(() => ResolveInFrame(target, kind));
    public Resolution SelectState(string desired) => SearchFrames(() =>
    {
        var selects = driver.FindElements(By.TagName("select")).Where(Usable).ToList();
        var wanted = Normalize(desired);
        IWebElement? stateSelect = selects.FirstOrDefault(s => new SelectElement(s).Options.Any(o => Normalize(o.Text) == wanted));
        if (stateSelect is not null)
        {
            var option = new SelectElement(stateSelect).Options.First(o => Normalize(o.Text) == wanted);
            new SelectElement(stateSelect).SelectByText(option.Text);
            Console.WriteLine($"  selected '{option.Text}' from dropdown {Describe(stateSelect)}");
            return new Resolution(stateSelect, "option text (case-insensitive)", 1.0, Describe(stateSelect));
        }
        var choices = string.Join(" | ", selects.SelectMany(s => new SelectElement(s).Options).Select(o => Normalize(o.Text)).Where(t => t.Length > 0).Distinct());
        if (selects.Count > 0)
            throw new NoSuchElementException($"'{desired}' is not an available option in the State dropdown. Choices: {choices}");
        var customTrigger = ResolveInFrame("State", "select");
        customTrigger.Element.Click();
        var customOption = wait.Until(_ => FindVisibleOption(driver, desired));
        customOption.Click();
        return new Resolution(customTrigger.Element, "custom dropdown option text", customTrigger.Score, customTrigger.Description);
    });
    private static IWebElement FindVisibleOption(IWebDriver driver, string text)
    {
        var wanted = Normalize(text);
        return driver.FindElements(By.XPath("//*[self::option or @role='option' or self::li or self::div or self::button]"))
            .FirstOrDefault(e => e.Displayed && Normalize(e.Text).Equals(wanted, StringComparison.OrdinalIgnoreCase))
            ?? throw new NoSuchElementException($"No visible option matched '{text}'.");
    }
    public Resolution ResolveRadio(string target, string desired) => SearchFrames(() =>
    {
        var radioCandidates = driver.FindElements(By.CssSelector("input[type='radio']"));
        var scored = new List<(IWebElement Element, double Score, string Strategy, string Description)>();
        foreach (var radio in radioCandidates)
        {
            if (!Usable(radio)) continue;
            var explicitLabel = driver.FindElements(By.XPath("//label")).FirstOrDefault(l => !string.IsNullOrEmpty(radio.GetAttribute("id")) && l.GetAttribute("for") == radio.GetAttribute("id"));
            var wrappingLabel = AssociatedLabel(radio);
            var label = explicitLabel ?? wrappingLabel;
            // A broad wrapping label can cover an entire group. Prefer the nearest option-sized wrapper's text.
            var localText = LocalOptionText(radio, wrappingLabel);
            var optionText = Normalize(explicitLabel?.Text ?? (localText is not null && !Normalize(localText).Contains(Normalize(target)) ? localText : radio.GetAttribute("value")));
            if (optionText != Normalize(desired) && !optionText.Contains(Normalize(desired))) continue;
            var groupLabels = driver.FindElements(By.XPath("//label")).Where(l => Normalize(l.Text).Contains(Normalize(target))).ToList();
            var labelIsGroupLabel = label is not null && Normalize(label.Text).Contains(Normalize(target));
            var nearGroupLabel = groupLabels.Any(l => Near(radio, l));
            var optionAssociated = label is not null;
            if (!labelIsGroupLabel && !nearGroupLabel && !optionAssociated) continue;
            var groupScore = labelIsGroupLabel ? 0.8 : nearGroupLabel ? 0.4 : 0;
            scored.Add((radio, 0.7 + groupScore + (optionText == Normalize(desired) ? 0.4 : 0) + (optionAssociated ? 0.15 : 0), "label + option", $"{optionText}; {radio.TagName}[type=radio]"));
        }
        return Choose(scored, target);
    });

    private Resolution ResolveInFrame(string target, string kind)
    {
        var elements = kind == "text" ? driver.FindElements(By.CssSelector("input[type='text'],input:not([type])")) :
            driver.FindElements(By.CssSelector("select,[role='combobox'],[aria-haspopup='listbox'],input[readonly]"));
        var labels = driver.FindElements(By.XPath("//label|//*[self::span or self::div or self::p][@id and (@role='label' or @class)]"));
        var matching = labels.Where(l => Normalize(l.Text).Equals(Normalize(target), StringComparison.OrdinalIgnoreCase) || Normalize(l.Text).Contains(Normalize(target), StringComparison.OrdinalIgnoreCase)).ToList();
        var scored = new List<(IWebElement Element, double Score, string Strategy, string Description)>();
        foreach (var el in elements.Where(Usable))
        {
            var associated = matching.Any(l => IsAssociated(l, el));
            var near = matching.Any(l => Near(el, l));
            var hints = new[] { el.GetAttribute("aria-label"), el.GetAttribute("placeholder"), el.GetAttribute("name"), el.GetAttribute("id") };
            var exactHint = hints.Any(x => Normalize(x).Equals(Normalize(target), StringComparison.OrdinalIgnoreCase));
            var partialHint = hints.Any(x => Normalize(x).Contains(Normalize(target), StringComparison.OrdinalIgnoreCase));
            var score = (associated ? 1.0 : 0) + (exactHint ? 0.65 : partialHint ? 0.35 : 0) + (near ? 0.5 : 0);
            if (score > 0) scored.Add((el, score, associated ? "label association" : exactHint || partialHint ? "attribute hint" : "label proximity", Describe(el)));
        }
        return Choose(scored, target);
    }

    private T SearchFrames<T>(Func<T> action)
    {
        driver.SwitchTo().DefaultContent();
        T? Search(int depth)
        {
            if (depth > 5) return default;
            try { return action(); } catch (NoSuchElementException) { }
            var frames = driver.FindElements(By.TagName("iframe"));
            for (var i = 0; i < frames.Count; i++)
            {
                try { driver.SwitchTo().Frame(i); var found = Search(depth + 1); if (found is not null) return found; }
                catch (NoSuchElementException) { }
                finally { driver.SwitchTo().ParentFrame(); }
            }
            return default;
        }
        return Search(0) ?? throw new NoSuchElementException("No matching element found in page or its frames.");
    }

    private static Resolution Choose(List<(IWebElement Element, double Score, string Strategy, string Description)> candidates, string target)
    {
        foreach (var c in candidates.OrderByDescending(c => c.Score)) Console.WriteLine($"  candidate score={c.Score:0.00} strategy={c.Strategy}: {c.Description}");
        var winner = candidates.OrderByDescending(c => c.Score).FirstOrDefault();
        return winner.Element is null ? throw new NoSuchElementException($"No type-compatible candidate found for '{target}'.") : new(winner.Element, winner.Strategy, winner.Score, winner.Description);
    }
    private static bool IsAssociated(IWebElement label, IWebElement el) =>
        label.FindElements(By.CssSelector("input,select,[role='combobox'],[aria-haspopup='listbox']")).Any(x => x.Equals(el)) ||
        label.GetAttribute("for") == el.GetAttribute("id") ||
        (el.GetAttribute("aria-labelledby") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(label.GetAttribute("id"));
    private static IWebElement? AssociatedLabel(IWebElement el) => el.FindElements(By.XPath("ancestor::label[1]")).FirstOrDefault();
    private bool Near(IWebElement a, IWebElement b)
    {
        try { return (bool)((IJavaScriptExecutor)driver).ExecuteScript("return Math.abs(arguments[0].getBoundingClientRect().top-arguments[1].getBoundingClientRect().top)<100", a, b)!; }
        catch { return false; }
    }
    private string? LocalOptionText(IWebElement radio, IWebElement? label)
    {
        try { return (string?)((IJavaScriptExecutor)driver).ExecuteScript("const r=arguments[0]; const p=r.closest('span,div'); return p && p!==arguments[1] ? p.innerText : null", radio, label); }
        catch { return null; }
    }
    private static bool Usable(IWebElement e) { try { return e.Displayed && e.Enabled; } catch { return false; } }
    private static string Describe(IWebElement e) => $"<{e.TagName} type={e.GetAttribute("type") ?? ""} id={e.GetAttribute("id") ?? ""} name={e.GetAttribute("name") ?? ""}>";
}
