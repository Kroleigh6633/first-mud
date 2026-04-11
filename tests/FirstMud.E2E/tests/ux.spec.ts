import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';

/**
 * UX / keybind / interaction tests for the FirstMud browser client.
 *
 * Sibling to smoke.spec.ts — these tests exercise the keyboard-driven UX
 * against a fully running docker stack. They reuse the seeded dev player
 * "Kira Ashwood" by priming localStorage.
 */

const DEV_PLAYER_ID = 'aeb2efff-2fd1-4f1d-bcf1-203a82443141';
const DEV_PLAYER_NAME = 'Kira Ashwood';

async function seedDevPlayer(page: Page) {
  await page.addInitScript(
    ([id, name]) => {
      localStorage.setItem('firstmud_player', JSON.stringify({ id, name }));
    },
    [DEV_PLAYER_ID, DEV_PLAYER_NAME],
  );
}

async function waitForAuth(page: Page) {
  await expect(page.getByText(DEV_PLAYER_NAME)).toBeVisible({ timeout: 10_000 });
}

test.describe('FirstMud browser client — UX', () => {
  test.beforeEach(async ({ page }) => {
    await seedDevPlayer(page);
  });

  test('"press [?] for help" hint is visible in the bottom-right', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByText('press [?] for help')).toBeVisible();
  });

  test('pressing ? opens the help overlay and Esc closes it', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('?');

    await expect(page.getByTestId('help-overlay')).toBeVisible();
    await expect(page.getByText('HELP — KEY BINDINGS')).toBeVisible();
    await expect(page.getByText('move north')).toBeVisible();
    await expect(page.getByText('open / close quest log')).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(page.getByTestId('help-overlay')).not.toBeVisible();
  });

  test('pressing h also opens the help overlay', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('h');
    await expect(page.getByTestId('help-overlay')).toBeVisible();
  });

  test('close [x] button dismisses the help overlay', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('?');
    await expect(page.getByTestId('help-overlay')).toBeVisible();

    await page.getByRole('button', { name: /close help/i }).click();
    await expect(page.getByTestId('help-overlay')).not.toBeVisible();
  });

  test('pressing Q twice toggles the quest log open and closed', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('q');
    await expect(page.getByText('QUEST LOG')).toBeVisible();

    await page.keyboard.press('q');
    await expect(page.getByText('QUEST LOG')).not.toBeVisible();
  });

  test('Esc closes the quest log', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('q');
    await expect(page.getByText('QUEST LOG')).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(page.getByText('QUEST LOG')).not.toBeVisible();
  });

  test('movement keys change the player position displayed in the status panel', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    const posLine = page.getByTestId('player-pos');
    await expect(posLine).toBeVisible();
    const before = (await posLine.textContent())?.trim();
    expect(before, 'initial Pos line should include numeric coords').toMatch(/Pos:\s*-?\d+,-?\d+/);

    await page.locator('body').click();
    await page.keyboard.press('d'); // move east
    // Wait for PlayerMoved broadcast round-trip
    await expect(async () => {
      const current = (await posLine.textContent())?.trim();
      expect(current).not.toBe(before);
    }).toPass({ timeout: 5_000 });
  });

  test('pressing I (inventory) does NOT produce an Unknown command error', async ({ page }) => {
    // Regression guard: client was sending "inventory" but the server hub
    // parses "openinventory", so pressing I spammed "Unknown command" into
    // the message log. This test asserts no such error surfaces.
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('i');
    // Give the command time to round-trip
    await page.waitForTimeout(800);

    const body = await page.textContent('body');
    expect(body ?? '').not.toContain('Unknown command: inventory');
    expect(body ?? '').not.toContain('Unknown command: openinventory');
  });

  test('pressing C (character) silently no-ops — no Unknown command error', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('c');
    await page.waitForTimeout(500);

    const body = await page.textContent('body');
    expect(body ?? '').not.toContain('Unknown command: character');
  });

  test('console has zero red errors on full page load (no filter)', async ({ page }) => {
    // Regression guard: SignalR was writing "The connection was stopped
    // during negotiation" to console.error directly. We now install a
    // custom quietSignalRLogger that drops those. This test asserts the
    // console is clean WITHOUT our Playwright-side filter — proving the
    // fix is at the source.
    const errors: string[] = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error') errors.push(msg.text());
    });

    await page.goto('/');
    await waitForAuth(page);
    await page.waitForTimeout(3000);

    expect(errors, `unexpected console errors:\n${errors.join('\n')}`).toEqual([]);
  });
});
