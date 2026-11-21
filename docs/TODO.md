# Open work

Last reviewed 2026-08-20, after the 1.3.0 release.

## Code signing (SignPath Foundation)

The application form has been walked through; what remains is submitting it
and what follows approval.

- **Project name to give: `SoftKnight Workshop Manager`.** The form asks for a
  name a search engine can identify. Searching "Workshop Manager" does not find
  this project - it returns an unrelated `SecMeyo/WorkshopManager` and a row of
  similarly named tools. With "SoftKnight" in front, softknight.de is the first
  three results.
- **Attribution goes up only after a certificate is issued**, not before. The
  wording is fixed: *"Free code signing provided by SignPath.io, certificate by
  SignPath Foundation"*, on the homepage and the download page, under a heading
  "Code signing policy".
- **Then**: add a signing step to `.github/workflows/release.yml`, between
  packaging and the release. Every signing needs manual approval, which fits the
  draft-release model already in place.
- Disclose if asked: the app's own source is Apache 2.0 and Newtonsoft.Json is
  MIT, but Microsoft's WebView2 SDK ships under Microsoft's own terms rather
  than an OSI licence. True of essentially every Windows app that embeds a
  browser.
- Expectation to keep: signing does **not** remove the SmartScreen warning
  immediately. Since 2024 not even an EV certificate does. It replaces "unknown
  publisher" with a name and starts the reputation building.

## Ideas, not scheduled

- **Deploying to hosted game servers** - written up in
  [ideas/server-deployment.md](ideas/server-deployment.md). The short version:
  FTP/SFTP covers nearly every host with one implementation, provider APIs only
  buy stopping the server and editing config, and the work that actually scales
  is the per-game mod layout.
- **Reuse requirements already known.** Checking requirements costs about a
  second per mod because Steam only publishes them on each mod's own page. The
  library stores `RequirementsChecked` per mod and `MainForm.cs:1001` skips
  anything already marked - but a mod added fresh from a collection does not
  appear to inherit that flag from the library, so known results are fetched
  again. Worth verifying and, if it holds, merging library data on add.
- **Extract the BBCode parser** as a standalone library. Evaluated earlier, not
  decided. It is self-contained (`BBCode/`), one tree with three emitters.
- **Ko-fi or similar.** Raised once, never pursued.
- **More green.** The accent colour is used sparingly; carry it further. Belongs
  in a session of its own.

## Not yet tested

The install path is now covered end to end (see below). Still unexercised:

- **Follow-ups from #1 (18 November), built on Linux only, none of it seen
  on Windows yet:**
  - *Dark theme:* the tab strip and the progress bar now paint themselves
    (`ThemedControls.cs`). Check that no light band is left beside the tabs,
    around the page or at the bottom, that switching tabs by mouse and by
    Ctrl+Tab still works, and that the progress bar fills during an install.
  - *High DPI:* the app is now system DPI aware. Check at 125 %, 150 % and
    200 % scaling: text sharp, nothing clipped - the tabs, the list columns,
    the bottom bar, the settings dialog and the linked collections dialog.
    Moving the window to a monitor with different scaling still stretches
    it, as before.
  - *Linked collections:* add a collection that links to others (one that
    links to none must not show the dialog). Check the counts in the dialog,
    that unchecked collections are left out, and with collection folders on
    that their mods land in `Collection\[Linked title]\`. The assignment and
    the folder layout are covered by a throwaway harness, the Steam lookup
    is not (blocked from the build machine).
  - *Info file:* with Workshop dates on, `mod_<id>.info` has no
    `Installation Date` line any more.

- **"Check installed / updates" asking Steam first** (issue #3). Built and
  checked on Linux against the WinForms reference assemblies, and the merge
  logic is covered by a throwaway harness. Not yet clicked in the real app on
  Windows: add a mod, install it, wait for an update on the Workshop (or lower
  `Time Updated` in its `mod_<id>.info` by hand) and press the button. The
  row should turn to "Update available" without re-adding the mod.

- **The info file inside the mod folder and collection folders** in the real
  app on Windows. Covered by the same harness: every layout is found again
  (beside, inside, in a collection, both), installed mods are skipped and
  updated in place, a mod from a second collection stays where it was first
  installed, a collection named like an existing mod folder goes to
  `<name> (collection)`. Not covered: resolving a real collection's title
  from Steam (blocked from the build machine) - add a collection, turn the
  option on, install, and check the folder name.
- **Replacing a mod folder on update, the preview image and the Workshop
  link** in the real app on Windows. Covered by a throwaway harness on Linux
  (stale files removed, read-only files, leftovers of a killed run, a failed
  copy leaving the old version intact, dates, the `mods/` layout, a foreign
  folder with the same title). What Linux cannot show: Windows refuses to
  rename a folder while the game holds a file in it open. The code relies on
  that refusal to keep the old version in place - worth trying once with the
  game running.
- the "Delete the raw SteamCMD downloads after installing" option. One
  specific risk, from forum reports about the Steam client, unconfirmed for
  SteamCMD: the option deletes `content/<appid>` but leaves
  `steamapps/workshop/appworkshop_<appid>.acf`, which records what SteamCMD
  believes is installed. If SteamCMD trusts that file, a later update of a
  cleaned-up mod would not download and end as "Failed". Test: install with
  the option on, then install the same mod again.
- importing a legacy SteamCMD script via **Load script...**
- a requirements check across a large collection (only 8 mods have been run)
- the archiving options against a game that ships a `mods/` subfolder - those
  mods are merged into the target and never get a folder of their own, so
  neither the title naming nor the date stamping applies to them

---

# Verified

What has actually been exercised, so a later session does not repeat it or
assume more than was shown.

## Updating (2026-08-20)

The real 1.1.0 build was downloaded from GitHub into a throwaway folder and
started. It reported "A new version is available: 1.2.0 (installed: 1.1.0)",
downloaded, replaced itself, restarted, and came back as `1.2.0+3130cc5` - the
exact merge commit. Log line: "Workshop Manager 1.2.0 is up to date".

Version ordering is covered by nine checks against `SemanticVersion`: a stable
release outranks any pre-release of the same numbers, build metadata (`+sha`) is
stripped, no downgrade to an older beta, and later releases still order
correctly.

## Installing (2026-08-20)

First end-to-end run of the whole path:

- **"Download it for me"** fetched SteamCMD (1.69 MB) into an empty folder.
- **Installing mod 3776637622** produced 315 files, 1.5 MB, including
  `Assemblies/UniqueMeleeWeapons.dll`, `About/` and `Textures/`.
- `mod_<id>.info` was written with title, tags, preview URL and description.
- The library recorded the mod.
- A target path containing an umlaut (`testfälle`) caused no trouble.

Found by this run: the status line ended every installation one short
("Installed 0 of 1 mods" beside a dialog saying 1). Fixed in `a9276a8`.

## The 1.3 archiving options (2026-08-20)

With both options on, mod 3776637622 installed into a folder named
`Unique Melee Weapons` rather than `3776637622`, and every one of its 315 files
carried exactly one modification date and one creation date:

| | value | source |
| --- | --- | --- |
| modified | 2026-08-19 04:00:22 UTC | `time_updated` from the Web API |
| created | 2026-08-02 22:51:58 UTC | `time_created` from the Web API |

**The authors' original file dates cannot be recovered**, and this is settled
rather than assumed: before the change, all 315 files sat inside a three-second
window - the download time - in the SteamCMD folder as well as the target. Steam
does not transmit them, so no downloader can produce them.

Preview images are a different matter: the CDN sends
`Last-Modified: Sun, 02 Aug 2026 22:51:57 GMT`, which matches the API's
`time_created` to the second. That is why a browser preserves the date when you
save a preview by hand, and the cache now does the same.

## The release workflow (2026-08-20)

Both paths have run for real:

- A push to `main` built, verified the executable and kept the artifact, while
  correctly skipping the two tag-only steps.
- A deliberately wrong tag (`v9.9.9` against a project saying 1.2.0) failed at
  the version check and skipped everything after it. No release was created.
- The 1.3.0 tag produced the draft release including its zip.

One trap, worth remembering: **a PATCH to the releases API without `tag_name`
clears the tag association.** It happened while adding release notes - the draft
then pointed at `untagged-950ef84...`, and that field is what the updater reads
to determine the version. Publishing it would have silently offered the update
to nobody. Always send `tag_name` along.
