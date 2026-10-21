import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../api.service';
import { asAwayTeam } from '../kits';
import { LiveService } from '../live.service';
import { SoundService } from '../sound.service';
import { CrestComponent } from './crest';
import { LineupCardComponent } from './lineup-card';
import { MatchSummary, Team } from '../models';

const CONNECT_ATTEMPTS = 20;
const CONNECT_INTERVAL_MS = 1500;
const delay = (ms: number) => new Promise<void>(resolve => setTimeout(resolve, ms));

/** Pick a fixture and kick off, or join a match that's already running. */
@Component({
  selector: 'tv-setup',
  imports: [FormsModule, CrestComponent, LineupCardComponent],
  template: `
    <section class="setup">
      <div class="intro">
        <h1 class="display">Twelfth Voice</h1>
        <p class="lede">Every fan gets their own match. Pick two clubs, kick off, and watch the game explained your way.</p>
      </div>

      @if (live.notice(); as notice) {
        <p class="notice" role="status">{{ notice }}</p>
      }

      <form class="fixture" (ngSubmit)="kickOff()">
        <div class="teams">
          <label>
            <span>Home</span>
            <span class="pick">
              <tv-crest [team]="homeTeam()" [size]="34" />
              <select name="home" [ngModel]="homeId()" (ngModelChange)="homeId.set($event)" required>
                @for (team of teams(); track team.id) {
                  <option [value]="team.id">{{ team.name }}</option>
                }
              </select>
            </span>
          </label>
          <span class="versus display">v</span>
          <label>
            <span>Away</span>
            <span class="pick">
              <tv-crest [team]="awayTeam()" [size]="34" />
              <select name="away" [ngModel]="awayId()" (ngModelChange)="awayId.set($event)" required>
                @for (team of teams(); track team.id) {
                  <option [value]="team.id" [disabled]="team.id === homeId()">{{ team.name }}</option>
                }
              </select>
            </span>
          </label>
        </div>

        <div class="options">
          <label>
            <span>Match seed</span>
            <input name="seed" type="number" min="1" [(ngModel)]="seed">
          </label>
          <label>
            <span>Speed</span>
            <select name="speed" [(ngModel)]="speed">
              <option [ngValue]="30">30× (half a minute per second)</option>
              <option [ngValue]="60">60× (a minute per second)</option>
              <option [ngValue]="120">120×</option>
              <option [ngValue]="300">300× (whole match in about 20 seconds)</option>
            </select>
          </label>
        </div>

        <button class="primary kick" type="submit" [disabled]="busy() || connecting() || teams().length === 0 || homeId() === awayId()">
          {{ busy() ? 'Starting…' : 'Kick off' }}
        </button>
        @if (connecting()) {
          <p class="muted" role="status">Connecting to the match server…</p>
        }
        @if (error()) {
          <p class="error" role="alert">{{ error() }}</p>
        }
      </form>

      @if (homeTeam() && awayTeam()) {
        <div class="sheets">
          <tv-lineup-card [team]="homeTeam()" />
          <tv-lineup-card [team]="awayTeam()" />
        </div>
      }

      @if (liveMatches().length > 0) {
        <div class="running">
          <h2 class="display">Already playing</h2>
          <ul>
            @for (m of liveMatches(); track m.matchId) {
              <li>
                <span>{{ m.homeTeam }} {{ m.homeScore }}–{{ m.awayScore }} {{ m.awayTeam }}</span>
                <span class="muted">{{ m.clock }}, {{ m.status.toLowerCase() }}</span>
                <button type="button" (click)="watch(m.matchId)">Watch</button>
              </li>
            }
          </ul>
        </div>
      }
    </section>
  `,
  styles: `
    .setup { max-width: 1080px; margin: 0 auto; padding: 56px 20px; }
    .fixture { max-width: 720px; }
    .pick { display: flex; gap: 10px; align-items: center; }
    .pick select { flex: 1; }
    .sheets { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; margin-top: 36px; }
    @media (max-width: 900px) { .sheets { grid-template-columns: 1fr; } }
    h1 { font-size: var(--step-3); margin: 0; line-height: 1; }
    .lede { color: var(--moss); font-size: var(--step-1); max-width: 34ch; margin: 12px 0 36px; }
    .fixture { display: grid; gap: 20px; }
    .teams { display: grid; grid-template-columns: 1fr auto 1fr; gap: 14px; align-items: end; }
    .versus { font-size: var(--step-2); color: var(--moss); padding-bottom: 4px; }
    .options { display: grid; grid-template-columns: 1fr 2fr; gap: 14px; }
    label { display: grid; gap: 6px; }
    label span { font-size: var(--step--1); color: var(--moss); }
    .kick { justify-self: start; padding: 10px 28px; font-size: var(--step-1); font-family: var(--display); }
    .error { color: var(--red-card); margin: 0; }
    .notice { margin: -16px 0 28px; padding: 10px 14px; border-left: 3px solid var(--floodlight); background: var(--night-2); }
    .running { margin-top: 48px; border-top: 1px solid var(--hairline); padding-top: 20px; }
    .running h2 { font-size: var(--step-1); margin: 0 0 12px; }
    .running ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 10px; }
    .running li { display: grid; grid-template-columns: 1fr auto auto; gap: 14px; align-items: center; }
    @media (max-width: 560px) {
      .teams { grid-template-columns: 1fr; }
      .versus { display: none; }
      .options { grid-template-columns: 1fr; }
      .running li { grid-template-columns: 1fr auto; }
    }
  `
})
export class SetupComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly live = inject(LiveService);
  private readonly sound = inject(SoundService);

  readonly teams = signal<Team[]>([]);
  readonly liveMatches = signal<MatchSummary[]>([]);
  readonly busy = signal(false);
  readonly connecting = signal(false);
  readonly error = signal<string | null>(null);

  readonly homeId = signal('HAR');
  readonly awayId = signal('IRV');
  readonly homeTeam = computed(() => this.teams().find(t => t.id === this.homeId()));
  readonly awayTeam = computed(() => {
    const away = this.teams().find(t => t.id === this.awayId());
    return away ? asAwayTeam(away, this.homeTeam()) : undefined;
  });
  seed = 7;
  speed = 60;

  async ngOnInit(): Promise<void> {
    // The API may still be starting (F5 launches it alongside this app, and a cloud host may be
    // waking up), so keep asking for a while before giving up.
    this.connecting.set(true);
    for (let attempt = 1; attempt <= CONNECT_ATTEMPTS; attempt++) {
      try {
        this.teams.set(await this.api.teams());
        const list = await this.api.matches();
        this.liveMatches.set(list.live.filter(m => m.status !== 'Finished'));
        this.connecting.set(false);
        return;
      } catch {
        if (attempt < CONNECT_ATTEMPTS) await delay(CONNECT_INTERVAL_MS);
      }
    }
    this.connecting.set(false);
    this.error.set('Can\'t reach the Twelfth Voice API. Start TwelfthVoice.Api (it listens on port 5080) and refresh.');
  }

  async kickOff(): Promise<void> {
    this.sound.unlock(); // browsers only allow audio that starts from a click
    this.busy.set(true);
    this.error.set(null);
    try {
      const match = await this.api.createMatch({ homeTeamId: this.homeId(), awayTeamId: this.awayId(), seed: this.seed, speed: this.speed });
      await this.live.join(match.matchId);
    } catch {
      this.error.set(this.live.error() ?? 'The match couldn\'t start. Check the API is running and try again.');
    } finally {
      this.busy.set(false);
    }
  }

  async watch(matchId: string): Promise<void> {
    this.sound.unlock();
    this.busy.set(true);
    try {
      await this.live.join(matchId);
    } catch {
      this.error.set('That match is no longer running. Kick off a new one.');
    } finally {
      this.busy.set(false);
    }
  }
}
