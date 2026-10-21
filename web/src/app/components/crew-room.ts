import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { ApiService } from '../api.service';
import { LiveService } from '../live.service';
import { CrewActivity } from '../models';

const REFRESH_MS = 3000;

/**
 * Behind the scenes: every moment the AI crew worked on, each draft the Commentator wrote,
 * what the Fact-checker said about it, and what finally went on air.
 */
@Component({
  selector: 'tv-crew-room',
  template: `
    <section aria-label="Crew room">
      <header>
        <div>
          <h2 class="display">Crew room</h2>
          <p class="muted">Behind the scenes: every draft, every fact-check, and what made it on air.</p>
        </div>
        <button type="button" (click)="toggle()" [attr.aria-expanded]="open()">{{ open() ? 'Close' : 'Look inside' }}</button>
      </header>

      @if (open()) {
        <p class="tally" role="status">
          {{ tally().moments }} moments worked on · {{ tally().aired }} crew lines on air ·
          {{ tally().rejected }} drafts sent back · {{ tally().heldBack }} held back
        </p>

        @if (items().length === 0) {
          <p class="muted">Nothing yet. The crew starts work on the first big moment of the match.</p>
        }

        <ol>
          @for (a of items(); track a.insightId + a.audience) {
            <li [class]="a.outcome">
              <div class="meta">
                <span class="minute display">{{ a.minute }}</span>
                <span class="kind">{{ kindLabel(a.kind) }}</span>
                <span class="muted">for {{ a.audience }}</span>
              </div>
              <p class="data"><span class="label">Data said</span> {{ a.dataHeadline }}</p>

              @for (d of a.drafts; track d.attempt) {
                <div class="draft" [class.approved]="d.approved">
                  <span class="label">Draft {{ d.attempt }}</span>
                  <p><strong>{{ d.headline }}</strong> {{ d.body }}</p>
                  <p class="verdict">
                    {{ d.approved ? '✓ Approved' : '✗ Sent back' }} by {{ d.checkedBy.toLowerCase() }}{{ d.reasons.length ? ':' : '' }}
                    @for (r of d.reasons; track $index) { <span class="reason">{{ r }}</span> }
                  </p>
                </div>
              }

              <p class="aired">
                <span class="label">{{ a.outcome === 'crew' ? 'On air' : a.outcome === 'data' ? 'Held back, data line stayed' : 'On air, translated' }}</span>
                <strong>{{ a.finalHeadline }}</strong> {{ a.finalBody }}
              </p>
              @if (a.translation) {
                <p class="muted small">{{ a.translation }}</p>
              }
            </li>
          }
        </ol>
      }
    </section>
  `,
  styles: `
    header { display: flex; justify-content: space-between; align-items: flex-start; gap: 12px; }
    h2 { margin: 0; font-size: var(--step-1); }
    header p { margin: 2px 0 0; font-size: var(--step--1); }
    .tally { font-size: var(--step--1); color: var(--floodlight); margin: 12px 0; }
    ol { list-style: none; margin: 0; padding: 0; display: grid; gap: 12px; max-height: 520px; overflow-y: auto; }
    li { border: 1px solid var(--hairline); border-left: 4px solid var(--chalk); border-radius: 0 8px 8px 0; padding: 10px 14px; background: var(--night-2); }
    li.crew { border-left-color: var(--floodlight); }
    li.data { border-left-color: var(--red-card); }
    .meta { display: flex; gap: 10px; align-items: baseline; flex-wrap: wrap; font-size: var(--step--1); }
    .minute { font-size: var(--step-1); }
    .kind { font-weight: 600; }
    p { margin: 6px 0 0; font-size: var(--step--1); max-width: 80ch; }
    .label { display: inline-block; min-width: 7.5em; color: var(--moss); }
    .draft { margin-top: 8px; padding: 6px 10px; border-radius: 6px; background: rgba(255, 107, 91, 0.08); }
    .draft.approved { background: rgba(246, 196, 69, 0.08); }
    .draft p { margin: 2px 0 0; }
    .verdict { color: var(--red-card); }
    .draft.approved .verdict { color: var(--floodlight); }
    .reason { display: block; margin-left: 1em; color: var(--chalk); opacity: 0.85; }
    .aired { margin-top: 8px; }
    .small { font-size: 0.75rem; }
  `
})
export class CrewRoomComponent implements OnDestroy {
  private readonly api = inject(ApiService);
  private readonly live = inject(LiveService);
  private timer: ReturnType<typeof setInterval> | null = null;

  readonly open = signal(false);
  readonly items = signal<CrewActivity[]>([]);

  readonly tally = computed(() => {
    const list = this.items();
    return {
      moments: list.length,
      aired: list.filter(a => a.outcome === 'crew').length,
      rejected: list.reduce((n, a) => n + a.drafts.filter(d => !d.approved).length, 0),
      heldBack: list.filter(a => a.outcome === 'data').length
    };
  });

  toggle(): void {
    this.open.update(o => !o);
    if (this.open()) {
      void this.refresh();
      this.timer = setInterval(() => void this.refresh(), REFRESH_MS);
    } else {
      this.stop();
    }
  }

  kindLabel(kind: string): string {
    return kind.replace(/([a-z])([A-Z])/g, '$1 $2');
  }

  ngOnDestroy(): void {
    this.stop();
  }

  private async refresh(): Promise<void> {
    const matchId = this.live.matchId();
    if (!matchId) return;
    try {
      this.items.set(await this.api.crewRoom(matchId));
    } catch {
      // Keep what we have; the next refresh will try again.
    }
  }

  private stop(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
  }
}
