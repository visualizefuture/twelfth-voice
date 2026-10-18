# Twelfth Voice

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
| **Render** | Insights include a machine-readable overlay hint (template, placement, duration, priority) for synchronised graphics. | `OverlayHint` → Angular overlay *(in progress)* |
| **Personalise** | Casual vs analyst modes, follow a club, player-focus mode; multi-language via AI agents. | CLI preview now; agents + Angular *(in progress)* |

## Quick start

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download) (or later).

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
tests/
  TwelfthVoice.Tests         xUnit tests for simulator realism, stats and insights
docs/
  ARCHITECTURE.md            Target architecture and build plan
  EVENT-SCHEMA.md            Event and insight formats for overlay renderers
```

## Roadmap

- [x] Synthetic match generator and live feed
- [x] Stats, trends and rule-based explanations
- [ ] ASP.NET Core API + SignalR live channel, Azure SQL persistence
- [ ] Multi-agent layer (Microsoft Agent Framework + Microsoft Foundry): Analyst, Explainer, Storyteller, Localizer, Director
- [ ] MCP server exposing match state as tools
- [ ] Angular broadcast overlay and personalised viewer modes
- [ ] Deployment to Azure Container Apps

## License

MIT — see [LICENSE](LICENSE).
