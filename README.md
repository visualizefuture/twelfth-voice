# Twelfth Voice

[![CI](https://github.com/visualizefuture/twelfth-voice/actions/workflows/ci.yml/badge.svg)](https://github.com/visualizefuture/twelfth-voice/actions/workflows/ci.yml)

**Every fan gets their own match.**

Twelfth Voice turns synthetic football events into explainable match intelligence: live stats, key moments, *why* they matter, broadcast-ready overlays and personalised commentary for every kind of viewer, from the casual fan to the data-hungry analyst, in any language.

Built for the *Microsoft × Premier League — Inside the Game* developer hackathon.

> All clubs, players, stadiums and matches in this repository are fictional and synthetically generated. No real match data or footage is used.

## The pipeline

| Stage | What it does | Where |
|---|---|---|
| **Ingest** | A deterministic simulator generates football-realistic matches and streams events live (passes, carries, dribbles, shots, tackles, pressure, possession changes, cards, subs, sprints, tracking). | `TwelfthVoice.Simulator` |
| **Interpret** | Running team and player stats, pass distance / accuracy / difficulty, ball and shot speed, xG, distance and top speed, plus rolling momentum, control-vs-chaos, pressing and tempo indicators. | `TwelfthVoice.Intelligence` |
| **Explain** | Every insight carries a plain-language *why it matters*, an analyst note and the evidence numbers it was derived from. | `MatchAnalyzer`, `Narration`, `RecapBuilder` |
| **Render** | Insights include a machine-readable overlay hint (template, placement, duration, priority), streamed live over SignalR and drawn over a live pitch. | `OverlayHint`, `TwelfthVoice.Api`, `web/` |
| **Personalise** | Casual vs analyst modes, favourite club, player-focus mode, language preference — each viewer gets their own stream. | `InsightPersonalizer`, SignalR hub; translation via agents *(in progress)* |

## Quick start

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build
dotnet test

# List the fictional clubs and squads
dotnet run --project src/TwelfthVoice.Cli -- teams

# Watch a match live (one match minute per second), casual mode
dotnet run --project src/TwelfthVoice.Cli -- replay --home HAR --away IRV --seed 7

# Analyst mode, following the Harbourside number 9 at 2x speed
dotnet run --project src/TwelfthVoice.Cli -- replay --home HAR --away IRV --seed 7 --speed 120 --mode analyst --follow HAR-09

# Automated recap
dotnet run --project src/TwelfthVoice.Cli -- recap --home KIN --away WMC --seed 11

# Export a full dataset (match, events, insights, final stats, recap) as JSON
dotnet run --project src/TwelfthVoice.Cli -- generate --home KIN --away WMC --seed 11 --out data/kin-wmc-11.json --pretty
```

The same seed and teams always produce the same match, event for event, which makes demos and tests repeatable.

## Fan app (Angular)

Requires [Node.js 22](https://nodejs.org/) or later. Run the API first, then in a second terminal:

```bash
cd web
npm install
npm start
```

Open **http://localhost:4200**. The dev server forwards `/api` and `/hubs` to the API on port 5080.

- **Live pitch**: the ball moves through every action, the player on the ball is tagged with what they did (pass length, shot speed, sprint speed), and insights appear as broadcast graphics where their overlay hint says.
- **Heat map and shot map** layers: the heat map follows whichever player you pick.
- **Momentum chart**: who was on top, minute by minute, with goals marked.
- **The story**: the personalised feed of key moments and why they matter.
- **Your view**: fan or analyst, your club, follow a player, language.

## Live API

```bash
dotnet run --project src/TwelfthVoice.Api
```

Then open **http://localhost:5080** for the live console: pick two clubs, kick off, and switch between casual and analyst modes, a favourite club or a single player while the match plays.

**Storage.** By default the API uses SQL Server LocalDB (installed with Visual Studio) and creates the `TwelfthVoice` database on first run. If no database is reachable it logs a warning and keeps running in memory; `GET /api/status` shows which mode is active. To skip SQL entirely, use the *http (memory only)* launch profile or set `Storage__Provider=None`. For Azure SQL, set `ConnectionStrings__TwelfthVoice`.

**REST** (OpenAPI document at `/openapi/v1.json`):

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/teams` | Fictional clubs and squads |
| POST | `/api/matches` | Simulate a match and start it live `{ homeTeamId, awayTeamId, seed, speed, autoStart }` |
| GET | `/api/matches` | Live matches plus recent stored matches |
| POST | `/api/matches/{id}/start` · `/pause` · `/resume` | Control playback |
| PUT | `/api/matches/{id}/speed` | `{ speed }` match seconds per real second |
| GET | `/api/matches/{id}/state` | Scoreboard, momentum, control, tempo (`?focusPlayerId=`) |
| GET | `/api/matches/{id}/snapshot` | Full team and player statistics |
| GET | `/api/matches/{id}/insights` | Personalised insights (`?mode=analyst&favoriteTeamId=HAR&focusPlayerId=HAR-09&language=es`) |
| GET | `/api/matches/{id}/events` | Released events (`?after=sequence&type=Shot`) |
| GET | `/api/matches/{id}/timeline` | Momentum, control, xG and score every ~30 match seconds |
| GET | `/api/matches/{id}/recap` | Automated recap |
| POST / PUT / GET | `/api/profiles` | Save and load viewer profiles |
| GET | `/api/status`, `/health` | Service and storage status |

**SignalR** hub at `/hubs/match`:

- Call `JoinMatch(matchId, profile)` → returns summary, match info, current state and recent insights so the screen can render at once.
- Receive `insight` (personalised for that viewer), `state` (scoreboard strip, every ~30 match seconds and on key moments) and `status` (live / paused / finished / speed).
- Call `UpdateProfile(profile)` to change mode, club, player or language mid-match, and `SubscribeEvents(true)` for the raw event feed (`event`), e.g. for an overlay renderer.

A viewer profile is `{ mode: "Casual" | "Analyst", favoriteTeamId, focusPlayerId, language }`.

## Explainable by design

The numbers on screen come from small, readable models in [`FootballMath`](src/TwelfthVoice.Domain/FootballMath.cs), and the simulator uses the *same* models to decide outcomes:

- **xG**: logistic model on shot distance and goal-mouth angle (headers and big chances adjusted).
- **Pass difficulty (0–10)**: length, forward progression, pressure, through balls, crosses, passes into the box.
- **Control vs chaos (0–100)**: pass completion, how often the ball changes hands, and how often fouls and duels break the game up, over the last five minutes.
- **Momentum (−100…100)**: weighted final-third entries, box entries, shots (by xG), corners and high regains for each side over the last five minutes.

The simulator is calibrated to roughly 1,000 passes at ~83% accuracy, ~25 shots, ~3 xG and ~2.7 goals per match.

## Repository layout

```
src/
  TwelfthVoice.Domain        Event model, pitch geometry, clock, football maths, JSON settings
  TwelfthVoice.Simulator     Fictional league, squad generator, match simulator, live feed
  TwelfthVoice.Intelligence  Stats, rolling indicators, insights, recap builder
  TwelfthVoice.Cli           Command-line runner (teams / generate / recap / replay)
  TwelfthVoice.Api           ASP.NET Core API, SignalR hub, EF Core storage, live console page
web/                         Angular fan app: live pitch, overlays, momentum, stats, personalised feed
tests/
  TwelfthVoice.Tests         xUnit tests for simulator realism, stats, insights, personalisation and live sessions
docs/
  ARCHITECTURE.md            Target architecture and build plan
  EVENT-SCHEMA.md            Event and insight formats for overlay renderers
```

## Roadmap

- [x] Synthetic match generator and live feed
- [x] Stats, trends and rule-based explanations
- [x] ASP.NET Core API + SignalR live channel, SQL persistence (LocalDB / Azure SQL)
- [x] Personalisation: casual vs analyst, favourite club, player focus, language preference
- [ ] Multi-agent layer (Microsoft Agent Framework + Microsoft Foundry): Analyst, Explainer, Storyteller, Localizer, Director
- [ ] MCP server exposing match state as tools
- [x] Angular fan app: live pitch with overlays, heat and shot maps, momentum chart, personalised feed
- [x] Continuous integration: build and test on every pull request
- [ ] Deployment to Azure Container Apps

## License

MIT — see [LICENSE](LICENSE).
