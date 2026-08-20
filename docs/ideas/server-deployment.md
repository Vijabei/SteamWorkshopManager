# Idea: deploying mods to hosted game servers

**Status:** idea only. Not planned, not scheduled, nothing implemented.
**Raised:** 2026-08-20

## The question

Today the app installs mods into a folder on the machine it runs on. People who
rent a game server have to take that folder and move it to the server by hand.
Could the app do that step, and could it do it the same way for every hosting
provider?

## Short answer

The file transfer can be uniform. The parts that cannot be uniform are stopping
the server and editing its configuration — and the genuinely hard part is
neither of those. It is the per-game mod layout.

A common assumption is that each provider needs its own authentication. That is
only true for the convenience features, not for getting the files across.

## What is uniform

Practically every host exposes the game directory over **FTP, FTPS or SFTP**
with a username and password. That is one implementation covering the large
majority of providers, using standard protocols and no provider-specific code.

## What is not uniform

### Stopping and starting the server

Writing into a running game directory does not work reliably: files are locked,
and some games write their own state back on shutdown and undo the change. A
deployment therefore needs the server stopped.

Without a provider API the only option is asking the user to stop the server in
their panel and confirm before the transfer starts. That is acceptable, but it
makes the feature a manual ritual rather than one click.

### Configuration and startup parameters

Several games need more than files on disk — a mod list in an ini, or a `-mod=`
startup parameter. Many panels **regenerate those config files from their own
database when the server starts**, which silently discards anything uploaded
over FTP. This cannot be worked around from the outside; it needs the provider's
API or it does not happen at all.

### Provider APIs, if they are ever added

**Pterodactyl** is an open source panel used by many smaller hosts, so a single
adapter covers a number of brands at once — the best coverage per unit of work.
Large brands such as Nitrado publish their own APIs. Support for those should be
demand-driven, not built speculatively.

*Not verified:* which provider offers which API today, and how each one behaves
over FTP, has not been checked. This needs a survey before any commitment.

## The actually hard part: per-game layout

`InstallModAsync` currently handles two cases: merge a `mods` subfolder into the
target, or drop the item into a folder named after its id. That is enough on a
client, where the game sorts out the rest.

On a server it is not enough, and the details differ per game:

| Game | What the server expects |
| --- | --- |
| RimWorld | `Mods/<name>/` |
| Project Zomboid | files plus `WorkshopItems=` and `Mods=` entries in the ini |
| ARK | an unpacked form plus a generated `.mod` file; workshop content is not directly usable |
| DayZ | `@ModName` folders plus matching `-mod=` startup parameters |
| Valheim | BepInEx plugin layout |

`GameRule` currently holds a single field, `TargetDirectory`. Server deployment
turns it into a layout profile per game.

**This work scales with the number of games, not the number of hosts.** It is
where most of the effort would go, and it is the part that cannot be borrowed
from anywhere.

## A cheaper alternative worth considering

Uploading mods means pushing gigabytes from a home connection to a machine that
could have fetched the same data at datacenter speed.

Where a panel offers a SteamCMD console, the better delivery is the **command
list**, not the files — no upload at all. softknight.de already generates
exactly that kind of script, so this would connect the website and the app
rather than duplicating work.

This does not cover every host, but where it applies it is strictly better.

## Advantages

- Removes the manual copy step for people running rented servers, which is a
  real and repeated chore.
- Uses the dependency resolution the app already has. Panels that install
  workshop items usually do not resolve required mods.
- Works for games and collection sizes that a provider's own installer does not
  handle.
- The transfer layer is standard protocols, so it is testable locally against
  any SFTP server without an account anywhere.
- A `IServerTarget` abstraction with the local folder as its first
  implementation would make the existing install path a special case of a
  general one, which is a tidier design than what exists now.

## Disadvantages and risks

- **Upload volume.** Ten gigabytes over a 40 Mbit upstream is roughly half an
  hour; over a 10 Mbit upstream, more than two hours. Repeated on every
  collection update. Delta upload is not a nice-to-have here, it is a
  precondition.
- **Per-game layouts are open-ended.** Each new game is new research, new
  handling and new ways to be subtly wrong. Getting one wrong produces a server
  that does not start, and the user cannot easily tell why.
- **Destructive by nature.** The feature writes into, and potentially deletes
  from, a directory the user pays for. A mistake is expensive and hard to undo.
  A dry run showing exactly what would change is mandatory, not optional.
- **Config regeneration by panels** can make a correct deployment look broken,
  with no way to detect it from our side.
- **Credential handling** raises the security stakes of the whole application
  considerably (see below).
- **Partial overlap with what hosts already provide.** Nitrado and GPORTAL ship
  workshop installers for popular games. The value is concentrated in
  unsupported games, large collections and dependency resolution — not in the
  common case.
- **Support burden.** Failures will look like our bug even when they are a
  provider quirk, a permission problem or a game requirement.

## Security

Storing access credentials for a server the user pays for changes the risk
profile of the app.

- Credentials must **not** go into `workshopmanager_settings.json`. That file is
  plain JSON stored next to the executable, so it travels whenever the folder is
  copied. Credentials belong under `%LOCALAPPDATA%`, encrypted with DPAPI
  (`ProtectedData`, scoped to the user).
- Prefer SFTP or FTPS. Plain FTP transmits the password in the clear and should
  produce a visible warning rather than a silent connection.
- .NET has no built-in SFTP client, so this means a dependency — SSH.NET for
  SFTP, and FluentFTP for FTP/FTPS since `FtpWebRequest` is obsolete. Both are
  compatible with Apache 2.0.
- Offer a "do not remember" option. Some people will prefer typing the password
  per deployment.

## If it were built

1. **Transport and one game.** `IServerTarget` with `Upload`/`Exists`/`Delete`,
   the local folder as the first implementation, then SFTP/FTPS/FTP. One layout
   profile, a dry run and delta upload. No provider code at all.
2. **More layout profiles.** This is where the actual user value is.
3. **Provider adapters,** starting with Pterodactyl, for stop/start and config.
   Only if there is demand.

Provider authentication is therefore the optional last step, not the foundation.

## Open questions

- Which providers to survey first, and what their FTP and API reality is.
- Which game to start with — probably whichever the existing users actually run.
- Whether the command-list route covers enough hosts to be the primary answer
  instead of file transfer.
