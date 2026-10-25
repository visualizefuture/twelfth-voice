import { Component, computed, inject } from '@angular/core';
import { LiveService } from '../live.service';
import { CrestComponent } from './crest';

interface StatRow { label: string; home: string; away: string; homeShare: number; }

/** Head-to-head team numbers, with a split bar for each row. */
@Component({
  selector: 'tv-stats-panel',
  imports: [CrestComponent],
  template: `
    <section aria-label="Team statistics">
      <h2 class="sr">Match stats</h2>
      @if (live.info(); as info) {
        <div class="support" role="group" aria-label="Support a club">
          <span>Support</span>
          <button type="button" [attr.aria-pressed]="favourite() === info.home.id" (click)="support(info.home.id)">
            <tv-crest [team]="info.home" [size]="18" />{{ info.home.shortName }}
          </button>
          <button type="button" [attr.aria-pressed]="!favourite()" (click)="support(null)">Neutral</button>
          <button type="button" [attr.aria-pressed]="favourite() === info.away.id" (click)="support(info.away.id)">
            <tv-crest [team]="info.away" [size]="18" />{{ info.away.shortName }}
          </button>
        </div>
      }
      @if (game(); as g) {
        <p class="read muted">
          {{ g.controlLabel }} game, {{ g.tempo.toLowerCase() }}.
          @if (controller(); as c) { {{ c }} are dictating play. }
        </p>
      }
      @if (visuals(); as v) {
        <div class="duo">
          <!-- possession: one ring split in the club colours -->
          <figure class="ring" role="img" [attr.aria-label]="'Possession ' + v.homeShort + ' ' + v.possHome + ' percent, ' + v.awayShort + ' ' + v.possAway + ' percent'">
            <svg viewBox="0 0 120 120" aria-hidden="true">
              <circle cx="60" cy="60" r="46" class="track away-stroke" pathLength="100" />
              <circle cx="60" cy="60" r="46" class="arc home-stroke" pathLength="100"
                      [attr.stroke-dasharray]="v.possHome + ' 100'" transform="rotate(-90 60 60)" />
              <text x="60" y="58" class="big home-fill">{{ v.possHome }}%</text>
              <text x="60" y="78" class="small away-fill">{{ v.possAway }}%</text>
            </svg>
            <figcaption>Possession</figcaption>
          </figure>

          <!-- pass accuracy: a gauge per club -->
          <figure class="gauges" role="img" [attr.aria-label]="'Pass accuracy ' + v.homeShort + ' ' + v.passHome + ' percent, ' + v.awayShort + ' ' + v.passAway + ' percent'">
            <div class="pair" aria-hidden="true">
              @for (g of [{ pct: v.passHome, side: 'home', name: v.homeShort }, { pct: v.passAway, side: 'away', name: v.awayShort }]; track g.side) {
                <svg viewBox="0 0 100 62">
                  <path d="M10 54 A40 40 0 0 1 90 54" class="track" pathLength="100" />
                  <path d="M10 54 A40 40 0 0 1 90 54" [class]="'arc ' + g.side + '-stroke'" pathLength="100" [attr.stroke-dasharray]="g.pct + ' 100'" />
                  <text x="50" y="50" [class]="'mid ' + g.side + '-fill'">{{ g.pct }}%</text>
                  <text x="50" y="61" class="who">{{ g.name }}</text>
                </svg>
              }
            </div>
            <figcaption>Pass accuracy</figcaption>
          </figure>
        </div>

        <!-- the xG race: chances created, building up through the match, goals marked -->
        <figure class="race" role="img" [attr.aria-label]="'Expected goals so far: ' + v.homeShort + ' ' + v.xgHome + ', ' + v.awayShort + ' ' + v.xgAway">
          <figcaption>Expected goals race <span class="muted">who is creating the better chances</span></figcaption>
          <svg viewBox="0 0 320 130" aria-hidden="true">
            @for (g of v.race.grid; track g.y) {
              <line [attr.x1]="v.race.left" [attr.y1]="g.y" [attr.x2]="v.race.right" [attr.y2]="g.y" class="grid" />
              <text x="20" [attr.y]="g.y + 3" class="axis end">{{ g.label }}</text>
            }
            <line [attr.x1]="v.race.half" y1="8" [attr.x2]="v.race.half" y2="112" class="half" />
            <text [attr.x]="v.race.left" y="124" class="axis">0'</text>
            <text [attr.x]="v.race.half" y="124" class="axis mid">45'</text>
            <text [attr.x]="v.race.full" y="124" class="axis mid">90'</text>
            <polyline [attr.points]="v.race.away" class="line away-stroke" />
            <polyline [attr.points]="v.race.home" class="line home-stroke" />
            @for (gm of v.race.goals; track $index) {
              <circle [attr.cx]="gm.x" [attr.cy]="gm.y" r="4" [class]="'goal ' + (gm.home ? 'home-fill' : 'away-fill')" />
            }
            <text [attr.x]="v.race.right + 8" [attr.y]="v.race.homeY + 4" class="tag home-fill">{{ v.homeCode }} {{ v.xgHome }}</text>
            <text [attr.x]="v.race.right + 8" [attr.y]="v.race.awayY + 4" class="tag away-fill">{{ v.awayCode }} {{ v.xgAway }}</text>
          </svg>
        </figure>

        <!-- shots: on target and off target for each club -->
        <figure class="shots" role="img" [attr.aria-label]="'Shots: ' + v.homeShort + ' ' + v.shotsHome + ' with ' + v.onHome + ' on target, ' + v.awayShort + ' ' + v.shotsAway + ' with ' + v.onAway + ' on target'">
          <figcaption>Shots <span class="muted">solid = on target</span></figcaption>
          @for (b of [{ side: 'home', name: v.homeShort, total: v.shotsHome, on: v.onHome }, { side: 'away', name: v.awayShort, total: v.shotsAway, on: v.onAway }]; track b.side) {
            <div class="shot-row" aria-hidden="true">
              <span class="who">{{ b.name }}</span>
              <span class="bar">
                <i [class]="'on ' + b.side + '-bg'" [style.width.%]="b.on / v.shotsMax * 100"></i>
                <i [class]="'off ' + b.side + '-line'" [style.width.%]="(b.total - b.on) / v.shotsMax * 100"></i>
              </span>
              <span class="count">{{ b.total }} <small>({{ b.on }})</small></span>
            </div>
          }
        </figure>
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
    .support { display: flex; gap: 6px; align-items: center; margin-bottom: 12px; flex-wrap: wrap; }
    .support span { font-size: var(--step--1); color: var(--moss); margin-right: 2px; }
    .support button { display: inline-flex; align-items: center; gap: 6px; font-size: var(--step--1); padding: 4px 10px; }
    .support button[aria-pressed="true"] { border-color: var(--floodlight); background: var(--turf); }
    figure { margin: 0; }
    figcaption { font-size: var(--step--1); color: var(--moss); }
    figcaption .muted { margin-left: 6px; opacity: 0.8; }
    .home-stroke { stroke: var(--home); } .away-stroke { stroke: var(--away); }
    .home-fill { fill: var(--home); } .away-fill { fill: var(--away); }
    .home-bg { background: var(--home); } .away-bg { background: var(--away); }
    .home-line { border: 1.5px solid var(--home); } .away-line { border: 1.5px solid var(--away); }
    svg text { font-family: var(--display); text-anchor: middle; }
    .track { fill: none; stroke-width: 12; stroke: var(--hairline); }
    .ring .track { stroke-width: 14; opacity: 0.9; }
    .arc { fill: none; stroke-width: 14; transition: stroke-dasharray 0.6s; }
    .gauges .arc { stroke-width: 12; stroke-linecap: round; }
    .duo { display: grid; grid-template-columns: 1fr 1.5fr; gap: 14px; align-items: center; margin-bottom: 14px; }
    .ring, .gauges { display: grid; gap: 4px; justify-items: center; }
    .ring svg { width: 100%; max-width: 132px; }
    .big { font-size: 24px; font-weight: 700; }
    .small { font-size: 14px; font-weight: 600; }
    .pair { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; width: 100%; }
    .mid { font-size: 18px; font-weight: 700; }
    .who { fill: var(--moss); font-size: 10px; }
    .race { margin-bottom: 14px; }
    .race svg { width: 100%; display: block; }
    .grid { stroke: var(--hairline); stroke-width: 1; }
    .half { stroke: var(--hairline); stroke-dasharray: 3 3; }
    .axis { fill: var(--moss); font-size: 10px; text-anchor: start; }
    .axis.mid { text-anchor: middle; } .axis.end { text-anchor: end; }
    .line { fill: none; stroke-width: 2.5; stroke-linejoin: round; }
    .goal { stroke: var(--night); stroke-width: 1.5; }
    .tag { font-size: 11px; font-weight: 700; text-anchor: start; }
    .shots { display: grid; gap: 6px; margin-bottom: 14px; }
    .shot-row { display: grid; grid-template-columns: 6.5em 1fr 3.6em; gap: 8px; align-items: center; }
    .shot-row .who { font-size: var(--step--1); color: var(--chalk); }
    .bar { display: flex; height: 12px; gap: 2px; }
    .bar i { display: block; height: 100%; box-sizing: border-box; border-radius: 2px; transition: width 0.4s; }
    .count { font-family: var(--display); font-size: var(--step-0); text-align: right; font-variant-numeric: tabular-nums; }
    .count small { color: var(--moss); font-size: 0.8em; }
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
  readonly live = inject(LiveService);
  readonly favourite = computed(() => this.live.profile().favoriteTeamId);

  /** Your club's moments show up more and are highlighted; the crew writes from your side. */
  support(teamId: string | null): void {
    void this.live.updateProfile({ favoriteTeamId: teamId });
  }

  readonly game = computed(() => this.live.state()?.game ?? null);

  readonly controller = computed(() => {
    const id = this.live.state()?.game.controllingTeamId;
    const info = this.live.info();
    if (!id || !info) return null;
    return id === info.home.id ? info.home.shortName : info.away.shortName;
  });

  /** Everything the drawn stats need: possession ring, passing gauges, the xG race and shots. */
  readonly visuals = computed(() => {
    const s = this.live.state();
    const info = this.live.info();
    if (!s || !info) return null;
    const h = s.home;
    const a = s.away;
    const possHome = Math.round(h.possessionPct);
    return {
      homeShort: info.home.shortName,
      homeCode: info.home.id,
      awayCode: info.away.id,
      awayShort: info.away.shortName,
      possHome,
      possAway: 100 - possHome,
      passHome: Math.round(h.passAccuracy * 100),
      passAway: Math.round(a.passAccuracy * 100),
      xgHome: h.xg.toFixed(2),
      xgAway: a.xg.toFixed(2),
      shotsHome: h.shots, shotsAway: a.shots,
      onHome: h.shotsOnTarget, onAway: a.shotsOnTarget,
      shotsMax: Math.max(1, h.shots, a.shots),
      race: this.race(h.xg, a.xg)
    };
  });

  /** Cumulative xG for each club as step lines over the match clock, with the goals on the scorer's line. */
  private race(homeXg: number, awayXg: number) {
    const info = this.live.info()!;
    const points = this.live.timeline();
    const left = 24, right = 262, top = 8, bottom = 112; // room on the right for the end labels
    const lastSecond = points.length ? points[points.length - 1].matchSecond : 0;
    const span = Math.max(5400, lastSecond + 60);
    const top_ = Math.max(1, homeXg, awayXg, ...points.map(p => Math.max(p.homeXg, p.awayXg)));
    const yMax = Math.ceil(top_ * 2) / 2 + (top_ % 0.5 === 0 ? 0.5 : 0);
    const x = (sec: number) => left + (Math.min(sec, span) / span) * (right - left);
    const y = (xg: number) => bottom - (xg / yMax) * (bottom - top);

    const series = [{ matchSecond: 0, homeXg: 0, awayXg: 0 }, ...points];
    const step = (pick: (p: { homeXg: number; awayXg: number }) => number) => {
      const out: string[] = [];
      series.forEach((p, i) => {
        if (i > 0) out.push(`${x(p.matchSecond).toFixed(1)},${y(pick(series[i - 1])).toFixed(1)}`);
        out.push(`${x(p.matchSecond).toFixed(1)},${y(pick(p)).toFixed(1)}`);
      });
      return out.join(' ');
    };
    const xgAt = (sec: number, home: boolean) => {
      let v = 0;
      for (const p of series) { if (p.matchSecond <= sec + 30) v = home ? p.homeXg : p.awayXg; else break; }
      return v;
    };
    const goals = this.live.events().filter(e => e.type === 'Goal').map(e => {
      const home = e.teamId === info.home.id;
      return { x: x(e.matchSecond), y: y(xgAt(e.matchSecond, home)), home };
    });

    let homeY = y(homeXg), awayY = y(awayXg);
    if (Math.abs(homeY - awayY) < 12) { // keep the end labels apart
      const midY = (homeY + awayY) / 2;
      homeY = homeXg >= awayXg ? midY - 6 : midY + 6;
      awayY = homeXg >= awayXg ? midY + 6 : midY - 6;
    }
    const grid = [] as { y: number; label: string }[];
    const stepSize = yMax > 3 ? 1 : 0.5;
    for (let g = 0; g <= yMax + 0.001; g += stepSize) grid.push({ y: y(g), label: g.toFixed(stepSize < 1 ? 1 : 0) });

    return { home: step(p => p.homeXg), away: step(p => p.awayXg), goals, grid, endX: x(lastSecond), homeY, awayY, half: x(2700), full: x(5400), left, right };
  }

  readonly rows = computed<StatRow[]>(() => {
    const s = this.live.state();
    if (!s) return [];
    const h = s.home;
    const a = s.away;
    const share = (x: number, y: number) => (x + y === 0 ? 0.5 : x / (x + y));
    return [
      { label: 'Pressures', home: `${h.pressures}`, away: `${a.pressures}`, homeShare: share(h.pressures, a.pressures) },
      { label: 'Corners', home: `${h.corners}`, away: `${a.corners}`, homeShare: share(h.corners, a.corners) },
      { label: 'Fouls', home: `${h.fouls}`, away: `${a.fouls}`, homeShare: share(h.fouls, a.fouls) },
      { label: 'Distance (km)', home: h.distanceKm.toFixed(1), away: a.distanceKm.toFixed(1), homeShare: share(h.distanceKm, a.distanceKm) }
    ];
  });
}
