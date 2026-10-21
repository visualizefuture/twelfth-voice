import { Component, computed, input } from '@angular/core';
import { Team } from '../models';
import { CrestComponent } from './crest';

/**
 * A club banner waving in the stand: the match kit's colours as a flag, with the crest on it.
 * The wave is pure CSS (a moving fold shading plus a gentle sway) and stops for reduced motion.
 */
@Component({
  selector: 'tv-club-banner',
  imports: [CrestComponent],
  template: `
    <span class="pole" [class.right]="side() === 'away'" [class.big]="size() === 'big'" aria-hidden="true">
      <span class="flag" [style.--a]="colours().a" [style.--b]="colours().b" [class.hoops]="hoops()">
        <tv-crest [team]="team()" [size]="size() === 'big' ? 34 : 16" />
        <span class="folds"></span>
      </span>
    </span>
  `,
  styles: `
    :host { display: inline-flex; }
    .pole { display: inline-flex; align-items: flex-start; padding-left: 3px; border-left: 2px solid var(--moss); height: 30px; }
    .pole.right { flex-direction: row-reverse; padding: 0 3px 0 0; border-left: 0; border-right: 2px solid var(--moss); }
    .pole.big { height: 64px; border-width: 3px; }
    .flag {
      position: relative; width: 34px; height: 22px; display: grid; place-items: center; overflow: hidden;
      background: linear-gradient(90deg, var(--a) 0 50%, var(--b) 50% 100%);
      transform-origin: left center; animation: sway 1.6s ease-in-out infinite;
      clip-path: polygon(0 0, 100% 6%, 100% 94%, 0 100%);
    }
    .flag.hoops { background: repeating-linear-gradient(180deg, var(--a) 0 25%, var(--b) 25% 50%); }
    .right .flag { transform-origin: right center; animation-delay: -0.8s; clip-path: polygon(0 6%, 100% 0, 100% 100%, 0 94%); }
    .big .flag { width: 78px; height: 50px; }
    .folds {
      position: absolute; inset: 0;
      background: repeating-linear-gradient(90deg,
        rgb(0 0 0 / 0.28) 0, rgb(255 255 255 / 0.18) 25%, rgb(0 0 0 / 0.28) 50%);
      background-size: 200% 100%; mix-blend-mode: soft-light; animation: ripple 1.1s linear infinite;
    }
    .flag tv-crest { position: relative; z-index: 1; filter: drop-shadow(0 1px 1px rgb(0 0 0 / 0.5)); }
    @keyframes sway {
      0%, 100% { transform: perspective(120px) rotateY(-14deg) skewY(2deg); }
      50% { transform: perspective(120px) rotateY(10deg) skewY(-3deg); }
    }
    @keyframes ripple { from { background-position: 0 0; } to { background-position: -200% 0; } }
    @media (prefers-reduced-motion: reduce) { .flag, .folds { animation: none; } }
  `
})
export class ClubBannerComponent {
  readonly team = input.required<Team>();
  readonly side = input<'home' | 'away'>('home');
  readonly size = input<'small' | 'big'>('small');

  readonly colours = computed(() => {
    const t = this.team();
    const kit = t.matchKit;
    return kit ? { a: kit.shirt, b: kit.trim } : { a: t.primaryColor, b: t.secondaryColor };
  });

  readonly hoops = computed(() => this.team().matchKit?.pattern === 'Hoops');
}
