import { Injectable, computed, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { ApiService } from './api.service';
import { SoundService } from './sound.service';
import {
  JoinResult, LiveState, MatchEvent, MatchInfo, MatchSummary, PersonalizedInsight, Player, Team, TimelinePoint, ViewerProfile
} from './models';

export interface ActiveOverlay {
  id: string;
  placement: string;
  insight: PersonalizedInsight;
}

export interface PlayerRef {
  player: Player;
  team: Team;
}

const PROFILE_KEY = 'twelfth-voice.profile';
const MAX_INSIGHTS = 250;
const HISTORY_PAGE = 2000;

const DEFAULT_PROFILE: ViewerProfile = { mode: 'Casual', favoriteTeamId: null, focusPlayerId: null, language: 'en' };

/**
 * Holds the live match state as signals. Fed by the SignalR hub (insight, state, status, event)
 * plus a one-off REST catch-up when joining a match that's already running.
 */
@Injectable({ providedIn: 'root' })
export class LiveService {
  private readonly api = inject(ApiService);
  private readonly sound = inject(SoundService);
  private connection: HubConnection | null = null;
  private readonly overlayTimers = new Map<string, ReturnType<typeof setTimeout>>();
  private seenSequences = new Set<number>();
  /** Crew rewrites for the current view, kept so a feed rebuild doesn't lose ones that arrived first. */
  private crewLines = new Map<string, PersonalizedInsight>();

  readonly info = signal<MatchInfo | null>(null);
  readonly summary = signal<MatchSummary | null>(null);
  readonly state = signal<LiveState | null>(null);
  readonly insights = signal<PersonalizedInsight[]>([]);
  readonly timeline = signal<TimelinePoint[]>([]);
  readonly events = signal<MatchEvent[]>([]);
  readonly lastAction = signal<MatchEvent | null>(null);
  readonly overlays = signal<ActiveOverlay[]>([]);
  readonly connectionState = signal<'idle' | 'connecting' | 'live' | 'reconnecting' | 'offline'>('idle');
  readonly error = signal<string | null>(null);
  /** A message for the fixtures screen, e.g. why the viewer was sent back to it. */
  readonly notice = signal<string | null>(null);
  readonly profile = signal<ViewerProfile>(loadProfile());

  readonly matchId = computed(() => this.info()?.matchId ?? null);

  /** Every player in the match by id, with their team. */
  readonly players = computed(() => {
    const map = new Map<string, PlayerRef>();
    const info = this.info();
    if (!info) return map;
    for (const team of [info.home, info.away]) {
      for (const player of team.squad) map.set(player.id, { player, team });
    }
    return map;
  });

  async join(matchId: string): Promise<void> {
    this.error.set(null);
    this.notice.set(null);
    try {
      const connection = await this.ensureConnected();
      this.reset();
      const result = await connection.invoke<JoinResult | null>('JoinMatch', matchId, this.profile());
      if (!result) throw new MatchUnavailableError();
      this.applyJoin(result);
      await connection.invoke('SubscribeEvents', true);
      await this.loadEventHistory(matchId);
    } catch (err) {
      this.error.set(describe(err));
      throw err;
    }
  }

  leave(): void {
    this.sound.stop();
    void this.connection?.invoke('LeaveMatch').catch(() => undefined);
    this.reset();
    this.info.set(null);
  }

  async updateProfile(changes: Partial<ViewerProfile>): Promise<void> {
    const next: ViewerProfile = { ...this.profile(), ...changes };
    this.profile.set(next);
    saveProfile(next);
    this.crewLines.clear();

    const matchId = this.matchId();
    if (!matchId || this.connection?.state !== HubConnectionState.Connected) return;

    const state = await this.connection.invoke<LiveState>('UpdateProfile', next);
    this.state.set(state);
    // Rebuild the feed so it reflects the new viewpoint straight away.
    const insights = await this.api.insights(matchId, next, MAX_INSIGHTS);
    this.insights.set([...insights].reverse().map(i => this.crewLines.get(i.insightId) ?? i));
  }

  async pause(): Promise<void> {
    const id = this.matchId();
    if (id) this.summary.set(await this.api.pause(id));
  }

  async resume(): Promise<void> {
    const id = this.matchId();
    if (id) this.summary.set(await this.api.resume(id));
  }

  async setSpeed(speed: number): Promise<void> {
    const id = this.matchId();
    if (id) this.summary.set(await this.api.setSpeed(id, speed));
  }

  // ------------------------------------------------------------------ connection

  private async ensureConnected(): Promise<HubConnection> {
    if (this.connection?.state === HubConnectionState.Connected) return this.connection;

    if (!this.connection) {
      const connection = new HubConnectionBuilder()
        .withUrl('/hubs/match')
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();

      connection.on('insight', (insight: PersonalizedInsight) => this.onInsight(insight));
      connection.on('insightUpdate', (insight: PersonalizedInsight) => this.onInsightUpdate(insight));
      connection.on('state', (state: LiveState) => this.onState(state));
      connection.on('status', (summary: MatchSummary) => this.summary.set(summary));
      connection.on('event', (e: MatchEvent) => this.onEvent(e));

      connection.onreconnecting(() => this.connectionState.set('reconnecting'));
      connection.onreconnected(async () => {
        this.connectionState.set('live');
        const id = this.matchId();
        if (!id) return;
        try {
          await this.join(id);
        } catch (err) {
          // The server restarted and the match is gone: back to the fixtures, with an explanation.
          this.leave();
          this.notice.set(err instanceof MatchUnavailableError
            ? 'The match you were watching ended when the server restarted. Kick off a new one.'
            : 'The connection came back, but the match couldn\'t be rejoined. Kick off a new one.');
        }
      });
      connection.onclose(() => this.connectionState.set('offline'));
      this.connection = connection;
    }

    this.connectionState.set('connecting');
    await this.connection.start();
    this.connectionState.set('live');
    return this.connection;
  }

  // ------------------------------------------------------------------ incoming

  private applyJoin(result: JoinResult): void {
    this.info.set(result.info);
    this.summary.set(result.match);
    this.state.set(result.state);
    this.timeline.set(result.timeline);
    this.insights.set([...result.recentInsights].reverse());
    applyTeamColours(result.info);
  }

  private onInsight(insight: PersonalizedInsight): void {
    this.insights.update(list => [insight, ...list].slice(0, MAX_INSIGHTS));
    this.showOverlay(insight);
    this.sound.onInsight(insight, this.profile());
  }

  /** The broadcast crew's fact-checked (and possibly translated) rewrite of a line already on screen. */
  private onInsightUpdate(insight: PersonalizedInsight): void {
    this.crewLines.set(insight.insightId, insight);
    this.insights.update(list => list.map(i => (i.insightId === insight.insightId ? insight : i)));
    this.overlays.update(list => list.map(o => (o.id === insight.insightId ? { ...o, insight } : o)));
  }

  private onState(state: LiveState): void {
    this.state.set(state);
    this.timeline.update(points => {
      const last = points[points.length - 1];
      if (last && last.period === state.period && last.matchSecond === state.matchSecond) return points;
      return [...points, {
        period: state.period,
        matchSecond: state.matchSecond,
        clock: state.clock,
        momentum: state.game.momentum,
        controlIndex: state.game.controlIndex,
        homeScore: state.homeScore,
        awayScore: state.awayScore,
        homeXg: state.home.xg,
        awayXg: state.away.xg
      }];
    });
  }

  private onEvent(e: MatchEvent): void {
    if (this.seenSequences.has(e.sequence)) return;
    this.seenSequences.add(e.sequence);
    if (e.start || e.end) this.lastAction.set(e);
    this.events.update(list => [...list, e]);
  }

  private async loadEventHistory(matchId: string): Promise<void> {
    let after = 0;
    const history: MatchEvent[] = [];
    for (;;) {
      const page = await this.api.events(matchId, after, HISTORY_PAGE);
      history.push(...page.filter(e => e.type !== 'TrackingSnapshot'));
      if (page.length < HISTORY_PAGE) break;
      after = page[page.length - 1].sequence;
    }

    // Live events may have arrived while the history was loading; keep each sequence once.
    const fresh = history.filter(e => !this.seenSequences.has(e.sequence));
    for (const e of fresh) this.seenSequences.add(e.sequence);
    const merged = [...fresh, ...this.events()].sort((a, b) => a.sequence - b.sequence);
    this.events.set(merged);
    if (!this.lastAction()) {
      const last = [...merged].reverse().find(e => e.start || e.end);
      if (last) this.lastAction.set(last);
    }
  }

  // ------------------------------------------------------------------ overlays

  /** Shows an insight as a broadcast graphic, one per screen region, higher priority wins. */
  private showOverlay(insight: PersonalizedInsight): void {
    const always = ['Goal', 'HalfTime', 'FullTime', 'Kickoff'].includes(insight.kind);
    if (!always && insight.relevance < 0.45) return;

    const placement = insight.overlay.placement;
    const current = this.overlays().find(o => o.placement === placement);
    if (current && current.insight.overlay.priority > insight.overlay.priority) return;

    const overlay: ActiveOverlay = { id: insight.insightId, placement, insight };
    this.overlays.update(list => [...list.filter(o => o.placement !== placement), overlay]);

    const previousTimer = this.overlayTimers.get(placement);
    if (previousTimer) clearTimeout(previousTimer);
    this.overlayTimers.set(placement, setTimeout(() => {
      this.overlays.update(list => list.filter(o => o.id !== overlay.id));
      this.overlayTimers.delete(placement);
    }, Math.max(3, insight.overlay.displaySeconds) * 1000));
  }

  private reset(): void {
    for (const timer of this.overlayTimers.values()) clearTimeout(timer);
    this.overlayTimers.clear();
    this.seenSequences = new Set<number>();
    this.crewLines.clear();
    this.summary.set(null);
    this.state.set(null);
    this.insights.set([]);
    this.timeline.set([]);
    this.events.set([]);
    this.lastAction.set(null);
    this.overlays.set([]);
  }
}

/** The match isn't running on the server any more (finished long ago, or the server restarted). */
export class MatchUnavailableError extends Error {
  constructor() {
    super('That match is no longer running. Kick off a new one.');
    this.name = 'MatchUnavailableError';
  }
}

function applyTeamColours(info: MatchInfo): void {
  const root = document.documentElement.style;
  const home = info.home.matchKit;
  const away = info.away.matchKit;
  root.setProperty('--home', home ? readable(home.shirt, home.trim) : readable(info.home.primaryColor, info.home.secondaryColor));
  root.setProperty('--away', away ? readable(away.shirt, away.trim) : readable(info.away.primaryColor, info.away.secondaryColor));
}

/** Club primaries can be very dark (navy, black); use the secondary colour when the primary won't show on the night pitch. */
function readable(primary: string, secondary: string): string {
  const hex = primary.replace('#', '');
  if (hex.length !== 6) return primary;
  const r = parseInt(hex.slice(0, 2), 16);
  const g = parseInt(hex.slice(2, 4), 16);
  const b = parseInt(hex.slice(4, 6), 16);
  const luminance = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;
  return luminance < 0.35 ? secondary : primary;
}

function loadProfile(): ViewerProfile {
  try {
    const raw = localStorage.getItem(PROFILE_KEY);
    return raw ? { ...DEFAULT_PROFILE, ...JSON.parse(raw) } : { ...DEFAULT_PROFILE };
  } catch {
    return { ...DEFAULT_PROFILE };
  }
}

function saveProfile(profile: ViewerProfile): void {
  try {
    localStorage.setItem(PROFILE_KEY, JSON.stringify(profile));
  } catch {
    // Storage unavailable (private mode); the profile still applies for this session.
  }
}

function describe(err: unknown): string {
  if (err instanceof Error) return err.message;
  return 'Something went wrong connecting to the match.';
}
