import { Kit, Player, Team } from './models';

/** The kit a player wears in this match: the keeper's kit for goalkeepers, otherwise the team's match kit. */
export function kitFor(team: Team | null | undefined, player: Player | null | undefined): Kit | undefined {
  if (!team) return undefined;
  if (player?.position === 'GK' && team.goalkeeperKit) return team.goalkeeperKit;
  return team.matchKit ?? team.homeKit;
}

/** Relative luminance (0 dark .. 1 light) of a #RRGGBB colour. */
export function luminance(hex: string): number {
  const h = hex.replace('#', '');
  if (h.length !== 6) return 0.5;
  const channel = (i: number) => {
    const c = parseInt(h.slice(i, i + 2), 16) / 255;
    return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
  };
  return 0.2126 * channel(0) + 0.7152 * channel(2) + 0.0722 * channel(4);
}

/** A colour that stands out on the dark pitch: the shirt, or the trim when the shirt is too dark. */
export function accentOnPitch(kit: Kit | undefined, fallback: string): string {
  if (!kit) return fallback;
  return luminance(kit.shirt) < 0.06 ? kit.trim : kit.shirt;
}

/** "Redmean" colour distance, the same rule the simulator uses to decide on away kits. */
function colourDistance(a: string, b: string): number {
  const p = (h: string) => [1, 3, 5].map(i => parseInt(h.slice(i, i + 2), 16));
  const [r1, g1, b1] = p(a);
  const [r2, g2, b2] = p(b);
  const r = (r1 + r2) / 2;
  return Math.sqrt((2 + r / 256) * (r1 - r2) ** 2 + 4 * (g1 - g2) ** 2 + (2 + (255 - r) / 256) * (b1 - b2) ** 2);
}

/** For the fixture preview: the away side in its away kit when the home shirts clash. */
export function asAwayTeam(away: Team, home: Team | undefined): Team {
  if (!home?.homeKit || !away.homeKit || !away.awayKit) return away;
  return colourDistance(away.homeKit.shirt, home.homeKit.shirt) < 150 ? { ...away, matchKit: away.awayKit } : away;
}
