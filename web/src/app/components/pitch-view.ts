import { Component, HostListener, computed, inject, signal } from '@angular/core';
import { playerTokens } from '../formation';
import { kitFor } from '../kits';
import { LiveService } from '../live.service';
import { Kit, MatchEvent, PersonalizedInsight, PitchPoint, PlayerLook } from '../models';
import { PITCH_LENGTH, PITCH_WIDTH, TOUCH_TYPES, actionLabel, ballPosition, toPercent, toScreen } from '../pitch';
import { ReplayService, minute } from '../replay.service';
import { crowdTile } from '../stadium';
import { ClubBannerComponent } from './club-banner';
import { CrestComponent } from './crest';
import { PortraitComponent } from './portrait';

type Layer = 'live' | 'heat' | 'shots';
type Camera = 'stadium' | 'tactical';

const CAMERA_KEY = 'twelfth-voice.camera';

interface HeatCell { x: number; y: number; w: number; h: number; opacity: number; }
interface ShotMark { x: number; y: number; r: number; home: boolean; result: string; label: string; }

const HEAT_COLUMNS = 21;
const HEAT_ROWS = 14;
const TRAIL_LENGTH = 6;

/**
 * The hero: a floodlit pitch showing the ball moving through each action, who has it and
 * what they did, with broadcast graphics placed where each insight's overlay hint asks.
 * Switch layers for the focus player's heat map or both teams' shot maps, the camera between a
 * floodlit stadium (the pitch tilted in 3D, players standing up, stands full of both clubs' fans)
 * and a flat tactical board, and replay any goal's build-up.
 */
@Component({
  selector: 'tv-pitch-view',
  imports: [PortraitComponent, ClubBannerComponent, CrestComponent],
  template: `
    <div class="layers">
      <div class="tabs" role="tablist" aria-label="Pitch view">
        <button role="tab" type="button" [attr.aria-selected]="layer() === 'live'" (click)="layer.set('live')">Live play</button>
        <button role="tab" type="button" [attr.aria-selected]="layer() === 'heat'" (click)="layer.set('heat')">Heat map</button>
        <button role="tab" type="button" [attr.aria-selected]="layer() === 'shots'" (click)="layer.set('shots')">Shot map</button>
      </div>
      <div class="camera" role="group" aria-label="Camera">
        <button type="button" [attr.aria-pressed]="camera() === 'stadium'" (click)="setCamera('stadium')">Stadium</button>
        <button type="button" [attr.aria-pressed]="camera() === 'tactical'" (click)="setCamera('tactical')">Tactical</button>
      </div>
      @if (replay.goals().length > 0) {
        <div class="replays" role="group" aria-label="Goal replays">
          <span class="muted">Replays</span>
          @for (g of replay.goals(); track g.sequence) {
            <button type="button" (click)="watchReplay(g)" [class.home]="g.teamId === homeId()" [attr.aria-label]="'Replay the goal: ' + goalLabel(g)">
              ▶ {{ goalLabel(g) }}
            </button>
          }
        </div>
      }
      <span class="direction muted">{{ homeShort() }} attack left to right</span>
    </div>

    <div class="stage" [class.stadium]="stadium()" [class.replaying]="!!replay.active()">
      @if (stadium()) {
        <div class="sky" aria-hidden="true"></div>
        <div class="stand far" [style.background-image]="crowd().far" aria-hidden="true"><i class="roof"></i></div>
        <div class="stand left" [style.background-image]="crowd().home" aria-hidden="true"></div>
        <div class="stand right" [style.background-image]="crowd().away" aria-hidden="true"></div>
        @for (light of floodlights; track light) {
          <div class="tower" [class]="'tower ' + light" aria-hidden="true"><i></i></div>
        }
        <!-- goal: the scoring club's fans unfurl a giant tifo across the far stand, and confetti falls -->
        @if (celebration(); as c) {
          <div class="tifo" [class.from-left]="c.home" [style.--a]="c.a" [style.--b]="c.b" aria-hidden="true">
            <tv-crest class="tifo-crest" [team]="c.team" [size]="96" />
            <span class="tag-text">{{ c.word }}</span>
            <span class="tag-sub">{{ c.scorer }}</span>
          </div>
          <div class="confetti" aria-hidden="true">
            @for (bit of confetti; track $index) {
              <i [style.left.%]="bit.left" [style.animation-delay.s]="bit.delay" [style.animation-duration.s]="bit.duration"
                 [style.background]="$index % 3 === 0 ? c.b : c.a" [style.width.px]="bit.size" [style.height.px]="bit.size * 1.6"></i>
            }
          </div>
        }
      }

      <div class="camera-rig">
        <div class="pitch">
          @if (stadium()) {
            <div class="apron" aria-hidden="true"></div>
            <div class="boards far" aria-hidden="true">
              @for (b of boards; track $index) { <span>{{ b }}</span> }
            </div>
            <div class="goal-frame left" aria-hidden="true"></div>
            <div class="goal-frame right" aria-hidden="true"></div>
          }
          <svg [attr.viewBox]="viewBox" preserveAspectRatio="none" aria-hidden="true">
            <defs>
              <filter id="heat-blur" x="-10%" y="-10%" width="120%" height="120%">
                <feGaussianBlur stdDeviation="2.4" />
              </filter>
              <marker id="arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="4" markerHeight="4" orient="auto-start-reverse">
                <path d="M0,0 L10,5 L0,10 z" class="arrow-head" />
              </marker>
              <radialGradient id="pool" cx="50%" cy="50%" r="60%">
                <stop offset="0%" stop-color="#ffffff" stop-opacity="0.10" />
                <stop offset="100%" stop-color="#000000" stop-opacity="0.18" />
              </radialGradient>
            </defs>

            <!-- turf and mowing stripes -->
            <rect x="0" y="0" [attr.width]="length" [attr.height]="width" class="turf" />
            @for (stripe of stripes; track stripe) {
              <rect [attr.x]="stripe" y="0" width="5.25" [attr.height]="width" class="stripe" />
            }
            @if (stadium()) {
              <rect x="0" y="0" [attr.width]="length" [attr.height]="width" fill="url(#pool)" />
            }

            <!-- markings -->
            <g class="lines">
              <rect x="0.5" y="0.5" [attr.width]="length - 1" [attr.height]="width - 1" />
              <line [attr.x1]="length / 2" y1="0.5" [attr.x2]="length / 2" [attr.y2]="width - 0.5" />
              <circle [attr.cx]="length / 2" [attr.cy]="width / 2" r="9.15" />
              <rect x="0.5" [attr.y]="width / 2 - 20.16" width="16.5" height="40.32" />
              <rect [attr.x]="length - 17" [attr.y]="width / 2 - 20.16" width="16.5" height="40.32" />
              <rect x="0.5" [attr.y]="width / 2 - 9.16" width="5.5" height="18.32" />
              <rect [attr.x]="length - 6" [attr.y]="width / 2 - 9.16" width="5.5" height="18.32" />
              <path [attr.d]="'M 16.5 ' + (width / 2 - 7.3) + ' A 9.15 9.15 0 0 1 16.5 ' + (width / 2 + 7.3)" />
              <path [attr.d]="'M ' + (length - 16.5) + ' ' + (width / 2 - 7.3) + ' A 9.15 9.15 0 0 0 ' + (length - 16.5) + ' ' + (width / 2 + 7.3)" />
            </g>
            <circle [attr.cx]="length / 2" [attr.cy]="width / 2" r="0.4" class="spot" />
            <circle cx="11" [attr.cy]="width / 2" r="0.4" class="spot" />
            <circle [attr.cx]="length - 11" [attr.cy]="width / 2" r="0.4" class="spot" />

            @switch (layer()) {
              @case ('heat') {
                <g filter="url(#heat-blur)">
                  @for (cell of heatCells(); track $index) {
                    <rect [attr.x]="cell.x" [attr.y]="cell.y" [attr.width]="cell.w" [attr.height]="cell.h"
                          class="heat" [attr.fill-opacity]="cell.opacity" />
                  }
                </g>
              }
              @case ('shots') {
                @for (shot of shots(); track $index) {
                  <circle [attr.cx]="shot.x" [attr.cy]="shot.y" [attr.r]="shot.r"
                          [class]="'shot ' + shot.result + (shot.home ? ' home' : ' away')">
                    <title>{{ shot.label }}</title>
                  </circle>
                }
              }
              @default {
                <!-- ball trail -->
                @if (trail().length > 1) {
                  <polyline [attr.points]="trailPoints()" class="trail" />
                }
                <!-- the current action -->
                @if (actionLine(); as line) {
                  <line [attr.x1]="line.from.x" [attr.y1]="line.from.y" [attr.x2]="line.to.x" [attr.y2]="line.to.y"
                        [class]="'action ' + line.kind" marker-end="url(#arrow)" />
                }
                <!-- a pass the insight engine flagged as special -->
                @if (!replay.active() && highlightPass(); as pass) {
                  <line [attr.x1]="pass.from.x" [attr.y1]="pass.from.y" [attr.x2]="pass.to.x" [attr.y2]="pass.to.y"
                        class="highlight" marker-end="url(#arrow)" />
                }
                @if (ball(); as b) {
                  <g class="ball" [style.transform]="'translate(' + b.x + 'px,' + b.y + 'px)'">
                    <ellipse rx="1.1" ry="0.55" cy="0.5" class="ball-shadow" />
                    <circle r="1.6" class="ball-glow" />
                    <circle r="0.85" class="ball-core" />
                  </g>
                }
              }
            }
          </svg>

          <!-- all 22 players, in their kits, moving with the play -->
          @if (layer() === 'live') {
            @for (t of tokens(); track t.playerId) {
              <div class="token" [class.away]="!t.home" [class.on-ball]="t.onBall"
                   [style.left.%]="t.x / length * 100" [style.top.%]="t.y / width * 100">
                <div class="figure">
                  <tv-portrait [playerLook]="lookOf(t.playerId)" [playerKit]="kitOf(t.playerId)" [size]="26"
                               [captain]="isCaptain(t.playerId)" [label]="nameOf(t.playerId)" />
                  <span class="num" [style.background]="kitOf(t.playerId)?.shirt" [style.color]="kitOf(t.playerId)?.number"
                        [style.border-color]="kitOf(t.playerId)?.trim">{{ numberOf(t.playerId) }}</span>
                </div>
              </div>
            }
          }

          <!-- player tag: real-time identification of whoever is on the ball -->
          @if (layer() === 'live' && tag(); as t) {
            <div class="tag" [class.away]="!t.home" [style.left.%]="t.left" [style.top.%]="t.top">
              <b>{{ t.shirt }}</b> {{ t.name }}<span>{{ t.action }}</span>
            </div>
          }

          @if (!replay.active()) {
            @for (o of live.overlays(); track o.id) {
              @if (o.placement === 'near-player') {
                @if (anchorFor(o.insight); as a) {
                  <div class="gfx near" [style.left.%]="a.left" [style.top.%]="a.top">{{ o.insight.headline }}</div>
                }
              }
            }
          }
        </div>
      </div>

      <!-- the flat layer on top: broadcast graphics, replay badge, controls -->
      <div class="hud">
        @if (layer() === 'heat') {
          <div class="layer-note">
            @if (focusName(); as name) {
              {{ name }}: {{ heatTouches() }} actions so far
            } @else {
              Choose a player in Your view to see their heat map
            }
          </div>
        }
        @if (layer() === 'shots') {
          <div class="layer-note">Bigger circle, better chance (xG). Filled means goal.</div>
        }

        @if (replay.active(); as r) {
          <div class="replay-badge">
            <span class="dot"></span><b class="display">REPLAY</b><span>{{ r.label }}</span>
            <button type="button" class="skip" (click)="replay.stop()">Back to live</button>
          </div>
          <div class="replay-progress"><i [style.width.%]="(r.index + 1) / r.frames.length * 100"></i></div>
        } @else {
          @for (o of chips(); track o.id) {
            <div class="gfx chip top" [class.right]="o.placement === 'top-right'">{{ o.insight.headline }}</div>
          }
        }

        @if (canPlayPause()) {
          <button type="button" class="playpause" (click)="togglePlay()"
                  [attr.aria-label]="isPaused() ? 'Resume the match' : 'Pause the match'" title="Pause / resume (space bar)">
            @if (isPaused()) {
              <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true"><path d="M8 5v14l11-7z" fill="currentColor" /></svg>
              <span>Resume</span>
            } @else {
              <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true"><path d="M7 5h4v14H7zM13 5h4v14h-4z" fill="currentColor" /></svg>
              <span>Pause</span>
            }
          </button>
        }
      </div>
    </div>

    <!-- broadcast graphics sit in a rail under the pitch, so they never cover the play -->
    <div class="rail" aria-live="polite">
      @if (!replay.active()) {
        @for (o of railGraphics(); track o.id) {
          <div class="card" [class.goal]="o.insight.kind === 'Goal'" [class.mine]="o.insight.perspective === 'Favorite'"
               [class.big]="o.placement === 'full-width' || o.placement === 'centre'">
            @if (o.insight.kind === 'Goal' && scoringTeam(o.insight); as team) {
              <tv-club-banner [team]="team" />
            }
            <div class="card-text">
              <strong [class.display]="o.placement === 'full-width' || o.placement === 'centre'">{{ o.insight.headline }}</strong>
              <p>{{ o.insight.body }}</p>
            </div>
            @if (o.insight.kind === 'Goal' && scoringTeam(o.insight); as team) {
              <tv-club-banner [team]="team" side="away" />
            }
          </div>
        } @empty {
          <div class="card idle muted">{{ idleLine() }}</div>
        }
      }
    </div>
  `,
  styles: `
    :host { display: block; }
    .layers { display: flex; gap: 10px 14px; align-items: center; margin-bottom: 10px; flex-wrap: wrap; }
    .tabs, .camera, .replays { display: flex; gap: 6px; align-items: center; flex-wrap: wrap; }
    .layers button[aria-selected="true"], .layers button[aria-pressed="true"] { border-color: var(--chalk); background: var(--turf); }
    .replays button { border-left: 3px solid var(--away); font-variant-numeric: tabular-nums; }
    .replays button.home { border-left-color: var(--home); }
    .replays .muted { font-size: var(--step--1); }
    .direction { margin-left: auto; font-size: var(--step--1); }

    /* ---------- stage: flat (tactical) by default ---------- */
    .stage { position: relative; width: 100%; aspect-ratio: 105 / 68; border-radius: 4px; overflow: hidden; --tilt: 0deg; }
    .camera-rig { position: absolute; inset: 0; }
    .pitch { position: absolute; inset: 0; }
    .hud { position: absolute; inset: 0; pointer-events: none; z-index: 6; }
    .hud button { pointer-events: auto; }
    .pitch > svg { position: absolute; inset: 0; width: 100%; height: 100%; }
    .playpause svg { flex: none; }
    .turf { fill: var(--turf); }
    .stripe { fill: var(--turf-stripe); }
    .lines { fill: none; stroke: var(--chalk); stroke-opacity: 0.55; stroke-width: 0.3; }
    .spot { fill: var(--chalk); fill-opacity: 0.6; }
    .arrow-head { fill: var(--chalk); }
    .heat { fill: var(--floodlight); }
    .shot { stroke-width: 0.35; fill: transparent; }
    .shot.home { stroke: var(--home); }
    .shot.away { stroke: var(--away); }
    .shot.goal { fill: var(--floodlight); stroke: var(--floodlight); }
    .shot.saved.home { fill: var(--home); fill-opacity: 0.35; }
    .shot.saved.away { fill: var(--away); fill-opacity: 0.35; }
    .trail { fill: none; stroke: var(--chalk); stroke-opacity: 0.25; stroke-width: 0.35; stroke-dasharray: 0.8 0.8; }
    .action { stroke-width: 0.4; stroke: var(--chalk); stroke-opacity: 0.8; }
    .action.fail { stroke: var(--red-card); stroke-dasharray: 1 0.8; }
    .action.shot { stroke: var(--floodlight); stroke-width: 0.5; }
    .highlight { stroke: var(--floodlight); stroke-width: 0.6; }
    .ball { transition: transform 0.45s ease-out; }
    .ball-shadow { fill: #000; fill-opacity: 0; }
    .ball-glow { fill: var(--floodlight); fill-opacity: 0.25; }
    .ball-core { fill: #ffffff; }

    .tag {
      position: absolute; white-space: nowrap; pointer-events: none; z-index: 4;
      transform: translate(-50%, calc(-100% - 22px)) rotateX(calc(var(--tilt) * -1)); transform-origin: 50% 100%;
      background: var(--night-2); border-left: 3px solid var(--home); padding: 3px 8px; font-size: var(--step--1);
      transition: left 0.45s ease-out, top 0.45s ease-out;
    }
    .tag.away { border-left-color: var(--away); }
    .tag b { font-family: var(--display); font-size: 1.05em; }
    .tag span { color: var(--moss); margin-left: 8px; }

    .token {
      position: absolute; width: 0; height: 0; z-index: 2; pointer-events: none;
      transition: left 0.6s ease-out, top 0.6s ease-out;
    }
    .figure {
      position: absolute; left: -13px; top: -13px; width: 26px; height: 26px; border-radius: 50%;
      box-shadow: 0 0 0 2px var(--home), 0 2px 6px rgba(0, 0, 0, 0.45);
      transform-origin: 50% 50%; transition: transform 0.2s;
    }
    .token.away .figure { box-shadow: 0 0 0 2px var(--away), 0 2px 6px rgba(0, 0, 0, 0.45); }
    .token.on-ball { z-index: 3; }
    .token.on-ball .figure { transform: scale(1.3); box-shadow: 0 0 0 2px var(--floodlight), 0 0 14px rgba(246, 196, 69, 0.6); }
    .num {
      position: absolute; right: -9px; bottom: -6px; min-width: 15px; height: 13px; padding: 0 2px; border-radius: 3px;
      border: 1px solid; font: 700 10px/11px var(--display); text-align: center; box-shadow: 0 1px 2px rgba(0, 0, 0, 0.5);
    }
    .replaying .token { transition-duration: 0.8s; transition-timing-function: ease-in-out; }
    .replaying .ball { transition-duration: 0.8s; transition-timing-function: ease-in-out; }
    .replaying .tag { transition-duration: 0.8s; }

    .rail { display: grid; gap: 8px; margin-top: 10px; min-height: 78px; align-content: start; }
    .card {
      display: flex; align-items: center; gap: 16px; padding: 10px 16px; background: var(--night-2);
      border-left: 4px solid var(--chalk); animation: gfx-in 0.35s ease-out;
    }
    .card-text { flex: 1; min-width: 0; }
    .card strong { display: block; font-size: var(--step-0); }
    .card strong.display { font-size: var(--step-1); }
    .card p { margin: 3px 0 0; font-size: var(--step--1); color: var(--moss); }
    .card.mine { border-left-color: var(--floodlight); }
    .card.goal { border-left: 0; border-top: 3px solid var(--floodlight); background: #1d1600; text-align: center; padding: 12px 18px; }
    .card.goal strong { color: var(--floodlight); font-size: var(--step-2); }
    .card.idle { border-left-color: var(--hairline); font-size: var(--step--1); }

    .tifo {
      position: absolute; left: 6%; right: 6%; top: 9%; height: 23%; z-index: 2; overflow: hidden;
      display: flex; align-items: center; justify-content: center; gap: 3%;
      background: repeating-linear-gradient(90deg, var(--a) 0 9%, var(--b) 9% 12%);
      box-shadow: inset 0 0 40px rgb(0 0 0 / 0.45);
      transform-origin: 100% 50%; animation: unfurl 0.9s cubic-bezier(.2,.8,.2,1) both, ripple-tifo 2.4s ease-in-out 0.9s infinite;
      /* a crowd-held cloth: slightly uneven edges */
      clip-path: polygon(0 4%, 12% 0, 30% 3%, 50% 0, 70% 4%, 88% 1%, 100% 3%, 100% 97%, 85% 100%, 64% 96%, 45% 100%, 25% 97%, 0 100%);
    }
    .tifo.from-left { transform-origin: 0 50%; }
    .tifo::after {
      content: ''; position: absolute; inset: 0; pointer-events: none;
      background: repeating-linear-gradient(90deg, rgb(0 0 0 / 0.18) 0, rgb(255 255 255 / 0.10) 6%, rgb(0 0 0 / 0.18) 12%);
      background-size: 200% 100%; mix-blend-mode: soft-light; animation: folds 1.4s linear infinite;
    }
    .tifo-crest { filter: drop-shadow(0 4px 8px rgb(0 0 0 / 0.5)); }
    .tifo .tag-text {
      font-family: 'Permanent Marker', var(--display); font-size: clamp(28px, 6.5vw, 92px); line-height: 1; color: #fff;
      transform: rotate(-4deg); letter-spacing: 0.02em;
      text-shadow: 3px 3px 0 rgb(0 0 0 / 0.65), -2px -2px 0 var(--b), 0 0 24px rgb(255 255 255 / 0.25);
      -webkit-text-stroke: 2px rgb(0 0 0 / 0.55);
    }
    .tifo .tag-sub {
      font-family: 'Permanent Marker', var(--display); font-size: clamp(14px, 2.2vw, 30px); color: #fff; transform: rotate(-3deg);
      text-shadow: 2px 2px 0 rgb(0 0 0 / 0.6);
    }
    @keyframes unfurl { from { transform: scaleX(0); } to { transform: scaleX(1); } }
    @keyframes ripple-tifo { 0%, 100% { filter: brightness(1); } 50% { filter: brightness(1.12); } }
    @keyframes folds { to { background-position: -200% 0; } }
    .confetti { position: absolute; inset: 0; pointer-events: none; overflow: hidden; z-index: 5; }
    .confetti i { position: absolute; top: -4%; border-radius: 1px; opacity: 0.95; animation: fall linear both; }
    @keyframes fall {
      0% { transform: translateY(0) rotate(0deg); }
      100% { transform: translateY(1000px) rotate(540deg); opacity: 0.7; }
    }

    .layer-note { position: absolute; left: 12px; bottom: 10px; font-size: var(--step--1); background: var(--night-2); padding: 4px 10px; }

    .gfx { position: absolute; pointer-events: none; animation: gfx-in 0.35s ease-out; }
    .gfx p { margin: 4px 0 0; font-size: var(--step--1); color: var(--moss); }
    .banner {
      left: 0; right: 0; top: 38%; padding: 14px 24px; background: var(--night-2);
      border-top: 3px solid var(--home); text-align: center;
    }
    .banner strong { font-size: var(--step-2); }
    .banner.goal { border-top-color: var(--floodlight); background: #1d1600; }
    .banner.goal strong { color: var(--floodlight); font-size: var(--step-3); }
    .banner .flag-left, .banner .flag-right { position: absolute; top: 50%; transform: translateY(-50%); }
    .banner .flag-left { left: 18px; }
    .banner .flag-right { right: 18px; }
    @media (max-width: 700px) { .banner .flag-left, .banner .flag-right { display: none; } }
    .summary { left: 15%; right: 15%; top: 22%; padding: 18px 22px; background: var(--night-2); border: 1px solid var(--hairline); }
    .summary strong { font-size: var(--step-2); }
    .chip { top: 10px; left: 50%; transform: translateX(-50%); background: var(--night-2); padding: 5px 14px; border-bottom: 2px solid var(--floodlight); }
    .chip.right { left: auto; right: 10px; transform: none; }
    .near {
      transform: translate(-50%, 18px) rotateX(calc(var(--tilt) * -1)); transform-origin: 50% 0;
      background: var(--floodlight); color: #1d1600; font-weight: 600;
      padding: 3px 9px; font-size: var(--step--1); white-space: nowrap; z-index: 5;
    }
    .lower {
      left: 12px; bottom: 12px; max-width: min(460px, 70%); background: var(--night-2);
      border-left: 4px solid var(--chalk); padding: 10px 14px;
    }
    .lower.mine { border-left-color: var(--floodlight); }
    @keyframes gfx-in { from { opacity: 0; transform: translateY(6px); } }
    .chip, .near { animation-name: none; }

    .playpause {
      position: absolute; right: 12px; bottom: 12px; display: inline-flex; align-items: center; gap: 6px;
      padding: 6px 12px 6px 8px; background: rgb(11 34 25 / 0.85); border-color: var(--hairline); backdrop-filter: blur(4px);
    }
    .playpause span { font-size: var(--step--1); }

    .replay-badge {
      position: absolute; left: 12px; top: 12px; display: flex; align-items: center; gap: 10px;
      background: rgb(11 34 25 / 0.9); padding: 6px 8px 6px 12px; border-left: 4px solid var(--floodlight);
    }
    .replay-badge b { color: var(--floodlight); letter-spacing: 0.12em; font-size: var(--step-1); }
    .replay-badge .dot { width: 9px; height: 9px; border-radius: 50%; background: #e5484d; animation: blink 1s steps(2) infinite; }
    .replay-badge .skip { font-size: var(--step--1); padding: 3px 10px; }
    .replay-progress { position: absolute; left: 0; right: 0; bottom: 0; height: 4px; background: rgb(0 0 0 / 0.35); }
    .replay-progress i { display: block; height: 100%; background: var(--floodlight); transition: width 0.8s linear; }
    @keyframes blink { 50% { opacity: 0.2; } }

    /* ---------- stadium camera ---------- */
    .stage.stadium {
      --tilt: 46deg; aspect-ratio: 16 / 10;
      --turf: #2c6a33; --turf-stripe: #337a3a;
      background: #0b0f13;
    }
    .stadium .sky {
      position: absolute; inset: 0 0 auto 0; height: 22%;
      background: radial-gradient(120% 140% at 50% 120%, #1d2a38 0%, #0b0f13 60%);
    }
    .stadium .stand { position: absolute; background-size: 180px 72px; filter: brightness(0.82); }
    .stadium .stand::after { content: ''; position: absolute; inset: 0; background: linear-gradient(180deg, rgb(0 0 0 / 0.55), rgb(0 0 0 / 0.05) 60%, rgb(0 0 0 / 0.35)); }
    .stadium .stand.far { left: 4%; right: 4%; top: 8%; height: 25%; }
    .stadium .stand.far .roof {
      position: absolute; left: -3%; right: -3%; top: -14%; height: 16%; z-index: 1;
      background: linear-gradient(180deg, #2b3138, #161a1f); box-shadow: 0 6px 16px rgb(0 0 0 / 0.6);
    }
    .stadium .stand.left { left: 0; top: 26%; bottom: 0; width: 26%; clip-path: polygon(0 0, 100% 0, 46% 100%, 0 100%); }
    .stadium .stand.right { right: 0; top: 26%; bottom: 0; width: 26%; clip-path: polygon(0 0, 100% 0, 100% 100%, 54% 100%); }
    .stadium .tower { position: absolute; top: 2%; width: 3%; height: 7%; z-index: 1; }
    .stadium .tower i {
      position: absolute; inset: 0; border-radius: 2px;
      background: repeating-linear-gradient(90deg, #fffbe6 0 22%, #6b6a5c 22% 28%);
      box-shadow: 0 0 18px 6px rgb(255 247 214 / 0.55), 0 0 60px 26px rgb(255 247 214 / 0.18);
    }
    .stadium .tower.a { left: 6%; } .stadium .tower.b { left: 33%; } .stadium .tower.c { right: 33%; } .stadium .tower.d { right: 6%; }

    .stadium .camera-rig {
      inset: 31% 15% 3% 15%; perspective: 1050px; perspective-origin: 50% -30%;
    }
    .stadium .pitch {
      inset: auto 0 0 0; aspect-ratio: 105 / 68; transform-style: preserve-3d;
      transform: rotateX(var(--tilt)); transform-origin: 50% 100%;
    }
    .stadium .apron {
      position: absolute; inset: -7% -5% -9% -5%; background:
        repeating-linear-gradient(90deg, #2c6a33 0 5%, #337a3a 5% 10%);
      box-shadow: 0 0 0 1px rgb(0 0 0 / 0.4), 0 40px 80px rgb(0 0 0 / 0.6);
    }
    .stadium .boards.far {
      position: absolute; left: -4%; right: -4%; top: -7%; height: 3.4%;
      display: flex; overflow: hidden; transform-origin: 50% 100%; transform: rotateX(-90deg);
      background: #0d1a2b; border-top: 1px solid #36506e;
    }
    .stadium .boards span {
      flex: 1; display: grid; place-items: center; font: 700 9px/1 var(--display); letter-spacing: 0.14em; white-space: nowrap;
      color: #cfe6ff; border-left: 1px solid #23364d;
    }
    .stadium .boards span:nth-child(odd) { color: var(--floodlight); }
    .stadium .goal-frame {
      position: absolute; top: calc(50% - 5.4%); height: 10.8%; width: 2.6%;
      border: 2px solid #f4f6f8; box-shadow: 0 0 6px rgb(0 0 0 / 0.5);
      background: repeating-linear-gradient(45deg, rgb(255 255 255 / 0.25) 0 2px, transparent 2px 6px);
    }
    .stadium .goal-frame.left { left: -2.6%; border-right: 0; }
    .stadium .goal-frame.right { right: -2.6%; border-left: 0; }
    .stadium .lines { stroke-opacity: 0.85; stroke-width: 0.35; }
    .stadium .ball-shadow { fill-opacity: 0.45; }
    .stadium .token { transform-style: preserve-3d; }
    .stadium .token .figure {
      transform-origin: 50% 100%; top: -24px;
      transform: rotateX(calc(var(--tilt) * -1));
      box-shadow: 0 0 0 2px var(--home), 0 6px 4px -2px rgba(0, 0, 0, 0.55);
    }
    .stadium .token.away .figure { box-shadow: 0 0 0 2px var(--away), 0 6px 4px -2px rgba(0, 0, 0, 0.55); }
    .stadium .token.on-ball .figure {
      transform: rotateX(calc(var(--tilt) * -1)) scale(1.3);
      box-shadow: 0 0 0 2px var(--floodlight), 0 0 14px rgba(246, 196, 69, 0.6);
    }
    .stadium .token::before {
      content: ''; position: absolute; left: -10px; top: -4px; width: 20px; height: 8px; border-radius: 50%;
      background: radial-gradient(closest-side, rgb(0 0 0 / 0.5), transparent);
    }
    .stadium .tag { transform: translate(-50%, calc(-100% - 40px)) rotateX(calc(var(--tilt) * -1)); }

    @media (max-width: 640px) {
      .banner strong { font-size: var(--step-1); }
      .banner.goal strong { font-size: var(--step-2); }
      .gfx p { display: none; }
      .stage.stadium { aspect-ratio: 4 / 3; }
      .stadium .camera-rig { inset: 30% 4% 3% 4%; }
      .stadium .token .figure { transform: rotateX(calc(var(--tilt) * -1)) scale(0.7); }
      .num { display: none; }
      .playpause span { display: none; }
    }
    @media (prefers-reduced-motion: reduce) {
      .token, .ball, .tag, .replay-progress i { transition: none !important; }
      .replay-badge .dot { animation: none; }
      .tifo, .tifo::after { animation: none; }
      .confetti { display: none; }
    }
  `
})
export class PitchViewComponent {
  readonly live = inject(LiveService);
  readonly replay = inject(ReplayService);
  readonly layer = signal<Layer>('live');
  readonly camera = signal<Camera>(loadCamera());
  readonly stadium = computed(() => this.camera() === 'stadium');

  readonly floodlights = ['a', 'b', 'c', 'd'];
  readonly boards = ['TWELFTH VOICE', 'LIVE AI COMMENTARY', 'INSIDE THE GAME', 'EVERY NUMBER CHECKED', 'TWELFTH VOICE', 'ASK THE MATCH'];

  /** The stands, filled with both clubs' fans: home end on the left, away end on the right. */
  readonly crowd = computed(() => {
    const info = this.live.info();
    const home = info?.home.matchKit?.shirt ?? info?.home.primaryColor ?? '#c8102e';
    const away = info?.away.matchKit?.shirt ?? info?.away.primaryColor ?? '#1d428a';
    const seed = info?.seed ?? 7;
    return {
      far: crowdTile(home, away, 0.5, seed),
      home: crowdTile(home, away, 0.92, seed + 1),
      away: crowdTile(home, away, 0.08, seed + 2)
    };
  });

  /** Short headline chips stay on the pitch's top edge; everything with a body goes in the rail below. */
  readonly chips = computed(() => this.live.overlays().filter(o => o.placement === 'top-bar' || o.placement === 'top-right'));

  /** Rail graphics, most important first, at most two so the layout stays steady. */
  readonly railGraphics = computed(() => this.live.overlays()
    .filter(o => !['top-bar', 'top-right', 'near-player', 'pitch-overlay'].includes(o.placement))
    .sort((a, b) => b.insight.overlay.priority - a.insight.overlay.priority)
    .slice(0, 2));

  readonly idleLine = computed(() => {
    const summary = this.live.summary();
    if (!summary) return '';
    if (summary.status === 'Finished') return 'Full time. Replay any goal above, or read the story.';
    if (summary.status === 'Paused') return 'Paused.';
    return 'Live. Big moments appear here as they happen.';
  });

  /** A goal is on screen: the scoring club's fans celebrate in the stands. */
  readonly celebration = computed(() => {
    if (this.replay.active()) return null;
    const goal = this.live.overlays().find(o => o.insight.kind === 'Goal');
    const team = goal ? this.scoringTeam(goal.insight) : null;
    if (!goal || !team) return null;
    const kit = team.matchKit;
    const info = this.live.info();
    const scorerId = goal.insight.playerIds[0];
    const scorer = scorerId ? this.live.players().get(scorerId)?.player : undefined;
    return {
      team,
      home: team.id === info?.home.id,
      a: kit?.shirt ?? team.primaryColor,
      b: kit?.trim ?? team.secondaryColor,
      word: GOAL_WORDS[this.live.profile().language] ?? 'GOOOAL!',
      scorer: scorer ? `${scorer.shortName} ${scorer.shirtNumber}` : team.shortName
    };
  });

  /** Confetti pieces, fixed per session so they don't reshuffle on every render. */
  readonly confetti = Array.from({ length: 46 }, (_, i) => ({
    left: (i * 37) % 100 + ((i * 13) % 7) / 7,
    delay: ((i * 17) % 20) / 10,
    duration: 3 + ((i * 7) % 10) / 4,
    size: 5 + (i % 4) * 2
  }));

  /** What the pitch shows: the replay frame when a replay is rolling, otherwise the live action. */
  private readonly action = computed(() => this.replay.frame() ?? this.live.lastAction());

  readonly isPaused = computed(() => this.live.summary()?.status === 'Paused');
  readonly canPlayPause = computed(() => !this.replay.active() && ['Live', 'Paused'].includes(this.live.summary()?.status ?? ''));

  setCamera(camera: Camera): void {
    this.camera.set(camera);
    try {
      localStorage.setItem(CAMERA_KEY, camera);
    } catch {
      // storage unavailable
    }
  }

  togglePlay(): void {
    if (this.isPaused()) void this.live.resume();
    else if (this.live.summary()?.status === 'Live') void this.live.pause();
  }

  /** Space bar pauses and resumes, unless you're typing or on a control. */
  @HostListener('document:keydown', ['$event'])
  onKey(event: KeyboardEvent): void {
    if (event.code !== 'Space' || event.repeat) return;
    const target = event.target as HTMLElement | null;
    if (target && /^(INPUT|SELECT|TEXTAREA|BUTTON)$/.test(target.tagName)) return;
    if (target?.isContentEditable) return;
    event.preventDefault();
    if (this.replay.active()) this.replay.stop();
    else this.togglePlay();
  }

  watchReplay(goal: MatchEvent): void {
    void this.replay.play(goal);
  }

  goalLabel(goal: MatchEvent): string {
    const scorer = goal.playerId ? this.live.players().get(goal.playerId)?.player.shortName : undefined;
    return `${minute(goal)} ${scorer ?? ''}`.trim();
  }

  numberOf(playerId: string): number | string {
    return this.live.players().get(playerId)?.player.shirtNumber ?? '';
  }

  readonly length = PITCH_LENGTH;
  readonly width = PITCH_WIDTH;
  readonly viewBox = `0 0 ${PITCH_LENGTH} ${PITCH_WIDTH}`;
  readonly stripes = Array.from({ length: 10 }, (_, i) => i * 10.5);

  readonly homeId = computed(() => this.live.info()?.home.id ?? '');

  /** The club that scored, for the waving banners on the goal graphic. */
  scoringTeam(insight: PersonalizedInsight) {
    const info = this.live.info();
    if (!info || !insight.teamId) return null;
    return insight.teamId === info.home.id ? info.home : insight.teamId === info.away.id ? info.away : null;
  }
  readonly homeShort = computed(() => this.live.info()?.home.shortName ?? 'Home');

  /** Recent ball positions, oldest first. */
  readonly trail = computed(() => {
    const r = this.replay.active();
    const events = r ? r.frames.slice(0, r.index + 1) : this.live.events();
    const home = this.homeId();
    const points: PitchPoint[] = [];
    for (let i = events.length - 1; i >= 0 && points.length < TRAIL_LENGTH; i--) {
      const p = ballPosition(events[i], home);
      if (p) points.unshift(p);
    }
    return points;
  });

  readonly trailPoints = computed(() => this.trail().map(p => `${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(' '));

  readonly ball = computed(() => {
    const e = this.action();
    return e ? ballPosition(e, this.homeId()) : null;
  });

  readonly actionLine = computed(() => {
    const e = this.action();
    if (!e?.start || !e.end || !['Pass', 'Carry', 'Shot', 'Dribble'].includes(e.type)) return null;
    const home = this.homeId();
    const kind = e.type === 'Shot' ? 'shot' : e.outcome === 'Fail' ? 'fail' : 'ok';
    return { from: toScreen(e.start, e.teamId, home), to: toScreen(e.end, e.teamId, home), kind };
  });

  /** If a "pass of note" graphic is showing, draw that pass. */
  readonly highlightPass = computed(() => {
    const overlay = this.live.overlays().find(o => o.placement === 'pitch-overlay');
    if (!overlay) return null;
    const e = this.findEvent(overlay.insight.sourceSequences[0]);
    if (!e?.start || !e.end) return null;
    const home = this.homeId();
    return { from: toScreen(e.start, e.teamId, home), to: toScreen(e.end, e.teamId, home) };
  });

  /** Synthetic positions for all 22 players, recomputed on every action. */
  readonly tokens = computed(() => {
    const info = this.live.info();
    if (!info) return [];
    const r = this.replay.active();
    // During a replay, only the events up to that moment decide who is on the pitch.
    const events = r ? this.live.events().filter(e => e.sequence <= r.frames[r.index].sequence) : this.live.events();
    return playerTokens(info, events, this.action());
  });

  lookOf(playerId: string): PlayerLook | undefined {
    return this.live.players().get(playerId)?.player.look;
  }

  kitOf(playerId: string): Kit | undefined {
    const ref = this.live.players().get(playerId);
    return ref ? kitFor(ref.team, ref.player) : undefined;
  }

  isCaptain(playerId: string): boolean {
    return !!this.live.players().get(playerId)?.player.isCaptain;
  }

  nameOf(playerId: string): string {
    const ref = this.live.players().get(playerId);
    return ref ? `${ref.player.shirtNumber} ${ref.player.name}` : '';
  }

  readonly tag = computed(() => {
    const e = this.action();
    if (!e?.playerId) return null;
    const ref = this.live.players().get(e.playerId);
    const position = this.ball();
    if (!ref || !position) return null;
    const anchor = e.start ? toScreen(e.start, e.teamId, this.homeId()) : position;
    const pct = toPercent(anchor);
    return {
      left: pct.left,
      top: pct.top,
      shirt: ref.player.shirtNumber,
      name: ref.player.shortName,
      action: actionLabel(e),
      home: ref.team.id === this.homeId()
    };
  });

  readonly focusName = computed(() => {
    const id = this.live.profile().focusPlayerId;
    return id ? this.live.players().get(id)?.player.name ?? null : null;
  });

  private readonly focusTouches = computed(() => {
    const id = this.live.profile().focusPlayerId;
    if (!id) return [];
    const home = this.homeId();
    return this.live.events()
      .filter(e => e.playerId === id && e.start && TOUCH_TYPES.has(e.type))
      .map(e => toScreen(e.start!, e.teamId, home));
  });

  readonly heatTouches = computed(() => this.focusTouches().length);

  readonly heatCells = computed<HeatCell[]>(() => {
    const touches = this.focusTouches();
    if (touches.length === 0) return [];
    const w = PITCH_LENGTH / HEAT_COLUMNS;
    const h = PITCH_WIDTH / HEAT_ROWS;
    const counts = new Array<number>(HEAT_COLUMNS * HEAT_ROWS).fill(0);
    for (const t of touches) {
      const col = Math.min(HEAT_COLUMNS - 1, Math.floor(t.x / w));
      const row = Math.min(HEAT_ROWS - 1, Math.floor(t.y / h));
      counts[row * HEAT_COLUMNS + col]++;
    }
    const max = Math.max(...counts);
    const cells: HeatCell[] = [];
    counts.forEach((count, index) => {
      if (count === 0) return;
      cells.push({
        x: (index % HEAT_COLUMNS) * w,
        y: Math.floor(index / HEAT_COLUMNS) * h,
        w, h,
        opacity: 0.15 + 0.75 * (count / max)
      });
    });
    return cells;
  });

  readonly shots = computed<ShotMark[]>(() => {
    const home = this.homeId();
    const players = this.live.players();
    return this.live.events()
      .filter(e => e.type === 'Shot' && e.start)
      .map(e => {
        const p = toScreen(e.start!, e.teamId, home);
        const xg = e.xg ?? 0;
        const result = e.outcome === 'Goal' ? 'goal' : e.outcome === 'Saved' ? 'saved' : 'missed';
        const who = e.playerId ? players.get(e.playerId)?.player.shortName ?? '' : '';
        return { x: p.x, y: p.y, r: 0.7 + xg * 3.2, home: e.teamId === home, result, label: `${who} ${e.clock}, xG ${xg.toFixed(2)}` };
      });
  });

  /** Where to pin a near-player graphic: the player's latest location, or the ball. */
  anchorFor(insight: PersonalizedInsight): { left: number; top: number } | null {
    const playerId = insight.playerIds[0];
    const home = this.homeId();
    if (playerId) {
      const events = this.live.events();
      for (let i = events.length - 1; i >= 0; i--) {
        const e = events[i];
        if (e.playerId === playerId && e.start) return toPercent(toScreen(e.start, e.teamId, home));
      }
    }
    const ball = this.ball();
    return ball ? toPercent(ball) : null;
  }

  private findEvent(sequence: number | undefined): MatchEvent | null {
    if (sequence === undefined) return null;
    const events = this.live.events();
    for (let i = events.length - 1; i >= 0; i--) {
      if (events[i].sequence === sequence) return events[i];
      if (events[i].sequence < sequence) break;
    }
    return null;
  }
}

function loadCamera(): Camera {
  try {
    return localStorage.getItem(CAMERA_KEY) === 'tactical' ? 'tactical' : 'stadium';
  } catch {
    return 'stadium';
  }
}

const GOAL_WORDS: Record<string, string> = {
  en: 'GOOOAL!', es: '¡GOOOL!', pt: 'GOOOL!', it: 'GOOOL!', fr: 'BUUUT !', de: 'TOOOR!',
  ar: 'هدف!', hi: 'गोल!', ja: 'ゴール!'
};
