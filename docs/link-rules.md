<sub><a href="../README.md">Lupe</a> › <a href="../README.md#documentation">Documentation</a> › Link rules</sub>

# Link rules

A link rule decides whether a code becomes a link and which one. Lupe took the idea from Barcode-Reader's regex whitelist with prefix and made it strict: whatever a code contains, Lupe opens nothing but an `http` or `https` address – and a code behind a prefix with a host cannot change that host.

**Contents:** [Where links are used](#where-links-are-used) · [A rule](#a-rule) · [How the link is built](#how-the-link-is-built) · [Examples](#examples) · [Safety checks](#safety-checks) · [Writing patterns](#writing-patterns) · [Error messages](#error-messages)

## Where links are used

A link exists for a code when an active rule matches it. Then:

- **Automatically**, if *Link öffnen* is ticked under *Einstellungen › Bei einem Treffer* – separately for *Kamera, Datei und Zwischenablage* and for *Bildschirmausschnitt*. The link opens in the default browser after copying and typing. Off by default.
- **On request**, regardless of that setting: every matching result has a *Link öffnen* button – on the pages *Kamera*, *Bildschirm* and *Datei*, in the result notification, and in the history (<kbd>Enter</kbd> opens it there). In the history, a link symbol marks matching entries.

Links are worked out with the rules **as they are now**, so a history entry gets a link as soon as you add a matching rule.

## A rule

| Column on the page | Meaning |
|---|---|
| *Aktiv* | Only active rules are applied. |
| *Regulärer Ausdruck* | A .NET regular expression the code is tested against. |
| *Präfix (leer = Code ist der Link)* | Text placed in front of the code. Empty: the code itself is the link. |

**Order matters.** Rules are checked from top to bottom; the first active rule that matches **and** yields a safe link wins. If a rule matches but the result is not a safe link, the next rule is tried. The arrow buttons move a rule up (*Nach oben (wird früher geprüft)*) or down; the bin deletes it; *Regel hinzufügen* adds an empty row.

***Code zum Ausprobieren*** (code to try) below the table shows what a code would open – `→ https://…` – or *Keine Regel passt – kein Link.* (no rule matches – no link). It uses the rules exactly as Lupe applies them.

**The default** is one rule: pattern `^https://\S+$`, no prefix. A code that consists of exactly one `https://` address is opened as it is; `http://` addresses are not covered.

## How the link is built

Before anything else, Lupe removes white space at both ends of the code – scanners and some codes add a line break. Then there are three cases, depending on the prefix:

| Prefix | Link | The code … |
|---|---|---|
| **empty** | the code itself | must be a complete `http://` or `https://` address. |
| **only `http://` or `https://`** | prefix + code, unchanged | is the rest of the address and **decides the host**, as with an empty prefix – write the pattern accordingly. |
| **with a host**, e.g. `https://example.com/suche?q=` | prefix + **percent-encoded** code | lands in the link only as data: everything except letters `A–Z` `a–z`, digits `0–9` and `-` `.` `_` `~` is encoded, so it cannot change host, path or further parameters. |

A prefix with a host must end the host with `/`, `?` or `#` – otherwise the code would be appended to the host name (`https://shop.example` + `x.evil.example`). In addition, the finished link must have the same scheme, host and port as the prefix.

## Examples

Checked with Lupe's own rule code:

| Rule | Code → link |
|---|---|
| `^https://\S+$`<br><sub>no prefix – the default</sub> | `https://example.org/lieferung/4711`<br>→ `https://example.org/lieferung/4711` |
| `^https://\S+$`<br><sub>no prefix</sub> | `http://example.org/x`<br>→ none – the default rule wants `https` |
| `^https?://\S+$`<br><sub>no prefix</sub> | `http://example.org/x`<br>→ `http://example.org/x` |
| `^\d{13}$`<br><sub>prefix</sub> `https://warenwirtschaft.example/artikel/` | `4012345000009`<br>→ `https://warenwirtschaft.example/artikel/4012345000009` |
| `^LS-\d{4}-\d{6}$`<br><sub>prefix</sub> `https://erp.example/lieferschein/` | `LS-2026-004711`<br>→ `https://erp.example/lieferschein/LS-2026-004711` |
| `.+`<br><sub>prefix</sub> `https://example.com/suche?q=` | `A&B 1/2`<br>→ `https://example.com/suche?q=A%26B%201%2F2` |
| `.+`<br><sub>prefix</sub> `https://shop.example/p/` | `x/../../admin`<br>→ `https://shop.example/p/x%2F..%2F..%2Fadmin` – stays on the path |
| `^\d+$`<br><sub>prefix</sub> `https://example.com/#` | `42`<br>→ `https://example.com/#42` |
| `^www\.example\.com/\S*$`<br><sub>prefix</sub> `https://` | `www.example.com/x?y=1`<br>→ `https://www.example.com/x?y=1` |
| `^https://\S+$`<br><sub>no prefix</sub> | `https://bank.example@evil.example/`<br>→ none – user name in the address |
| `^.+$`<br><sub>no prefix</sub> | `file:///C:/Windows`<br>→ none – not `http`/`https` |

Typical uses:

- **Item pages from EAN-13:** `^\d{13}$` with the article URL of the web shop or ERP as prefix.
- **Delivery notes or orders:** a pattern for your own number format, e.g. `^LS-\d{4}-\d{6}$`, with the URL of the document view.
- **Search:** `^[A-Z0-9-]+$` with `https://erp.example/suche?nr=` – the code becomes the search term.

## Safety checks

Whatever the code contains, Lupe only opens a link that is

- an **absolute `http` or `https` address with a host** – never `file:`, `javascript:`, `ms-settings:`, `data:`, UNC paths or other schemes;
- **free of control characters and backslashes** – browsers and .NET read those differently;
- **without user name or password** (`https://bank.example@evil.example` is rejected) – that form is used to disguise the real host;
- **stable** – the normalised address that goes to the browser must be read back with the same host, port and scheme; host names that are invalid under IDN rules are rejected;
- for prefixes with a host: **on the prefix's host** (scheme, host and port compared).

The browser is started only for `http` and `https`; this is checked a second time just before Windows opens the link.

**Regular expressions run with a time limit** of 100 milliseconds per rule and code. A pattern with catastrophic backtracking cannot freeze Lupe; it simply does not match. An invalid pattern is marked in the table and skipped until you fix it.

## Writing patterns

- **The pattern matches anywhere in the code** unless anchored. `\d{13}` also matches `abc4012345000009` – and with a prefix that becomes `…/artikel/abc4012345000009`. Use `^` and `$` to mean the whole code: `^\d{13}$`.
- **Case matters.** `^https://` does not match `HTTPS://EXAMPLE.ORG`. Add `(?i)` at the start to ignore case: `(?i)^https?://\S+$`.
- **Escape dots and other special characters:** `www\.example\.com`, not `www.example.com`.
- **`\S+` instead of `.+`** for addresses: no spaces, no line breaks.
- **Test with *Code zum Ausprobieren*** before ticking *Link öffnen*.

## Error messages

Shown below the row while you type:

| Message | Meaning |
|---|---|
| *Bitte einen regulären Ausdruck eingeben.* | A prefix without a pattern. Rows with an empty pattern are not saved. |
| *Das Muster ist kein gültiger regulärer Ausdruck: … (bei Zeichen N).* | Syntax error at position N, e.g. *es fehlt eine schließende Klammer „)“*. The rule is saved, so your input is not lost, but not applied. |
| *Das Präfix muss leer sein oder mit http:// bzw. https:// beginnen …* | Another scheme, e.g. `javascript:`. |
| *Nach dem Hostnamen fehlt ein „/“ – z. B.* `https://example.com/` *statt* `https://example.com`*.* | The host is not closed with `/`, `?` or `#`. |
| *Das Präfix darf keinen Benutzernamen und kein Kennwort enthalten.* | `https://user:pw@example.com/` |
| *Das Präfix darf keine Steuerzeichen und keinen Backslash (\\) enthalten.* | Control character or `\` in the prefix. |

---

<p align="center"><sub>
<a href="installation.md">Installation</a> · <a href="usage.md">Using the app</a> · <a href="settings.md">Settings</a> · <b>Link rules</b> · <a href="command-line.md">Command line</a> · <a href="how-it-works.md">How it works</a> · <a href="data-and-privacy.md">Data &amp; privacy</a> · <a href="limitations.md">Limitations</a> · <a href="development.md">Development</a><br>
<a href="../README.md">← Back to Lupe</a>
</sub></p>
