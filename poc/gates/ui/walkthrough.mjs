// RT-108 gates G2 and G6: signs in through Keycloak, opens the designer, takes the screenshots,
// runs axe-core (WCAG 2.1 A/AA) and authors a small report with the keyboard only.
//   npm ci && node walkthrough.mjs      (CHROME_PATH overrides the browser; RT_WEB_PORT the port)
// Evidence goes to ../../evidence (attached to RT-108, not committed).
import { createRequire } from 'node:module';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import puppeteer from 'puppeteer-core';

const require = createRequire(import.meta.url);
const axeSource = require('axe-core').source;
const here = dirname(fileURLToPath(import.meta.url));
const evidence = join(here, '..', '..', 'evidence');
mkdirSync(evidence, { recursive: true });

const base = `http://localhost:${process.env.RT_WEB_PORT ?? '8080'}`;
const chrome = process.env.CHROME_PATH ?? 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const result = { base, steps: [], axe: {}, tabOrder: [] };
const step = (name, pass, detail = '') => {
  result.steps.push({ name, pass, detail });
  console.log(`  [${pass ? 'PASS' : 'FAIL'}] ${name} ${detail}`);
};

const browser = await puppeteer.launch({
  executablePath: chrome,
  headless: true,
  args: ['--lang=en-GB', '--no-first-run', '--no-default-browser-check'],
  defaultViewport: { width: 1440, height: 960 },
});
const page = await browser.newPage();
const status = () => page.$eval('p.status', (el) => el.textContent.trim());
const shot = (name) => page.screenshot({ path: join(evidence, name), fullPage: false });

async function axeScan(label) {
  await page.evaluate(axeSource);
  const report = await page.evaluate(() =>
    window.axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] } }),
  );
  writeFileSync(join(evidence, `g6-axe-${label}.json`), JSON.stringify(report, null, 1));
  result.axe[label] = {
    violations: report.violations.map((v) => ({ id: v.id, impact: v.impact, nodes: v.nodes.length, help: v.help })),
    passes: report.passes.length,
    incomplete: report.incomplete.map((v) => ({ id: v.id, nodes: v.nodes.length })),
  };
  const serious = report.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  step(`axe ${label}: no serious or critical WCAG 2.1 A/AA violations`, serious.length === 0,
    report.violations.map((v) => `${v.id}(${v.impact},${v.nodes.length})`).join(' ') || 'none');
}

/** Presses Tab until the focused element matches, proving it is reachable by keyboard. */
async function tabTo(predicateSource, limit = 250) {
  for (let i = 0; i < limit; i++) {
    await page.keyboard.press('Tab');
    const hit = await page.evaluate(`(${predicateSource})(document.activeElement)`);
    if (hit) return i + 1;
  }
  return -1;
}

try {
  // ----- G2: designer behind Keycloak sign-in -----
  await page.goto(`${base}/designer`, { waitUntil: 'networkidle2' });
  const onKeycloak = page.url().includes('/auth/realms/reporting-tool/');
  step('unauthenticated visit is sent to Keycloak sign-in', onKeycloak, new URL(page.url()).pathname);
  await shot('g2-keycloak-sign-in.png');
  await page.type('#username', 'author.ar');
  await page.type('#password', 'local-poc-only');
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), page.click('#kc-login')]);
  await page.waitForSelector('.designer .page', { timeout: 30000 });
  const user = await page.$eval('.app-header .user', (el) => el.textContent.trim());
  step('signed in and back on the designer (code + PKCE)', page.url().startsWith(`${base}/designer`) && user === 'author.ar', user);
  await shot('g2-designer-new.png');
  await axeScan('designer-new');

  const option = await page.$$eval('.toolbar select option', (opts) => {
    const o = opts.find((x) => x.textContent.includes('كشف'));
    return o?.value ?? null;
  });
  await page.select('.toolbar select', option);
  await page.waitForFunction(() => document.querySelector('p.status')?.textContent.includes('Opened'));
  const direction = await page.$eval('.designer .page', (el) => el.getAttribute('dir'));
  step('the Arabic statement opens mirrored (RTL) on the canvas', direction === 'rtl', await status());
  await shot('g2-designer-statement-rtl.png');
  await axeScan('designer-statement-rtl');

  // ----- G6: keyboard-only authoring -----
  await page.goto(`${base}/designer`, { waitUntil: 'networkidle2' });
  await page.waitForSelector('.designer .page');
  await page.evaluate(() => document.activeElement?.blur());
  for (let i = 0; i < 45; i++) {
    await page.keyboard.press('Tab');
    result.tabOrder.push(await page.evaluate(() => {
      const el = document.activeElement;
      const style = getComputedStyle(el);
      return {
        tag: el.tagName.toLowerCase(),
        role: el.getAttribute('role'),
        name: (el.getAttribute('aria-label') ?? el.textContent ?? '').trim().replace(/\s+/g, ' ').slice(0, 70),
        focusVisible: style.outlineStyle !== 'none' || style.boxShadow !== 'none',
      };
    }));
  }
  // Focus wraps to <body> after the last control; that is the browser, not a tab stop.
  const invisible = result.tabOrder.filter((t) => !t.focusVisible && t.tag !== 'body');
  step('every tab stop shows a focus indicator', invisible.length === 0, `${result.tabOrder.length} stops, ${invisible.length} without`);

  await page.evaluate(() => document.activeElement?.blur());
  const toNew = await tabTo(`(el) => el.textContent?.trim() === 'New (English, LTR)'`);
  await page.keyboard.press('Enter');
  const toTable = await tabTo(`(el) => el.tagName === 'BUTTON' && el.textContent?.trim() === 'Table'`);
  await page.keyboard.press('Enter');
  await new Promise((r) => setTimeout(r, 300));
  const added = await status();
  step('Tab + Enter adds a table without the mouse', toNew > 0 && toTable > 0 && /Table added at position/.test(added), `${toNew}+${toTable} tabs: ${added}`);

  const toText = await tabTo(`(el) => el.tagName === 'BUTTON' && el.textContent?.trim() === 'Text box'`);
  await page.keyboard.press('Enter');
  await new Promise((r) => setTimeout(r, 300));
  const focusedComp = await page.evaluate(() => document.activeElement?.id ?? '');
  step('a new component takes the focus', focusedComp.startsWith('comp-'), focusedComp);
  await page.keyboard.down('Alt');
  await page.keyboard.press('ArrowUp');
  await page.keyboard.up('Alt');
  await new Promise((r) => setTimeout(r, 200));
  const moved = await status();
  step('Alt+Up reorders the body (keyboard alternative to drag)', toText > 0 && /moved to position 1/.test(moved), moved);
  await page.keyboard.down('Control');
  await page.keyboard.press('KeyZ');
  await page.keyboard.up('Control');
  await new Promise((r) => setTimeout(r, 200));
  step('Ctrl+Z undoes the move', (await status()) === 'Undone.', await status());

  const toHeader = await tabTo(`(el) => el.textContent?.trim() === 'Text in page header'`);
  await page.keyboard.press('Enter');
  await new Promise((r) => setTimeout(r, 300));
  const before = await page.evaluate(() => document.activeElement?.getAttribute('style') ?? '');
  for (let i = 0; i < 3; i++) await page.keyboard.press('ArrowRight');
  await page.keyboard.down('Shift');
  await page.keyboard.press('ArrowDown');
  await page.keyboard.up('Shift');
  await new Promise((r) => setTimeout(r, 200));
  const after = await page.evaluate(() => document.activeElement?.getAttribute('style') ?? '');
  step('arrow keys move a header item (keyboard alternative to free-form drag)', toHeader > 0 && before !== after, await status());
  await page.keyboard.press('Delete');
  await new Promise((r) => setTimeout(r, 200));
  step('Delete removes the selected item', /removed/.test(await status()), await status());
  await shot('g6-designer-after-keyboard.png');

  const live = await page.$eval('p.status', (el) => `${el.getAttribute('role')}/${el.getAttribute('aria-live')}`);
  step('changes are announced in a polite live region', live === 'status/polite', live);
  await axeScan('designer-after-keyboard');
} catch (error) {
  step('walkthrough completed', false, String(error));
  await shot('g6-walkthrough-error.png');
} finally {
  writeFileSync(join(evidence, 'g2-g6-walkthrough.json'), JSON.stringify(result, null, 1));
  await browser.close();
}

const failed = result.steps.filter((s) => !s.pass);
console.log(`\n${result.steps.length} checks, ${failed.length} failed`);
process.exitCode = failed.length ? 1 : 0;
