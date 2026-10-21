import { Component, OnDestroy, OnInit, output, signal } from '@angular/core';

const SEEN_KEY = 'twelfth-voice.intro-seen';
const SHOW_MS = 2400;
const FADE_MS = 500;

/**
 * The brand intro: a couple of seconds under the floodlights before the fixtures screen.
 * Shown once per browser session; a click or any key skips it. An optional photo
 * (public/intro-photo.jpg) appears on the left if it exists.
 */
@Component({
  selector: 'tv-intro',
  template: `
    <div class="intro" [class.leaving]="leaving()" role="presentation" (click)="skip()">
      <div class="lights" aria-hidden="true">
        <i></i><i></i><i></i><i></i>
      </div>
      <svg class="pitch" viewBox="0 0 105 68" preserveAspectRatio="xMidYMid slice" aria-hidden="true">
        <g fill="none" stroke="currentColor" stroke-width="0.35">
          <rect x="1" y="1" width="103" height="66" />
          <line x1="52.5" y1="1" x2="52.5" y2="67" />
          <circle cx="52.5" cy="34" r="9.15" />
          <rect x="1" y="13.84" width="16.5" height="40.32" />
          <rect x="87.5" y="13.84" width="16.5" height="40.32" />
        </g>
      </svg>

      @if (photo()) {
        <img class="photo" src="intro-photo.jpg" alt="" (error)="photo.set(false)" />
      }

      <div class="brand" [class.with-photo]="photo()">
        <p class="kicker">AI match intelligence</p>
        <h1 class="display">Twelfth <span>Voice</span></h1>
        <p class="line">Every detail of the game, in view. For fans, and for professional coaching.</p>
      </div>
      <button type="button" class="skip" (click)="skip(); $event.stopPropagation()">Skip</button>
    </div>
  `,
  styles: `
    .intro {
      position: fixed; inset: 0; z-index: 100; overflow: hidden; cursor: pointer;
      background: radial-gradient(120% 90% at 50% 110%, #164233 0%, #0b2219 45%, #050d0a 100%);
      display: grid; place-items: center; animation: fade-in 0.4s ease-out;
      transition: opacity ${FADE_MS}ms ease-in;
    }
    .intro.leaving { opacity: 0; }
    .pitch {
      position: absolute; left: 50%; bottom: -12%; width: 140%; height: 70%; color: rgb(238 244 238 / 0.18);
      transform: translateX(-50%) perspective(600px) rotateX(58deg); transform-origin: 50% 100%;
    }
    .lights { position: absolute; top: 6%; left: 8%; right: 8%; display: flex; justify-content: space-between; }
    .lights i {
      width: 46px; height: 22px; border-radius: 3px;
      background: repeating-linear-gradient(90deg, #fffbe6 0 22%, #6b6a5c 22% 28%);
      box-shadow: 0 0 30px 10px rgb(255 247 214 / 0.55), 0 0 120px 60px rgb(255 247 214 / 0.14);
      animation: flick 0.9s steps(3) both;
    }
    .lights i:nth-child(2) { animation-delay: 0.15s; } .lights i:nth-child(3) { animation-delay: 0.3s; } .lights i:nth-child(4) { animation-delay: 0.45s; }
    .brand { position: relative; text-align: center; padding: 0 24px; }
    .brand.with-photo { text-align: left; margin-left: 30%; }
    .kicker {
      margin: 0 0 10px; font-family: var(--display); letter-spacing: 0.3em; text-transform: uppercase;
      color: var(--moss); font-size: var(--step-0); animation: rise 0.6s 0.35s ease-out both;
    }
    h1 {
      margin: 0; font-size: clamp(56px, 12vw, 168px); line-height: 0.88; text-transform: uppercase; letter-spacing: 0.01em;
      animation: rise 0.7s 0.5s cubic-bezier(.2,.8,.2,1) both;
    }
    h1 span { color: var(--floodlight); }
    .line { margin: 18px auto 0; max-width: 34ch; font-size: var(--step-1); color: var(--chalk); animation: rise 0.6s 0.95s ease-out both; }
    .with-photo .line { margin-left: 0; }
    .photo {
      position: absolute; left: 4%; bottom: 0; height: 88%; max-width: 34%; object-fit: contain; object-position: bottom;
      filter: drop-shadow(0 10px 40px rgb(0 0 0 / 0.6)); animation: rise 0.8s 0.3s ease-out both;
    }
    .skip { position: absolute; right: 20px; bottom: 20px; font-size: var(--step--1); background: rgb(11 34 25 / 0.7); }
    @keyframes fade-in { from { opacity: 0; } }
    @keyframes rise { from { opacity: 0; transform: translateY(24px); } }
    @keyframes flick { 0% { opacity: 0; } 50% { opacity: 0.6; } 100% { opacity: 1; } }
    @media (max-width: 700px) {
      .photo { display: none; }
      .brand.with-photo { margin-left: 0; text-align: center; }
      .with-photo .line { margin-left: auto; }
    }
    @media (prefers-reduced-motion: reduce) {
      .intro, .lights i, .kicker, h1, .line, .photo { animation: none; }
    }
  `
})
export class IntroComponent implements OnInit, OnDestroy {
  readonly done = output<void>();
  readonly leaving = signal(false);
  readonly photo = signal(true);
  private timer: ReturnType<typeof setTimeout> | null = null;
  private readonly onKey = () => this.skip();

  /** Whether the intro should play in this browser session. */
  static shouldShow(): boolean {
    try {
      return sessionStorage.getItem(SEEN_KEY) !== '1';
    } catch {
      return true;
    }
  }

  ngOnInit(): void {
    try {
      sessionStorage.setItem(SEEN_KEY, '1');
    } catch {
      // storage unavailable: it may show again on refresh, which is fine
    }
    document.addEventListener('keydown', this.onKey);
    this.timer = setTimeout(() => this.skip(), SHOW_MS);
  }

  ngOnDestroy(): void {
    document.removeEventListener('keydown', this.onKey);
    if (this.timer) clearTimeout(this.timer);
  }

  skip(): void {
    if (this.leaving()) return;
    this.leaving.set(true);
    if (this.timer) clearTimeout(this.timer);
    setTimeout(() => this.done.emit(), FADE_MS);
  }
}
