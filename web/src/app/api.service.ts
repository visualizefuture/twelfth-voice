import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  AnalystAnswer, CreateMatchRequest, MatchEvent, MatchList, MatchSummary, PersonalizedInsight, StoryRecap, Team, ViewerProfile
} from './models';

/** Thin wrapper over the REST API. The dev server proxies /api and /hubs to the .NET API. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  teams(): Promise<Team[]> {
    return firstValueFrom(this.http.get<Team[]>('/api/teams'));
  }

  matches(): Promise<MatchList> {
    return firstValueFrom(this.http.get<MatchList>('/api/matches'));
  }

  createMatch(request: CreateMatchRequest): Promise<MatchSummary> {
    return firstValueFrom(this.http.post<MatchSummary>('/api/matches', request));
  }

  pause(matchId: string): Promise<MatchSummary> {
    return firstValueFrom(this.http.post<MatchSummary>(`/api/matches/${matchId}/pause`, {}));
  }

  resume(matchId: string): Promise<MatchSummary> {
    return firstValueFrom(this.http.post<MatchSummary>(`/api/matches/${matchId}/resume`, {}));
  }

  setSpeed(matchId: string, speed: number): Promise<MatchSummary> {
    return firstValueFrom(this.http.put<MatchSummary>(`/api/matches/${matchId}/speed`, { speed }));
  }

  events(matchId: string, after: number, take: number): Promise<MatchEvent[]> {
    return firstValueFrom(this.http.get<MatchEvent[]>(`/api/matches/${matchId}/events`, { params: { after, take } }));
  }

  ask(matchId: string, question: string, profile: ViewerProfile): Promise<AnalystAnswer> {
    return firstValueFrom(this.http.post<AnalystAnswer>(`/api/matches/${matchId}/ask`, {
      question, mode: profile.mode, language: profile.language
    }));
  }

  story(matchId: string, profile: ViewerProfile): Promise<StoryRecap> {
    return firstValueFrom(this.http.get<StoryRecap>(`/api/matches/${matchId}/story`, {
      params: { mode: profile.mode, language: profile.language }
    }));
  }

  insights(matchId: string, profile: ViewerProfile, take = 200): Promise<PersonalizedInsight[]> {
    const params: Record<string, string | number> = { mode: profile.mode, language: profile.language, take };
    if (profile.favoriteTeamId) params['favoriteTeamId'] = profile.favoriteTeamId;
    if (profile.focusPlayerId) params['focusPlayerId'] = profile.focusPlayerId;
    return firstValueFrom(this.http.get<PersonalizedInsight[]>(`/api/matches/${matchId}/insights`, { params }));
  }
}
