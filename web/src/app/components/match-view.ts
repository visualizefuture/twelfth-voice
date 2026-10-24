import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { LiveService } from '../live.service';
import { AskPanelComponent } from './ask-panel';
import { CrewRoomComponent } from './crew-room';
import { InsightFeedComponent } from './insight-feed';
import { MatchSummaryComponent } from './match-summary';
import { PlayerCompareComponent } from './player-compare';
import { MomentumChartComponent } from './momentum-chart';
import { PitchViewComponent } from './pitch-view';
import { PlayerCardComponent } from './player-card';
import { ScorebugComponent } from './scorebug';
import { StatsPanelComponent } from './stats-panel';
import { StoryPanelComponent } from './story-panel';
import { ViewerSettingsComponent } from './viewer-settings';

@Component({
  selector: 'tv-match-view',
  imports: [
    ScorebugComponent, PitchViewComponent, MomentumChartComponent, InsightFeedComponent, AskPanelComponent,
    StoryPanelComponent, StatsPanelComponent, PlayerCardComponent, ViewerSettingsComponent, CrewRoomComponent,
    PlayerCompareComponent, MatchSummaryComponent
  ],
  template: `
    <tv-scorebug (openSettings)="settingsOpen.set(true)" />

    @if (live.connectionState() === 'reconnecting') {
      <p class="banner" role="status">Connection dropped. Reconnecting to the match…</p>
    }

    <main class="match">
      <div class="stage">
        <tv-pitch-view />
        <tv-momentum-chart />
      </div>
      <!-- beside the pitch: ask the match and the latest moments, then teams, players or the full-time summary -->
      <div class="side">
        <tv-ask-panel class="ask" />
        <tv-insight-feed class="block feed" [limit]="2" />

        <div class="tabs" role="tablist" aria-label="Match details">
          @for (t of tabs(); track t.key) {
            <button type="button" role="tab" [attr.aria-selected]="tab() === t.key" (click)="tab.set(t.key)">{{ t.label }}</button>
          }
        </div>
        <div class="tab-body" role="tabpanel">
          @switch (tab()) {
            @case ('players') { <tv-player-compare /> }
            @case ('summary') { <tv-match-summary /> }
            @default { <tv-stats-panel /> }
          }
        </div>
      </div>
      <tv-story-panel class="panel wide" />
      <tv-player-card class="panel wide" />
      <tv-crew-room class="panel wide" />
    </main>

    @if (settingsOpen()) {
      <tv-viewer-settings (closed)="settingsOpen.set(false)" />
    }
  `,
  styles: `
    .match {
      display: grid; grid-template-columns: minmax(0, 2fr) minmax(300px, 1fr); gap: 24px 28px;
      padding: 20px; max-width: 1480px; margin: 0 auto;
    }
    .stage { display: grid; gap: 18px; align-content: start; }
    .side { display: flex; flex-direction: column; gap: 18px; min-height: 0; }
    .ask { display: block; border-bottom: 1px solid var(--hairline); padding-bottom: 16px; }
    .block { display: block; border-bottom: 1px solid var(--hairline); padding-bottom: 14px; }
    /* fixed height so the tabs below never jump; the newest moment is always at the top, older ones scroll */
    .feed { height: 250px; overflow: hidden; }
    .tabs { display: flex; gap: 6px; margin-bottom: -6px; }
    .tabs button { flex: 1; font-family: var(--display); font-size: var(--step-0); padding: 6px 10px; }
    .tabs button[aria-selected="true"] { border-color: var(--chalk); background: var(--turf); }
    .tabs button.done { border-color: var(--floodlight); }
    .tab-body { min-height: 300px; }
    .panel { display: block; border-top: 1px solid var(--hairline); padding-top: 16px; }
    .wide { grid-column: 1 / -1; }
    .banner { margin: 0; padding: 8px 20px; background: #3a2a00; color: var(--floodlight); }
    @media (max-width: 960px) {
      .match { grid-template-columns: minmax(0, 1fr); }
    }
  `
})
export class MatchViewComponent {
  readonly live = inject(LiveService);
  readonly settingsOpen = signal(false);

  readonly finished = computed(() => this.live.summary()?.status === 'Finished');
  readonly tab = signal<'teams' | 'players' | 'summary'>('teams');
  readonly tabs = computed(() => [
    { key: 'teams' as const, label: 'Teams' },
    { key: 'players' as const, label: 'Players' },
    ...(this.finished() ? [{ key: 'summary' as const, label: 'Full time' }] : [])
  ]);

  constructor() {
    // At the final whistle, open the summary; a new match starts back on Teams.
    effect(() => {
      const finished = this.finished();
      untracked(() => {
        if (finished) this.tab.set('summary');
        else if (this.tab() === 'summary') this.tab.set('teams');
      });
    });
  }
}
