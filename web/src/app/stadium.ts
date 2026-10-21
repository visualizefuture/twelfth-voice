/**
 * Textures for the stadium view, drawn in code (no image files): a packed crowd in the two
 * clubs' colours, home fans at the left end and away fans at the right.
 */

/** Small deterministic random numbers, so the crowd doesn't reshuffle on every render. */
function rng(seed: number): () => number {
  let state = seed >>> 0 || 1;
  return () => {
    state ^= state << 13;
    state ^= state >>> 17;
    state ^= state << 5;
    return ((state >>> 0) % 100000) / 100000;
  };
}

const NEUTRAL = ['#d9d4c7', '#3a3f45', '#8a8f96', '#c94f3d', '#2d4f7c', '#e6e1d3', '#24282c'];
const SKIN = ['#f1c9a5', '#e0ac85', '#c68863', '#9a6646', '#6e4630', '#4a2f20'];

/**
 * A tile of crowd: rows of heads and shoulders. `bias` runs 0..1 from all-away to all-home
 * colours, so each stand can lean towards one set of fans.
 */
export function crowdTile(home: string, away: string, bias: number, seed: number, width = 240, height = 96): string {
  const random = rng(seed);
  const parts: string[] = [];
  const rowGap = 8;
  for (let row = 0; row * rowGap < height + rowGap; row++) {
    const y = row * rowGap + 4;
    const offset = (row % 2) * 3.5;
    for (let x = offset; x < width + 7; x += 7) {
      const roll = random();
      const shirt = roll < 0.62 * bias ? home : roll < 0.62 ? away : NEUTRAL[Math.floor(random() * NEUTRAL.length)];
      const skin = SKIN[Math.floor(random() * SKIN.length)];
      const jx = x + (random() - 0.5) * 2;
      const jy = y + (random() - 0.5) * 1.5;
      // shoulders, then head; a few fans have their arms up
      parts.push(`<rect x="${(jx - 3).toFixed(1)}" y="${(jy + 2).toFixed(1)}" width="6" height="5" rx="2" fill="${shirt}"/>`);
      parts.push(`<circle cx="${jx.toFixed(1)}" cy="${(jy).toFixed(1)}" r="2" fill="${skin}"/>`);
      if (random() < 0.06) {
        parts.push(`<rect x="${(jx - 3.6).toFixed(1)}" y="${(jy - 5).toFixed(1)}" width="1.4" height="7" fill="${shirt}"/>`);
        parts.push(`<rect x="${(jx + 2.2).toFixed(1)}" y="${(jy - 5).toFixed(1)}" width="1.4" height="7" fill="${shirt}"/>`);
      }
    }
  }
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}">`
    + `<rect width="${width}" height="${height}" fill="#14181c"/>${parts.join('')}</svg>`;
  return `url("data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}")`;
}
