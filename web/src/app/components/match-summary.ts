import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { ApiService } from '../api.service';
import { kitFor } from '../kits';
import { LiveService } from '../live.service';
import { MatchRecap } from '../models';
import { ReplayService, minute } from '../replay.service';
import { CrestComponent } from './crest';
import { PortraitComponent } from './portrait';

/**
 * Full time: the result, who scored, the player of the match, the numbers that decided it,
 * and every goal one click away as a replay.
 */
@Component({
  selector: 'tv-match-summary',
  imports: [CrestComponent, PortraitComponent],
  template: `
    <section aria-label="Match summary">
      @if (live.info(); as info) {
        <div class="result">
          <span class="club"><tv-crest [team]="info.home" [size]="34" />{{ info.home.shortName }}</span>
          <span class="score display">{{ homeScore() }}–{{ awayScore() }}</span>
          <span class="club away">{{ info.away.shortName }}<tv-crest [team]="info.away" [size]="34" /></span>
        </div>
      }

      @if (recap(); as r) {
        <h3 class="display">{{ r.headline }}</h3>
        <p class="summary">{{ r.summary }}</p>

        @if (r.playerOfTheMatch; as p) {
          <div class="potm">
            <tv-portrait [playerLook]="potmRef()?.player?.look" [playerKit]="potmKit()" [number]="p.shirtNumber" [size]="56"
                         [captain]="!!potmRef()?.player?.isCaptain" [label]="p.name" />
            <div>
              <span class="label">Player of the match</span>
              <strong>{{ p.name }}</strong>
              <span class="muted">{{ p.goals }} {{ p.goals === 1 ? 'goal' : 'goals' }}, {{ p.assists }} {{ p.assists === 1 ? 'assist' : 'assists' }}, {{ p.passesCompleted }} passes, {{ (p.distanceM / 1000).toFixed(1) }} km</span>
            </div>
            <span class="rating display">{{ p.rating.toFixed(1) }}</span>
          </div>
        }

        @if (r.statLines.length > 0) {
          <ul class="lines">
            @for (line of r.statLines; track $index) { <li>{{ line }}</li> }
          </ul>
        }
      } @else {
        <p class="muted">Putting the summary together…</p>
      }

      @if (replay.goals().length > 0) {
        <h4>Watch the goals</h4>
        <div class="goals">
          @for (g of replay.goals(); track g.sequence) {
            <button type="button" (click)="watch(g)" [class.home]="g.teamId === live.info()?.home?.id">
              ▶ @if (teamOf(g.teamId); as t) { <tv-crest [team]="t" [size]="18" /> } {{ goalLabel(g) }}
            </button>
          }
        </div>
      } @else {
        <p class="muted">No goals to replay in this one.</p>
      }

      <button type="button" class="story" (click)="openStory()">Read the full story</button>
    </section>
  `,
  styles: `
    section { display: grid; gap: 12px; }
    .result { display: flex; align-items: center; justify-content: center; gap: 14px; font-size: var(--step-1); }
    .club { display: inline-flex; align-items: center; gap: 8px; }
    .score { font-size: var(--step-3); color: var(--floodlight); font-variant-numeric: tabular-nums; }
    h3 { margin: 0; font-size: var(--step-1); line-height: 1.15; }
    .summary { margin: 0; color: var(--moss); font-size: var(--step--1); line-height: 1.5; }
    .potm { display: grid; grid-template-columns: auto 1fr auto; gap: 12px; align-items: center; padding: 10px 12px; background: var(--night-2); border-left: 3px solid var(--floodlight); }
    .potm div { display: grid; gap: 2px; }
    .potm .label { font-size: var(--step--1); color: var(--floodlight); }
    .potm .muted { font-size: var(--step--1); }
    .rating { font-size: var(--step-2); color: var(--floodlight); }
    .lines { margin: 0; padding-left: 18px; display: grid; gap: 4px; font-size: var(--step--1); }
    h4 { margin: 6px 0 0; font-family: var(--display); font-size: var(--step-0); }
    .goals { display: flex; flex-wrap: wrap; gap: 6px; }
    .goals button { display: inline-flex; align-items: center; gap: 6px; border-left: 3px solid var(--away); font-variant-numeric: tabular-nums; }
    .goals button.home { border-left-color: var(--home); }
    .story { justify-self: start; }
  `
})
export class MatchSummaryComponent {
  readonly live = inject(LiveService);
  readonly replay = inject(ReplayService);
  private readonly api = inject(ApiService);

  readonly recap = signal<MatchRecap | null>(null);

  readonly homeScore = computed(() => this.live.state()?.homeScore ?? this.live.summary()?.homeScore ?? 0);
  readonly awayScore = computed(() => this.live.state()?.awayScore ?? this.live.summary()?.awayScore ?? 0);

  readonly potmRef = computed(() => {
    const p = this.recap()?.playerOfTheMatch;
    return p ? this.live.players().get(p.playerId) ?? null : null;
  });
  readonly potmKit = computed(() => {
    const ref = this.potmRef();
    return ref ? kitFor(ref.team, ref.player) : undefined;
  });

  constructor() {
    // Load the final recap once the whistle has gone.
    effect(() => {
      const id = this.live.matchId();
      const finished = this.live.summary()?.status === 'Finished';
      if (!id || !finished) return;
      untracked(() => this.api.recap(id).then(r => this.recap.set(r)).catch(() => undefined));
    });
  }

  watch(goal: Parameters<ReplayService['play']>[0]): void {
    void this.replay.play(goal);
    document.querySelector('tv-pitch-view')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  goalLabel(goal: Parameters<ReplayService['play']>[0]): string {
    const scorer = goal.playerId ? this.live.players().get(goal.playerId)?.player.shortName : undefined;
    return `${minute(goal)} ${scorer ?? ''}`.trim();
  }

  teamOf(teamId: string | undefined) {
    const info = this.live.info();
    return !info || !teamId ? null : teamId === info.home.id ? info.home : teamId === info.away.id ? info.away : null;
  }

  openStory(): void {
    document.querySelector('tv-story-panel')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }
}
