import { Component, computed, inject, output } from '@angular/core';
import { LiveService } from '../live.service';
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

      <label>
        <span>Your club</span>
        <select (change)="setFavorite($event)">
          <option value="">No favourite</option>
          @if (live.info(); as info) {
            <option [value]="info.home.id" [selected]="profile().favoriteTeamId === info.home.id">{{ info.home.name }}</option>
            <option [value]="info.away.id" [selected]="profile().favoriteTeamId === info.away.id">{{ info.away.name }}</option>
          }
        </select>
        <small class="muted">Their moments show up more, and are highlighted.</small>
      </label>

      <label>
        <span>Follow a player</span>
        <select (change)="setFocus($event)">
          <option value="">Whole match</option>
          @if (live.info(); as info) {
            <optgroup [label]="info.home.name">
              @for (p of info.home.squad; track p.id) {
                <option [value]="p.id" [selected]="profile().focusPlayerId === p.id">{{ p.shirtNumber }}. {{ p.name }} ({{ p.position }})</option>
              }
            </optgroup>
            <optgroup [label]="info.away.name">
              @for (p of info.away.squad; track p.id) {
                <option [value]="p.id" [selected]="profile().focusPlayerId === p.id">{{ p.shirtNumber }}. {{ p.name }} ({{ p.position }})</option>
              }
            </optgroup>
          }
        </select>
        <small class="muted">The feed follows them, and the pitch shows their heat map.</small>
      </label>

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

  setFavorite(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    void this.live.updateProfile({ favoriteTeamId: value || null });
  }

  setFocus(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    void this.live.updateProfile({ focusPlayerId: value || null });
  }

  setLanguage(event: Event): void {
    void this.live.updateProfile({ language: (event.target as HTMLSelectElement).value });
  }
}
