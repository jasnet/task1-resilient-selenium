using System.Text.RegularExpressions;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

// Usage: dotnet run -- --url <page> [--first-name X] [--gender Male] [--state "Goa"] [--headless]
// URL can also come from env FORM_URL.
var opt = Options.Parse(args);
if (!opt.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
    !opt.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
    !opt.Url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
{
    if (File.Exists(opt.Url))
        opt.Url = new Uri(Path.GetFullPath(opt.Url)).AbsoluteUri;
}
var options = new ChromeOptions();
if (opt.Headless) options.AddArgument("--headless=new");
options.AddArgument("--window-size=1400,1000");
using IWebDriver driver = new ChromeDriver(options);
driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;

int failures = 0;
void Report(string name, bool ok, string detail)
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
    if (!ok) failures++;
}

try
{
    driver.Navigate().GoToUrl(opt.Url);
    new WebDriverWait(driver, TimeSpan.FromSeconds(20))
        .Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")!.Equals("complete"));
    var finder = new SemanticFinder(driver);

    // ---- First Name (text box) ----
    try
    {
        var el = finder.Find(new FieldSpec("First Name", new[] { "first name", "firstname", "fname", "given name" },
            new[] { "last", "middle", "sur", "company", "user", "father", "mother" }, FieldKind.Text));
        el.Clear(); el.SendKeys(opt.FirstName);
        var actual = el.GetAttribute("value");
        Report("First Name", actual == opt.FirstName, $"typed '{opt.FirstName}', field now holds '{actual}'");
    }
    catch (Exception e) { Report("First Name", false, e.Message); }

    // ---- Gender (radio) ----
    try
    {
        var el = finder.FindRadio("Gender", opt.Gender);
        finder.ClickRobust(el);
        Report("Gender", el.Selected, $"selected option '{opt.Gender}'");
    }
    catch (Exception e) { Report("Gender", false, e.Message); }

    // ---- State (dropdown) ----
    try
    {
        var spec = new FieldSpec("State", new[] { "state", "province" },
            new[] { "country", "city", "zip", "postal", "united", "address", "status" }, FieldKind.Select);
        var el = finder.Find(spec);
        var sel = new SelectElement(el);
        var real = sel.Options.Where(o => !string.IsNullOrWhiteSpace(o.GetAttribute("value"))
                                          && !Regex.IsMatch(o.Text, @"^\W*(select|choose|--)", RegexOptions.IgnoreCase)).ToList();
        if (real.Count == 0) throw new Exception("dropdown has no selectable options");
        var wanted = opt.State != null
            ? real.FirstOrDefault(o => SemanticFinder.Norm(o.Text) == SemanticFinder.Norm(opt.State))
              ?? throw new Exception($"option '{opt.State}' not in dropdown")
            : real[0];
        sel.SelectByText(wanted.Text);
        var chosen = sel.SelectedOption.Text;
        Report("State", chosen == wanted.Text, $"selected '{chosen}'");
    }
    catch (Exception e) { Report("State", false, e.Message); }
}
catch (Exception e) { Console.WriteLine("Fatal: " + e.Message); failures++; }

Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;

// ======================= support types =======================
enum FieldKind { Text, Select }
record FieldSpec(string Label, string[] Aliases, string[] Exclude, FieldKind Kind);

class Options
{
    public string Url = Environment.GetEnvironmentVariable("FORM_URL") ?? "https://app.cloudqa.io/home/AutomationPracticeForm";
    public string FirstName = "Jasneet";
    public string Gender = "Male";
    public string? State;
    public bool Headless;
    public static Options Parse(string[] a)
    {
        var o = new Options();
        for (int i = 0; i < a.Length; i++)
            switch (a[i])
            {
                case "--url": o.Url = a[++i]; break;
                case "--first-name": o.FirstName = a[++i]; break;
                case "--gender": o.Gender = a[++i]; break;
                case "--state": o.State = a[++i]; break;
                case "--headless": o.Headless = true; break;
            }
        return o;
    }
}

/// Finds elements by what a human sees (label text, placeholder, aria, nearby text), never by a fixed id/xpath.
class SemanticFinder
{
    readonly IWebDriver _d;
    readonly IJavaScriptExecutor _js;
    public SemanticFinder(IWebDriver d) { _d = d; _js = (IJavaScriptExecutor)d; }

    public static string Norm(string s)
    {
        s = Regex.Replace(s ?? "", @"([a-z])([A-Z])", "$1 $2");          // firstName -> first Name
        return Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
    }
    static bool HasWord(string hay, string needle) => (" " + hay + " ").Contains(" " + needle + " ");

    const string DescribeJs = """
        const el = arguments[0];
        const t = n => ((n && n.textContent) || '').replace(/\s+/g, ' ').trim();
        const o = { labels: [], near: '', after: '', group: '' };
        if (el.labels) for (const l of el.labels) o.labels.push(t(l));
        const lb = el.getAttribute('aria-labelledby');
        if (lb) lb.split(/\s+/).forEach(i => { const n = document.getElementById(i); if (n) o.labels.push(t(n)); });
        const w = el.closest('label'); if (w) o.labels.push(t(w));
        let cur = el, near = [];
        for (let d = 0; d < 3 && cur; d++) {
          let p = cur.previousElementSibling, c = 0;
          while (p && c < 2) { if (!p.querySelector('input,select,textarea')) near.push(t(p)); p = p.previousElementSibling; c++; }
          cur = cur.parentElement;
        }
        o.near = near.join(' | ');
        const ns = el.nextSibling;
        if (ns && ns.nodeType === 3) o.after = ns.textContent.trim();
        else if (el.nextElementSibling && !el.nextElementSibling.querySelector('input,select')) o.after = t(el.nextElementSibling);
        if (el.type === 'radio' && el.name) {
          const all = document.querySelectorAll('input[type=radio]');
          const same = [...all].filter(r => r.name === el.name);
          let a = el.parentElement;
          while (a && [...a.querySelectorAll('input[type=radio]')].filter(r => r.name === el.name).length < same.length) a = a.parentElement;
          if (a) { o.group = t(a).slice(0, 200); const ps = a.previousElementSibling; if (ps) o.group += ' ' + t(ps).slice(0, 80); }
        }
        const r = el.getBoundingClientRect();
        o.visible = !!(r.width || r.height) && getComputedStyle(el).visibility !== 'hidden';
        return o;
        """;

    Dictionary<string, object> Describe(IWebElement e) =>
        (Dictionary<string, object>)_js.ExecuteScript(DescribeJs, e)!;

    static List<string> Strs(object? o) => (o as System.Collections.IEnumerable)?.Cast<object>().Select(x => x.ToString() ?? "").ToList() ?? new();

    int Score(IWebElement e, FieldSpec s, Dictionary<string, object> d)
    {
        int best = 0, penalty = 0;
        var targets = s.Aliases.Select(Norm).Append(Norm(s.Label)).Distinct().ToList();
        foreach (var raw in Strs(d["labels"]))
        {
            var l = Norm(raw);
            if (targets.Contains(l)) best = Math.Max(best, 100);
            else if (targets.Any(t => HasWord(l, t))) best = Math.Max(best, 70);
            if (s.Exclude.Any(x => HasWord(l, x) || l.StartsWith(x))) penalty = 90;
        }
        foreach (var attr in new[] { "placeholder", "aria-label", "title" })
        {
            var v = Norm(e.GetAttribute(attr) ?? "");
            if (v.Length == 0) continue;
            if (targets.Contains(v)) best = Math.Max(best, 85);
            else if (targets.Any(t => HasWord(v, t))) best = Math.Max(best, 60);
            if (s.Exclude.Any(x => HasWord(v, x))) penalty = Math.Max(penalty, 90);
        }
        foreach (var attr in new[] { "name", "id" })
        {
            var v = Norm(e.GetAttribute(attr) ?? "");
            var compact = v.Replace(" ", "");
            if (v.Length == 0) continue;
            if (targets.Any(t => HasWord(v, t) || compact.Contains(t.Replace(" ", "")))) best = Math.Max(best, 40);
            if (s.Exclude.Any(x => HasWord(v, x))) penalty = Math.Max(penalty, 60);
        }
        var near = Norm(d["near"]?.ToString() ?? "");
        if (targets.Any(t => near.Contains(t))) best = Math.Max(best, 30);
        if (best > 0 && best <= 40 && s.Exclude.Any(x => HasWord(near.Split('|').LastOrDefault()?.Trim() ?? "", x))) penalty = Math.Max(penalty, 50);
        return best - penalty;
    }

    public IWebElement Find(FieldSpec s)
    {
        var tag = s.Kind == FieldKind.Select ? "select" : "input,textarea";
        var cands = _d.FindElements(By.CssSelector(tag)).Where(e => e.Enabled).ToList();
        if (s.Kind == FieldKind.Text)
            cands = cands.Where(e => (e.GetAttribute("type") ?? "text").ToLower() is "text" or "search" or "").ToList();
        IWebElement? bestEl = null; int bestScore = 0, second = 0;
        foreach (var e in cands)
        {
            var d = Describe(e);
            if (!(bool)d["visible"]) continue;
            int sc = Score(e, s, d);
            if (sc > bestScore) { second = bestScore; bestScore = sc; bestEl = e; }
            else if (sc > second) second = sc;
        }
        if (bestEl == null || bestScore < 30) throw new Exception($"no {s.Kind} field looked like '{s.Label}'");
        if (bestScore == second) Console.WriteLine($"  (warning: ambiguous match for '{s.Label}', using first of equal score {bestScore})");
        return bestEl;
    }

    public IWebElement FindRadio(string group, string option)
    {
        var wantOpt = Norm(option); var wantGrp = Norm(group);
        IWebElement? best = null; int bestScore = 0;
        foreach (var e in _d.FindElements(By.CssSelector("input[type=radio]")))
        {
            var d = Describe(e);
            var texts = Strs(d["labels"]).Append(d["after"]?.ToString() ?? "").Append(e.GetAttribute("value") ?? "")
                                         .Select(Norm).Where(x => x.Length > 0).ToList();
            int sc = 0;
            if (texts.Contains(wantOpt)) sc += 100;
            else continue;
            var grp = Norm(d["group"]?.ToString() ?? "");
            if (HasWord(grp, wantGrp)) sc += 50;
            if (Norm(e.GetAttribute("name") ?? "").Contains(wantGrp)) sc += 20;
            if (sc > bestScore) { bestScore = sc; best = e; }
        }
        return best ?? throw new Exception($"no radio option '{option}' found");
    }

    public void ClickRobust(IWebElement e)
    {
        try { _js.ExecuteScript("arguments[0].scrollIntoView({block:'center'})", e); e.Click(); }
        catch (WebDriverException)
        {
            try { _js.ExecuteScript("(arguments[0].labels&&arguments[0].labels[0]||arguments[0].parentElement).click()", e); }
            catch { _js.ExecuteScript("arguments[0].click()", e); }
        }
        if (!e.Selected) _js.ExecuteScript("arguments[0].click()", e);
    }
}
