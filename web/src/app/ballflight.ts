import { MatchEvent, PitchPoint } from './models';
import { toScreen } from './pitch';

/**
 * How the ball travels for one action: a curved path along the ground (players bend passes and
 * shots) and, for long balls, a lift into the air and back down. Everything is in pitch metres,
 * in screen orientation (home attacks left to right).
 */
export interface Flight {
  from: PitchPoint;
  to: PitchPoint;
  /** Quadratic Bézier control point for the bend. */
  control: PitchPoint;
  /** Peak height in metres (0 = along the ground). */
  height: number;
  /** Real milliseconds the ball takes at normal playback. */
  ms: number;
  kind: 'ok' | 'fail' | 'shot' | 'carry';
}

/** Screen units the ball is drawn above its shadow per metre of height. */
export const LIFT_PER_METRE = 0.55;

export function flightFor(e: MatchEvent | null, homeTeamId: string): Flight | null {
  if (!e?.start || !e.end || !['Pass', 'Carry', 'Shot', 'Dribble'].includes(e.type)) return null;
  const from = toScreen(e.start, e.teamId, homeTeamId);
  const to = toScreen(e.end, e.teamId, homeTeamId);
  const dx = to.x - from.x;
  const dy = to.y - from.y;
  const distance = Math.hypot(dx, dy);
  if (distance < 0.5) return null;

  const ground = e.type === 'Carry' || e.type === 'Dribble';
  const shot = e.type === 'Shot';
  // A stable "random" per event, so the same action always bends the same way.
  const r = unit(e.sequence);
  const side = r < 0.5 ? -1 : 1;

  let bend = 0;
  let height = 0;
  if (shot) {
    bend = Math.min(distance * 0.07, 2.2) * (0.4 + r);
    height = distance > 14 ? Math.min(distance * 0.06, 1.8) : 0.4;
  } else if (!ground) {
    bend = Math.min(distance * 0.09, 4) * (0.3 + r);
    // Long passes and switches go in the air; short ones stay on the grass.
    height = distance > 24 ? Math.min(2 + (distance - 24) * 0.22, 9) : distance > 16 ? 0.8 : 0;
    if ((e.distanceM ?? 0) > 0 && e.qualifiers?.some(q => /cross|long|lofted|chip/i.test(q))) height = Math.max(height, 4);
  }

  // Perpendicular to the line of the kick.
  const nx = -dy / distance;
  const ny = dx / distance;
  const mid = { x: (from.x + to.x) / 2, y: (from.y + to.y) / 2 };
  const control = { x: mid.x + nx * bend * 2 * side, y: mid.y + ny * bend * 2 * side };

  const speed = shot ? 0.45 : ground ? 0.9 : height > 2 ? 0.7 : 0.55;
  const ms = Math.min(1300, 260 + distance * 22 * speed);
  const kind = shot ? 'shot' : ground ? 'carry' : e.outcome === 'Fail' ? 'fail' : 'ok';
  return { from, to, control, height, ms, kind };
}

/** Where the ball is at t (0..1): its shadow on the grass and its height above it. */
export function pointAt(f: Flight, t: number): { ground: PitchPoint; height: number } {
  const u = 1 - t;
  const ground = {
    x: u * u * f.from.x + 2 * u * t * f.control.x + t * t * f.to.x,
    y: u * u * f.from.y + 2 * u * t * f.control.y + t * t * f.to.y
  };
  return { ground, height: 4 * f.height * t * (1 - t) };
}

/** The path the viewer sees the ball take (lifted by its height), as SVG path data. */
export function flightPath(f: Flight, steps = 20): string {
  const parts: string[] = [];
  for (let i = 0; i <= steps; i++) {
    const { ground, height } = pointAt(f, i / steps);
    parts.push(`${i === 0 ? 'M' : 'L'}${ground.x.toFixed(2)},${(ground.y - height * LIFT_PER_METRE).toFixed(2)}`);
  }
  return parts.join(' ');
}

/** The shadow's path on the grass, drawn faintly under lofted balls. */
export function groundPath(f: Flight): string {
  return `M${f.from.x.toFixed(2)},${f.from.y.toFixed(2)} Q${f.control.x.toFixed(2)},${f.control.y.toFixed(2)} ${f.to.x.toFixed(2)},${f.to.y.toFixed(2)}`;
}

/** Ease-out: the ball leaves the boot fast and slows a little. */
export function ease(t: number): number {
  return 1 - Math.pow(1 - t, 1.6);
}

function unit(seed: number): number {
  let h = Math.imul(seed ^ 0x9e3779b9, 0x85ebca6b);
  h ^= h >>> 13;
  h = Math.imul(h, 0xc2b2ae35);
  h ^= h >>> 16;
  return (h >>> 0) / 4294967296;
}
