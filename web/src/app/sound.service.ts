import { Injectable, signal } from '@angular/core';
import { PersonalizedInsight, ViewerProfile } from './models';

const SOUND_KEY = 'twelfth-voice.sound';

interface SoundSettings {
  /** Crowd and the goal call. */
  enabled: boolean;
  /** Read the crew's commentary lines aloud too. */
  commentary: boolean;
}

const DEFAULT_SETTINGS: SoundSettings = { enabled: true, commentary: true };

/** Moments worth reading aloud when spoken commentary is on (goals get their own call). */
const SPOKEN_KINDS = new Set(['Kickoff', 'BigChance', 'Card', 'MomentumShift', 'PlayerMilestone', 'HalfTime', 'FullTime']);
const MAX_QUEUE = 2;
/** Lines older than this (real seconds) are skipped: the match has moved on. */
const STALE_AFTER_MS = 20_000;

interface QueuedLine {
  insightId: string;
  matchId: string;
  profile: ViewerProfile;
  queuedAt: number;
}

/**
 * The match's sound: a crowd roar made in the browser (Web Audio, no files), the goal call and
 * spoken commentary from Azure AI Speech via the API. Browsers only allow audio after a click, so
 * {@link unlock} is called from the Kick off and Watch buttons.
 */
@Injectable({ providedIn: 'root' })
export class SoundService {
  private context: AudioContext | null = null;
  private noise: AudioBuffer | null = null;
  private readonly queue: QueuedLine[] = [];
  private speaking = false;
  private voice: AudioBufferSourceNode | null = null;
  private goalInProgress: Promise<void> | null = null;
  /** Bumped on every goal, so commentary that was loading at the time is dropped. */
  private generation = 0;
  private readonly spoken = new Set<string>();
  private listening = false;

  readonly settings = signal<SoundSettings>(loadSettings());
  /** False once the server says Azure AI Speech isn't configured; the browser's own voice stands in. */
  readonly cloudVoice = signal(true);

  /**
   * Call from a click: creates or resumes the audio context so later sounds may play. Phones are
   * stricter than desktops: iOS only unlocks Web Audio if a sound actually starts inside the tap,
   * and mutes it when the ring switch is on silent unless the page asks for media playback.
   */
  unlock(): void {
    try {
      // Safari 16.4+: play like a video (ignores the silent switch) rather than like a notification.
      const session = (navigator as Navigator & { audioSession?: { type: string } }).audioSession;
      if (session) session.type = 'playback';

      this.context ??= new AudioContext();
      if (this.context.state !== 'running') void this.context.resume();

      // A one-sample silent sound started inside the tap is what unlocks audio on iOS.
      const silence = this.context.createBuffer(1, 1, this.context.sampleRate);
      const source = this.context.createBufferSource();
      source.buffer = silence;
      source.connect(this.context.destination);
      source.start(0);

      if (!this.listening) {
        this.listening = true;
        // Phones suspend audio when the tab is hidden or the screen locks; wake it on the next touch.
        const wake = () => {
          if (this.context && this.context.state !== 'running') void this.context.resume();
        };
        document.addEventListener('visibilitychange', wake);
        document.addEventListener('touchend', wake, { passive: true });
        document.addEventListener('click', wake);
      }
    } catch {
      this.context = null; // no Web Audio; the app stays silent
    }
  }

  update(changes: Partial<SoundSettings>): void {
    const next = { ...this.settings(), ...changes };
    this.settings.set(next);
    saveSettings(next);
    if (!next.enabled) this.stop();
    else this.unlock();
  }

  toggle(): void {
    this.update({ enabled: !this.settings().enabled });
  }

  /** Stops anything playing and forgets queued lines (e.g. when leaving a match). */
  stop(): void {
    this.queue.length = 0;
    this.spoken.clear();
    this.generation++;
    this.stopVoice();
  }

  /** A new moment arrived live. */
  onInsight(insight: PersonalizedInsight, profile: ViewerProfile): void {
    if (!this.settings().enabled || !this.context) return;

    if (insight.kind === 'Goal') {
      this.roar(6, 1);
      void this.goalCall(insight, profile);
      return;
    }
    if (insight.kind === 'BigChance') this.roar(2.2, 0.45);
    if (insight.kind === 'Card') this.roar(1.6, 0.3);
    if (insight.kind === 'FullTime') this.roar(4, 0.6);

    if (this.settings().commentary && SPOKEN_KINDS.has(insight.kind) && insight.relevance >= 0.5) {
      // Give the crew a moment to rewrite (and translate) the line, so the viewer hears the AI version.
      setTimeout(() => this.enqueue(insight, profile), 3500);
    }
  }

  // ------------------------------------------------------------------ crowd

  /** Filtered noise swelling and fading: a stadium roar, generated on the fly. */
  private roar(seconds: number, loudness: number): void {
    const ctx = this.context;
    if (!ctx) return;
    this.noise ??= makeNoise(ctx, 4);

    const now = ctx.currentTime;
    const master = ctx.createGain();
    master.gain.setValueAtTime(0.0001, now);
    master.gain.exponentialRampToValueAtTime(0.32 * loudness, now + 0.35);
    master.gain.setValueAtTime(0.32 * loudness, now + seconds * 0.55);
    master.gain.exponentialRampToValueAtTime(0.0001, now + seconds);
    master.connect(ctx.destination);

    // Two bands: the crowd's body and the higher "voices", each slightly wobbling.
    for (const [frequency, q, level] of [[450, 0.7, 1], [1400, 1.2, 0.55]] as const) {
      const source = ctx.createBufferSource();
      source.buffer = this.noise;
      source.loop = true;
      const band = ctx.createBiquadFilter();
      band.type = 'bandpass';
      band.frequency.value = frequency;
      band.Q.value = q;
      const gain = ctx.createGain();
      gain.gain.value = level;

      const wobble = ctx.createOscillator();
      wobble.frequency.value = 0.6 + Math.random() * 0.8;
      const depth = ctx.createGain();
      depth.gain.value = frequency * 0.15;
      wobble.connect(depth).connect(band.frequency);

      source.connect(band).connect(gain).connect(master);
      source.start(now, Math.random() * 3);
      source.stop(now + seconds + 0.1);
      wobble.start(now);
      wobble.stop(now + seconds + 0.1);
    }
  }

  // ------------------------------------------------------------------ voice

  /**
   * One voice at a time. A goal call cuts off whatever commentary is playing and empties the
   * queue; commentary waits until the goal call has finished.
   */
  private async goalCall(insight: PersonalizedInsight, profile: ViewerProfile): Promise<void> {
    if (this.spoken.has(insight.insightId)) return;
    this.spoken.add(insight.insightId);
    this.generation++;
    this.queue.length = 0;
    this.stopVoice();

    const url = `/api/matches/${encodeURIComponent(insight.matchId)}/speech/${encodeURIComponent(insight.insightId)}/call`
      + `?language=${encodeURIComponent(profile.language)}`;
    const done = (async () => {
      const clip = await this.fetchClip(url);
      if (clip) {
        this.stopVoice(); // anything that slipped in while the call was loading
        await this.play(clip);
      } else if (!this.cloudVoice()) {
        // Only when the server has no Azure voice at all: the browser's own, with the word stretched out.
        // (If Azure is set up but one call fails, stay silent rather than mix in a different voice.)
        await this.speakLocally(`Gooooooooooooaaaaaaal! ${insight.headline.replace(/^GOAL!\s*/i, '')}`, profile.language, 1.25);
      }
    })();
    this.goalInProgress = done;
    try {
      await done;
    } finally {
      if (this.goalInProgress === done) this.goalInProgress = null;
    }
  }

  private enqueue(insight: PersonalizedInsight, profile: ViewerProfile): void {
    if (this.spoken.has(insight.insightId) || !this.settings().commentary || !this.settings().enabled) return;
    this.queue.push({ insightId: insight.insightId, matchId: insight.matchId, profile, queuedAt: Date.now() });
    while (this.queue.length > MAX_QUEUE) this.queue.shift();
    void this.drain();
  }

  private async drain(): Promise<void> {
    if (this.speaking) return;
    this.speaking = true;
    try {
      for (let next = this.queue.shift(); next; next = this.queue.shift()) {
        while (this.goalInProgress) await this.goalInProgress;
        if (Date.now() - next.queuedAt > STALE_AFTER_MS || this.spoken.has(next.insightId)) continue;
        this.spoken.add(next.insightId);
        const p = next.profile;
        const params = new URLSearchParams({ mode: p.mode, language: p.language });
        if (p.focusPlayerId) params.set('focusPlayerId', p.focusPlayerId);
        if (p.favoriteTeamId) params.set('favoriteTeamId', p.favoriteTeamId);

        const generation = this.generation;
        const clip = await this.fetchClip(`/api/matches/${encodeURIComponent(next.matchId)}/speech/${encodeURIComponent(next.insightId)}?${params}`);
        // A goal went in while this line was loading: the line is old news.
        if (!clip || generation !== this.generation || this.goalInProgress || !this.settings().commentary) continue;
        await this.play(clip);
      }
    } finally {
      this.speaking = false;
    }
  }

  /** MP3 from the API, decoded. Null when the server has no voice or the request fails. */
  private async fetchClip(url: string): Promise<AudioBuffer | null> {
    const ctx = this.context;
    if (!ctx || !this.cloudVoice()) return null;
    try {
      const response = await fetch(url);
      if (response.status === 503) {
        this.cloudVoice.set(false);
        return null;
      }
      if (!response.ok || response.status === 204) return null;
      return await ctx.decodeAudioData(await response.arrayBuffer());
    } catch {
      return null;
    }
  }

  /** Plays a clip on the single voice channel and resolves when it ends or is cut off. */
  private play(clip: AudioBuffer): Promise<void> {
    const ctx = this.context;
    if (!ctx || !this.settings().enabled) return Promise.resolve();
    if (ctx.state !== 'running') void ctx.resume();
    this.stopVoice();
    const source = ctx.createBufferSource();
    source.buffer = clip;
    const gain = ctx.createGain();
    gain.gain.value = 1.4; // sit on top of the crowd
    source.connect(gain).connect(ctx.destination);
    this.voice = source;
    return new Promise<void>(resolve => {
      source.onended = () => {
        if (this.voice === source) this.voice = null;
        resolve();
      };
      source.start();
    });
  }

  private stopVoice(): void {
    const current = this.voice;
    this.voice = null;
    try {
      current?.stop();
    } catch {
      // already stopped
    }
    try {
      window.speechSynthesis?.cancel();
    } catch {
      // not supported
    }
  }

  private speakLocally(text: string, language: string, pitch = 1): Promise<void> {
    return new Promise<void>(resolve => {
      try {
        const synth = window.speechSynthesis;
        if (!synth) return resolve();
        const utterance = new SpeechSynthesisUtterance(text);
        utterance.lang = language;
        utterance.rate = 0.9;
        utterance.pitch = pitch;
        utterance.onend = () => resolve();
        utterance.onerror = () => resolve();
        synth.speak(utterance);
        setTimeout(resolve, 12_000); // never hold the queue forever
      } catch {
        resolve();
      }
    });
  }
}

function makeNoise(ctx: AudioContext, seconds: number): AudioBuffer {
  const buffer = ctx.createBuffer(1, ctx.sampleRate * seconds, ctx.sampleRate);
  const data = buffer.getChannelData(0);
  // Pinkish noise (Paul Kellet's filter) sounds more like a crowd than white noise.
  let b0 = 0, b1 = 0, b2 = 0;
  for (let i = 0; i < data.length; i++) {
    const white = Math.random() * 2 - 1;
    b0 = 0.99765 * b0 + white * 0.099046;
    b1 = 0.963 * b1 + white * 0.2965164;
    b2 = 0.57 * b2 + white * 1.0526913;
    data[i] = (b0 + b1 + b2 + white * 0.1848) * 0.2;
  }
  return buffer;
}

function loadSettings(): SoundSettings {
  try {
    const raw = localStorage.getItem(SOUND_KEY);
    return raw ? { ...DEFAULT_SETTINGS, ...JSON.parse(raw) } : { ...DEFAULT_SETTINGS };
  } catch {
    return { ...DEFAULT_SETTINGS };
  }
}

function saveSettings(settings: SoundSettings): void {
  try {
    localStorage.setItem(SOUND_KEY, JSON.stringify(settings));
  } catch {
    // storage unavailable; settings apply for this session
  }
}
