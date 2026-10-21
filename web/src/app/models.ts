// Shapes of the JSON sent by TwelfthVoice.Api (camelCase, enums as strings, nulls omitted).

export type ViewerMode = 'Casual' | 'Analyst';
export type MatchStatus = 'Ready' | 'Live' | 'Paused' | 'Finished';
export type Perspective = 'Neutral' | 'Favorite' | 'Opponent';

export interface PitchPoint { x: number; y: number; }

export type KitPattern = 'Plain' | 'Stripes' | 'Hoops' | 'Halves' | 'Sash' | 'Sleeves';

export interface Kit {
  shirt: string;
  trim: string;
  pattern: KitPattern;
  shorts: string;
  socks: string;
  number: string;
}

export interface Crest {
  shape: 'Shield' | 'Roundel' | 'Pennant' | 'Badge';
  emblem: string;
  field: string;
  accent: string;
  founded: number;
  motto: string;
}

export interface PlayerLook {
  skinTone: string;
  hairStyle: string;
  hairColor: string;
  facialHair: string;
}

export interface LineupSlot {
  playerId: string;
  position: string;
  base: PitchPoint;
}

export interface Player {
  id: string;
  name: string;
  shortName: string;
  shirtNumber: number;
  position: string;
  nationality: string;
  isCaptain?: boolean;
  look?: PlayerLook;
}

export interface Team {
  id: string;
  name: string;
  shortName: string;
  city: string;
  stadium: string;
  primaryColor: string;
  secondaryColor: string;
  formation: string;
  squad: Player[];
  nickname?: string;
  homeKit?: Kit;
  awayKit?: Kit;
  goalkeeperKit?: Kit;
  matchKit?: Kit;
  crest?: Crest;
  lineup?: LineupSlot[];
}

export interface MatchInfo {
  matchId: string;
  competition: string;
  venue: string;
  kickOff: string;
  home: Team;
  away: Team;
  seed: number;
  referee: string;
  weather: string;
}

export interface MatchSummary {
  matchId: string;
  status: MatchStatus;
  homeTeamId: string;
  homeTeam: string;
  awayTeamId: string;
  awayTeam: string;
  homeScore: number;
  awayScore: number;
  period: number;
  clock: string;
  speed: number;
  seed: number;
  venue: string;
  createdUtc: string;
  viewers: number;
}

export interface MatchEvent {
  matchId: string;
  sequence: number;
  type: string;
  period: number;
  matchSecond: number;
  clock: string;
  teamId?: string;
  playerId?: string;
  relatedPlayerId?: string;
  start?: PitchPoint;
  end?: PitchPoint;
  outcome?: string;
  distanceM?: number;
  ballSpeedKmh?: number;
  playerSpeedKmh?: number;
  xg?: number;
  passDifficulty?: number;
  bodyPart?: string;
  homeScore?: number;
  awayScore?: number;
  qualifiers?: string[];
}

export interface OverlayHint {
  template: string;
  placement: string;
  displaySeconds: number;
  priority: number;
}

export interface PersonalizedInsight {
  insightId: string;
  matchId: string;
  kind: string;
  period: number;
  matchSecond: number;
  clock: string;
  importance: number;
  relevance: number;
  headline: string;
  body: string;
  analystNote?: string;
  teamId?: string;
  playerIds: string[];
  evidence: Record<string, number>;
  sourceSequences: number[];
  overlay: OverlayHint;
  perspective: Perspective;
  aboutFocusPlayer: boolean;
  language: string;
  /** 'rules' = written by code from the data; 'ai' = written by the broadcast crew and fact-checked. */
  source?: 'rules' | 'ai';
  verified?: boolean;
  checks?: string[];
}

export interface TeamLine {
  teamId: string;
  name: string;
  possessionPct: number;
  shots: number;
  shotsOnTarget: number;
  xg: number;
  passAccuracy: number;
  corners: number;
  fouls: number;
  yellowCards: number;
  redCards: number;
  pressures: number;
  distanceKm: number;
}

export interface GameState {
  momentum: number;
  momentumTeamId?: string;
  controlIndex: number;
  controlLabel: string;
  controllingTeamId?: string;
  passCompletionLast5: number;
  turnoversPerMinute: number;
  actionsPerMinute: number;
  tempo: string;
  homePressuresPerMinute: number;
  awayPressuresPerMinute: number;
}

export interface PlayerStats {
  playerId: string;
  teamId: string;
  name: string;
  shortName: string;
  shirtNumber: number;
  position: string;
  onPitch: boolean;
  touches: number;
  passesAttempted: number;
  passesCompleted: number;
  progressivePasses: number;
  keyPasses: number;
  passAccuracy: number;
  averagePassDifficulty: number;
  longestPassM: number;
  shots: number;
  shotsOnTarget: number;
  goals: number;
  assists: number;
  xg: number;
  fastestShotKmh: number;
  tackles: number;
  interceptions: number;
  dribblesCompleted: number;
  dribblesAttempted: number;
  distanceM: number;
  topSpeedKmh: number;
  sprints: number;
  rating: number;
}

export interface LiveState {
  matchId: string;
  status: MatchStatus;
  period: number;
  matchSecond: number;
  clock: string;
  homeScore: number;
  awayScore: number;
  home: TeamLine;
  away: TeamLine;
  game: GameState;
  focusPlayer?: PlayerStats;
}

export interface TimelinePoint {
  period: number;
  matchSecond: number;
  clock: string;
  momentum: number;
  controlIndex: number;
  homeScore: number;
  awayScore: number;
  homeXg: number;
  awayXg: number;
}

export interface JoinResult {
  match: MatchSummary;
  info: MatchInfo;
  state: LiveState;
  recentInsights: PersonalizedInsight[];
  timeline: TimelinePoint[];
}

export interface ViewerProfile {
  mode: ViewerMode;
  favoriteTeamId?: string | null;
  focusPlayerId?: string | null;
  language: string;
  displayName?: string | null;
}

export interface CreateMatchRequest {
  homeTeamId?: string;
  awayTeamId?: string;
  seed?: number;
  speed?: number;
  autoStart?: boolean;
}

export interface MatchList {
  live: MatchSummary[];
  stored: unknown[];
}

export interface AnalystAnswer {
  question: string;
  answer: string;
  language: string;
  verified: boolean;
  fromAi: boolean;
  toolsUsed: string[];
  checks: string[];
}

export interface StoryRecap {
  headline: string;
  body: string;
  language: string;
  fromAi: boolean;
  verified: boolean;
  checks: string[];
}
