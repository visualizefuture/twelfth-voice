import { Component, DestroyRef, HostListener, computed, effect, inject, signal, untracked } from '@angular/core';
import { LIFT_PER_METRE, ease, flightFor, flightPath, groundPath, pointAt } from '../ballflight';
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
              ▶ @if (teamOf(g.teamId); as t) { <tv-crest [team]="t" [size]="18" /> } {{ goalLabel(g) }}
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
      }

      <!-- goal: confetti thrown up from the stands, in the scoring club's colours -->
      @if (celebration(); as c) {
        <div class="confetti" aria-hidden="true">
          @for (bit of confetti; track $index) {
            <i [style.left.%]="bit.x" [style.top.%]="bit.y" [style.--dx]="bit.dx + 'px'" [style.--up]="bit.up + 'px'" [style.--fall]="bit.fall + 'px'"
               [style.animation-delay.s]="bit.delay" [style.animation-duration.s]="bit.duration">
              <b [class]="'bit ' + bit.shape" [style.background]="bit.tone === 0 ? c.a : bit.tone === 1 ? c.b : bit.tone === 2 ? '#ffffff' : '#f6c445'"
                 [style.animation-duration.s]="bit.flutter" [style.--spin]="bit.spin + 'deg'"></b>
            </i>
          }
        </div>
      }

      <div class="camera-rig">
        <div class="pitch">
          @if (stadium()) {
            <div class="apron" aria-hidden="true"></div>
            <div class="boards far" aria-hidden="true">
              @for (b of boards; track $index) { <span>{{ b }}</span> }
            </div>
          }
          <!-- goals: posts, crossbar and net; stood up in 3D in the stadium, seen from above on the tactical board -->
          @for (side of goalSides; track side) {
            <div [class]="'goalmouth ' + side" aria-hidden="true">
              <i class="net-side near"></i>
              <i class="net-side far"></i>
              <i class="net-top"><i class="net-back"></i></i>
            </div>
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
                          class="heat" [style.fill]="heatColour()" [attr.fill-opacity]="cell.opacity" />
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
                @if (flightLines(); as f) {
                  @if (f.lofted) {
                    <path [attr.d]="f.ground" class="ground-track" />
                  }
                  <path [attr.d]="f.air" [class]="'action ' + f.kind" marker-end="url(#arrow)" />
                }
                <!-- a pass the insight engine flagged as special -->
                @if (!replay.active() && highlightPass(); as pass) {
                  <line [attr.x1]="pass.from.x" [attr.y1]="pass.from.y" [attr.x2]="pass.to.x" [attr.y2]="pass.to.y"
                        class="highlight" marker-end="url(#arrow)" />
                }
                @if (ballState(); as b) {
                  <ellipse [attr.cx]="b.gx" [attr.cy]="b.gy + 0.4" [attr.rx]="1.1 * b.shadow" [attr.ry]="0.55 * b.shadow"
                           class="ball-shadow" [attr.fill-opacity]="0.45 * b.shadow" />
                  <g [attr.transform]="'translate(' + b.x + ' ' + b.y + ') scale(' + b.scale + ')'">
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
          <div class="layer-note heat-pick" role="group" aria-label="Whose heat map">
            @for (o of heatOptions(); track o.key) {
              <button type="button" [attr.aria-pressed]="heatSubject() === o.key" (click)="heatChoice.set(o.key)">{{ o.label }}</button>
            }
            <span>{{ heatTouches() }} actions</span>
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
    .replays button { display: inline-flex; align-items: center; gap: 6px; border-left: 3px solid var(--away); font-variant-numeric: tabular-nums; }
    .replays button.home { border-left-color: var(--home); }
    .replays .muted { font-size: var(--step--1); }
    .direction { margin-left: auto; font-size: var(--step--1); }

    /* ---------- stage: flat (tactical) by default ---------- */
    .stage:not(.stadium) .camera-rig { inset: 0 1.8%; }
    .stage:not(.stadium) { background: var(--night-2); }
    .stage { position: relative; width: 100%; aspect-ratio: 109 / 68; border-radius: 4px; overflow: hidden; --tilt: 0deg; }
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
    .ground-track { fill: none; stroke: #000; stroke-opacity: 0.28; stroke-width: 0.35; stroke-dasharray: 0.6 0.9; }
    .ball-shadow { fill: #000; }

    /* ---------- goals (tactical: from above, outside the goal line) ---------- */
    .goalmouth {
      position: absolute; top: calc(50% - 5.38%); height: 10.76%; width: 1.9%; z-index: 1;
      border: 2px solid #f4f6f8; box-shadow: 0 0 4px rgb(0 0 0 / 0.5);
      background:
        repeating-linear-gradient(0deg, rgb(255 255 255 / 0.35) 0 1px, transparent 1px 5px),
        repeating-linear-gradient(90deg, rgb(255 255 255 / 0.35) 0 1px, transparent 1px 5px),
        rgb(0 0 0 / 0.25);
    }
    .goalmouth.left { left: -1.9%; border-right: 0; }
    .goalmouth.right { right: -1.9%; border-left: 0; }
    .goalmouth::before, .goalmouth::after {
      content: ''; position: absolute; width: 5px; height: 5px; border-radius: 50%; background: #fff; box-shadow: 0 0 3px #000;
    }
    .goalmouth.left::before { right: -3px; top: -4px; } .goalmouth.left::after { right: -3px; bottom: -4px; }
    .goalmouth.right::before { left: -3px; top: -4px; } .goalmouth.right::after { left: -3px; bottom: -4px; }
    .goalmouth > i { display: none; }

    .trail { fill: none; stroke: var(--chalk); stroke-opacity: 0.25; stroke-width: 0.35; stroke-dasharray: 0.8 0.8; }
    .action { fill: none; stroke-width: 0.4; stroke: var(--chalk); stroke-opacity: 0.8; }
    .action.fail { stroke: var(--red-card); stroke-dasharray: 1 0.8; }
    .action.shot { stroke: var(--floodlight); stroke-width: 0.5; }
    .highlight { stroke: var(--floodlight); stroke-width: 0.6; }
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
      position: absolute; right: -10px; bottom: -7px; z-index: 1; min-width: 17px; height: 16px; padding: 0 3px; box-sizing: border-box;
      display: grid; place-items: center; border-radius: 4px; border: 1.5px solid;
      font-family: var(--display); font-weight: 700; font-size: 12px; line-height: 1; font-variant-numeric: tabular-nums;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.6);
    }
    .replaying .token { transition-duration: 0.8s; transition-timing-function: ease-in-out; }
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

    .confetti { position: absolute; inset: 0; pointer-events: none; overflow: hidden; z-index: 5; }
    /* the outer piece is thrown up and drifts down; the inner one flutters */
    .confetti > i {
      position: absolute; width: 0; height: 0; animation-name: toss; animation-timing-function: linear; animation-fill-mode: both;
    }
    .confetti .bit { position: absolute; display: block; animation: flutter ease-in-out infinite alternate; opacity: 0.95; }
    .confetti .strip { width: 4px; height: 11px; border-radius: 1px; }
    .confetti .square { width: 7px; height: 7px; }
    .confetti .dot { width: 6px; height: 6px; border-radius: 50%; }
    @keyframes toss {
      0% { transform: translate(0, 0); opacity: 0; animation-timing-function: cubic-bezier(.2,.7,.4,1); }
      6% { opacity: 1; }
      28% { transform: translate(calc(var(--dx) * 0.35), calc(var(--up) * -1)); animation-timing-function: cubic-bezier(.4,0,.8,.6); }
      100% { transform: translate(var(--dx), var(--fall)); opacity: 0.85; }
    }
    @keyframes flutter {
      0% { transform: rotate(0deg) rotateX(0deg) translateX(-3px); }
      100% { transform: rotate(var(--spin)) rotateX(320deg) translateX(3px); }
    }

    .heat-pick { display: flex; gap: 6px; align-items: center; pointer-events: auto; padding: 5px 6px; }
    .heat-pick button { padding: 3px 10px; font-size: var(--step--1); }
    .heat-pick button[aria-pressed="true"] { border-color: var(--chalk); background: var(--turf); }
    .heat-pick span { color: var(--moss); margin: 0 6px; }
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
    .stadium .lines { stroke-opacity: 0.85; stroke-width: 0.35; }
    /* goals stand up in 3D: the face (posts and crossbar) rises from the goal line, nets fold back from it */
    .stadium .goalmouth {
      top: calc(50% - 5.38%); height: 10.76%; width: 3.8%; border: 0; box-shadow: none; background: none;
      transform-style: preserve-3d; z-index: 3;
    }
    .stadium .goalmouth::before, .stadium .goalmouth::after { display: none; }
    .stadium .goalmouth.left { left: 0; transform-origin: 0 50%; transform: rotateY(-90deg); }
    .stadium .goalmouth.right { right: 0; left: auto; transform-origin: 100% 50%; transform: rotateY(90deg); }
    /* posts = the face's long edges, crossbar = its far edge */
    .stadium .goalmouth.left { border-top: 3px solid #fff; border-bottom: 3px solid #fff; border-right: 3px solid #fff; }
    .stadium .goalmouth.right { border-top: 3px solid #fff; border-bottom: 3px solid #fff; border-left: 3px solid #fff; }
    .stadium .goalmouth > i, .stadium .net-top > i { display: block; position: absolute; transform-style: preserve-3d; }
    .stadium .net-side, .stadium .net-top, .stadium .net-back {
      background:
        repeating-linear-gradient(0deg, rgb(255 255 255 / 0.55) 0 1px, transparent 1px 4px),
        repeating-linear-gradient(90deg, rgb(255 255 255 / 0.55) 0 1px, transparent 1px 4px);
      outline: 1px solid rgb(255 255 255 / 0.5);
    }
    .stadium .net-side { left: 0; width: 100%; height: 26%; transform-origin: 50% 0; transform: rotateX(90deg); }
    .stadium .net-side.near { top: 100%; }
    .stadium .net-side.far { top: 0; }
    .stadium .goalmouth.left .net-top { left: 100%; top: 0; height: 100%; width: 50%; transform-origin: 0 50%; transform: rotateY(-90deg); }
    .stadium .goalmouth.right .net-top { right: 100%; top: 0; height: 100%; width: 50%; transform-origin: 100% 50%; transform: rotateY(90deg); }
    .stadium .goalmouth.left .net-back { left: 100%; top: 0; height: 100%; width: 200%; transform-origin: 0 50%; transform: rotateY(-90deg); }
    .stadium .goalmouth.right .net-back { right: 100%; top: 0; height: 100%; width: 200%; transform-origin: 100% 50%; transform: rotateY(90deg); }
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

  constructor() {
    // Fly the ball along each new action's path.
    effect(() => {
      const action = this.action();
      const home = this.homeId();
      const slow = !!this.replay.active();
      untracked(() => this.fly(action, home, slow));
    });
    inject(DestroyRef).onDestroy(() => cancelAnimationFrame(this.frame));
  }

  private fly(action: MatchEvent | null, homeId: string, slow: boolean): void {
    cancelAnimationFrame(this.frame);
    const end = action ? ballPosition(action, homeId) : null;
    if (!end) {
      this.ballState.set(null);
      return;
    }
    const at = (x: number, y: number, height: number) => ({
      x, y: y - height * LIFT_PER_METRE, gx: x, gy: y, scale: 1 + height / 5, shadow: 1 / (1 + height / 4)
    });
    const flight = flightFor(action, homeId);
    const reduced = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (!flight || reduced) {
      this.ballState.set(at(end.x, end.y, 0));
      return;
    }
    const duration = flight.ms * (slow ? 1.5 : 1);
    const started = performance.now();
    const step = (now: number) => {
      const t = Math.min(1, (now - started) / duration);
      const { ground, height } = pointAt(flight, ease(t));
      this.ballState.set(at(ground.x, ground.y, height));
      if (t < 1) this.frame = requestAnimationFrame(step);
    };
    this.frame = requestAnimationFrame(step);
  }

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
    return {
      team,
      a: kit?.shirt ?? team.primaryColor,
      b: kit?.trim ?? team.secondaryColor,
    };
  });

  readonly goalSides = ['left', 'right'];

  /**
   * Confetti thrown up from the stands: each piece starts in a stand, flies up, then drifts down
   * across the stadium while fluttering. Fixed per session so it doesn't reshuffle on every render.
   */
  readonly confetti = Array.from({ length: 140 }, (_, i) => {
    const r = (n: number) => ((Math.sin((i + 1) * 12.9898 + n * 78.233) * 43758.5453) % 1 + 1) % 1;
    const stand = i % 4; // 0 far stand, 1 left, 2 right, 3 far stand again
    const x = stand === 1 ? 2 + r(1) * 20 : stand === 2 ? 78 + r(1) * 20 : 6 + r(1) * 88;
    const y = stand === 1 || stand === 2 ? 30 + r(2) * 60 : 10 + r(2) * 20;
    const shapes = ['strip', 'strip', 'square', 'dot'];
    return {
      x, y,
      dx: (stand === 1 ? 1 : stand === 2 ? -1 : r(3) < 0.5 ? -1 : 1) * (30 + r(4) * 160),
      up: 60 + r(5) * 170,
      fall: 260 + r(6) * 520,
      delay: (i % 2) * 1.1 + r(7) * 1.4,
      duration: 3.6 + r(8) * 2.6,
      flutter: 0.35 + r(9) * 0.5,
      spin: 180 + r(10) * 540,
      tone: Math.floor(r(11) * 4),
      shape: shapes[Math.floor(r(12) * shapes.length)]
    };
  });

  /** The kick drawn as it travels: bent along the grass and, for long balls, up in the air. */
  readonly flightLines = computed(() => {
    const f = flightFor(this.action(), this.homeId());
    if (!f || f.kind === 'carry') {
      const e = this.action();
      if (!f || !e?.start || !e.end) return null;
      return { air: flightPath(f, 2), ground: '', lofted: false, kind: f.kind === 'carry' ? 'ok' : f.kind };
    }
    return { air: flightPath(f), ground: groundPath(f), lofted: f.height > 1, kind: f.kind };
  });

  /** The ball as drawn this frame: its shadow on the grass, and the ball lifted and scaled by its height. */
  readonly ballState = signal<{ x: number; y: number; gx: number; gy: number; scale: number; shadow: number } | null>(null);
  private frame = 0;

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

  /** The club a goal was scored for, so each replay button carries its crest. */
  teamOf(teamId: string | undefined) {
    const info = this.live.info();
    return !info || !teamId ? null : teamId === info.home.id ? info.home : teamId === info.away.id ? info.away : null;
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

  /** Whose heat map: a club, or the player being followed. Null = the sensible default. */
  readonly heatChoice = signal<'player' | 'home' | 'away' | null>(null);

  readonly heatOptions = computed(() => {
    const info = this.live.info();
    const options: { key: 'player' | 'home' | 'away'; label: string }[] = [];
    if (this.focusName()) options.push({ key: 'player', label: this.live.players().get(this.live.profile().focusPlayerId!)?.player.shortName ?? 'Player' });
    if (info) {
      options.push({ key: 'home', label: info.home.shortName });
      options.push({ key: 'away', label: info.away.shortName });
    }
    return options;
  });

  /** The followed player if there is one, else the viewer's club, else the home side. */
  readonly heatSubject = computed<'player' | 'home' | 'away'>(() => {
    const choice = this.heatChoice();
    if (choice === 'player' && this.focusName()) return 'player';
    if (choice === 'home' || choice === 'away') return choice;
    if (this.focusName()) return 'player';
    const favourite = this.live.profile().favoriteTeamId;
    return favourite && favourite === this.live.info()?.away.id ? 'away' : 'home';
  });

  readonly heatColour = computed(() => {
    const subject = this.heatSubject();
    return subject === 'home' ? 'var(--home)' : subject === 'away' ? 'var(--away)' : 'var(--floodlight)';
  });

  private readonly focusTouches = computed(() => {
    const subject = this.heatSubject();
    const info = this.live.info();
    const home = this.homeId();
    const playerId = this.live.profile().focusPlayerId;
    const teamId = subject === 'home' ? info?.home.id : subject === 'away' ? info?.away.id : undefined;
    return this.live.events()
      .filter(e => e.start && TOUCH_TYPES.has(e.type) && (subject === 'player' ? e.playerId === playerId : e.teamId === teamId))
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

