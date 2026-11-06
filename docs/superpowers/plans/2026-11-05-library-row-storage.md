# Plan: Library rows out of memory (Rocky's choice, 2026-11-05)

**Why:** at 50,000 Library rows the idle app holds 185.5 MB against the 150 MB budget (`docs/PERFORMANCE.md`). Rocky chose to rework row storage over a per-row budget allowance.

**Done when:** `tools/Measure-Budgets.ps1 -Scenario idle -Assert -WatchedFolder <50,000 files>` exits 0 on the reference PC; search still narrows as you type at 50,000 rows (today's test asserts under 150 ms a keystroke, `tests/Stx.App.Tests/LibraryCollectionTests.cs:155-169`); the Library and screenshot gates are unchanged.

## ASSUMPTIONS (unread or unmeasured, check first)

- The SQLite that Microsoft.Data.Sqlite bundles here has FTS5 with the **trigram** tokenizer (SQLite 3.34 or later). Search today is a substring match (`ong` finds `Song`); FTS5's default tokenizer matches only whole tokens or prefixes, which would change what search finds. Step 1 checks the version and stops if trigram is missing.
- WinUI's `ListView` loads more rows through `ISupportIncrementalLoading` as you scroll, with the current item containers (`src/Stx.App/Panels/LibraryPanel.cs:116`). Not yet tried in this app.
- The estimate (2 to 4 working days) is a guess.

## What holds every row today (read this session)

| Where | What it does with all rows |
|---|---|
| `src/Stx.Data/MediaRepo.cs:115-135` | `Library()` reads every `media_files` and `media_items` row |
| `src/Stx.ViewModels/LibraryViewModel.cs:216-250` | `Build()` makes a row object per file, and calls `File.Exists` on each, on every refresh |
| `src/Stx.ViewModels/LibraryViewModel.cs:46-49` | each row keeps a lower-case copy of title, file name and folder for search |
| `src/Stx.ViewModels/LibraryViewModel.cs:270-274` | search filters the in-memory list |
| `src/Stx.ViewModels/LibraryViewModel.cs:162`, `:177`, `:267` | `IsEmpty`, `CountText`, `Find(path)` read the whole list |
| `src/Stx.ViewModels/LibraryViewModel.cs:255` | SpaceText walks every row for Shrink copies |
| `src/Stx.App/Panels/LibraryPanel.cs:50` | the `ListView` is bound to the whole `Rows` collection |
| `src/Stx.App/Composition.cs:215` | PLAY builds PREV/NEXT's list from every openable row |

## Steps (each test-first, each break-checked)

1. **Search in SQLite.** A trigram FTS5 table over title, file name and folder, kept current by triggers on `media_files` and `media_items`; a migration that fills it for existing databases. Queries: count, a page in the Library's order, find by path (the `media_files_path` index exists, `src/Stx.Data/Migrations.cs:67`), the next and previous openable row, and Shrink copies with their originals. The 50,000-row timing test moves here.
2. **Rows on demand.** `LibraryViewModel` holds only the loaded pages: an incremental-loading collection for the `ListView`; `File.Exists` only for rows that are loaded; `CountText`, `IsEmpty` and `Find` from queries. Rows that didn't change stay the same objects, as now, so the list isn't redrawn.
3. **PREV and NEXT from queries** instead of the list built at `Composition.cs:215`.
4. **SpaceText from the Shrink query**, keeping today's rule (one original per item, against its smallest copy).
5. **Prove it:** Library, App and Data suites; screenshot gate; `Measure-Budgets.ps1` at 0 and 50,000 rows; `docs/PERFORMANCE.md` updated with the numbers.

## Not in this plan

- Nothing changes in how folders are scanned or watched (`src/Stx.Core/Library/CollectionWatcher.cs`).
- The view-model tests that build a fake catalog (`tests/Stx.App.Tests/ShrinkTests.cs`, `LibraryCollectionTests.cs`) will need the new catalog methods; how many tests that touches hasn't been counted.
