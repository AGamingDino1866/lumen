# Lumen — Design Spec

**Date:** 2026-08-21
**Status:** Awaiting approval
**Product:** Native Windows desktop PDF reader that transcribes selected pages via the Google Gemini API.

---

## 1. Product definition

A single-window Windows application. The user opens a PDF, sees every page as a thumbnail,
selects the pages they care about, and presses **Extract Text**. Gemini transcribes those pages
into clean markdown that the user can read, copy with one button, and export to Word.

No backend, no account, no telemetry. The only network call in the entire application is to
`generativelanguage.googleapis.com`.

**Name:** Lumen. Retained. It is short, pronounceable, unclaimed in this category, and means a
unit of visible light — apt for a tool that makes the contents of a scan readable.

---

## 2. Decisions taken by the user (2026-08-21)

These four were put to the user before design and answered explicitly. They override the
recommendations offered alongside them.

| # | Question | Decision | Consequence for this spec |
|---|----------|----------|---------------------------|
| 1 | Native text layer first, Gemini for scans? | **Gemini only. No text layer at all.** | PdfPig is not a dependency. There is no per-page heuristic and no free pre-flight signal. A page-count plus model confirmation replaces the cost banner. |
| 2 | Default results view | **Merged document** | Results render as continuous flow. Page identity survives as an anchor divider, not a card. |
| 3 | Word export pagination | **Page breaks between source pages, by default** | `PageBreakBetweenPages` defaults to `true` in the export dialog. |
| 4 | Install missing tooling | **Install .NET 8 SDK and Inno Setup 6 via winget** | Done. Verified: SDK 8.0.424, Inno Setup 6.7.3. |

---

## 3. Environment findings

Recorded because two of them change build scripts.

- **.NET SDK was absent** at the start. Only runtimes were present (NETCore and WindowsDesktop
  6.0.36 / 8.0.24 / 10.0.7). VS 2022 BuildTools is installed but carries no .NET SDK.
  Installed `Microsoft.DotNet.SDK.8` to **8.0.424**.
- **Inno Setup was absent.** Installed `JRSoftware.InnoSetup` to **6.7.3**.
- **ISCC.exe is a per-user install** at `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`.
  It is *not* in Program Files and *not* on PATH. `build.ps1` must probe, in order:
  PATH, then `%ProgramFiles%`, then `%ProgramFiles(x86)%`, then `%LOCALAPPDATA%\Programs`.
- Git 2.55.0 and winget are available.

---

## 4. Architecture

Two projects, split on a single rule: **`Lumen.Core` references no WPF.**

```
Lumen.sln
+- Directory.Build.props        shared: lang version, nullable, warnings-as-errors, version
+- src/Lumen.Core/              net8.0 - no WPF, fully unit-testable
|   Pdf/        PdfRenderService, PdfDocumentInfo, PdfPasswordRequiredException
|   Gemini/     GeminiClient, request/response DTOs, RetryPolicy, GeminiErrorMapper,
|               ExtractionPromptLoader + extraction-prompt.txt (embedded resource)
|   Markdown/   MarkdownDocument model, MarkdigParser (markdown to model)
|   Export/     WordExporter (OpenXML), MarkdownExporter, PlainTextExporter, MarkdownStripper
|   Selection/  PageRangeParser
|   Security/   DpapiSecretStore
|   Settings/   LumenSettings, SettingsStore
+- src/Lumen.App/              net8.0-windows - WPF, WPF-UI, CommunityToolkit.Mvvm
|   Themes/     Primitives.xaml, Semantic.Light.xaml, Semantic.Dark.xaml,
|               Components.xaml, Motion.xaml, Icons.xaml
|   Views/ ViewModels/ Services/ Behaviors/ Converters/
+- tests/Lumen.Core.Tests/     xUnit + FluentAssertions
+- tests/Lumen.UiTests/        FlaUI
+- installer/Lumen.iss
+- build.ps1
+- README.md
```

### Why the split

`PdfRenderService` renders through PDFtoImage/SkiaSharp and returns **PNG bytes** — a
WPF-free type. Only `Lumen.App` converts those bytes into a `BitmapSource` and calls
`.Freeze()`. This puts the freeze in exactly one place instead of scattering it across the
render path, and it is what makes the test suite runnable without a UI thread.

### Rejected alternatives

- **Single WPF project.** Every piece of logic would drag in a `net8.0-windows` TFM and the
  test project would inherit WPF's threading model. Rejected.
- **Out-of-process PDFium render worker.** Real crash isolation for malformed PDFs, but buys an
  IPC layer, a second executable in the installer, and lifetime management. Rejected as
  premature. Mitigation: per-page try/catch that marks a page unrenderable.

---

## 5. Dependencies

Exactly the stack specified, plus one addition.

Versions below were resolved against nuget.org on 2026-08-21 and are pinned, not floated.

| Package | Version | Purpose |
|---------|---------|---------|
| `WPF-UI` (lepoco) | **4.3.0** | Fluent controls, Mica backdrop, light/dark theming |
| `CommunityToolkit.Mvvm` | 8.4.2 | `[ObservableProperty]`, `[RelayCommand]` source generators |
| `PDFtoImage` | 5.4.0 | PDF page to image |
| `bblanchon.PDFium.Win32` | 153.0.8009 | Native PDFium, arrives via NuGet |
| `SkiaSharp` + `SkiaSharp.NativeAssets.Win32` | 4.150.1 | Transitive through PDFtoImage — see note |
| `DocumentFormat.OpenXml` | 3.5.1 | Real `.docx`. **Not** Word Interop, which requires Word installed |
| `System.Security.Cryptography.ProtectedData` | **8.0.0** | DPAPI for the API key |
| `xunit` | 2.9.3 | Unit tests |
| `FluentAssertions` | **7.2.2** | Assertions — see licence note |
| `FlaUI.UIA3` | 5.0.0 | UI automation tests |
| **`Markdig`** *(addition)* | 1.3.2 | Markdown parsing |

### Version findings that changed this spec

**WPF-UI is pinned to 4.3.0, not v3.** An earlier draft of this spec said to pin v3 and avoid a
"v4 preview". That was wrong: 4.3.0 is the current *stable* release and 3.1.1 is the last v3.
The v4 line has been stable since 4.0.0, so there is no preview risk to avoid and no reason to
adopt a superseded major.

**FluentAssertions is pinned to 7.2.2 because 8.x is not free.** Every release from 8.0.0 onward
carries a paid commercial licence; 7.2.2 is the last Apache-2.0 version. Pinning to 7.2.2 keeps
the package the build spec named while avoiding a licence obligation attached to a personal
utility. `AwesomeAssertions` 9.6.0 is the maintained Apache-2.0 fork and is the drop-in
alternative if tracking current matters more than keeping the original package name.

**`ProtectedData` is pinned to 8.0.0, not the latest.** The newest release is 10.0.11, a .NET 10
package. Pinning to the 8.0.x line matches the `net8.0` target.

**PDFtoImage brings a second native dependency.** It depends transitively on **SkiaSharp
4.150.1** plus `SkiaSharp.NativeAssets.Win32`, so the published output must carry **two** native
DLLs — `pdfium.dll` *and* `libSkiaSharp.dll` — not one. This materially raises the single-file
extraction risk identified in §18, because `IncludeNativeLibrariesForSelfExtract` has to place
both correctly. **The published-build verification gate checks for both DLLs, and whether either
requires the MSVC runtime.**

PDFtoImage also declares Linux and macOS PDFium/Skia natives. A RID-specific
`-r win-x64` publish filters those out, but the published payload size is checked to confirm it,
rather than assumed.

**Markdig 1.3.2** is a real stable release (the package moved 0.45.0 → 1.0.0 → 1.3.2).

**Markdig justification.** Both the results panel and the Word exporter must understand the
markdown Gemini returns. Hand-rolling CommonMark tables and nested emphasis is a bug farm.
Markdig is MIT, pure managed, and has no native dependencies. It parses once into a
`MarkdownDocument`; two visitors then walk it — a WPF `FlowDocument` renderer and an OpenXML
renderer. One parser, two backends, both tested against the same fixtures.

**`PublishTrimmed` stays off**, per the build spec — WPF does not trim reliably and produces
`XamlParseException`s that only surface after install. Direct consequence: the installer will be
roughly **90-130 MB**, because the .NET runtime is inside it. That is the cost of "the user
installs nothing else."

---

## 6. Visual direction

**Committed direction: minimalist-ui (premium utilitarian minimalism).**

Chosen over `high-end-visual-design` because Lumen is an Operate-mode surface — the user is in a
task, not being persuaded. The agency idiom of high-end-visual-design (heavy shadows, oversized
display type, expensive-feeling hero treatments) fights directly with what `impeccable`'s Operate
mode names as the goal: *the tool should disappear into the task*. A reading tool must be the
quietest thing on screen so the document is the loudest.

### What the skills actually changed

| Source | Decision it changed |
|--------|--------------------|
| `ui-ux-pro-max` | Supplied the **Scanner and Document Manager** palette, an exact product-type match ("document grey plus scan blue"). Adopted as accent and status base. Its *pattern* recommendation (Newsletter / Content First landing page) and its GSAP scroll-reveal preset were discarded as browser/marketing constructs with no meaning in a WPF utility. |
| `minimalist-ui` | **Banned Inter**, which `ui-ux-pro-max` had recommended. Forced the move to **Geist Sans plus Geist Mono**. Also pushed the canvas from cool `#F8FAFC` to warm bone `#FBFBFA`, correct for a reading tool, which should read as paper rather than clinical. Supplied the muted-pastel status vocabulary. |
| `impeccable` (Operate) | One type family, tighter scale ratio (1.125-1.2), accent reserved for primary action / selection / state only, **skeletons not spinners**, empty states that teach, 150-250 ms motion, no orchestrated load sequences. |
| `emil-design-eng` | **Never animate keyboard-initiated actions** - the single most valuable rule extracted. Custom `KeySpline` instead of WPF's weak built-in easings. Scale from 0.92, never 0. Press feedback at `scale(0.97)`. Exit faster than enter. `BetweenShowDelay=0` for instant subsequent tooltips. |
| `apple-design` | Respond on pointer-**down**, not release. Interruptibility, therefore `VisualStateManager` (which blends from the live value) rather than `Storyboard.Begin` (which restarts). Never stack a translucent surface on another, therefore the results panel is opaque over Mica. Dim-to-focus for blocking surfaces only. Size-specific tracking. Respect **reduced transparency**, not just reduced motion. |
| `animation-vocabulary` | Named the motion precisely, correcting two instincts: the selection badge is a **Scale in**, not a **Pop in** (no overshoot in an Operate tool); results arriving are individual **Enter** animations, **not a Stagger** (synthetic delay on real arrival times is exactly wrong when the user is waiting). |

### Type

**Geist Sans** (UI, body, headings) and **Geist Mono** (page-range input, keyboard hints,
per-page metadata). Both OFL, bundled as embedded resources — no network fetch, no system
dependency. One family for the interface, per Operate-mode guidance; hierarchy comes from
weight, size, and tracking as a set.

Tracking is **size-specific**, never one global value:

| Role | Size | Weight | Tracking | Leading |
|------|------|--------|----------|---------|
| Display | 28 | 600 | -0.02em | 1.1 |
| Title | 20 | 600 | -0.01em | 1.2 |
| Subtitle | 16 | 500 | 0 | 1.35 |
| Body | 14 | 400 | 0 | 1.6 |
| Small | 12 | 400 | +0.01em | 1.5 |
| Label (caps) | 11 | 500 | +0.05em | 1.4 |

Body text is never pure black — charcoal `#2F3437` on light.

---

## 7. Design tokens — three layers

Mapped onto WPF `ResourceDictionary` entries, not CSS variables.

**Layer 1 — Primitives** (`Themes/Primitives.xaml`). Raw ramps only. The *only* file permitted
to contain a hex literal.
`Lumen.Primitive.Bone.50/100/200`, `Lumen.Primitive.Ink.100..900`,
`Lumen.Primitive.Blue.400/500/600/700`, `Lumen.Primitive.Pastel.Red/Blue/Green/Yellow` plus
their text pairs, `Lumen.Primitive.Space.1..24` (4px base), `Lumen.Primitive.Radius.Sm/Md/Lg`,
`Lumen.Primitive.Type.Size.*`, `Lumen.Primitive.Type.Weight.*`.

**Layer 2 — Semantic** (`Semantic.Light.xaml` / `Semantic.Dark.xaml`). Purpose aliases; the
theme swap replaces this layer only.
`Lumen.Surface.Canvas`, `Lumen.Surface.Raised`, `Lumen.Surface.Sunken`,
`Lumen.Text.Primary/Secondary/Disabled`, `Lumen.Border.Subtle/Strong`,
`Lumen.Accent.Default/Hover/Pressed`, `Lumen.Focus.Ring`,
`Lumen.State.Selected.Border/Fill`, `Lumen.Status.Queued/Running/Done/Failed` plus `.Fg` pairs.

**Layer 3 — Component** (`Components.xaml`). Per-component tokens and styles.
`Lumen.PageTile.Radius`, `Lumen.PageTile.SelectedBorderThickness`, `Lumen.Button.Primary.Bg`,
`Lumen.Panel.Padding`, `Lumen.Divider.Thickness`.

**Enforcement.** A unit test scans every `.xaml` outside `Primitives.xaml` for a
`#RRGGBB`/`#AARRGGBB` literal and fails the build on a hit. Zero hardcoded colors is a testable
property, not an aspiration.

### Palette

**Light** — canvas `#FBFBFA` (warm bone), raised `#FFFFFF`, border `#EAEAEA`,
text primary `#2F3437`, secondary `#787774`, accent `#2563EB`, primary button `#111111`.

**Dark** — derived here, since minimalist-ui is light-only. Warm-neutral, never pure black:
canvas `#1A1A19`, raised `#232322`, border `#2E2E2C`, text primary `#EDEDEB`,
secondary `#A1A09A`, accent lifted to `#5B9DFF` for AA contrast, primary button `#EDEDEB` on
dark ink.

**Role separation** (this resolves the minimalist-ui / ui-ux-pro-max conflict): the primary
button is near-black ink; **blue is reserved exclusively for selection state and the focus
ring.** They never compete for the same meaning.

**Status pastels** carry per-page extraction state: queued (pale yellow), running (pale blue),
done (pale green), failed (pale red), each with its darker text pair — so status is never
conveyed by color alone; every chip also carries a label.

---

## 8. Motion spec

Durations live in `Motion.xaml` as `Duration` resources. Curves are `KeySpline` values, because
WPF's built-in `CubicEase` is too weak to read as intentional.

- `Lumen.Ease.Out` = `0.23,1 0.32,1` (matches cubic-bezier(0.23, 1, 0.32, 1))
- `Lumen.Ease.InOut` = `0.77,0 0.175,1`

| Motion | Name | Duration | Curve |
|--------|------|----------|-------|
| Selection check badge | **Scale in** (0.92 to 1 plus fade) | 120 ms | Ease.Out |
| Press feedback | **Press feedback** (scale 0.97) | 160 ms | Ease.Out |
| Hover surface shift | **Hover effect** | 150 ms | ease |
| Thumbnail arrival | **Skeleton** then **Crossfade** | 180 ms | Ease.Out |
| Copy confirmation | **Morph** plus blur-masked crossfade, hold 1.5 s | 200 ms each way | Ease.Out |
| Drop overlay | **Fade in** plus inner **Scale in** | 160 ms in / 120 ms out | Ease.Out |
| Result block arrival | **Enter** (fade plus 8px rise) | 200 ms | Ease.Out |
| Panel/dialog | **Scale in** from 0.96 plus scrim fade | 200 ms | Ease.Out |

**Rules.**

1. **No animation on keyboard-initiated actions.** `Ctrl+A` on a 312-page PDF fires zero badge
   storyboards. `Space`-toggle and arrow navigation are instant. The badge animates only when
   selection originated from a pointer.
2. **Exit is faster than enter**, always.
3. **`VisualStateManager` with `VisualTransition` for anything state-driven**, so motion blends
   from the live on-screen value and can be redirected mid-flight. Raw `Storyboard.Begin` only
   for one-shot confirmations.
4. **No stagger on result arrival.** Blocks appear when their page actually finishes.
5. **Windows "Show animations" off means all durations become zero**, via `MotionService`
   swapping the `Motion.xaml` values at startup and on system parameter change.
6. **Windows "Transparency effects" off means Mica is not applied**; solid `Surface.Canvas`
   instead.

---

## 9. Window layout and information architecture

Single window, custom title bar, Mica backdrop where supported.

```
+---------------------------------------------------------------+
| [icon] Lumen   report.pdf | 312 pages | 8.4 MB      [- [] x]  |  title bar
+---------------------------------------------------------------+
| Open  |  Pages: [1-5, 12, 20-24    ]  |  Copy All v  Export v |  toolbar
+--------------------------------++-----------------------------+
|                                ||                             |
|      PAGE GRID                 ||     RESULTS (merged)        |
|      virtualized wrap          ||     continuous flow         |
|                                ||     -- Page 12 --  [copy]   |
|                                ||                             |
+--------------------------------++-----------------------------+
| 7 of 312 pages selected              [ Extract Text ]         |  action bar
+---------------------------------------------------------------+
                                  ^ GridSplitter, position persisted
```

**Wayfinding.** Every state answers: what document am I in (title bar), what have I chosen
(action bar count), what can I do next (primary button), how do I get out (Open swaps documents
without restart).

**Progressive disclosure.** The common path — open, select, extract, copy — is on the surface.
Model choice, theme override, and key management live one level deeper in Settings.

### Screens and their three states

Every async surface defines loading, error, and empty.

| Surface | Empty | Loading | Error |
|---------|-------|---------|-------|
| Start | Drop target plus recent files (last 10, clearable) that teaches the interface | - | Inline invalid-file message, never a `MessageBox` |
| Page grid | "No document open" with an Open affordance | Aspect-correct **skeletons**, no spinner | Per-page "couldn't render" tile; the grid survives |
| Results | "Select pages, then Extract" pointing at the primary button | Per-page status chips plus `IProgress` line | Per-page failed chip plus retry; one bad page never kills the run |
| Key gate | - | Validating spinner on the button only | Inline error under the field |

### API key gate

A blocking `ContentDialog` over a dimmed scrim — non-dismissable by clicking away, per spec.
One sentence of explanation, a clickable link to `https://aistudio.google.com/apikey`, and a
`PasswordBox` with a show/hide toggle. `impeccable` warns that "modal as first thought is
laziness"; the modal is justified here because the app is genuinely non-functional without a key
— a hard prerequisite, not a deferrable choice. Everywhere else uses inline messaging.

---

## 10. Page grid

- Virtualizing `ListView`: `IsVirtualizing=True`, `VirtualizationMode=Recycling`, wrap-style
  panel. A 400-page PDF must never allocate 400 bitmaps.
- Thumbnails render on background threads through a **bounded queue (3-4 concurrent)**,
  prioritized by viewport proximity. **Every `BitmapSource` is `.Freeze()`d before it crosses to
  the UI thread** — an unfrozen bitmap created off-thread throws the moment WPF touches it.
- Aspect-correct placeholder while rendering, so the grid never reflows and scroll position
  never jumps.
- **Selection:** click toggles; `Ctrl+Click` toggles without clearing; `Shift+Click` selects a
  contiguous range; rubber-band drag across the grid; `Ctrl+A` select all; `Esc` clear; arrows
  move focus; `Space` toggles. `SelectionMode="Extended"` is the starting point but will be
  *verified*, not assumed.
- Page-range textbox parses `1-5, 12, 20-24`. Invalid input shows inline and never throws.
- Selected state: accent border plus check badge plus subtle scale. Badge **Scale in** at 120 ms,
  pointer-initiated only.
- Double-click opens a full-size preview with arrow navigation and zoom.
- `AutomationProperties.Name` per tile so Narrator announces "Page 4, selected".

---

## 11. Extraction

- Render each selected page to PNG at ~150-200 DPI, longest edge capped near 2048 px.
- `POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent`
- Auth via **`x-goog-api-key` header**. Never a query parameter.
- Default model `gemini-2.5-flash`; `gemini-2.5-pro` selectable in Settings.
- Body: `contents[0].parts = [ {text: instruction}, {inline_data: {mime_type, data}} ]`,
  `generationConfig.temperature = 0`.
- **One `HttpClient`** for the application lifetime.
- **`SemaphoreSlim`** limiting to 3-4 in flight.
- **Progress** via `IProgress<T>`: "Extracting page 4 of 7", per-page queued/running/done/failed,
  and results appearing as each page finishes.
- **Cancellation** through a real `CancellationToken` that aborts in-flight requests.
- **Retries:** 3 attempts, exponential backoff with full jitter, `Retry-After` honored, on 429
  and 5xx. Then mark the page failed and offer per-page retry.
- **Errors by status**, one human sentence each: 400 payload/request, 401/403 invalid key (offer
  to re-enter), 429 rate limited, 5xx Gemini unavailable. Never a stack trace, never the key.
- **Async all the way down.** No `.Result`, no `.Wait()`, no `async void` outside event handlers.

### Extraction prompt

Lives in `extraction-prompt.txt` as an embedded resource. Lines beginning `#!` are stripped
commentary, so every clause is documented next to itself and a unit test asserts the stripping.
Clauses: transcribe all visible text verbatim in natural reading order; tables as markdown
tables; heading hierarchy as markdown headings; figures and charts described in a bracketed note
rather than skipped; never summarize, editorialize, or add commentary; return empty for a blank
page rather than inventing content.

---

## 12. Results, copy, export

**Merged view (default).** One virtualized `ItemsControl` of page sections in continuous flow
with no card chrome — a hairline rule and a small-caps "Page 12" divider between sections. That
divider is the anchor: grid-to-result scroll sync targets it, and hovering or keyboard-focusing
it reveals per-page **copy** and **retry**, so per-page recovery survives the merge. A **Cards**
toggle swaps only the `DataTemplate` against the same items source — same anchors, same sync.

Find-in-results with match highlighting (`Ctrl+F`). Draggable `GridSplitter`, position persisted.

**Copy.** Per-page button plus **Copy All** (`Ctrl+Shift+C`). On click the button **Morphs** to a
checkmark reading "Copied" for ~1.5 s, then eases back, with a blur-masked crossfade so the two
states read as one object rather than two overlapping ones. Copy All dropdown: formatted text /
markdown / plain text. Clipboard access is wrapped in a **3-attempt, ~50 ms retry** because WPF
throws `COMException` when another process holds the clipboard; a hiccup must never crash the
app.

**Word export** via Open XML — a real `.docx`, never a renamed `.txt`.
Markdown headings become Word `Heading 1/2/3` styles so the navigation pane and TOC work;
markdown tables become real Word tables with borders and a styled header row; bold and italic are
preserved as character formatting; bulleted and numbered lists become real Word list numbering.
**Page break between source pages: ticked by default** (user decision #3). Optional per-page
"Page 12" heading. Header carries the source PDF filename; footer carries page numbers.
Document properties: Title = source filename, Application = Lumen, creation date set.
`SaveFileDialog` defaults to `<sourcename>-extracted.docx` in the source PDF's folder. After
export, offer **open the file** and **show in folder**. A locked file (Word already has it open)
produces "close it and try again," not an unhandled exception. The same dialog's filter dropdown
also exports `.md` and `.txt`.

---

## 13. Security and privacy

- API key stored **DPAPI-encrypted** (`ProtectedData.Protect`, `DataProtectionScope.CurrentUser`,
  with app entropy) as base64 in `%APPDATA%\Lumen\settings.json`. Never plaintext.
- **Validated before saving** — a cheap request must succeed before the key is persisted.
- Settings allows view (masked), replace, delete. Deleting re-opens the gate.
- The key never appears in a log, a URL query string, or any error message shown to the user.
  A redaction filter strips key-shaped strings before any log line is written.
- PDF contents and extracted text are **session-only, in memory**. Never persisted to disk.
- Global `DispatcherUnhandledException` and `TaskScheduler.UnobservedTaskException` handlers log
  to `%APPDATA%\Lumen\logs\` and show a recoverable dialog. The app must not vanish on an error.

---

## 14. Shell and persistence

Persisted to `%APPDATA%\Lumen\settings.json` (atomic write via temp plus `File.Replace`; a
corrupt file is backed up and defaults restored): window size, position, maximized state,
splitter position, theme override, model choice, recent files, export preferences.

Window placement is **validated on restore** — if the saved rect does not sufficiently intersect
a currently-connected monitor's working area, the window centers on primary instead. This handles
the unplugged-second-monitor case.

Light and dark are both genuinely designed; the app follows the Windows system theme by default
with a persisted manual override. Per-monitor DPI changes re-request thumbnails at the new scale
so they stay crisp when dragged to a different-DPI display. Resizable to a sensible minimum
without layout collapse.

---

## 15. Packaging

**Publish:** self-contained, single-file, ReadyToRun, `win-x64`.
`--self-contained true`, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`,
`PublishReadyToRun=true`, `DebugType=none`, **`PublishTrimmed` off**.

The PDFium native DLL must be **verified present alongside the exe in the published output** —
single-file extraction and native dependencies interact badly, so the *published* build is what
gets tested, not `dotnet run`. Whether `bblanchon.PDFium.Win32` requires the MSVC runtime will be
checked; if it does, the redistributable is bundled rather than assumed present.

**`win-arm64`** is produced only if the PDFium package ships an arm64 native asset. If it does
not, arm64 is skipped rather than shipping a build that crashes on first render.

**Installer (`installer/Lumen.iss`):** per-user to `%LOCALAPPDATA%\Programs\Lumen`,
`PrivilegesRequired=lowest`, no UAC. Modern wizard, custom icon, proper `AppPublisher`,
`AppVersion`, `AppId` GUID. Start Menu shortcut always; **Desktop shortcut as an unticked
checkbox**. Optional `.pdf` association under `HKCU\Software\Classes`, unticked, cleaned up on
uninstall. "Launch Lumen" on the finish page. Real uninstaller in Add/Remove Programs with icon,
publisher, version, estimated size. Uninstall **keeps `%APPDATA%\Lumen`** unless the user ticks
"also remove my settings and API key." Running-instance detection on upgrade offers to close
rather than failing. No bundled extras, no telemetry screen.

**`build.ps1`:** publishes, locates `iscc.exe` by probing PATH, `%ProgramFiles%`,
`%ProgramFiles(x86)%`, `%LOCALAPPDATA%\Programs`, compiles the `.iss`, drops
`Lumen-Setup-x.y.z.exe` into `dist/`. Fails loudly with a clear message and a download link if
the compiler is missing. Contains a commented-out `SignTool` block for a future certificate.

**Code signing:** binaries are unsigned, so SmartScreen will show "Windows protected your PC" on
first run. This is stated plainly in the README with the More info, Run anyway workaround.

---

## 16. Testing

TDD — tests written before implementation for every non-XAML behavior.

| Area | Cases |
|------|-------|
| `PageRangeParser` | `1-5, 12, 20-24`; whitespace; reversed ranges; out-of-bounds; empty; garbage; duplicates |
| Gemini payload | base64 correctness, part order, `temperature: 0`, header not query param |
| Gemini responses | success, empty candidate, safety block, malformed JSON |
| `RetryPolicy` | 429 plus `Retry-After`, 5xx backoff with jitter, give up after 3, no retry on 400/401 |
| `WordExporter` | H1-H3 styles, tables with header row, bold/italic runs, bullet and numbered lists, page-break flag, doc properties |
| `MarkdownStripper` | syntax removed, content preserved |
| `DpapiSecretStore` | round-trip, tamper detection |
| `SettingsStore` | atomic write, corrupt-file recovery, unknown-field tolerance |
| Window placement | rect off-screen centers on primary |
| Token discipline | no hex literal in any XAML outside `Primitives.xaml` |

`RetryPolicy` tests inject `TimeProvider`, so backoff timing is asserted **without sleeping**.
`GeminiClient` tests inject `HttpMessageHandler`. No test makes a network call.

FlaUI covers: launch, open a PDF, non-contiguous selection, extract against a stubbed client,
copy confirmation, export.

---

## 17. Definition of done

1. `dotnet build -c Release` clean — zero warnings from Lumen code.
2. `dotnet test` passes, output pasted.
3. `.\build.ps1` produces `dist\Lumen-Setup-x.y.z.exe`.
4. Installer runs end to end: install, Start Menu shortcut, launch, open a real multi-page PDF,
   non-contiguous selection, extract, text appears, copy confirms, Word export opens with correct
   headings and tables, uninstall leaves only settings.
5. Screenshots: start, grid mid-selection, extraction in progress, results with copy
   confirmation, settings, light, dark, installer wizard.
6. `README.md`: install, where to get a Gemini key, SmartScreen warning and workaround,
   architecture paragraph, build from source, known limitations.
7. A written account of the committed visual direction and which skills changed which decisions.

---

## 18. Open risks

| Risk | Handling |
|------|----------|
| Single-file extraction of **two** natives (`pdfium.dll` + `libSkiaSharp.dll`) | Test the **published** build, not `dotnet run`. Verify both DLLs and MSVC-runtime need. Non-negotiable gate. |
| Installer size 90-130 MB | Accepted and stated. The direct cost of self-contained. |
| WPF-UI API surface | Pinned to 4.3.0 stable. |
| FluentAssertions 8.x licence | Pinned to 7.2.2, the last Apache-2.0 release. |
| Live Gemini verification needs a real key | The app prompts. A key can be supplied for an end-to-end test; otherwise extraction is verified against a stubbed handler and the live path exercised manually. |
| `SelectionMode="Extended"` behavior | Verified against the real control, not assumed. |
