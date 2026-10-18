# Twelfth Voice — architecture and build plan

## Target architecture

```
 ┌───────────────────┐    events     ┌────────────────────┐   stats, state,   ┌──────────────────────────┐
 │ Match Simulator   │ ────────────▶ │ Intelligence       │ ── insights ────▶ │ Agent layer               │
 │ (.NET worker)     │   live feed   │ (MatchAnalyzer)    │                   │ Microsoft Agent Framework │
 └───────────────────┘               └────────────────────┘                   │ + Microsoft Foundry models│
          │                                    │                              │                           │
          │ persist                            │ persist                      │  Analyst    (interprets)  │
          ▼                                    ▼                              │  Explainer  (why it matters)
 ┌─────────────────────────────────────────────────────────┐   MCP tools     │  Storyteller (live + recap)
 │ Azure SQL: matches, events, insights, viewer profiles,  │ ◀──────────────▶│  Localizer  (languages)   │
 │ generated narratives                                    │  (C# MCP server)│  Director   (what's on screen)
 └─────────────────────────────────────────────────────────┘                  └──────────────────────────┘
                                   │                                                     │
                                   ▼                                                     ▼
                      ┌──────────────────────────────────────────────────────────────────────────┐
                      │ ASP.NET Core API + SignalR hub: per-viewer streams (mode, club, player,   │
                      │ language) of timed, machine-readable overlay messages                    │
                      └──────────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
                      ┌──────────────────────────────────────────────────────────────────────────┐
                      │ Angular app: broadcast overlay view, casual / analyst / player-focus      │
                      │ modes, language picker, match recap page                                  │
                      └──────────────────────────────────────────────────────────────────────────┘

Hosting: Azure Container Apps (API + worker), Azure SQL, Microsoft Foundry project.
```

### Design principles

1. **Facts from code, words from AI.** Every number comes from the deterministic Intelligence layer. Agents rewrite, prioritise and translate, but never invent statistics. Each insight carries its evidence, so the agent output can be checked.
2. **Works offline.** Rule-based narration means the demo runs even without a model endpoint. AI improves it but is never a single point of failure.
3. **Machine-readable first.** Insights are JSON with overlay hints (template, placement, duration, priority), so any renderer can draw them in sync with the match clock.
4. **Repeatable.** Seeds make every match reproducible for testing, judging and the demo video.

### Agent roles (planned)

| Agent | Input | Output |
|---|---|---|
| Analyst | Snapshot + recent events (via MCP tools) | Tactical read of the current phase |
| Explainer | An insight + its evidence | Audience-specific "why it matters" (casual vs analyst) |
| Storyteller | Insight stream, recap skeleton | Live commentary lines, half-time and full-time recap |
| Localizer | Any text + target language | Natural, football-idiomatic translation |
| Director | Candidate insights + viewer profile | Which overlays to show, when, and for how long |

## Build plan (submission due 27 October 2026)

| Date | Milestone |
|---|---|
| 18–19 Oct | Simulator, data model, stats engine, CLI, tests ✅ · Azure SQL schema |
| 20–21 Oct | Agent layer on Microsoft Foundry, MCP server |
| 22–23 Oct | ASP.NET Core API + SignalR, Angular overlay and personalised modes |
| 24 Oct | Deploy to Azure Container Apps |
| 25 Oct | README, architecture diagram, screenshots, 2-minute demo video |
| 26 Oct | Submit (one day of buffer) |
