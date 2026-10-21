import { Component, computed, effect, inject, signal } from '@angular/core';
import { ApiService } from '../api.service';
import { LiveService } from '../live.service';
import { StoryRecap } from '../models';

/** The match story, written by the Storyteller agent from the verified recap. Opens itself at full time. */
@Component({
  selector: 'tv-story-panel',
  template: `
    <section aria-label="Match story">
      <header>
        <h2 class="display">{{ finished() ? 'The story of the match' : 'The story so far' }}</h2>
        <button type="button" (click)="load()" [disabled]="busy()">
          {{ busy() ? 'Writing…' : story() ? 'Rewrite' : finished() ? 'Read the recap' : 'Catch me up' }}
        </button>
      </header>
      @if (story(); as s) {
        <article [attr.lang]="s.language">
          <h3 class="display">{{ s.headline }}</h3>
          @for (paragraph of paragraphs(); track $index) {
            <p>{{ paragraph }}</p>
          }
          <p class="checks" [attr.title]="s.checks.join('. ')">
            {{ s.fromAi ? '✓ Written by the Storyteller agent, every number checked' : 'Written from the match data' }}
          </p>
        </article>
      } @else if (error()) {
        <p class="error">{{ error() }}</p>
      }
    </section>
  `,
  styles: `
    header { display: flex; justify-content: space-between; align-items: baseline; gap: 12px; }
    h2 { font-size: var(--step-1); margin: 0; }
    h3 { font-size: var(--step-2); margin: 14px 0 8px; line-height: 1.1; }
    article p { max-width: 68ch; margin: 0 0 10px; }
    .checks { font-size: 0.75rem; color: var(--floodlight); }
    .error { color: var(--red-card); }
  `
})
export class StoryPanelComponent {
  private readonly api = inject(ApiService);
  private readonly live = inject(LiveService);

  readonly story = signal<StoryRecap | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly finished = computed(() => this.live.summary()?.status === 'Finished');
  readonly paragraphs = computed(() => (this.story()?.body ?? '').split(/\n\s*\n/).filter(p => p.trim().length > 0));

  private autoLoaded = false;

  constructor() {
    // At full time, write the recap without being asked.
    effect(() => {
      if (this.finished() && !this.autoLoaded) {
        this.autoLoaded = true;
        void this.load();
      }
    });
  }

  async load(): Promise<void> {
    const matchId = this.live.matchId();
    if (!matchId || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      this.story.set(await this.api.story(matchId, this.live.profile()));
    } catch {
      this.error.set('The story couldn\'t be written just now. Try again in a moment.');
    } finally {
      this.busy.set(false);
    }
  }
}
