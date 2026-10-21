import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { LiveService } from './live.service';
import { MatchEvent } from './models';

/** One goal's build-up, played back on the pitch. */
export interface Replay {
  goal: MatchEvent;
  frames: MatchEvent[];
  index: number;
  label: string;
}

const REPLAY_KEY = 'twelfth-voice.auto-replay';
/** How far back a replay starts, in match seconds. */
const BUILD_UP_SECONDS = 14;
const MIN_FRAMES = 5;
const MAX_FRAMES = 16;
/** Real milliseconds per action: slow motion. */
const FRAME_MS = 850;
const HOLD_GOAL_MS = 2200;
/** Wait for the goal call before the automatic replay rolls. */
const AUTO_DELAY_MS = 5500;

/** Events that move the ball or a player somewhere worth showing. */
const SHOWN = new Set(['KickOff', 'Pass', 'Carry', 'Dribble', 'Shot', 'Goal', 'Tackle', 'Interception', 'Clearance', 'Block', 'Save',
  'Corner', 'FreeKick', 'ThrowIn', 'GoalKick', 'Pressure', 'Foul']);

/**
 * Goal replays. Every action carries where the ball and the player on it were, and the other 20
 * players are placed from that (see formation.ts), so a goal's build-up can be played back
 * exactly from the event log, in slow motion. The match pauses for the replay and picks up after.
 */
@Injectable({ providedIn: 'root' })
export class ReplayService {
  private readonly live = inject(LiveService);
  private timer: ReturnType<typeof setTimeout> | null = null;
  private autoTimer: ReturnType<typeof setTimeout> | null = null;
  private resumeAfter = false;

  readonly active = signal<Replay | null>(null);
  readonly auto = signal(loadAuto());

  /** The goals so far, oldest first. */
  readonly goals = computed(() => this.live.events().filter(e => e.type === 'Goal'));

  /** The action the pitch should show right now during a replay. */
  readonly frame = computed(() => {
    const replay = this.active();
    return replay ? replay.frames[replay.index] : null;
  });

  constructor() {
    // Leaving the match ends any replay.
    effect(() => {
      if (!this.live.matchId()) untracked(() => this.reset());
    });

    // Roll the replay automatically after each goal scored live, once the goal call has had its moment.
    effect(() => {
      const goal = this.live.lastLiveGoal();
      if (!goal) return;
      untracked(() => {
        if (this.autoTimer) clearTimeout(this.autoTimer);
        if (!this.auto()) return;
        this.autoTimer = setTimeout(() => {
          this.autoTimer = null;
          if (this.live.matchId() === goal.matchId && !this.active()) void this.play(goal);
        }, AUTO_DELAY_MS);
      });
    });
  }

  setAuto(on: boolean): void {
    this.auto.set(on);
    try {
      localStorage.setItem(REPLAY_KEY, JSON.stringify(on));
    } catch {
      // storage unavailable
    }
  }

  async play(goal: MatchEvent): Promise<void> {
    this.stop();
    const frames = buildUp(this.live.events(), goal);
    if (frames.length === 0) return;

    const scorer = goal.playerId ? this.live.players().get(goal.playerId)?.player : undefined;
    const label = `${minute(goal)} ${scorer?.shortName ?? 'Goal'}`;
    this.active.set({ goal, frames, index: 0, label });

    if (this.live.summary()?.status === 'Live') {
      this.resumeAfter = true;
      try {
        await this.live.pause();
      } catch {
        this.resumeAfter = false;
      }
    }
    this.schedule();
  }

  /** Ends the replay now and puts the live match back on. */
  stop(): void {
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
    if (!this.active()) return;
    this.active.set(null);
    if (this.resumeAfter) {
      this.resumeAfter = false;
      void this.live.resume().catch(() => undefined);
    }
  }

  /** Forget everything, e.g. when leaving the match. */
  reset(): void {
    if (this.autoTimer) clearTimeout(this.autoTimer);
    this.autoTimer = null;
    this.resumeAfter = false;
    this.stop();
  }

  private schedule(): void {
    const replay = this.active();
    if (!replay) return;
    const last = replay.index >= replay.frames.length - 1;
    this.timer = setTimeout(() => {
      const current = this.active();
      if (!current) return;
      if (current.index >= current.frames.length - 1) {
        this.stop();
        return;
      }
      this.active.set({ ...current, index: current.index + 1 });
      this.schedule();
    }, last ? HOLD_GOAL_MS : FRAME_MS);
  }
}

/** The actions leading up to a goal, ending with the goal itself. */
export function buildUp(events: MatchEvent[], goal: MatchEvent): MatchEvent[] {
  const before = events.filter(e =>
    e.sequence < goal.sequence && e.period === goal.period && SHOWN.has(e.type) && (e.start || e.end));
  let frames = before.filter(e => e.matchSecond >= goal.matchSecond - BUILD_UP_SECONDS);
  if (frames.length < MIN_FRAMES) frames = before.slice(-MIN_FRAMES);
  frames = frames.slice(-MAX_FRAMES);
  return [...frames, goal];
}

/** "23'", "45+2'", "90+4'" (match seconds count from kick-off across both halves). */
export function minute(e: MatchEvent): string {
  const end = e.period <= 1 ? 2700 : 5400;
  if (e.matchSecond > end) return `${end / 60}+${Math.ceil((e.matchSecond - end) / 60)}'`;
  return `${Math.floor(e.matchSecond / 60) + 1}'`;
}

function loadAuto(): boolean {
  try {
    const raw = localStorage.getItem(REPLAY_KEY);
    return raw === null ? true : JSON.parse(raw) === true;
  } catch {
    return true;
  }
}
