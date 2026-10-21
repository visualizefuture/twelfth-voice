import { Component, computed, input } from '@angular/core';
import { Kit, PlayerLook } from '../models';

let nextId = 0;

const DEFAULT_LOOK: PlayerLook = { skinTone: '#C68B59', hairStyle: 'short', hairColor: '#2B1B10', facialHair: 'none' };
const DEFAULT_KIT: Kit = { shirt: '#5c6b7a', trim: '#e8ecf0', pattern: 'Plain', shorts: '#e8ecf0', socks: '#5c6b7a', number: '#ffffff' };

/**
 * An illustrated head-and-shoulders portrait in the player's kit. Generated from the player's
 * look, so every player is distinct, and no real person is depicted.
 */
@Component({
  selector: 'tv-portrait',
  template: `
    <svg viewBox="0 0 64 64" [attr.width]="size()" [attr.height]="size()" role="img" [attr.aria-label]="label() || null"
         [attr.aria-hidden]="label() ? null : true">
      <defs>
        <clipPath [attr.id]="frameId"><circle cx="32" cy="32" r="31" /></clipPath>
        <clipPath [attr.id]="shirtId"><path [attr.d]="shirtPath" /></clipPath>
      </defs>
      <g [attr.clip-path]="'url(#' + frameId + ')'">
        <rect width="64" height="64" [attr.fill]="background()" />

        @if (look().hairStyle === 'afro') {
          <circle cx="32" cy="24" r="16.5" [attr.fill]="look().hairColor" />
        }
        @if (look().hairStyle === 'long') {
          <path d="M19 24 Q18 44 24 47 L40 47 Q46 44 45 24 Z" [attr.fill]="look().hairColor" />
        }

        <!-- shirt, with the club's pattern -->
        <path [attr.d]="shirtPath" [attr.fill]="kit().shirt" />
        <g [attr.clip-path]="'url(#' + shirtId + ')'" [attr.fill]="kit().trim">
          @switch (kit().pattern) {
            @case ('Stripes') { <rect x="13" y="40" width="7" height="24" /><rect x="28.5" y="40" width="7" height="24" /><rect x="44" y="40" width="7" height="24" /> }
            @case ('Hoops') { <rect x="0" y="48" width="64" height="5" /><rect x="0" y="58" width="64" height="5" /> }
            @case ('Halves') { <rect x="32" y="40" width="32" height="24" /> }
            @case ('Sash') { <path d="M14 40 L24 40 L54 64 L44 64 Z" /> }
            @case ('Sleeves') { <rect x="0" y="40" width="15" height="24" /><rect x="49" y="40" width="15" height="24" /> }
          }
        </g>
        <path d="M26 45 L32 52 L38 45" fill="none" [attr.stroke]="kit().trim" stroke-width="2.4" stroke-linejoin="round" />
        @if (number() !== null) {
          <text x="32" y="62" text-anchor="middle" font-size="9" font-weight="700" font-family="Barlow Condensed, Arial Narrow, sans-serif"
                [attr.fill]="kit().number">{{ number() }}</text>
        }

        <!-- neck and head -->
        <rect x="27.5" y="36" width="9" height="10" rx="3" [attr.fill]="look().skinTone" />
        <rect x="27.5" y="40" width="9" height="3" fill="#000" opacity="0.12" />
        <ellipse cx="32" cy="27" rx="11" ry="13" [attr.fill]="look().skinTone" />
        <ellipse cx="20.6" cy="28" rx="1.8" ry="2.8" [attr.fill]="look().skinTone" />
        <ellipse cx="43.4" cy="28" rx="1.8" ry="2.8" [attr.fill]="look().skinTone" />

        <!-- hair -->
        @switch (look().hairStyle) {
          @case ('short') { <path d="M21 25 Q20 13 32 13 Q44 13 43 25 Q40 18 32 18.5 Q24 18 21 25 Z" [attr.fill]="look().hairColor" /> }
          @case ('buzz') { <path d="M21.5 24 Q21 14 32 14 Q43 14 42.5 24 Q39 18.5 32 18.5 Q25 18.5 21.5 24 Z" [attr.fill]="look().hairColor" opacity="0.7" /> }
          @case ('fringe') { <path d="M21 26 Q20 12 32 12 Q44 12 43 26 Q41 20 36 21 Q30 18 25 22 Q22 23 21 26 Z" [attr.fill]="look().hairColor" /> }
          @case ('curly') {
            <g [attr.fill]="look().hairColor">
              <circle cx="23.5" cy="20" r="4.2" /><circle cx="29" cy="16" r="4.4" /><circle cx="35" cy="16" r="4.4" />
              <circle cx="40.5" cy="20" r="4.2" /><circle cx="32" cy="14.5" r="3.6" />
            </g>
          }
          @case ('afro') { <path d="M20 25 Q19 11 32 11 Q45 11 44 25 Q40 17 32 17 Q24 17 20 25 Z" [attr.fill]="look().hairColor" /> }
          @case ('long') { <path d="M20.5 27 Q19.5 12 32 12 Q44.5 12 43.5 27 Q41 18 32 18 Q23 18 20.5 27 Z" [attr.fill]="look().hairColor" /> }
          @case ('bun') {
            <circle cx="32" cy="10.5" r="4.6" [attr.fill]="look().hairColor" />
            <path d="M21 25 Q20 13 32 13 Q44 13 43 25 Q40 18 32 18.5 Q24 18 21 25 Z" [attr.fill]="look().hairColor" />
          }
        }

        <!-- face -->
        <circle cx="27.8" cy="27.5" r="1.25" fill="#1d1d1d" />
        <circle cx="36.2" cy="27.5" r="1.25" fill="#1d1d1d" />
        <path d="M26 24.3 Q27.8 23.3 29.6 24.1 M34.4 24.1 Q36.2 23.3 38 24.3" fill="none" [attr.stroke]="look().hairColor" stroke-width="1" stroke-linecap="round" opacity="0.8" />
        <path d="M32 29 Q31 32.5 32.6 33" fill="none" stroke="#000" stroke-opacity="0.2" stroke-width="0.9" stroke-linecap="round" />
        @switch (look().facialHair) {
          @case ('stubble') { <path d="M22.5 31 Q23 40 32 40.5 Q41 40 41.5 31 Q38 36 32 36.5 Q26 36 22.5 31 Z" [attr.fill]="look().hairColor" opacity="0.28" /> }
          @case ('beard') { <path d="M21.6 29 Q22 41.5 32 42 Q42 41.5 42.4 29 Q39.5 35.2 32 35.6 Q24.5 35.2 21.6 29 Z" [attr.fill]="look().hairColor" /> }
          @case ('moustache') { <path d="M28 34.2 Q32 32.6 36 34.2 Q32 35.4 28 34.2 Z" [attr.fill]="look().hairColor" /> }
        }
        <path d="M29.4 36.3 Q32 37.8 34.6 36.3" fill="none" stroke="#5a2a1f" stroke-width="1" stroke-linecap="round" opacity="0.75" />
      </g>

      @if (captain()) {
        <g aria-hidden="true">
          <circle cx="53" cy="53" r="9" fill="#f6c445" stroke="#0b2219" stroke-width="2" />
          <text x="53" y="57.2" text-anchor="middle" font-size="11" font-weight="700" fill="#1d1600" font-family="Barlow Condensed, Arial Narrow, sans-serif">C</text>
        </g>
      }
    </svg>
  `,
  styles: `:host { display: inline-flex; line-height: 0; }`
})
export class PortraitComponent {
  readonly playerLook = input<PlayerLook | null | undefined>(null);
  readonly playerKit = input<Kit | null | undefined>(null);
  readonly size = input(48);
  readonly number = input<number | null>(null);
  readonly captain = input(false);
  readonly label = input<string>('');
  readonly backdrop = input<string | null>(null);

  readonly frameId = `tv-portrait-frame-${nextId}`;
  readonly shirtId = `tv-portrait-shirt-${nextId++}`;
  readonly shirtPath = 'M4 64 Q6 46 20 42 L27 40.5 Q32 44 37 40.5 L44 42 Q58 46 60 64 Z';

  readonly look = computed(() => this.playerLook() ?? DEFAULT_LOOK);
  readonly kit = computed(() => this.playerKit() ?? DEFAULT_KIT);
  readonly background = computed(() => this.backdrop() ?? '#1f4f3d');
}
