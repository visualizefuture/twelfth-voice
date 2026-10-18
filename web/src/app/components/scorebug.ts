import { Component, computed, inject, output } from '@angular/core';
import { LiveService } from '../live.service';

/** Broadcast-style score bug with playback controls. */
@Component({
  selector: 'tv-scorebug',
  template: `
    @if (live.info(); as info) {
      <header class="bug">
        <button class="back" type="button" (click)="live.leave()" aria-label="Back to fixtures">‹</button>

        <div class="score display" aria-live="polite">
          <span class="team home"><i></i>{{ info.home.shortName }}</span>
          <span class="goals">{{ homeScore() }}<span class="dash">–</span>{{ awayScore() }}</span>
          <span class="team away">{{ info.away.shortName }}<i></i></span>
          <span class="clock">{{ clock() }}</span>
        </div>

        <div class="meta muted">
          <span>{{ info.venue }}</span>
          <span>{{ statusText() }}</span>
        </div>

        <div class="controls">
          @if (live.summary()?.status === 'Paused') {
            <button type="button" (click)="live.resume()">Resume</button>
          } @else if (live.summary()?.status === 'Live') {
            <button type="button" (click)="live.pause()">Pause</button>
          }
          <label class="speed">
            <span class="sr">Speed</span>
            <select (change)="onSpeed($event)">
              @for (s of speeds; track s) {
                <option [value]="s" [selected]="speedValue() === s">{{ s }}×</option>
              }
            </select>
          </label>
          <button type="button" class="primary" (click)="openSettings.emit()">Your view</button>
        </div>
      </header>
    }
  `,
  styles: `
    .bug {
      display: grid; grid-template-columns: auto auto 1fr auto; gap: 18px; align-items: center;
      padding: 12px 20px; border-bottom: 1px solid var(--hairline); background: var(--night-2);
    }
    .back { font-size: var(--step-2); line-height: 1; padding: 0 10px 4px; border: 0; }
    .score { display: flex; align-items: stretch; font-size: var(--step-2); line-height: 1; }
    .team { display: flex; align-items: center; gap: 8px; padding: 6px 12px; background: var(--turf); }
    .team i { width: 6px; align-self: stretch; }
    .home i { background: var(--home); }
    .away i { background: var(--away); }
    .goals { padding: 6px 14px; background: var(--chalk); color: var(--night); font-weight: 700; font-variant-numeric: tabular-nums; }
    .dash { padding: 0 4px; }
    .clock { padding: 6px 12px; color: var(--floodlight); font-variant-numeric: tabular-nums; min-width: 4.5em; }
    .meta { display: flex; gap: 14px; font-size: var(--step--1); flex-wrap: wrap; }
    .controls { display: flex; gap: 10px; align-items: center; }
    .speed select { width: auto; }
    .sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
    @media (max-width: 900px) {
      .bug { grid-template-columns: auto 1fr; }
      .meta { display: none; }
      .controls { grid-column: 1 / -1; }
    }
  `
})
export class ScorebugComponent {
  readonly live = inject(LiveService);
  readonly openSettings = output<void>();
  readonly speeds = [1, 30, 60, 120, 300, 600];

  readonly homeScore = computed(() => this.live.state()?.homeScore ?? this.live.summary()?.homeScore ?? 0);
  readonly awayScore = computed(() => this.live.state()?.awayScore ?? this.live.summary()?.awayScore ?? 0);
  readonly clock = computed(() => this.live.lastAction()?.clock ?? this.live.state()?.clock ?? '00:00');
  readonly speedValue = computed(() => this.live.summary()?.speed ?? 60);

  readonly statusText = computed(() => {
    const summary = this.live.summary();
    if (!summary) return '';
    const watching = summary.viewers === 1 ? '1 watching' : `${summary.viewers} watching`;
    switch (summary.status) {
      case 'Finished': return 'Full time';
      case 'Paused': return `Paused, ${watching}`;
      case 'Ready': return 'About to kick off';
      default: return summary.period === 1 ? `First half, ${watching}` : `Second half, ${watching}`;
    }
  });

  onSpeed(event: Event): void {
    const value = Number((event.target as HTMLSelectElement).value);
    void this.live.setSpeed(value);
  }
}
