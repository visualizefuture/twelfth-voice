import { Component, computed, inject, output } from '@angular/core';
import { LiveService } from '../live.service';
import { ReplayService } from '../replay.service';
import { SoundService } from '../sound.service';
import { ViewerMode } from '../models';

/** "Your view": the same match, shaped for you. */
@Component({
  selector: 'tv-viewer-settings',
  template: `
    <aside role="dialog" aria-modal="true" aria-labelledby="view-title">
      <header>
        <h2 id="view-title" class="display">Your view</h2>
        <button type="button" (click)="closed.emit()">Done</button>
      </header>

      <fieldset>
        <legend>How much detail</legend>
        <div class="segmented">
          <button type="button" [attr.aria-pressed]="profile().mode === 'Casual'" (click)="setMode('Casual')">
            <b>Fan</b><span>The big moments, in plain words</span>
          </button>
          <button type="button" [attr.aria-pressed]="profile().mode === 'Analyst'" (click)="setMode('Analyst')">
            <b>Analyst</b><span>Every insight, with the numbers</span>
          </button>
        </div>
      </fieldset>

      <p class="hint muted">Support a club in the <b>Teams</b> tab and follow a player in the <b>Players</b> tab.</p>

      <label>
        <span>Language</span>
        <select (change)="setLanguage($event)">
          @for (l of languages; track l.code) {
            <option [value]="l.code" [selected]="profile().language === l.code">{{ l.name }}</option>
          }
        </select>
        <small class="muted">The AI crew translates commentary and answers, then re-checks every number.</small>
      </label>

      <fieldset class="sound">
        <legend>Replays</legend>
        <label class="check">
          <input type="checkbox" [checked]="replay.auto()" (change)="replay.setAuto(checked($event))" />
          <span>Replay every goal automatically</span>
        </label>
        <small class="muted">The build-up in slow motion, rebuilt from the event data. The match pauses for it and picks up after.</small>
      </fieldset>

      <fieldset class="sound">
        <legend>Sound</legend>
        <label class="check">
          <input type="checkbox" [checked]="sound.settings().enabled" (change)="sound.update({ enabled: checked($event) })" />
          <span>Crowd and the goal call</span>
        </label>
        <label class="check">
          <input type="checkbox" [checked]="sound.settings().commentary" [disabled]="!sound.settings().enabled"
                 (change)="sound.update({ commentary: checked($event) })" />
          <span>Read the commentary aloud</span>
        </label>
        <small class="muted">
          @if (sound.cloudVoice()) {
            Neural voices from Azure AI Speech, in your language. Only lines the crew has checked are spoken.
          } @else {
            Azure AI Speech isn't set up on this server, so your browser's own voice is used for goals.
          }
        </small>
      </fieldset>
    </aside>
  `,
  styles: `
    aside {
      position: fixed; top: 0; right: 0; bottom: 0; width: min(380px, 100%); z-index: 10;
      background: var(--night-2); border-left: 1px solid var(--hairline); padding: 20px; overflow-y: auto;
      display: grid; gap: 22px; align-content: start;
    }
    header { display: flex; justify-content: space-between; align-items: center; }
    h2 { margin: 0; font-size: var(--step-2); }
    fieldset { border: 0; padding: 0; margin: 0; }
    legend, label > span { font-size: var(--step--1); color: var(--moss); margin-bottom: 6px; }
    label { display: grid; gap: 6px; }
    small { font-size: var(--step--1); }
    .segmented { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
    .hint { margin: -8px 0 0; font-size: var(--step--1); }
    .hint b { color: var(--chalk); font-weight: 600; }
    .sound { display: grid; gap: 8px; }
    .check { display: flex; gap: 10px; align-items: center; }
    .check input { width: auto; }
    .check span { font-size: var(--step-0); color: inherit; margin: 0; }
    .segmented button { display: grid; gap: 2px; text-align: left; padding: 10px; }
    .segmented button[aria-pressed="true"] { border-color: var(--floodlight); background: var(--turf); }
    .segmented b { font-family: var(--display); font-size: var(--step-1); }
    .segmented span { font-size: var(--step--1); color: var(--moss); }
  `
})
export class ViewerSettingsComponent {
  readonly live = inject(LiveService);
  readonly sound = inject(SoundService);
  readonly replay = inject(ReplayService);
  readonly closed = output<void>();
  readonly profile = computed(() => this.live.profile());

  readonly languages = [
    { code: 'en', name: 'English' },
    { code: 'es', name: 'Español' },
    { code: 'fr', name: 'Français' },
    { code: 'pt', name: 'Português' },
    { code: 'ar', name: 'العربية' },
    { code: 'hi', name: 'हिन्दी' },
    { code: 'ja', name: '日本語' },
    { code: 'de', name: 'Deutsch' },
    { code: 'it', name: 'Italiano' }
  ];

  checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  setMode(mode: ViewerMode): void {
    void this.live.updateProfile({ mode });
  }

  setLanguage(event: Event): void {
    void this.live.updateProfile({ language: (event.target as HTMLSelectElement).value });
  }
}
