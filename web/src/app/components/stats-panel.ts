import { Component, computed, inject } from '@angular/core';
import { LiveService } from '../live.service';

interface StatRow { label: string; home: string; away: string; homeShare: number; }

/** Head-to-head team numbers, with a split bar for each row. */
@Component({
  selector: 'tv-stats-panel',
  template: `
    <section aria-label="Team statistics">
      <h2 class="sr">Match stats</h2>
      @if (game(); as g) {
        <p class="read muted">
          {{ g.controlLabel }} game, {{ g.tempo.toLowerCase() }}.
          @if (controller(); as c) { {{ c }} are dictating play. }
        </p>
      }
      <dl>
        @for (row of rows(); track row.label) {
          <div class="row">
            <dd class="num">{{ row.home }}</dd>
            <dt>{{ row.label }}</dt>
            <dd class="num right">{{ row.away }}</dd>
            <div class="split" aria-hidden="true">
              <i class="home" [style.width.%]="row.homeShare * 100"></i>
              <i class="away" [style.width.%]="(1 - row.homeShare) * 100"></i>
            </div>
          </div>
        }
      </dl>
    </section>
  `,
  styles: `
    .sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
    .read { margin: 0 0 12px; font-size: var(--step--1); }
    dl { margin: 0; display: grid; gap: 10px; }
    .row { display: grid; grid-template-columns: 4.5em 1fr 4.5em; align-items: baseline; }
    dt { text-align: center; color: var(--moss); font-size: var(--step--1); }
    dd { margin: 0; }
    .num { font-family: var(--display); font-size: var(--step-1); font-variant-numeric: tabular-nums; }
    .right { text-align: right; }
    .split { grid-column: 1 / -1; display: flex; gap: 2px; height: 4px; margin-top: 3px; }
    .split i { display: block; height: 100%; transition: width 0.4s; }
    .split .home { background: var(--home); }
    .split .away { background: var(--away); }
  `
})
export class StatsPanelComponent {
  private readonly live = inject(LiveService);

  readonly game = computed(() => this.live.state()?.game ?? null);

  readonly controller = computed(() => {
    const id = this.live.state()?.game.controllingTeamId;
    const info = this.live.info();
    if (!id || !info) return null;
    return id === info.home.id ? info.home.shortName : info.away.shortName;
  });

  readonly rows = computed<StatRow[]>(() => {
    const s = this.live.state();
    if (!s) return [];
    const h = s.home;
    const a = s.away;
    const share = (x: number, y: number) => (x + y === 0 ? 0.5 : x / (x + y));
    const pct = (n: number) => `${Math.round(n * 100)}%`;
    return [
      { label: 'Possession', home: `${Math.round(h.possessionPct)}%`, away: `${Math.round(a.possessionPct)}%`, homeShare: share(h.possessionPct, a.possessionPct) },
      { label: 'Expected goals', home: h.xg.toFixed(2), away: a.xg.toFixed(2), homeShare: share(h.xg, a.xg) },
      { label: 'Shots', home: `${h.shots}`, away: `${a.shots}`, homeShare: share(h.shots, a.shots) },
      { label: 'On target', home: `${h.shotsOnTarget}`, away: `${a.shotsOnTarget}`, homeShare: share(h.shotsOnTarget, a.shotsOnTarget) },
      { label: 'Pass accuracy', home: pct(h.passAccuracy), away: pct(a.passAccuracy), homeShare: share(h.passAccuracy, a.passAccuracy) },
      { label: 'Pressures', home: `${h.pressures}`, away: `${a.pressures}`, homeShare: share(h.pressures, a.pressures) },
      { label: 'Corners', home: `${h.corners}`, away: `${a.corners}`, homeShare: share(h.corners, a.corners) },
      { label: 'Fouls', home: `${h.fouls}`, away: `${a.fouls}`, homeShare: share(h.fouls, a.fouls) },
      { label: 'Distance (km)', home: h.distanceKm.toFixed(1), away: a.distanceKm.toFixed(1), homeShare: share(h.distanceKm, a.distanceKm) }
    ];
  });
}
