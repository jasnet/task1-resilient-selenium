# Resilient Selenium test (C#) – CloudQA Automation Practice Form

Fills **First Name** (text box), **Gender** (radio) and **State** (dropdown) without fixed XPath/CSS/id selectors.

## Setup
- .NET 8 SDK, Google Chrome (Selenium Manager downloads the matching driver automatically)

```bash
# Test against live practice form:
dotnet run -- --url https://app.cloudqa.io/home/AutomationPracticeForm
dotnet run -- --url https://app.cloudqa.io/home/AutomationPracticeForm --first-name Jasneet --gender Female --state "Goa" --headless

# Test offline resilience using local fixtures (standard & scrambled layouts):
dotnet run -- --url fixtures/test_form.html --state "Goa" --headless
dotnet run -- --url fixtures/scrambled_form.html --state "Goa" --headless
```

URL can also be set with env `FORM_URL`. Exit code 0 = all checks passed.

## How it stays stable when the page changes
Locators are *semantic*. For every candidate `input/select`, a small JS helper gathers what a human would use to identify it:
`<label for>`, wrapping `<label>`, `aria-labelledby`, `aria-label`, `placeholder`, `title`, text just before/after the control, and for radios the group heading.
Each candidate gets a score (exact label 100 > aria/placeholder 85 > partial label 70 > name/id tokens 40 > nearby text 30), with penalties for look-alikes
("Last Name", "Middle Name", "Country", "City"). The highest score wins and ties produce a warning. Name/id are only weak hints, so renaming them does not break the run.
- Reordered fields / changed structure: nothing depends on position or DOM path.
- Radio "Male" requires an *exact* option-text match, so it never hits "Female"; the group must look like "Gender".
- Each action is verified (value read back, `Selected`, selected option) before reporting PASS.

## Limitations
- Native `<select>` only for State (custom div dropdowns are not handled).
- Same-document only: no iframes / shadow DOM traversal.
- English labels (the aliases list in `Program.cs` is easy to extend).
- If a page has no label/placeholder/aria text at all, only weak name/id hints remain.
