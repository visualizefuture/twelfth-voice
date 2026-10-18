# Event and insight formats

All JSON uses camelCase, enums as strings, and omits null fields.

## Coordinates

Pitch is 105 m × 68 m. Every `start` / `end` point is in the **attacking frame of the event's team**: `x = 0` is that team's own goal line, `x = 105` the opponent's goal line, `y` runs 0–68 across the pitch (`y = 0` is the right touchline when facing the opponent's goal). To draw both teams on one pitch, mirror the away team's points: `(105 − x, 68 − y)`.

## Match clock

`matchSecond` is continuous match time. The second half always starts at 2700, even if the first half ran into added time, so use `period` with `matchSecond`. `clock` is broadcast-style: `"23:05"`, `"45+01:30"`, `"90+03:12"`.

## MatchEvent

| Field | Type | Notes |
|---|---|---|
| `matchId` | string | e.g. `TV-HAR-IRV-7` |
| `sequence` | int | Strictly increasing |
| `type` | string | See event types below |
| `period` | int | 1 or 2 |
| `matchSecond` | number | Seconds |
| `clock` | string | Display clock |
| `teamId` | string? | Team performing the action |
| `playerId` | string? | Player performing the action, e.g. `HAR-09` |
| `relatedPlayerId` | string? | Receiver, player tackled or fouled, assister, sub coming on |
| `start`, `end` | `{x, y}`? | Attacking frame of `teamId` |
| `outcome` | string? | `Success`, `Fail`, `Goal`, `Saved`, `OffTarget`, `Blocked` |
| `distanceM` | number? | Pass, carry or dribble distance; shot distance to goal |
| `ballSpeedKmh` | number? | Pass and shot speed |
| `playerSpeedKmh` | number? | Sprint top speed |
| `xg` | number? | Shots |
| `passDifficulty` | number? | 0–10 |
| `bodyPart` | string? | `RightFoot`, `LeftFoot`, `Head` |
| `homeScore`, `awayScore` | int? | On goals, period ends, match end |
| `qualifiers` | string[] | e.g. `Through ball`, `Under pressure`, `Progressive`, `Cross`, `Long ball`, `Big chance`, `Penalty`, `High regain`, `Box defending` |
| `tracking` | array? | `TrackingSnapshot` only: `{playerId, distanceM, topSpeedKmh, sprints}` per player on the pitch |

**Event types:** `MatchStart`, `PeriodStart`, `PeriodEnd`, `MatchEnd`, `KickOff`, `Pass`, `Carry`, `Dribble`, `Shot`, `Goal`, `Save`, `Block`, `Tackle`, `Interception`, `Clearance`, `Pressure`, `Foul`, `YellowCard`, `RedCard`, `FreeKick`, `Corner`, `ThrowIn`, `GoalKick`, `Offside`, `Substitution`, `Sprint`, `PossessionChange`, `TrackingSnapshot`.

## Insight

| Field | Type | Notes |
|---|---|---|
| `id` | string | Unique per match |
| `kind` | string | `Goal`, `BigChance`, `SpeedAlert`, `DistanceMilestone`, `PassOfNote`, `PlayerMilestone`, `MomentumShift`, `PressingShift`, `ControlShift`, `TempoShift`, `Card`, `Substitution`, `HalfTime`, `FullTime`, `Kickoff` |
| `period`, `matchSecond`, `clock` | | When to show it |
| `importance` | 0–1 | How much it matters to the story |
| `headline` | string | Casual-friendly line |
| `whyItMatters` | string | The explanation |
| `analystNote` | string? | Numbers-first version |
| `teamId` | string? | |
| `playerIds` | string[] | Players to tag on screen |
| `evidence` | object | Named numbers behind the insight, e.g. `{ "xg": 0.31, "shot_speed_kmh": 104 }` |
| `sourceSequences` | int[] | Event sequences it came from |
| `overlay` | object | `{ template, placement, displaySeconds, priority }` |

### Overlay templates

| Template | Placement | Used for |
|---|---|---|
| `goal-banner` | full-width | Goals |
| `chance-card` | lower-third | Big chances |
| `pass-graphic` | pitch-overlay | Passes of note (draw start → end) |
| `player-tag-speed` | near-player | Sprint speed above threshold |
| `player-tag-distance` | near-player | Distance milestones |
| `player-tag-stat` | near-player | Player milestones |
| `shot-speed` | near-player | Very hard shots |
| `momentum-bar` | top-bar | Momentum swings |
| `control-meter` | top-right | Control vs chaos changes |
| `pressure-graphic` | lower-third | Pressing changes |
| `stat-card` | lower-third | Tempo and general stats |
| `card-alert` | lower-third | Yellow and red cards |
| `sub-board` | lower-third | Substitutions |
| `summary-panel` | centre | Half-time and full-time |
| `match-intro` | full-width | Kick-off |

When two insights compete for the same placement, show the one with the higher `priority`.
