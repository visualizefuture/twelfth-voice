import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { ApiService } from '../api.service';
import { kitFor } from '../kits';
import { LiveService } from '../live.service';
import { Kit, PlayerStats } from '../models';
import { PortraitComponent } from './portrait';

const PICKS_KEY = 'twelfth-voice.compare';
const REFRESH_MS = 3000;

interface Row { label: string; a: string; b: string; aShare: number; }
type Preset = 'captains' | 'rated' | 'scorers' | 'keepers';

/**
 * Two players side by side, live: start with the captains, the best rated, the top scorers or the
 * goalkeepers, or pick any two (even from the same club). Picks are remembered in this browser.
 */
@Component({
  selector: 'tv-player-compare',
  imports: [PortraitComponent],
  template: `
    <section aria-label="Compare players">
      <div class="presets" role="group" aria-label="Quick picks">
        @for (p of presets; track p.key) {
          <button type="button" (click)="applyPreset(p.key)">{{ p.label }}</button>
        }
      </div>

      <div class="heads">
        @for (side of sides(); track side.key) {
          <div class="head" [class.b]="side.key === 'b'">
            @if (side.ref; as r) {
              <tv-portrait [playerLook]="r.player.look" [playerKit]="side.kit" [number]="r.player.shirtNumber" [size]="58"
                           [captain]="!!r.player.isCaptain" [label]="r.player.name" />
            }
            <label>
              <span class="sr">{{ side.key === 'a' ? 'First player' : 'Second player' }}</span>
              <select (change)="pick(side.key, $event)">
                @if (live.info(); as info) {
                  @for (team of [info.home, info.away]; track team.id) {
                    <optgroup [label]="team.name">
                      @for (pl of team.squad; track pl.id) {
                        <option [value]="pl.id" [selected]="side.id === pl.id">{{ pl.shirtNumber }}. {{ pl.name }}{{ pl.isCaptain ? ' (C)' : '' }}</option>
                      }
                    </optgroup>
                  }
                }
              </select>
            </label>
            @if (side.stats; as s) {
              <span class="rating display" [attr.aria-label]="'Rating ' + s.rating.toFixed(1)">{{ s.rating.toFixed(1) }}</span>
            }
          </div>
        }
      </div>

      @if (rows().length > 0) {
        <dl>
          @for (row of rows(); track row.label) {
            <div class="row">
              <dd class="num">{{ row.a }}</dd>
              <dt>{{ row.label }}</dt>
              <dd class="num right">{{ row.b }}</dd>
              <div class="split" aria-hidden="true">
                <i class="a" [style.width.%]="row.aShare * 100" [style.background]="colourA()"></i>
                <i class="b" [style.width.%]="(1 - row.aShare) * 100" [style.background]="colourB()"></i>
              </div>
            </div>
          }
        </dl>
      } @else {
        <p class="muted">Stats appear once the match is under way.</p>
      }
    </section>
  `,
  styles: `
    .presets { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: 12px; }
    .presets button { font-size: var(--step--1); padding: 4px 10px; }
    .heads { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-bottom: 14px; }
    .head { display: grid; grid-template-columns: auto 1fr; grid-template-rows: auto auto; gap: 4px 10px; align-items: center; }
    .head tv-portrait { grid-row: 1 / span 2; }
    .head.b { grid-template-columns: 1fr auto; text-align: right; }
    .head.b tv-portrait { grid-column: 2; grid-row: 1 / span 2; }
    .head.b label, .head.b .rating { grid-column: 1; }
    .head select { width: 100%; font-size: var(--step--1); padding: 4px 6px; }
    .rating { font-size: var(--step-2); color: var(--floodlight); line-height: 1; }
    dl { margin: 0; display: grid; gap: 9px; }
    .row { display: grid; grid-template-columns: 4.5em 1fr 4.5em; align-items: baseline; }
    dt { text-align: center; color: var(--moss); font-size: var(--step--1); }
    dd { margin: 0; }
    .num { font-family: var(--display); font-size: var(--step-1); font-variant-numeric: tabular-nums; }
    .right { text-align: right; }
    .split { grid-column: 1 / -1; display: flex; gap: 2px; height: 4px; margin-top: 3px; }
    .split i { display: block; height: 100%; transition: width 0.4s; }
    .sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
  `
})
export class PlayerCompareComponent {
  readonly live = inject(LiveService);
  private readonly api = inject(ApiService);

  readonly presets: { key: Preset; label: string }[] = [
    { key: 'captains', label: 'Captains' },
    { key: 'rated', label: 'Top rated' },
    { key: 'scorers', label: 'Top scorers' },
    { key: 'keepers', label: 'Goalkeepers' }
  ];

  private readonly stats = signal<Map<string, PlayerStats>>(new Map());
  readonly picks = signal<{ a: string | null; b: string | null }>(loadPicks());
  private loading = false;
  private lastLoad = 0;

  constructor() {
    // Refresh everyone's numbers as the match moves on (at most every few seconds).
    effect(() => {
      const state = this.live.state();
      const matchId = this.live.matchId();
      if (!state || !matchId) return;
      untracked(() => void this.refresh(matchId));
    });
    const timer = setInterval(() => {
      const id = this.live.matchId();
      if (id && this.live.summary()?.status === 'Live') void this.refresh(id);
    }, REFRESH_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  /** The two players shown, defaulting to the two captains. */
  private readonly ids = computed(() => {
    const info = this.live.info();
    const p = this.picks();
    const players = this.live.players();
    const valid = (id: string | null) => (id && players.has(id) ? id : null);
    const captain = (squad: { id: string; isCaptain?: boolean }[] | undefined) => squad?.find(x => x.isCaptain)?.id ?? squad?.[0]?.id ?? null;
    return {
      a: valid(p.a) ?? captain(info?.home.squad),
      b: valid(p.b) ?? captain(info?.away.squad)
    };
  });

  readonly sides = computed(() => {
    const ids = this.ids();
    const players = this.live.players();
    const stats = this.stats();
    return (['a', 'b'] as const).map(key => {
      const id = ids[key];
      const ref = id ? players.get(id) ?? null : null;
      const kit: Kit | undefined = ref ? kitFor(ref.team, ref.player) : undefined;
      return { key, id, ref, kit, stats: id ? stats.get(id) ?? null : null };
    });
  });

  readonly colourA = computed(() => this.teamColour(this.sides()[0].ref?.team.id));
  readonly colourB = computed(() => {
    const [a, b] = this.sides();
    return a.ref?.team.id === b.ref?.team.id ? 'var(--floodlight)' : this.teamColour(b.ref?.team.id);
  });

  readonly rows = computed<Row[]>(() => {
    const [sa, sb] = this.sides();
    const a = sa.stats;
    const b = sb.stats;
    if (!a || !b) return [];
    const share = (x: number, y: number) => (x + y === 0 ? 0.5 : x / (x + y));
    const row = (label: string, x: number, y: number, fmt: (n: number) => string = n => `${n}`): Row =>
      ({ label, a: fmt(x), b: fmt(y), aShare: share(x, y) });
    const km = (n: number) => (n / 1000).toFixed(1);
    return [
      row('Rating', a.rating, b.rating, n => n.toFixed(1)),
      row('Goals', a.goals, b.goals),
      row('Assists', a.assists, b.assists),
      row('Expected goals', a.xg, b.xg, n => n.toFixed(2)),
      { label: 'Shots (on target)', a: `${a.shots} (${a.shotsOnTarget})`, b: `${b.shots} (${b.shotsOnTarget})`, aShare: share(a.shots, b.shots) },
      row('Key passes', a.keyPasses, b.keyPasses),
      { label: 'Passes (accuracy)', a: `${a.passesCompleted} (${Math.round(a.passAccuracy * 100)}%)`, b: `${b.passesCompleted} (${Math.round(b.passAccuracy * 100)}%)`, aShare: share(a.passesCompleted, b.passesCompleted) },
      row('Progressive passes', a.progressivePasses, b.progressivePasses),
      row('Tackles + interceptions', a.tackles + a.interceptions, b.tackles + b.interceptions),
      row('Dribbles won', a.dribblesCompleted, b.dribblesCompleted),
      row('Distance (km)', a.distanceM, b.distanceM, km),
      // Top speed is only measured on sprints; a player who hasn't sprinted shows a dash, not 0.
      row('Top speed (km/h)', a.topSpeedKmh, b.topSpeedKmh, n => (n > 0 ? n.toFixed(1) : '–'))
    ];
  });

  pick(side: 'a' | 'b', event: Event): void {
    const id = (event.target as HTMLSelectElement).value;
    this.setPicks({ ...this.ids(), [side]: id });
  }

  applyPreset(preset: Preset): void {
    const info = this.live.info();
    if (!info) return;
    const stats = this.stats();
    const best = (teamId: string, score: (s: PlayerStats) => number) =>
      [...stats.values()].filter(s => s.teamId === teamId).sort((x, y) => score(y) - score(x))[0]?.playerId ?? null;
    const role = (squad: { id: string; position: string; isCaptain?: boolean }[], test: (p: { position: string; isCaptain?: boolean }) => boolean) =>
      squad.find(test)?.id ?? null;
    let a: string | null = null;
    let b: string | null = null;
    switch (preset) {
      case 'captains':
        a = role(info.home.squad, p => !!p.isCaptain); b = role(info.away.squad, p => !!p.isCaptain); break;
      case 'keepers':
        a = role(info.home.squad, p => /^GK|goalkeeper/i.test(p.position)); b = role(info.away.squad, p => /^GK|goalkeeper/i.test(p.position)); break;
      case 'rated':
        a = best(info.home.id, s => s.rating); b = best(info.away.id, s => s.rating); break;
      case 'scorers':
        a = best(info.home.id, s => s.goals * 10 + s.xg); b = best(info.away.id, s => s.goals * 10 + s.xg); break;
    }
    this.setPicks({ a: a ?? this.ids().a, b: b ?? this.ids().b });
  }

  private setPicks(picks: { a: string | null; b: string | null }): void {
    this.picks.set(picks);
    try {
      localStorage.setItem(PICKS_KEY, JSON.stringify(picks));
    } catch {
      // storage unavailable
    }
  }

  private teamColour(teamId: string | undefined): string {
    return teamId && teamId === this.live.info()?.away.id ? 'var(--away)' : 'var(--home)';
  }

  private async refresh(matchId: string): Promise<void> {
    if (this.loading || Date.now() - this.lastLoad < REFRESH_MS - 200) return;
    this.loading = true;
    try {
      const snapshot = await this.api.snapshot(matchId);
      this.lastLoad = Date.now();
      if (snapshot.matchId === this.live.matchId()) {
        this.stats.set(new Map(snapshot.players.map(p => [p.playerId, p])));
      }
    } catch {
      // keep the last numbers
    } finally {
      this.loading = false;
    }
  }
}

function loadPicks(): { a: string | null; b: string | null } {
  try {
    const raw = localStorage.getItem(PICKS_KEY);
    const parsed = raw ? JSON.parse(raw) : null;
    return { a: parsed?.a ?? null, b: parsed?.b ?? null };
  } catch {
    return { a: null, b: null };
  }
}
