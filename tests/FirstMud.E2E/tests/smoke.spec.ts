import { test, expect } from '@playwright/test';

/**
 * Smoke tests for the FirstMud browser client.
 *
 * These hit a fully running docker stack (client + gameserver + sql + neo4j)
 * and verify the core playable loop without mocking anything.
 *
 * The dev player "Kira Ashwood" is seeded on server boot, so we inject its
 * id into localStorage to bypass the player-creation modal.
 */

const DEV_PLAYER_ID = 'aeb2efff-2fd1-4f1d-bcf1-203a82443141';
const DEV_PLAYER_NAME = 'Kira Ashwood';

async function seedDevPlayer(page: import('@playwright/test').Page) {
  // Must set localStorage before the React app runs, so navigate first with a
  // blank page, then set storage, then navigate to the actual app.
  await page.addInitScript(
    ([id, name]) => {
      localStorage.setItem(
        'firstmud_player',
        JSON.stringify({ id, name }),
      );
    },
    [DEV_PLAYER_ID, DEV_PLAYER_NAME],
  );
}

test.describe('FirstMud browser client — smoke', () => {
  test.beforeEach(async ({ page }) => {
    await seedDevPlayer(page);
  });

  test('loads app shell and connects to server', async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('console', msg => {
      if (msg.type() === 'error') consoleErrors.push(msg.text());
    });

    await page.goto('/');
    // Status panel label is always rendered, even before world state loads
    await expect(page.getByText('STATUS')).toBeVisible();

    // Give SignalR time to complete negotiation + auth + first WorldState push
    await page.waitForTimeout(4000);

    // The "Failed to connect to game server" toast must NOT appear
    const body = await page.textContent('body');
    expect(body ?? '').not.toContain('Failed to connect to game server');

    // Only Vite HMR banners are allowed; everything else must be gone.
    const meaningfulErrors = consoleErrors.filter(e => !/\[vite\]/i.test(e));
    expect(meaningfulErrors, `unexpected console errors:\n${meaningfulErrors.join('\n')}`).toEqual([]);
  });

  test('status panel shows the dev player after auth', async ({ page }) => {
    await page.goto('/');
    // The server pushes a WorldState snapshot on Authenticate; the status
    // panel should render the player name within a few seconds.
    await expect(page.getByText(DEV_PLAYER_NAME)).toBeVisible({ timeout: 10_000 });
  });

  test('pressing Q opens quest log with real seeded quests', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByText(DEV_PLAYER_NAME)).toBeVisible({ timeout: 10_000 });

    // Focus the body and press Q
    await page.locator('body').click();
    await page.keyboard.press('q');

    // Quest log heading appears
    await expect(page.getByText('QUEST LOG')).toBeVisible({ timeout: 5_000 });

    // At least one seeded quest from the lore seeder
    await expect(page.getByText('A Delivery Gone Wrong')).toBeVisible();
    // Accept button should be present on at least one quest card
    const acceptButtons = page.getByRole('button', { name: /accept/i });
    await expect(acceptButtons.first()).toBeVisible();
  });

  test('quest log shows exactly one @ glyph representing the player', async ({ page }) => {
    // This test pins a regression fix: React StrictMode was double-mounting
    // the WorldMap rot.js canvas and painting TWO @ glyphs. We now clear the
    // container before appendChild so only one canvas exists at a time.
    await page.goto('/');
    await expect(page.getByText(DEV_PLAYER_NAME)).toBeVisible({ timeout: 10_000 });
    // Let the blink animation + initial zone view settle
    await page.waitForTimeout(1500);

    // There should be exactly one <canvas> in the world-map panel
    const canvases = await page.locator('canvas').count();
    expect(canvases).toBe(1);
  });

  test('accepting a quest adds a message to the log (no [object Object])', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByText(DEV_PLAYER_NAME)).toBeVisible({ timeout: 10_000 });

    await page.locator('body').click();
    await page.keyboard.press('q');
    await expect(page.getByText('A Delivery Gone Wrong')).toBeVisible();

    // Click the first Accept button
    await page.getByRole('button', { name: /accept/i }).first().click();

    // A message with the quest id (string, not "[object Object]") should
    // appear in the message log.
    await expect(page.getByText(/Quest accepted:/i)).toBeVisible({ timeout: 5_000 });
    const log = await page.textContent('body');
    expect(log ?? '').not.toContain('[object Object]');
  });
});
