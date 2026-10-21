import { Component, computed, input } from '@angular/core';
import { kitFor } from '../kits';
import { Player, Team } from '../models';
import { CrestComponent } from './crest';
import { PortraitComponent } from './portrait';

const POSITION_ORDER = ['GK', 'RB', 'CB', 'LB', 'DM', 'CM', 'AM', 'RW', 'LW', 'ST'];
const POSITION_NAMES: Record<string, string> = {
  GK: 'Goalkeeper', RB: 'Right back', CB: 'Centre back', LB: 'Left back', DM: 'Defensive midfield',
  CM: 'Central midfield', AM: 'Attacking midfield', RW: 'Right wing', LW: 'Left wing', ST: 'Striker'
};

/** A club's team sheet: crest and kit, the captain and keeper up top, then the XI and the bench. */
@Component({
  selector: 'tv-lineup-card',
  imports: [CrestComponent, PortraitComponent],
  template: `
    @if (team(); as t) {
      <article [attr.aria-label]="t.name + ' team sheet'">
        <header>
          <tv-crest [team]="t" [size]="54" />
          <div>
            <h3 class="display">{{ t.name }}</h3>
            <p class="muted">{{ t.nickname }} · {{ t.formation }}{{ t.crest ? ' · Est. ' + t.crest.founded : '' }}</p>
          </div>
        </header>

        <div class="leaders">
          @for (lead of leaders(); track lead.player.id) {
            <figure>
              <tv-portrait [playerLook]="lead.player.look" [playerKit]="kitOf(lead.player)" [number]="lead.player.shirtNumber"
                           [captain]="!!lead.player.isCaptain" [size]="84" [label]="lead.player.name" />
              <figcaption>
                <span class="role">{{ lead.role }}</span>
                <strong>{{ lead.player.name }}</strong>
                <span class="muted">#{{ lead.player.shirtNumber }} · {{ positionName(lead.player.position) }}</span>
              </figcaption>
            </figure>
          }
        </div>

        <h4>Starting XI</h4>
        <ul class="xi">
          @for (p of others(); track p.id) {
            <li>
              <tv-portrait [playerLook]="p.look" [playerKit]="kitOf(p)" [size]="30" />
              <span class="num">{{ p.shirtNumber }}</span>
              <span class="name">{{ p.name }}</span>
              <span class="pos muted" [attr.title]="positionName(p.position)">{{ p.position }}</span>
            </li>
          }
        </ul>

        <h4>Bench</h4>
        <p class="bench muted">
          @for (p of bench(); track p.id; let last = $last) {
            <span>{{ p.shirtNumber }} {{ p.shortName }} ({{ p.position }}){{ last ? '' : ', ' }}</span>
          }
        </p>
      </article>
    }
  `,
  styles: `
    article { background: var(--night-2); border: 1px solid var(--hairline); border-radius: 10px; padding: 16px; }
    header { display: flex; gap: 12px; align-items: center; }
    h3 { margin: 0; font-size: var(--step-2); line-height: 1; }
    header p { margin: 4px 0 0; font-size: var(--step--1); }
    .leaders { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin: 16px 0 12px; }
    figure { margin: 0; display: flex; gap: 10px; align-items: center; }
    figcaption { display: grid; gap: 2px; font-size: var(--step--1); }
    figcaption strong { font-size: var(--step-0); }
    .role { color: var(--floodlight); font-weight: 600; }
    h4 { margin: 12px 0 6px; font-size: var(--step--1); color: var(--moss); font-weight: 600; }
    .xi { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: 1fr 1fr; gap: 6px 12px; }
    .xi li { display: grid; grid-template-columns: auto 1.6em 1fr auto; gap: 6px; align-items: center; font-size: var(--step--1); }
    .num { font-family: var(--display); color: var(--moss); text-align: right; }
    .name { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .bench { margin: 0; font-size: var(--step--1); }
    @media (max-width: 480px) { .leaders, .xi { grid-template-columns: 1fr; } }
  `
})
export class LineupCardComponent {
  readonly team = input<Team | null | undefined>(null);

  private readonly starters = computed(() => (this.team()?.squad ?? []).slice(0, 11));

  readonly leaders = computed(() => {
    const xi = this.starters();
    const captain = xi.find(p => p.isCaptain);
    const keeper = xi.find(p => p.position === 'GK');
    const list: { player: Player; role: string }[] = [];
    if (captain) list.push({ player: captain, role: 'Captain' });
    if (keeper) list.push({ player: keeper, role: 'Goalkeeper' });
    return list;
  });

  readonly others = computed(() => {
    const featured = new Set(this.leaders().map(l => l.player.id));
    return this.starters()
      .filter(p => !featured.has(p.id))
      .sort((a, b) => POSITION_ORDER.indexOf(a.position) - POSITION_ORDER.indexOf(b.position));
  });

  readonly bench = computed(() => (this.team()?.squad ?? []).slice(11));

  kitOf(player: Player) {
    return kitFor(this.team(), player);
  }

  positionName(position: string): string {
    return POSITION_NAMES[position] ?? position;
  }
}
