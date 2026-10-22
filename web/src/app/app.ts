import { Component, inject, signal } from '@angular/core';
import { IntroComponent } from './components/intro';
import { MatchViewComponent } from './components/match-view';
import { SetupComponent } from './components/setup';
import { LiveService } from './live.service';

@Component({
  selector: 'tv-root',
  imports: [SetupComponent, MatchViewComponent, IntroComponent],
  template: `
    @if (live.info()) {
      <tv-match-view />
    } @else {
      <tv-setup />
    }
    @if (intro()) {
      <tv-intro (done)="intro.set(false)" />
    }
  `
})
export class App {
  readonly live = inject(LiveService);
  readonly intro = signal(IntroComponent.shouldShow());
}
