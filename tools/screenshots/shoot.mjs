// Drives the fan app in a headless browser and saves screenshots: fixtures, live stadium view,
// a goal, the goal replay and the tactical camera. Run by .github/workflows/screenshots.yml.
import { chromium } from 'playwright';
import { mkdir } from 'node:fs/promises';

const base = process.env.APP_URL ?? 'http://localhost:5080';
const out = process.env.OUT_DIR ?? 'screenshots';
await mkdir(out, { recursive: true });

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2 });
page.on('console', m => { if (m.type() === 'error') console.log('console error:', m.text()); });
const shot = async (name, locator) => {
  const target = locator ?? page;
  await target.screenshot({ path: `${out}/${name}.png` });
  console.log('saved', name);
};

await page.goto(base);
// The brand intro plays first; capture it, then let it finish.
await page.waitForTimeout(1400);
await shot('00-intro');
await page.waitForSelector('tv-intro', { state: 'detached', timeout: 8000 }).catch(() => undefined);
await page.waitForSelector('form.fixture select[name="home"] option', { state: 'attached' });
await page.waitForTimeout(800);
await shot('01-fixtures');

await page.locator('input[name="speed"]').fill('2'); // 120x
await page.click('details.more summary');
await page.fill('input[name="seed"]', '7');
await shot('01b-fixtures-options');
await page.click('button.kick');
await page.waitForSelector('tv-pitch-view .stage');
await page.waitForTimeout(6000);
await shot('02-stadium-live');
await shot('02b-pitch-stadium', page.locator('tv-pitch-view .stage'));

// Wait for a goal (up to 90 s real time), capture the banner, then the automatic replay.
try {
  await page.waitForSelector('.card.goal', { timeout: 90_000 });
  await page.waitForTimeout(1200);
  await shot('03-goal');
  await shot('03b-goal-pitch', page.locator('tv-pitch-view'));
  await page.waitForTimeout(1500);
  await shot('03c-confetti', page.locator('tv-pitch-view .stage'));
  await page.waitForSelector('.replay-badge', { timeout: 15_000 });
  await page.waitForTimeout(2500);
  await shot('04-replay');
  await shot('04b-replay-pitch', page.locator('tv-pitch-view .stage'));
  await page.waitForTimeout(4000);
  await shot('05-replay-late', page.locator('tv-pitch-view .stage'));
  await page.waitForSelector('.replay-badge', { state: 'detached', timeout: 30_000 });
} catch (err) {
  console.log('no goal captured:', err.message);
}

const stage = page.locator('tv-pitch-view .stage');
const box = await stage.boundingBox();
if (box) {
  await page.screenshot({ path: `${out}/08-goal-closeup.png`, clip: { x: box.x, y: box.y + box.height * 0.35, width: box.width * 0.35, height: box.height * 0.5 } });
  console.log('saved 08-goal-closeup');
}

await page.click('.camera button:has-text("Tactical")');
await page.click('.tabs button:has-text("Heat map")');
await page.waitForTimeout(800);
await shot('06b-team-heat', page.locator('tv-pitch-view'));
await page.click('.tabs button:has-text("Live play")');
await page.waitForTimeout(2500);
await shot('06-tactical');

// Phone width
await page.click('.camera button:has-text("Stadium")');
await page.setViewportSize({ width: 390, height: 844 });
await page.waitForTimeout(2000);
await shot('07-phone');

await browser.close();
