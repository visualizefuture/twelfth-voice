import { Component, inject, signal } from '@angular/core';
import { LiveService } from '../live.service';
import { AskPanelComponent } from './ask-panel';
import { CrewRoomComponent } from './crew-room';
import { InsightFeedComponent } from './insight-feed';
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
    StoryPanelComponent, StatsPanelComponent, PlayerCardComponent, ViewerSettingsComponent, CrewRoomComponent
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
      <!-- everything at a glance beside the pitch: ask, the numbers, the latest two moments -->
      <div class="side">
        <tv-ask-panel class="ask" />
        <tv-stats-panel class="block" />
        <tv-insight-feed class="block feed" [limit]="2" />
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
    .feed { border-bottom: 0; max-height: 70vh; overflow-y: auto; }
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
}
