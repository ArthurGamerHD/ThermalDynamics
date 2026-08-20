# The balance lab

A rig for deciding what good balance *is*, from a population of real ships rather than from a
hull this repository built for itself.

## Why

Every number this mod ships was chosen against a synthetic rig. [`Census`](../sim/Thermodynamics.Harness/Census.cs)
is honest about the cost of that in its own summary — its tiers come from the telemetry dump of one
1,381-block ship, and *"one ship is one ship"*. The reactor retune in
[balance.md](balance.md#reactor-waste-heat) is the same shape of argument one level up: a fraction
measured against two rigs, both of which this repository invented.

That is enough to say *a ship like this overheats*. It is not enough to say *eleven per cent of the
ships people actually build overheat*, and only the second kind of statement can decide a default.

The lab exists to make the second kind of statement.

## The process

The original sketch was: pull ten thousand grids, run them through scenarios, derive guidelines
from the matrix. That is right in outline and has a gap in the middle — step three has no verdict
function. A matrix of temperatures does not say which of them are *good*, and criteria invented
after seeing the data are criteria fitted to the data.

So the refined process puts the criteria first, and stages the runs so the expensive ones are only
paid where they buy something.

### 0. Define good balance, before collecting anything

Six criteria, each a number the matrix can produce and each able to fail. These are proposals to
argue with — the point is that they are written down before the data is, so a run that fails them
is a finding rather than an excuse to move a threshold.

| # | Criterion | Fails when | Why it matters |
| --- | --- | --- | --- |
| **G1** | **Idle is safe.** A ship at rest in the environment it was built for does not overheat. | more than ~1 % of the corpus goes critical at idle | A mod that kills parked ships is uninstalled, whatever else it does right. |
| **G2** | **Load bites.** Under sustained full power, a meaningful share of uncooled ships reach a warning state. | fewer than ~20 % ever get warm | Heat that never arrives is scenery. This is the criterion the current build most likely fails. |
| **G3** | **Cooling works.** Fitting radiators or a loop moves the outcome. | median Δpeak from a standard cooling fit is small | If the answer does not respond to the one lever the player has, the mechanic is decoration. |
| **G4** | **Design decides, not size.** Outcome correlates with things a builder controls — exposed area per watt, radiator count, how buried the reactor is — more than with block count or grid size. | rank correlation with block count exceeds that with exposure | Otherwise the mod taxes big ships rather than rewarding good ones. |
| **G5** | **No death spiral.** A ship past critical that throttles to idle returns below critical in bounded time. | recovery time unbounded, or damage continues after the load stops | A player must be able to react to a warning. |
| **G6** | **Affordable across the population.** Substep demand and step cost at p95/p99 of the corpus, not at the mean. | p99 substep demand exceeds what the shipped caps grant | The census hull is one point; the tail is what stutters. |

**G6 is not a balance criterion and is here on purpose.** The corpus answers the open questions in
[stiffness.md](stiffness.md) and the D-series of the [backlog](backlog.md) — lumping, multirate,
the substep cap — better than any synthetic ladder can, because those decisions turn entirely on
the *shape of the tail* of the stiffness distribution, and nobody has ever seen it.

### 1. Acquire a corpus

The target is ten thousand popular unmodded designs. See [Acquisition](#acquisition) below for what
that actually costs — it is the one step with constraints outside this repository.

The corpus is a directory of `bp.sbc` files. Nothing downstream cares how they got there, so the
acquisition route can change without touching the lab.

### 2. Screen everything (cheap)

Every ship in the corpus, no solver:

* block census by type, mass and derived thermal properties — this is what replaces `Census`;
* installed power, thrust and their ratio to hull mass;
* exposed area, buried fraction, room count;
* cooling fitted: radiators, loops, heat pumps, vents;
* **stiffness demand** — conductance over capacity per block, and the distribution's tail.

This pass alone answers G6 and re-founds `Census` on a population. It also produces the strata for
what follows.

### 3. Run a scenario battery (medium, stratified sample)

Not every ship: a stratified sample across size, grid type, power density and cooling fitted.
Roughly 500 ships, so a result can be attributed to a stratum rather than to one hull.

Each scenario has to answer a question no other scenario answers:

| Scenario | The question |
| --- | --- |
| `idle-orbit` | Does it survive doing nothing in space? **G1** |
| `idle-surface` | Does it survive doing nothing on a hot world? **G1** |
| `full-power` | Where does it settle with every reactor at rating? **G2** |
| `burn` | Sustained full thrust — the real load case. **G2** |
| `combat` | Full power plus weapons plus manoeuvring. **G2** |
| `reentry` | Atmospheric friction at speed on the leading face. |
| `sunward` | Unshaded, sun-facing, no rotation. |
| `night-side` | The cold case — does anything freeze past a floor? |
| `sealed` | Crew compartments with the air coupled. |
| `cooled` | The same ship with a standard cooling fit. **G3** |
| `recovery` | From past critical, throttled to idle. **G5** |
| `derate` | Power drawn down in steps, to find the sustainable fraction. |

The comparison that matters is **within a ship**: `full-power` against `cooled` is G3, and
`full-power` against `idle-orbit` is G2. Cross-ship comparison is what the strata are for.

### 4. Sweep the settings space (expensive, small sample)

Roughly 50 ships spanning the strata, across a settings space rather than the five named profiles.
The output is not one answer but a **region**: every settings vector that satisfies G1–G6, with
its cost. The named profiles are then points chosen inside that region for different tastes, which
is what the user asked for and what [profiles.md](profiles.md) currently asserts without evidence.

Axes worth sweeping: `HeatTimeScale`, `MaxSubsteps`, `MaxSubstepsPerBlock`,
`MaxElementVisitsPerStep`, and the definition-side dials the derivation exposes — the waste
fractions and the critical-temperature blend.

Coordinate sweeps rather than a full grid: the space is large and the axes are close to separable
in their effect on cost, if not on balance.

## Acquisition

**This is the step with constraints this repository cannot remove.** Three routes, and they trade
off effort against scale:

| Route | Needs | Scale | Notes |
| --- | --- | --- | --- |
| **Subscribe in game** | nothing | tens to hundreds | Steam unpacks subscribed items to `steamapps/workshop/content/244850`, already in the layout the lab reads. Free, no credentials, no rate limit. This is what the lab runs on today. |
| **Steam Web API + direct UGC** | a free Web API key | thousands, if it works | `IPublishedFileService/QueryFiles` lists popular blueprints by tag and sort order. Whether the items expose a downloadable `file_url` is **unverified** — newer workshop items often do not, and it has to be probed rather than assumed. |
| **SteamCMD** | a Steam account owning the game | ten thousand | `workshop_download_item 244850 <id>` in batches. Anonymous login generally fails for a paid title. Hours of downloading, and worth rate-limiting out of courtesy. |

**The route taken is the second for listing and the third for fetching**, built as
[`CorpusFetch`](../sim/Thermodynamics.Sim/CorpusFetch.cs):

```bash
# list only: writes out/corpus/manifest.csv, downloads nothing
dotnet run --project Thermodynamics.Sim -- corpus-fetch --key <webapi> --top 10000 --list-only

# list and fetch
dotnet run --project Thermodynamics.Sim -- corpus-fetch --key <webapi> --user <steam-account> --top 10000
```

Ranked by **total unique subscriptions**, not by votes or recency: the corpus is meant to be the
designs people build and fly, and a subscription is the closest signal the workshop has to that.
Ranking by vote would over-weight the spectacular and by date the untested. Items tagged `Mod`
alongside `Blueprint` are dropped at listing rather than downloaded and rejected.

**No credential passes through this tool.** SteamCMD is invoked with a user name and no password,
which works once the account has been logged in by hand:

```bash
steamcmd +login <user> +quit      # once, interactively; SteamCMD caches it
```

Anything else would mean this program handling a password, and it has no business doing that.

The run is resumable and rate-limited: pages pause, fetches go in batches of fifty, the manifest is
reused if it is already long enough, and anything already on disk is skipped. SteamCMD unpacks into
the layout `Blueprints` already reads, so the corpus directory is pointed at rather than assembled.
A nonzero SteamCMD exit is not treated as failure — on a corpus this size an item being deleted or
made private between listing and fetching is routine.

Two filters apply whatever the route:

* **Unmodded only**, and strictly: one unresolved subtype disqualifies a ship, because there is no
  way to tell whether the block that failed to resolve was a decorative panel or the reactor.
* **Designs, not fragments.** Ships under 25 blocks are cockpits, doors and test rigs.

## What exists now

| Piece | State |
| --- | --- |
| [`GameBlocks`](../sim/Thermodynamics.Harness/GameBlocks.cs) | Reads every definition out of the installed game — size, mounts, sealing, build cost. |
| [`Blueprints`](../sim/Thermodynamics.Harness/Blueprints.cs) | Turns a `bp.sbc` into ships the solver can run. Models are derived and shared across the corpus. |
| [`CorpusLab`](../sim/Thermodynamics.Harness/CorpusLab.cs) | Corpus yield and size distribution. `-- corpus [--path <dir>]`. |
| [`BlueprintTests`](../sim/Thermodynamics.Tests/BlueprintTests.cs) | Seven tests on the yield, ending with a real subscribed ship building a simulation that steps. |
| [`CorpusFetch`](../sim/Thermodynamics.Sim/CorpusFetch.cs) | Lists and fetches the corpus. `-- corpus-fetch`. Unexercised against a real key. |
| Steps 0, 2, 3, 4 | Designed here, unbuilt. |

Measured on the 16 blueprints already subscribed on the development machine: 140 ships, 1 modded,
107 under the size floor, **32 usable** totalling 25,893 blocks, from 26 up to 9,378 blocks each.
The yield is low because a subscription list is mostly mods; a corpus acquired as blueprints
deliberately will do far better.

## Open questions

* **The fetcher has never run against a real key.** Its failure paths are checked — a missing key,
  a rejected key and a missing account name all report and stop cleanly — but whether the query
  parameters return what is wanted can only be settled by a run.
* ~~Is one unresolved block too strict?~~ **Decided: it stays strict.** A softer rule keyed on
  whether the unresolved blocks carry power would raise yield, at the cost of the corpus measuring
  *nearly* vanilla balance. Revisit only if the real yield turns out to be unusably low.
* **Whether `Census` should be replaced or kept beside the corpus.** Its tiers are a hypothesis the
  corpus can now test; if they hold, that is worth knowing, and if they do not, every scale figure
  taken on them wants re-reading.
* **Subgrids.** A blueprint's rotor and piston subgrids are read as separate ships today. They are
  thermally connected in game, through the bridges `ThermalBridges` builds, and the lab does not
  reassemble them.
