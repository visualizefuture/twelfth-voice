import { Component, computed, inject } from '@angular/core';
import { LiveService } from '../live.service';
import { TimelinePoint } from '../models';

interface Bar { x: number; y: number; h: number; home: boolean; }
interface GoalMark { x: number; home: boolean; label: string; }

const WIDTH = 1000;
const HEIGHT = 140;
const MID = HEIGHT / 2;
const HALF = 2700;
const FULL = 5400;

/**
 * Who's on top, minute by minute: bars above the line favour the home side, below the away side.
 * Goals are marked where they happened, so you can see whether they came with or against the run of play.
 */
@Component({
  selector: 'tv-momentum-chart',
  template: `
    <figure>
      <figcaption>
        <span class="display">Momentum</span>
        <span class="legend"><i class="home"></i>{{ homeName() }} <i class="away"></i>{{ awayName() }}</span>
      </figcaption>
      <svg [attr.viewBox]="'0 0 ' + width + ' ' + height" preserveAspectRatio="none" role="img"
           [attr.aria-label]="summary()">
        <line x1="0" [attr.y1]="mid" [attr.x2]="width" [attr.y2]="mid" class="axis" />
        <line [attr.x1]="halfX()" y1="6" [attr.x2]="halfX()" [attr.y2]="height - 6" class="half" />
        @for (bar of bars(); track $index) {
          <rect [attr.x]="bar.x" [attr.y]="bar.y" [attr.width]="barWidth()" [attr.height]="bar.h"
                [class]="bar.home ? 'bar home' : 'bar away'" />
        }
        @for (goal of goals(); track $index) {
          <g [attr.transform]="'translate(' + goal.x + ',' + (goal.home ? 10 : height - 10) + ')'">
            <circle r="6" class="goal" />
            <title>{{ goal.label }}</title>
          </g>
        }
      </svg>
      <div class="ticks muted"><span>0'</span><span>45'</span><span>90'</span></div>
    </figure>
  `,
  styles: `
    figure { margin: 0; }
    figcaption { display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 6px; }
    figcaption .display { font-size: var(--step-1); }
    .legend { font-size: var(--step--1); color: var(--moss); display: flex; gap: 6px; align-items: center; }
    .legend i { display: inline-block; width: 10px; height: 10px; margin-left: 8px; }
    .legend .home { background: var(--home); }
    .legend .away { background: var(--away); }
    svg { width: 100%; height: 120px; display: block; background: var(--night-2); }
    .axis { stroke: var(--hairline); stroke-width: 1; }
    .half { stroke: var(--hairline); stroke-dasharray: 4 4; }
    .bar.home { fill: var(--home); }
    .bar.away { fill: var(--away); }
    .goal { fill: var(--floodlight); stroke: var(--night); stroke-width: 2; }
    .ticks { display: flex; justify-content: space-between; font-size: var(--step--1); margin-top: 2px; }
  `
})
export class MomentumChartComponent {
  private readonly live = inject(LiveService);
  readonly width = WIDTH;
  readonly height = HEIGHT;
  readonly mid = MID;

  readonly homeName = computed(() => this.live.info()?.home.shortName ?? 'Home');
  readonly awayName = computed(() => this.live.info()?.away.shortName ?? 'Away');

  /** First-half added time pushes the second half to the right so the halves don't overlap. */
  private readonly firstHalfExtra = computed(() => {
    const p1 = this.live.timeline().filter(p => p.period === 1);
    const last = p1.length ? p1[p1.length - 1].matchSecond : HALF;
    return Math.max(0, last - HALF);
  });

  private readonly totalSeconds = computed(() => {
    const points = this.live.timeline();
    const last = points.length ? this.position(points[points.length - 1]) : 0;
    return Math.max(FULL + this.firstHalfExtra() + 240, last);
  });

  readonly halfX = computed(() => ((HALF + this.firstHalfExtra()) / this.totalSeconds()) * WIDTH);
  readonly barWidth = computed(() => Math.max(2, (30 / this.totalSeconds()) * WIDTH - 1));

  readonly bars = computed<Bar[]>(() => {
    const total = this.totalSeconds();
    return this.live.timeline().map(p => {
      const h = (Math.abs(p.momentum) / 100) * (MID - 8);
      const home = p.momentum >= 0;
      return { x: (this.position(p) / total) * WIDTH, y: home ? MID - h : MID, h: Math.max(h, 0.5), home };
    });
  });

  readonly goals = computed<GoalMark[]>(() => {
    const total = this.totalSeconds();
    const marks: GoalMark[] = [];
    let previous: TimelinePoint | null = null;
    for (const p of this.live.timeline()) {
      if (previous) {
        const x = (this.position(p) / total) * WIDTH;
        if (p.homeScore > previous.homeScore) marks.push({ x, home: true, label: `${this.homeName()} goal, ${p.clock}` });
        if (p.awayScore > previous.awayScore) marks.push({ x, home: false, label: `${this.awayName()} goal, ${p.clock}` });
      }
      previous = p;
    }
    return marks;
  });

  readonly summary = computed(() => {
    const points = this.live.timeline();
    if (!points.length) return 'Momentum chart, waiting for the first minutes of play';
    const latest = points[points.length - 1].momentum;
    const side = latest > 20 ? this.homeName() : latest < -20 ? this.awayName() : 'neither side';
    return `Momentum chart: ${side} on top right now`;
  });

  private position(p: TimelinePoint): number {
    return p.period === 1 ? p.matchSecond : p.matchSecond + this.firstHalfExtra();
  }
}
