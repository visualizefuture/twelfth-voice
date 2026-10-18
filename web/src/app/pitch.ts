import { MatchEvent, PitchPoint } from './models';

export const PITCH_LENGTH = 105;
export const PITCH_WIDTH = 68;

/**
 * Event coordinates are in the attacking frame of the event's team. On screen the home team
 * always attacks left to right, so away-team points are mirrored. Screen y grows downwards.
 */
export function toScreen(p: PitchPoint, teamId: string | undefined, homeTeamId: string): PitchPoint {
  return !teamId || teamId === homeTeamId
    ? { x: p.x, y: PITCH_WIDTH - p.y }
    : { x: PITCH_LENGTH - p.x, y: p.y };
}

/** Percent offsets inside a container with the pitch's aspect ratio. */
export function toPercent(p: PitchPoint): { left: number; top: number } {
  return { left: (p.x / PITCH_LENGTH) * 100, top: (p.y / PITCH_WIDTH) * 100 };
}

/** Where the ball ends up after an event. */
export function ballPosition(e: MatchEvent, homeTeamId: string): PitchPoint | null {
  const point = e.end ?? e.start;
  return point ? toScreen(point, e.teamId, homeTeamId) : null;
}

/** Short on-pitch caption for the player tag, e.g. "Pass 24 m" or "Shot 104 km/h". */
export function actionLabel(e: MatchEvent): string {
  const round = (n: number | undefined) => Math.round(n ?? 0);
  switch (e.type) {
    case 'Pass':
      return e.outcome === 'Success' ? `Pass ${round(e.distanceM)} m` : 'Pass lost';
    case 'Carry': return `Carry ${round(e.distanceM)} m`;
    case 'Dribble': return e.outcome === 'Success' ? 'Beats his man' : 'Dribble stopped';
    case 'Shot': return `Shot ${round(e.ballSpeedKmh)} km/h`;
    case 'Goal': return 'Goal';
    case 'Save': return 'Save';
    case 'Block': return 'Block';
    case 'Tackle': return 'Tackle won';
    case 'Interception': return 'Interception';
    case 'Clearance': return 'Clearance';
    case 'Pressure': return 'Pressing';
    case 'Foul': return 'Foul';
    case 'Sprint': return `${(e.playerSpeedKmh ?? 0).toFixed(1)} km/h`;
    case 'Corner': return 'Corner';
    case 'FreeKick': return 'Free kick';
    case 'ThrowIn': return 'Throw-in';
    case 'GoalKick': return 'Goal kick';
    case 'KickOff': return 'Kick-off';
    case 'Offside': return 'Offside';
    default: return '';
  }
}

/** Events whose location reflects where a player touched the ball (used for heat maps). */
export const TOUCH_TYPES = new Set(['Pass', 'Carry', 'Dribble', 'Shot', 'Tackle', 'Interception', 'Clearance', 'Block', 'Pressure']);
