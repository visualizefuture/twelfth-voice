import { Component, computed, input } from '@angular/core';
import { Crest, Team } from '../models';

const SHAPES: Record<Crest['shape'], string> = {
  Shield: 'M14 8 H86 V50 Q86 80 50 95 Q14 80 14 50 Z',
  Roundel: 'M50 5 A45 45 0 1 1 49.99 5 Z',
  Pennant: 'M12 8 H88 V58 L50 95 L12 58 Z',
  Badge: 'M30 5 H70 L95 30 V70 L70 95 H30 L5 70 V30 Z'
};

/** Original emblems, drawn for this app. Filled paths use the accent colour; strokes use it too. */
const EMBLEMS: Record<string, { fill?: string; stroke?: string }> = {
  anchor: {
    stroke: 'M50 22 A6 6 0 1 0 50.01 22 M50 34 V76 M37 42 H63 M27 62 Q29 80 50 80 Q71 80 73 62 M27 62 L22 67 M73 62 L78 67'
  },
  crown: { fill: 'M27 68 L31 38 L42 53 L50 31 L58 53 L69 38 L73 68 Z M27 72 H73 V78 H27 Z' },
  anvil: { fill: 'M24 42 H72 Q74 51 63 53 H58 L63 67 H71 V74 H29 V67 H37 L42 53 H33 Q25 50 24 42 Z' },
  stag: {
    fill: 'M43 56 Q50 50 57 56 L56 70 Q50 80 44 70 Z',
    stroke: 'M45 55 L38 36 M41 44 L31 40 M39 39 L34 30 M55 55 L62 36 M59 44 L69 40 M61 39 L66 30'
  },
  oak: {
    fill: 'M50 24 C63 31 70 44 61 56 C68 61 62 73 54 76 L50 80 L46 76 C38 73 32 61 39 56 C30 44 37 31 50 24 Z',
    stroke: 'M50 40 V86'
  },
  lantern: {
    fill: 'M41 38 H59 L63 46 V66 L59 73 H41 L37 66 V46 Z',
    stroke: 'M45 38 Q50 25 55 38 M41 78 H59'
  }
};

/** A club crest drawn from its description, at any size. */
@Component({
  selector: 'tv-crest',
  template: `
    @if (crest(); as c) {
      <svg viewBox="0 0 100 100" [attr.width]="size()" [attr.height]="size()" role="img" [attr.aria-label]="label()">
        <path [attr.d]="shape()" [attr.fill]="c.field" [attr.stroke]="c.accent" stroke-width="5" />
        <path [attr.d]="shape()" fill="none" stroke="#ffffff" stroke-opacity="0.25" stroke-width="1.5" transform="translate(50 50) scale(0.86) translate(-50 -50)" />
        @if (emblem().fill; as f) {
          <path [attr.d]="f" [attr.fill]="c.accent" />
        }
        @if (emblem().stroke; as s) {
          <path [attr.d]="s" fill="none" [attr.stroke]="c.accent" stroke-width="5" stroke-linecap="round" stroke-linejoin="round" />
        }
      </svg>
    } @else {
      <svg viewBox="0 0 100 100" [attr.width]="size()" [attr.height]="size()" aria-hidden="true">
        <circle cx="50" cy="50" r="44" fill="none" stroke="currentColor" stroke-width="4" />
      </svg>
    }
  `,
  styles: `:host { display: inline-flex; line-height: 0; } svg { overflow: visible; }`
})
export class CrestComponent {
  readonly team = input<Team | null | undefined>(null);
  readonly size = input(40);

  readonly crest = computed(() => this.team()?.crest ?? null);
  readonly shape = computed(() => SHAPES[this.crest()?.shape ?? 'Shield'] ?? SHAPES.Shield);
  readonly emblem = computed(() => EMBLEMS[this.crest()?.emblem ?? ''] ?? {});
  readonly label = computed(() => `${this.team()?.name ?? 'Club'} crest`);
}
