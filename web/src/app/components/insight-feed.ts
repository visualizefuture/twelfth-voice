import { Component, computed, inject, input, signal } from '@angular/core';
import { LiveService } from '../live.service';
import { PersonalizedInsight } from '../models';

/** The running story of the match, newest first, explained for this viewer. */
@Component({
  selector: 'tv-insight-feed',
  template: `
    <section aria-label="Match story">
      <header>
        <h2 class="display">{{ title() }}</h2>
        @if (limit() && live.insights().length > limit()!) {
          <button type="button" class="more" (click)="expanded.set(!expanded())" [attr.aria-expanded]="expanded()">
            {{ expanded() ? 'Latest only' : 'All ' + live.insights().length }}
          </button>
        }
      </header>
      @if (live.insights().length === 0) {
        <p class="muted">The story starts at kick-off. Key moments and why they matter will appear here.</p>
      }
      <ol aria-live="polite">
        @for (item of shown(); track item.insightId) {
          <li [class]="kindClass(item)" [class.mine]="item.perspective === 'Favorite'" [class.focus]="item.aboutFocusPlayer">
            <span class="time">{{ item.clock }}</span>
            <div>
              <strong>{{ item.headline }}</strong>
              <p>{{ item.body }}</p>
              @if (item.analystNote) {
                <p class="note">{{ item.analystNote }}</p>
              }
              @if (item.source === 'ai') {
                <p class="crew" [attr.title]="(item.checks ?? []).join('. ')">
                  <span class="tick" aria-hidden="true">✓</span> Written by the AI crew, fact-checked{{ item.language !== 'en' ? ', translated' : '' }}
                </p>
              } @else if (item.language !== 'en') {
                <p class="crew" [attr.title]="(item.checks ?? []).join('. ')">Translated by the AI crew, numbers re-checked</p>
              }
            </div>
          </li>
        }
      </ol>
    </section>
  `,
  styles: `
    :host { display: block; }
    section { display: flex; flex-direction: column; height: 100%; }
    header { display: flex; justify-content: space-between; align-items: baseline; gap: 10px; margin: 0 0 6px; }
    h2 { font-size: var(--step-1); margin: 0; }
    .more { font-size: var(--step--1); padding: 3px 10px; }
    ol { list-style: none; margin: 0; padding: 0 4px 0 0; overflow-y: auto; flex: 1; min-height: 0; }
    li { display: grid; grid-template-columns: 3.6em 1fr; gap: 10px; padding: 10px 0 10px 10px; border-left: 3px solid transparent; border-bottom: 1px solid var(--hairline); }
    .time { font-family: var(--display); color: var(--moss); font-variant-numeric: tabular-nums; }
    strong { font-weight: 600; }
    p { margin: 3px 0 0; font-size: var(--step--1); color: var(--moss); max-width: 60ch; }
    .note { color: var(--chalk); opacity: 0.7; }
    .crew { font-size: 0.75rem; color: var(--floodlight); opacity: 0.85; margin-top: 5px; }
    .tick { font-weight: 700; }
    li.goal { border-left-color: var(--floodlight); }
    li.goal strong { color: var(--floodlight); }
    li.card { border-left-color: var(--red-card); }
    li.trend { border-left-color: var(--chalk); }
    li.mine { background: rgba(246, 196, 69, 0.06); }
    li.focus .time { color: var(--floodlight); }
  `
})
export class InsightFeedComponent {
  readonly live = inject(LiveService);
  /** Show only the newest few until the viewer asks for all of them. */
  readonly limit = input<number | null>(null);
  readonly expanded = signal(false);
  readonly shown = computed(() => {
    const all = this.live.insights();
    const n = this.limit();
    return n && !this.expanded() ? all.slice(0, n) : all;
  });

  readonly title = computed(() => {
    const p = this.live.profile();
    if (p.focusPlayerId) {
      const name = this.live.players().get(p.focusPlayerId)?.player.shortName;
      return name ? `The match through ${name}` : 'The story';
    }
    return p.mode === 'Analyst' ? 'The story, with the numbers' : 'The story';
  });

  kindClass(item: PersonalizedInsight): string {
    switch (item.kind) {
      case 'Goal': return 'goal';
      case 'Card': return 'card';
      case 'MomentumShift':
      case 'PressingShift':
      case 'ControlShift':
      case 'TempoShift': return 'trend';
      default: return '';
    }
  }
}
