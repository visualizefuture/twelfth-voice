import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../api.service';
import { LiveService } from '../live.service';
import { AnalystAnswer } from '../models';

interface Exchange { id: number; question: string; answer: AnalystAnswer | null; failed: boolean; }

/** Ask the match: the Analyst agent looks the live data up with tools and its numbers are checked. */
@Component({
  selector: 'tv-ask-panel',
  imports: [FormsModule],
  template: `
    <section aria-label="Ask the match">
      <form (ngSubmit)="ask()">
        <label for="ask-input" class="display">Ask the match</label>
        <div class="row">
          <input id="ask-input" name="question" [(ngModel)]="question" maxlength="300" autocomplete="off"
                 [placeholder]="placeholder()" [disabled]="busy()">
          <button type="submit" class="primary" [disabled]="busy() || !question.trim()">{{ busy() ? 'Looking…' : 'Ask' }}</button>
        </div>
      </form>

      @if (exchanges().length === 0) {
        <div class="ideas">
          @for (idea of ideas(); track idea) {
            <button type="button" (click)="question = idea; ask()" [disabled]="busy()">{{ idea }}</button>
          }
        </div>
      }

      <ol aria-live="polite">
        @for (x of exchanges(); track x.id) {
          <li>
            <p class="q">{{ x.question }}</p>
            @if (x.answer; as a) {
              <p class="a" [attr.lang]="a.language">{{ a.answer }}</p>
              @if (a.fromAi) {
                <p class="checks" [attr.title]="a.checks.join('. ')">✓ {{ a.checks[0] }}{{ a.checks.length > 1 ? ', numbers checked' : '' }}</p>
              }
            } @else if (x.failed) {
              <p class="a error">The question didn't reach the analyst. Check the API is running and try again.</p>
            } @else {
              <p class="a muted">Looking at the match data…</p>
            }
          </li>
        }
      </ol>
    </section>
  `,
  styles: `
    label { font-size: var(--step-1); display: block; margin-bottom: 6px; }
    .row { display: grid; grid-template-columns: 1fr auto; gap: 8px; }
    .ideas { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 10px; }
    .ideas button { font-size: var(--step--1); color: var(--moss); padding: 4px 10px; border-radius: 999px; }
    ol { list-style: none; margin: 10px 0 0; padding: 0; display: grid; gap: 12px; max-height: 260px; overflow-y: auto; }
    .q { margin: 0; font-weight: 600; }
    .a { margin: 4px 0 0; font-size: var(--step--1); color: var(--chalk); opacity: 0.9; max-width: 60ch; }
    .error { color: var(--red-card); }
    .checks { margin: 4px 0 0; font-size: 0.75rem; color: var(--floodlight); }
  `
})
export class AskPanelComponent {
  private readonly api = inject(ApiService);
  private readonly live = inject(LiveService);

  readonly exchanges = signal<Exchange[]>([]);
  readonly busy = signal(false);
  question = '';
  private nextId = 1;

  readonly ideas = computed(() => {
    const info = this.live.info();
    const focus = this.live.profile().focusPlayerId;
    const focusName = focus ? this.live.players().get(focus)?.player.shortName : null;
    const team = info?.home.shortName ?? 'the home side';
    return [
      'Who has been the best player so far?',
      `Why are ${team} on top, or not?`,
      focusName ? `How is ${focusName} playing?` : 'Which team is creating the better chances?',
      'What changed in the second half?'
    ];
  });

  readonly placeholder = computed(() =>
    this.live.profile().language === 'en' ? 'e.g. Who is controlling the game?' : 'Ask in any language');

  async ask(): Promise<void> {
    const question = this.question.trim();
    const matchId = this.live.matchId();
    if (!question || !matchId || this.busy()) return;

    const exchange: Exchange = { id: this.nextId++, question, answer: null, failed: false };
    this.exchanges.update(list => [exchange, ...list].slice(0, 10));
    this.question = '';
    this.busy.set(true);
    try {
      const answer = await this.api.ask(matchId, question, this.live.profile());
      this.exchanges.update(list => list.map(x => (x.id === exchange.id ? { ...x, answer } : x)));
    } catch {
      this.exchanges.update(list => list.map(x => (x.id === exchange.id ? { ...x, failed: true } : x)));
    } finally {
      this.busy.set(false);
    }
  }
}
