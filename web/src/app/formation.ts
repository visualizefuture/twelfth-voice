import { MatchEvent, MatchInfo, PitchPoint, Team } from './models';
import { PITCH_LENGTH, PITCH_WIDTH, toScreen } from './pitch';

/** One player on the pitch, in screen coordinates (home attacks left to right). */
export interface PlayerToken {
  playerId: string;
  teamId: string;
  home: boolean;
  x: number;
  y: number;
  onBall: boolean;
}

/** Who is on the pitch for one team, and where each of them lines up. */
type Shape = Map<string, PitchPoint>;

/** Events after which the event's team does NOT have the ball (they were defending). */
const DEFENSIVE = new Set(['Pressure', 'Foul', 'Save', 'Block', 'Clearance']);

/**
 * Synthetic tracking for all 22 players. The simulator only records where the ball and the
 * player on it are, so everyone else is placed from their formation slot, shifted with the
 * ball the way real teams move: the whole block slides up and down the pitch, narrows towards
 * the ball, pushes on in possession and drops off without it. Substitutions and red cards
 * change who is out there.
 */
export function playerTokens(info: MatchInfo, events: MatchEvent[], action: MatchEvent | null): PlayerToken[] {
  const shapes = new Map<string, Shape>([
    [info.home.id, startingShape(info.home)],
    [info.away.id, startingShape(info.away)]
  ]);
  if (shapes.get(info.home.id)!.size === 0) return [];

  for (const e of events) {
    if (!e.teamId || !e.playerId) continue;
    const shape = shapes.get(e.teamId);
    if (!shape) continue;
    if (e.type === 'Substitution' && e.relatedPlayerId) {
      const slot = shape.get(e.playerId);
      shape.delete(e.playerId);
      if (slot) shape.set(e.relatedPlayerId, slot);
    } else if (e.type === 'RedCard') {
      shape.delete(e.playerId);
    }
  }

  const homeId = info.home.id;
  const ball = action ? (action.end ?? action.start) : undefined;
  const ballScreen: PitchPoint = ball && action ? toScreen(ball, action.teamId, homeId) : { x: PITCH_LENGTH / 2, y: PITCH_WIDTH / 2 };
  const inPossession = possessionTeam(action, info);
  const placed = new Map<string, PitchPoint>(); // players pinned by the current action, screen coordinates

  if (action?.playerId && action.start) {
    const atStart = toScreen(action.start, action.teamId, homeId);
    const movedWithBall = action.end && action.outcome === 'Success' && (action.type === 'Carry' || action.type === 'Dribble');
    placed.set(action.playerId, movedWithBall ? toScreen(action.end!, action.teamId, homeId) : atStart);
    if (action.type === 'Pass' && action.outcome === 'Success' && action.relatedPlayerId && action.end) {
      placed.set(action.relatedPlayerId, toScreen(action.end, action.teamId, homeId));
    }
  }

  const tokens: PlayerToken[] = [];
  for (const [teamId, shape] of shapes) {
    const home = teamId === homeId;
    const ballInFrame = fromScreen(ballScreen, home);
    const attacking = inPossession === teamId;

    for (const [playerId, base] of shape) {
      let point: PitchPoint;
      const pinned = placed.get(playerId);
      if (pinned) {
        point = pinned;
      } else {
        const keeper = base.x < 10;
        const slide = keeper ? 0.12 : 0.5;
        const push = keeper ? 0 : attacking ? 5 : -4;
        const narrow = keeper ? 0.1 : attacking ? 0.18 : 0.32;
        const wobble = jitter(playerId, action?.sequence ?? 0);
        const x = clamp(base.x + (ballInFrame.x - PITCH_LENGTH / 2) * slide + push + wobble.x, 2, keeper ? 18 : 101);
        const y = clamp(base.y + (ballInFrame.y - base.y) * narrow + wobble.y, 2, PITCH_WIDTH - 2);
        point = toScreen({ x, y }, teamId, homeId);
      }
      tokens.push({ playerId, teamId, home, x: point.x, y: point.y, onBall: playerId === action?.playerId });
    }
  }
  return tokens;
}

function startingShape(team: Team): Shape {
  return new Map((team.lineup ?? []).map(slot => [slot.playerId, slot.base] as [string, PitchPoint]));
}

function possessionTeam(action: MatchEvent | null, info: MatchInfo): string | undefined {
  if (!action?.teamId) return undefined;
  if (!DEFENSIVE.has(action.type)) return action.teamId;
  return action.teamId === info.home.id ? info.away.id : info.home.id;
}

/** Inverse of toScreen: a screen point in the given team's attacking frame. */
function fromScreen(p: PitchPoint, home: boolean): PitchPoint {
  return home ? { x: p.x, y: PITCH_WIDTH - p.y } : { x: PITCH_LENGTH - p.x, y: p.y };
}

/** A small, stable wobble per player and moment, so the shape breathes instead of moving in lockstep. */
function jitter(playerId: string, sequence: number): PitchPoint {
  let h = 2166136261;
  const key = `${playerId}:${Math.floor(sequence / 3)}`;
  for (let i = 0; i < key.length; i++) {
    h ^= key.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  const a = ((h >>> 0) % 1000) / 1000;
  const b = (((h >>> 10) >>> 0) % 1000) / 1000;
  return { x: (a - 0.5) * 3, y: (b - 0.5) * 3 };
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}
