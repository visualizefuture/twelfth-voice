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
      <header class="hero">
        <h1 class="display">Twelfth Voice</h1>
        <p class="lede">Every fan gets their own match. Pick two clubs, kick off, and watch the game explained your way.</p>
      </header>

      @if (live.notice(); as notice) {
        <p class="notice" role="status">{{ notice }}</p>
      }

      <form class="fixture" (ngSubmit)="kickOff()">
        <div class="sides">
          <div class="side">
            <label class="pick">
              <span>Home</span>
              <span class="select">
                <tv-crest [team]="homeTeam()" [size]="34" />
                <select name="home" [ngModel]="homeId()" (ngModelChange)="homeId.set($event)" required>
                  @for (team of teams(); track team.id) {
                    <option [value]="team.id">{{ team.name }}</option>
                  }
                </select>
              </span>
            </label>
            @if (homeTeam(); as team) {
              <tv-lineup-card [team]="team" />
            }
          </div>

          <span class="versus display" aria-hidden="true">v</span>

          <div class="side">
            <label class="pick">
              <span>Away</span>
              <span class="select">
                <tv-crest [team]="awayTeam()" [size]="34" />
                <select name="away" [ngModel]="awayId()" (ngModelChange)="awayId.set($event)" required>
                  @for (team of teams(); track team.id) {
                    <option [value]="team.id" [disabled]="team.id === homeId()">{{ team.name }}</option>
                  }
                </select>
              </span>
            </label>
            @if (awayTeam(); as team) {
              <tv-lineup-card [team]="team" />
            }
          </div>
        </div>

        <div class="launch">
          <label class="speed">
            <span>Match speed <b>{{ speedLabel() }}</b></span>
            <input type="range" name="speed" min="0" [max]="speeds.length - 1" step="1"
                   [value]="speedIndex()" (input)="speedIndex.set(+$any($event.target).value)"
                   [attr.aria-valuetext]="speedLabel()" />
            <span class="ticks" aria-hidden="true">
              @for (s of speeds; track s.value) { <i>{{ s.short }}</i> }
            </span>
          </label>

          <details class="more">
            <summary>More options</summary>
            <label>
              <span>Match seed</span>
              <input name="seed" type="number" min="1" placeholder="New match every time" [(ngModel)]="seed">
              <small class="muted">The same seed and clubs always play the same match, handy for demos and replays.</small>
            </label>
          </details>

          <button class="primary kick" type="submit" [disabled]="busy() || connecting() || teams().length === 0 || homeId() === awayId()">
            {{ busy() ? 'Starting…' : 'Kick off' }}
          </button>
          @if (connecting()) {
            <p class="muted" role="status">Connecting to the match server…</p>
          }
          @if (error()) {
            <p class="error" role="alert">{{ error() }}</p>
          }
        </div>
      </form>

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
    .setup { max-width: 1180px; margin: 0 auto; padding: 40px 20px 56px; }
    .hero { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: 8px 32px;
      padding-bottom: 20px; margin-bottom: 28px; border-bottom: 1px solid var(--hairline); }
    h1 { font-size: var(--step-3); margin: 0; line-height: 1; }
    .lede { color: var(--moss); font-size: var(--step-1); margin: 0; max-width: 62ch; }
    .notice { margin: -8px 0 24px; padding: 10px 14px; border-left: 3px solid var(--floodlight); background: var(--night-2); }

    .sides { display: grid; grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr); gap: 20px; align-items: start; }
    .side { display: grid; gap: 14px; align-content: start; }
    .versus { font-size: var(--step-3); color: var(--moss); padding-top: 26px; }
    label { display: grid; gap: 6px; }
    label > span { font-size: var(--step--1); color: var(--moss); }
    .select { display: flex; gap: 10px; align-items: center; }
    .select select { flex: 1; font-size: var(--step-0); }

    .launch { display: grid; gap: 18px; max-width: 560px; margin: 32px auto 0; text-align: center; }
    .speed b { color: var(--chalk); font-weight: 600; margin-left: 6px; }
    .speed input { width: 100%; accent-color: var(--floodlight); }
    .ticks { display: flex; justify-content: space-between; font-size: var(--step--1); color: var(--moss); }
    .ticks i { font-style: normal; }
    .more { text-align: left; }
    .more summary { cursor: pointer; color: var(--moss); font-size: var(--step--1); }
    .more label { margin-top: 10px; }
    .more small { font-size: var(--step--1); }
    .kick { justify-self: center; padding: 12px 48px; font-size: var(--step-2); font-family: var(--display); }
    .error { color: var(--red-card); margin: 0; }

    .running { margin-top: 48px; border-top: 1px solid var(--hairline); padding-top: 20px; }
    .running h2 { font-size: var(--step-1); margin: 0 0 12px; }
    .running ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 10px; }
    .running li { display: grid; grid-template-columns: 1fr auto auto; gap: 14px; align-items: center; }
    @media (max-width: 860px) {
      .sides { grid-template-columns: minmax(0, 1fr); }
      .versus { display: none; }
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
  /** Empty = a new match every time. */
  seed: number | null = null;

  readonly speeds = [
    { value: 30, short: '30×', label: '30×, half a minute per second' },
    { value: 60, short: '60×', label: '60×, a minute per second' },
    { value: 120, short: '120×', label: '120×, two minutes per second' },
    { value: 300, short: '300×', label: '300×, the whole match in about 20 seconds' }
  ];
  readonly speedIndex = signal(1);
  readonly speedLabel = computed(() => this.speeds[this.speedIndex()].label);

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
      const seed = this.seed && this.seed > 0 ? this.seed : Math.floor(Math.random() * 1_000_000) + 1;
      const match = await this.api.createMatch({
        homeTeamId: this.homeId(), awayTeamId: this.awayId(), seed, speed: this.speeds[this.speedIndex()].value
      });
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
