<sub><a href="../README.md">Lupe</a> › <a href="../README.md#documentation">Documentation</a> › Error codes</sub>

# Error codes

Some messages start with a code such as `LUP-0001`. The code is the same in every interface language, so support can tell what happened from a screenshot or a log line in any language. Please quote it when you report a problem ([Support](../SUPPORT.md)).

| Code | Where | Meaning |
|---|---|---|
| [`LUP-0001`](#lup-0001) | App, command line | The recognition library cannot be loaded on this system. |

## LUP-0001

**The recognition library (zxing-cpp) cannot be loaded.** Lupe reads codes with the native library `ZXing.dll` (Windows), `libZXing.so` (Linux) or `libZXing.dylib` (macOS) that ships next to the program. When the operating system refuses to load it, or it lacks functions Lupe needs, Lupe cannot read any code.

- **App:** a red notice at the top of the window right after the start, and again for every scan, instead of *no code found*.
- **Command line:** `lesen` prints the message on stderr and ends with exit code `3` without reading further images.

The message ends with the reason the operating system gives, for example:

| Reason in the message | Cause | What to do |
|---|---|---|
| `version 'GLIBC_2.38' not found` | The Linux system is older than the library (Lupe's own builds need glibc 2.31 or later: Ubuntu 20.04, Debian 11 and newer). | Use a current Lupe build from the release *latest*, or update the system. |
| `cannot open shared object file`, `image not found`, `The specified module could not be found` | The file is missing – usually an incomplete copy of the program folder. | Install or unpack Lupe again. |
| `… lacks the functions …` | The library belongs to a different zxing-cpp version than the rest of Lupe. | Do not replace files in the program folder; install Lupe again. |
| `bad ELF`, `wrong architecture`, `is not a valid Win32 application` | The library is for a different processor (e.g. an x64 package on an ARM machine). | Download the package for your processor. |

If none of this applies, report the full message.

---

<p align="center"><sub>
<a href="installation.md">Installation</a> · <a href="usage.md">Using the app</a> · <a href="settings.md">Settings</a> · <a href="link-rules.md">Link rules</a> · <a href="command-line.md">Command line</a> · <a href="how-it-works.md">How it works</a> · <a href="data-and-privacy.md">Data &amp; privacy</a> · <a href="limitations.md">Limitations</a> · <b>Error codes</b> · <a href="development.md">Development</a><br>
<a href="../README.md">← Back to Lupe</a>
</sub></p>
