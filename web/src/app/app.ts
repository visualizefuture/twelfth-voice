import { Component, inject } from '@angular/core';
import { MatchViewComponent } from './components/match-view';
import { SetupComponent } from './components/setup';
import { LiveService } from './live.service';

@Component({
  selector: 'tv-root',
  imports: [SetupComponent, MatchViewComponent],
  template: `
    @if (live.info()) {
      <tv-match-view />
    } @else {
      <tv-setup />
    }
  `
})
export class App {
  readonly live = inject(LiveService);
}
