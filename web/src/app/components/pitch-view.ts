import { Component, computed, inject, signal } from '@angular/core';
import { LiveService } from '../live.service';
import { MatchEvent, PersonalizedInsight, PitchPoint } from '../models';
import { PITCH_LENGTH, PITCH_WIDTH, TOUCH_TYPES, actionLabel, ballPosition, toPercent, toScreen } from '../pitch';

type Layer = 'live' | 'heat' | 'shots';

interface HeatCell { x: number; y: number; w: number; h: number; opacity: number; }
interface ShotMark { x: number; y: number; r: number; home: boolean; result: string; label: string; }

const HEAT_COLUMNS = 21;
const HEAT_ROWS = 14;
const TRAIL_LENGTH = 6;

/**
 * The hero: a floodlit pitch showing the ball moving through each action, who has it and
 * what they did, with broadcast graphics placed where each insight's overlay hint asks.
 * Switch layers for the focus player's heat map or both teams' shot maps.
 */
@Component({
  selector: 'tv-pitch-view',
  template: `
    <div class="layers" role="tablist" aria-label="Pitch view">
      <button role="tab" type="button" [attr.aria-selected]="layer() === 'live'" (click)="layer.set('live')">Live play</button>
      <button role="tab" type="button" [attr.aria-selected]="layer() === 'heat'" (click)="layer.set('heat')">Heat map</button>
      <button role="tab" type="button" [attr.aria-selected]="layer() === 'shots'" (click)="layer.set('shots')">Shot map</button>
      <span class="direction muted">{{ homeShort() }} attack left to right</span>
    </div>

    <div class="pitch">
      <svg [attr.viewBox]="viewBox" preserveAspectRatio="none" aria-hidden="true">
        <defs>
          <filter id="heat-blur" x="-10%" y="-10%" width="120%" height="120%">
            <feGaussianBlur stdDeviation="2.4" />
          </filter>
          <marker id="arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="4" markerHeight="4" orient="auto-start-reverse">
            <path d="M0,0 L10,5 L0,10 z" class="arrow-head" />
          </marker>
        </defs>

        <!-- turf and mowing stripes -->
        <rect x="0" y="0" [attr.width]="length" [attr.height]="width" class="turf" />
        @for (stripe of stripes; track stripe) {
          <rect [attr.x]="stripe" y="0" width="5.25" [attr.height]="width" class="stripe" />
        }

        <!-- markings -->
        <g class="lines">
          <rect x="0.5" y="0.5" [attr.width]="length - 1" [attr.height]="width - 1" />
          <line [attr.x1]="length / 2" y1="0.5" [attr.x2]="length / 2" [attr.y2]="width - 0.5" />
          <circle [attr.cx]="length / 2" [attr.cy]="width / 2" r="9.15" />
          <rect x="0.5" [attr.y]="width / 2 - 20.16" width="16.5" height="40.32" />
          <rect [attr.x]="length - 17" [attr.y]="width / 2 - 20.16" width="16.5" height="40.32" />
          <rect x="0.5" [attr.y]="width / 2 - 9.16" width="5.5" height="18.32" />
          <rect [attr.x]="length - 6" [attr.y]="width / 2 - 9.16" width="5.5" height="18.32" />
          <path [attr.d]="'M 16.5 ' + (width / 2 - 7.3) + ' A 9.15 9.15 0 0 1 16.5 ' + (width / 2 + 7.3)" />
          <path [attr.d]="'M ' + (length - 16.5) + ' ' + (width / 2 - 7.3) + ' A 9.15 9.15 0 0 0 ' + (length - 16.5) + ' ' + (width / 2 + 7.3)" />
        </g>
        <circle [attr.cx]="length / 2" [attr.cy]="width / 2" r="0.4" class="spot" />
        <circle cx="11" [attr.cy]="width / 2" r="0.4" class="spot" />
        <circle [attr.cx]="length - 11" [attr.cy]="width / 2" r="0.4" class="spot" />

        @switch (layer()) {
          @case ('heat') {
            <g filter="url(#heat-blur)">
              @for (cell of heatCells(); track $index) {
                <rect [attr.x]="cell.x" [attr.y]="cell.y" [attr.width]="cell.w" [attr.height]="cell.h"
                      class="heat" [attr.fill-opacity]="cell.opacity" />
              }
            </g>
          }
          @case ('shots') {
            @for (shot of shots(); track $index) {
              <circle [attr.cx]="shot.x" [attr.cy]="shot.y" [attr.r]="shot.r"
                      [class]="'shot ' + shot.result + (shot.home ? ' home' : ' away')">
                <title>{{ shot.label }}</title>
              </circle>
            }
          }
          @default {
            <!-- ball trail -->
            @if (trail().length > 1) {
              <polyline [attr.points]="trailPoints()" class="trail" />
            }
            <!-- the current action -->
            @if (actionLine(); as line) {
              <line [attr.x1]="line.from.x" [attr.y1]="line.from.y" [attr.x2]="line.to.x" [attr.y2]="line.to.y"
                    [class]="'action ' + line.kind" marker-end="url(#arrow)" />
            }
            <!-- a pass the insight engine flagged as special -->
            @if (highlightPass(); as pass) {
              <line [attr.x1]="pass.from.x" [attr.y1]="pass.from.y" [attr.x2]="pass.to.x" [attr.y2]="pass.to.y"
                    class="highlight" marker-end="url(#arrow)" />
            }
            @if (ball(); as b) {
              <g class="ball" [style.transform]="'translate(' + b.x + 'px,' + b.y + 'px)'">
                <circle r="1.6" class="ball-glow" />
                <circle r="0.85" class="ball-core" />
              </g>
            }
          }
        }
      </svg>

      <!-- player tag: real-time identification of whoever is on the ball -->
      @if (layer() === 'live' && tag(); as t) {
        <div class="tag" [class.away]="!t.home" [style.left.%]="t.left" [style.top.%]="t.top">
          <b>{{ t.shirt }}</b> {{ t.name }}<span>{{ t.action }}</span>
        </div>
      }

      @if (layer() === 'heat') {
        <div class="layer-note">
          @if (focusName(); as name) {
            {{ name }}: {{ heatTouches() }} actions so far
          } @else {
            Choose a player in Your view to see their heat map
          }
        </div>
      }
      @if (layer() === 'shots') {
        <div class="layer-note">Bigger circle, better chance (xG). Filled means goal.</div>
      }

      <!-- broadcast graphics -->
      @for (o of live.overlays(); track o.id) {
        @switch (o.placement) {
          @case ('full-width') {
            <div class="gfx banner" [class.goal]="o.insight.kind === 'Goal'">
              <strong class="display">{{ o.insight.headline }}</strong>
              <p>{{ o.insight.body }}</p>
            </div>
          }
          @case ('centre') {
            <div class="gfx summary">
              <strong class="display">{{ o.insight.headline }}</strong>
              <p>{{ o.insight.body }}</p>
            </div>
          }
          @case ('top-bar') {
            <div class="gfx chip top">{{ o.insight.headline }}</div>
          }
          @case ('top-right') {
            <div class="gfx chip top right">{{ o.insight.headline }}</div>
          }
          @case ('near-player') {
            @if (anchorFor(o.insight); as a) {
              <div class="gfx near" [style.left.%]="a.left" [style.top.%]="a.top">{{ o.insight.headline }}</div>
            }
          }
          @default {
            <div class="gfx lower" [class.mine]="o.insight.perspective === 'Favorite'">
              <strong>{{ o.insight.headline }}</strong>
              <p>{{ o.insight.body }}</p>
            </div>
          }
        }
      }
    </div>
  `,
  styles: `
    :host { display: block; }
    .layers { display: flex; gap: 6px; align-items: center; margin-bottom: 10px; flex-wrap: wrap; }
    .layers button[aria-selected="true"] { border-color: var(--chalk); background: var(--turf); }
    .direction { margin-left: auto; font-size: var(--step--1); }
    .pitch { position: relative; width: 100%; aspect-ratio: 105 / 68; border-radius: 4px; overflow: hidden; }
    svg { position: absolute; inset: 0; width: 100%; height: 100%; }
    .turf { fill: var(--turf); }
    .stripe { fill: var(--turf-stripe); }
    .lines { fill: none; stroke: var(--chalk); stroke-opacity: 0.55; stroke-width: 0.3; }
    .spot { fill: var(--chalk); fill-opacity: 0.6; }
    .arrow-head { fill: var(--chalk); }
    .heat { fill: var(--floodlight); }
    .shot { stroke-width: 0.35; fill: transparent; }
    .shot.home { stroke: var(--home); }
    .shot.away { stroke: var(--away); }
    .shot.goal { fill: var(--floodlight); stroke: var(--floodlight); }
    .shot.saved.home { fill: var(--home); fill-opacity: 0.35; }
    .shot.saved.away { fill: var(--away); fill-opacity: 0.35; }
    .trail { fill: none; stroke: var(--chalk); stroke-opacity: 0.25; stroke-width: 0.35; stroke-dasharray: 0.8 0.8; }
    .action { stroke-width: 0.4; stroke: var(--chalk); stroke-opacity: 0.8; }
    .action.fail { stroke: var(--red-card); stroke-dasharray: 1 0.8; }
    .action.shot { stroke: var(--floodlight); stroke-width: 0.5; }
    .highlight { stroke: var(--floodlight); stroke-width: 0.6; }
    .ball { transition: transform 0.45s ease-out; }
    .ball-glow { fill: var(--floodlight); fill-opacity: 0.25; }
    .ball-core { fill: #ffffff; }

    .tag {
      position: absolute; transform: translate(-50%, calc(-100% - 14px)); white-space: nowrap; pointer-events: none;
      background: var(--night-2); border-left: 3px solid var(--home); padding: 3px 8px; font-size: var(--step--1);
      transition: left 0.45s ease-out, top 0.45s ease-out;
    }
    .tag.away { border-left-color: var(--away); }
    .tag b { font-family: var(--display); font-size: 1.05em; }
    .tag span { color: var(--moss); margin-left: 8px; }
    .layer-note { position: absolute; left: 12px; bottom: 10px; font-size: var(--step--1); background: var(--night-2); padding: 4px 10px; }

    .gfx { position: absolute; pointer-events: none; animation: gfx-in 0.35s ease-out; }
    .gfx p { margin: 4px 0 0; font-size: var(--step--1); color: var(--moss); }
    .banner {
      left: 0; right: 0; top: 38%; padding: 14px 24px; background: var(--night-2);
      border-top: 3px solid var(--home); text-align: center;
    }
    .banner strong { font-size: var(--step-2); }
    .banner.goal { border-top-color: var(--floodlight); background: #1d1600; }
    .banner.goal strong { color: var(--floodlight); font-size: var(--step-3); }
    .summary { left: 15%; right: 15%; top: 22%; padding: 18px 22px; background: var(--night-2); border: 1px solid var(--hairline); }
    .summary strong { font-size: var(--step-2); }
    .chip { top: 10px; left: 50%; transform: translateX(-50%); background: var(--night-2); padding: 5px 14px; border-bottom: 2px solid var(--floodlight); }
    .chip.right { left: auto; right: 10px; transform: none; }
    .near {
      transform: translate(-50%, 18px); background: var(--floodlight); color: #1d1600; font-weight: 600;
      padding: 3px 9px; font-size: var(--step--1); white-space: nowrap;
    }
    .lower {
      left: 12px; bottom: 12px; max-width: min(460px, 70%); background: var(--night-2);
      border-left: 4px solid var(--chalk); padding: 10px 14px;
    }
    .lower.mine { border-left-color: var(--floodlight); }
    @keyframes gfx-in { from { opacity: 0; transform: translateY(6px); } }
    .chip, .near { animation-name: none; }
    @media (max-width: 640px) {
      .banner strong { font-size: var(--step-1); }
      .banner.goal strong { font-size: var(--step-2); }
      .gfx p { display: none; }
    }
  `
})
export class PitchViewComponent {
  readonly live = inject(LiveService);
  readonly layer = signal<Layer>('live');

  readonly length = PITCH_LENGTH;
  readonly width = PITCH_WIDTH;
  readonly viewBox = `0 0 ${PITCH_LENGTH} ${PITCH_WIDTH}`;
  readonly stripes = Array.from({ length: 10 }, (_, i) => i * 10.5);

  private readonly homeId = computed(() => this.live.info()?.home.id ?? '');
  readonly homeShort = computed(() => this.live.info()?.home.shortName ?? 'Home');

  /** Recent ball positions, oldest first. */
  readonly trail = computed(() => {
    const events = this.live.events();
    const home = this.homeId();
    const points: PitchPoint[] = [];
    for (let i = events.length - 1; i >= 0 && points.length < TRAIL_LENGTH; i--) {
      const p = ballPosition(events[i], home);
      if (p) points.unshift(p);
    }
    return points;
  });

  readonly trailPoints = computed(() => this.trail().map(p => `${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(' '));

  readonly ball = computed(() => {
    const e = this.live.lastAction();
    return e ? ballPosition(e, this.homeId()) : null;
  });

  readonly actionLine = computed(() => {
    const e = this.live.lastAction();
    if (!e?.start || !e.end || !['Pass', 'Carry', 'Shot', 'Dribble'].includes(e.type)) return null;
    const home = this.homeId();
    const kind = e.type === 'Shot' ? 'shot' : e.outcome === 'Fail' ? 'fail' : 'ok';
    return { from: toScreen(e.start, e.teamId, home), to: toScreen(e.end, e.teamId, home), kind };
  });

  /** If a "pass of note" graphic is showing, draw that pass. */
  readonly highlightPass = computed(() => {
    const overlay = this.live.overlays().find(o => o.placement === 'pitch-overlay');
    if (!overlay) return null;
    const e = this.findEvent(overlay.insight.sourceSequences[0]);
    if (!e?.start || !e.end) return null;
    const home = this.homeId();
    return { from: toScreen(e.start, e.teamId, home), to: toScreen(e.end, e.teamId, home) };
  });

  readonly tag = computed(() => {
    const e = this.live.lastAction();
    if (!e?.playerId) return null;
    const ref = this.live.players().get(e.playerId);
    const position = this.ball();
    if (!ref || !position) return null;
    const anchor = e.start ? toScreen(e.start, e.teamId, this.homeId()) : position;
    const pct = toPercent(anchor);
    return {
      left: pct.left,
      top: pct.top,
      shirt: ref.player.shirtNumber,
      name: ref.player.shortName,
      action: actionLabel(e),
      home: ref.team.id === this.homeId()
    };
  });

  readonly focusName = computed(() => {
    const id = this.live.profile().focusPlayerId;
    return id ? this.live.players().get(id)?.player.name ?? null : null;
  });

  private readonly focusTouches = computed(() => {
    const id = this.live.profile().focusPlayerId;
    if (!id) return [];
    const home = this.homeId();
    return this.live.events()
      .filter(e => e.playerId === id && e.start && TOUCH_TYPES.has(e.type))
      .map(e => toScreen(e.start!, e.teamId, home));
  });

  readonly heatTouches = computed(() => this.focusTouches().length);

  readonly heatCells = computed<HeatCell[]>(() => {
    const touches = this.focusTouches();
    if (touches.length === 0) return [];
    const w = PITCH_LENGTH / HEAT_COLUMNS;
    const h = PITCH_WIDTH / HEAT_ROWS;
    const counts = new Array<number>(HEAT_COLUMNS * HEAT_ROWS).fill(0);
    for (const t of touches) {
      const col = Math.min(HEAT_COLUMNS - 1, Math.floor(t.x / w));
      const row = Math.min(HEAT_ROWS - 1, Math.floor(t.y / h));
      counts[row * HEAT_COLUMNS + col]++;
    }
    const max = Math.max(...counts);
    const cells: HeatCell[] = [];
    counts.forEach((count, index) => {
      if (count === 0) return;
      cells.push({
        x: (index % HEAT_COLUMNS) * w,
        y: Math.floor(index / HEAT_COLUMNS) * h,
        w, h,
        opacity: 0.15 + 0.75 * (count / max)
      });
    });
    return cells;
  });

  readonly shots = computed<ShotMark[]>(() => {
    const home = this.homeId();
    const players = this.live.players();
    return this.live.events()
      .filter(e => e.type === 'Shot' && e.start)
      .map(e => {
        const p = toScreen(e.start!, e.teamId, home);
        const xg = e.xg ?? 0;
        const result = e.outcome === 'Goal' ? 'goal' : e.outcome === 'Saved' ? 'saved' : 'missed';
        const who = e.playerId ? players.get(e.playerId)?.player.shortName ?? '' : '';
        return { x: p.x, y: p.y, r: 0.7 + xg * 3.2, home: e.teamId === home, result, label: `${who} ${e.clock}, xG ${xg.toFixed(2)}` };
      });
  });

  /** Where to pin a near-player graphic: the player's latest location, or the ball. */
  anchorFor(insight: PersonalizedInsight): { left: number; top: number } | null {
    const playerId = insight.playerIds[0];
    const home = this.homeId();
    if (playerId) {
      const events = this.live.events();
      for (let i = events.length - 1; i >= 0; i--) {
        const e = events[i];
        if (e.playerId === playerId && e.start) return toPercent(toScreen(e.start, e.teamId, home));
      }
    }
    const ball = this.ball();
    return ball ? toPercent(ball) : null;
  }

  private findEvent(sequence: number | undefined): MatchEvent | null {
    if (sequence === undefined) return null;
    const events = this.live.events();
    for (let i = events.length - 1; i >= 0; i--) {
      if (events[i].sequence === sequence) return events[i];
      if (events[i].sequence < sequence) break;
    }
    return null;
  }
}
