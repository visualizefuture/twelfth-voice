import { Component, computed, inject } from '@angular/core';
import { LiveService } from '../live.service';

/** Player-focus mode: one player's match, live. */
@Component({
  selector: 'tv-player-card',
  template: `
    <section aria-label="Focus player">
      @if (player(); as p) {
        <header>
          <span class="shirt display" [class.away]="!isHome()">{{ p.shirtNumber }}</span>
          <div>
            <h2 class="display">{{ p.name }}</h2>
            <p class="muted">{{ teamName() }}, {{ p.position }}{{ p.onPitch ? '' : ', off the pitch' }}</p>
          </div>
          <span class="rating display" [attr.aria-label]="'Match rating ' + p.rating.toFixed(1)">{{ p.rating.toFixed(1) }}</span>
        </header>
        <dl>
          <div><dt>Touches</dt><dd>{{ p.touches }}</dd></div>
          <div><dt>Passes</dt><dd>{{ p.passesCompleted }}/{{ p.passesAttempted }}</dd></div>
          <div><dt>Pass difficulty</dt><dd>{{ p.averagePassDifficulty.toFixed(1) }}</dd></div>
          <div><dt>Shots (xG)</dt><dd>{{ p.shots }} ({{ p.xg.toFixed(2) }})</dd></div>
          <div><dt>Goals, assists</dt><dd>{{ p.goals }}, {{ p.assists }}</dd></div>
          <div><dt>Tackles, interceptions</dt><dd>{{ p.tackles }}, {{ p.interceptions }}</dd></div>
          <div><dt>Distance</dt><dd>{{ (p.distanceM / 1000).toFixed(1) }} km</dd></div>
          <div><dt>Top speed</dt><dd>{{ p.topSpeedKmh.toFixed(1) }} km/h</dd></div>
          <div><dt>Sprints</dt><dd>{{ p.sprints }}</dd></div>
        </dl>
      } @else {
        <h2 class="display">Follow one player</h2>
        <p class="muted">Pick a player in Your view. You'll get their stats here, their heat map on the pitch, and every moment they're involved in.</p>
      }
    </section>
  `,
  styles: `
    header { display: grid; grid-template-columns: auto 1fr auto; gap: 12px; align-items: center; margin-bottom: 14px; }
    .shirt { width: 44px; height: 44px; display: grid; place-items: center; font-size: var(--step-1); background: var(--home); color: var(--night); }
    .shirt.away { background: var(--away); }
    h2 { margin: 0; font-size: var(--step-1); }
    header p { margin: 0; font-size: var(--step--1); }
    .rating { font-size: var(--step-2); color: var(--floodlight); }
    dl { margin: 0; display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; }
    dt { font-size: var(--step--1); color: var(--moss); }
    dd { margin: 0; font-family: var(--display); font-size: var(--step-1); font-variant-numeric: tabular-nums; }
    @media (max-width: 480px) { dl { grid-template-columns: repeat(2, 1fr); } }
  `
})
export class PlayerCardComponent {
  private readonly live = inject(LiveService);

  readonly player = computed(() => this.live.state()?.focusPlayer ?? null);

  readonly isHome = computed(() => this.player()?.teamId === this.live.info()?.home.id);

  readonly teamName = computed(() => {
    const p = this.player();
    const info = this.live.info();
    if (!p || !info) return '';
    return p.teamId === info.home.id ? info.home.shortName : info.away.shortName;
  });
}
