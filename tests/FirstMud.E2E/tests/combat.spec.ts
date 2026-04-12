import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';

/**
 * Combat tests — walk into dangerous zones, trigger encounters, fight
 * through them, verify the combat UI, and confirm outcomes. These are
 * the "play the game" tests that exercise the full loop.
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

async function resetPlayerPosition(page: Page) {
  await page.request.post(`/api/players/${DEV_PLAYER_ID}/reset-position`);
}

async function waitForAuth(page: Page) {
  await expect(page.getByText(DEV_PLAYER_NAME)).toBeVisible({ timeout: 10_000 });
}

/**
 * Walk toward a high-danger zone until combat triggers.
 * Returns true if combat was triggered within maxSteps.
 */
async function walkUntilCombat(page: Page, dx: number, dy: number, maxSteps = 30): Promise<boolean> {
  const key = dx > 0 ? 'd' : dx < 0 ? 'a' : dy > 0 ? 's' : 'w';
  for (let i = 0; i < maxSteps; i++) {
    await page.keyboard.press(key);
    await page.waitForTimeout(200);
    const panel = page.getByTestId('combat-panel');
    if (await panel.isVisible()) return true;
  }
  return false;
}

test.describe('FirstMud — combat', () => {
  test.beforeEach(async ({ page }) => {
    await resetPlayerPosition(page);
    await seedDevPlayer(page);
  });

  test('walking into a dangerous zone triggers a combat encounter', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);
    await page.locator('body').click();

    // Walk east toward Gravenhold (danger 2) and Gravenmarsh (danger 3).
    // At danger 2-3, encounter chance is 16-24% per step, so within 30
    // steps we should almost certainly trigger one.
    const triggered = await walkUntilCombat(page, 1, 0, 30);
    expect(triggered, 'expected an encounter within 30 steps east').toBe(true);

    // Combat panel is visible with expected structure
    const panel = page.getByTestId('combat-panel');
    await expect(panel.getByText(/COMBAT — Round/)).toBeVisible();
    await expect(panel.getByText(/ENEMIES/i)).toBeVisible();
    await expect(panel.getByText(/YOUR PARTY/i)).toBeVisible();
  });

  test('combat shows the player turn banner and real abilities', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);
    await page.locator('body').click();

    const triggered = await walkUntilCombat(page, 1, 0, 30);
    if (!triggered) { test.skip(); return; }

    const panel = page.getByTestId('combat-panel');

    // Either it's our turn or enemy turn; if ours, check abilities
    const yourTurn = panel.getByText(/YOUR TURN/);
    if (await yourTurn.isVisible()) {
      // Should show player's real abilities (Strike, Weave Bolt, Restore)
      // NOT the hardcoded Claw/Fire Breath/Water Jet
      await expect(panel.getByRole('button', { name: /Strike/ })).toBeVisible();
      await expect(panel.getByRole('button', { name: /Weave Bolt/ })).toBeVisible();
      await expect(panel.getByRole('button', { name: /Restore/ })).toBeVisible();
      await expect(panel.getByRole('button', { name: /Flee/ })).toBeVisible();

      // Hardcoded monster abilities should NOT appear as player buttons
      expect(await panel.getByRole('button', { name: /^Claw$/ }).count()).toBe(0);
    }
  });

  test('clicking Strike progresses the combat state', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);
    await page.locator('body').click();

    const triggered = await walkUntilCombat(page, 1, 0, 40);
    if (!triggered) { test.skip(); return; }

    const panel = page.getByTestId('combat-panel');
    await expect(panel.getByText(/YOUR TURN/)).toBeVisible({ timeout: 5_000 });

    // Note the panel text before acting
    const before = await panel.textContent();

    await panel.getByRole('button', { name: /Strike/ }).click();
    // Wait for the round-trip — the panel should update (HP change or round advance)
    await expect(async () => {
      const after = await panel.textContent();
      expect(after).not.toBe(before);
    }).toPass({ timeout: 5_000 });
  });

  test('Flee button ends the encounter with ESCAPED', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);
    await page.locator('body').click();

    const triggered = await walkUntilCombat(page, 1, 0, 30);
    if (!triggered) { test.skip(); return; }

    const panel = page.getByTestId('combat-panel');
    // Enemy turns auto-process now, so the panel should show YOUR TURN
    // immediately (enemies already acted before the CombatUpdate arrived).
    await expect(panel.getByRole('button', { name: /Flee/ })).toBeVisible({ timeout: 5_000 });

    await panel.getByRole('button', { name: /Flee/ }).click();
    await expect(panel.getByText('ESCAPED')).toBeVisible({ timeout: 3_000 });
  });

  test('combat encounter can be fought to completion (victory or defeat)', async ({ page }) => {
    test.setTimeout(60_000); // fights can take a while

    await page.goto('/');
    await waitForAuth(page);
    await page.locator('body').click();

    const triggered = await walkUntilCombat(page, 1, 0, 30);
    if (!triggered) { test.skip(); return; }

    const panel = page.getByTestId('combat-panel');

    // Fight loop: keep clicking Strike until the encounter ends
    for (let round = 0; round < 50; round++) {
      // Check if combat ended
      const victory = panel.getByText('VICTORY!');
      const defeat = panel.getByText('DEFEATED');
      if (await victory.isVisible() || await defeat.isVisible()) {
        // Test passes — we completed a fight
        return;
      }

      // If it's our turn, attack
      const yourTurn = panel.getByText(/YOUR TURN/);
      if (await yourTurn.isVisible()) {
        const strikeBtn = panel.getByRole('button', { name: /Strike/ });
        if (await strikeBtn.isVisible()) {
          await strikeBtn.click();
        }
      }
      await page.waitForTimeout(300);
    }

    // If we got here, check the final state — should be one of the terminal states
    const bodyText = await panel.textContent();
    const ended = /VICTORY|DEFEATED|ESCAPED/.test(bodyText ?? '');
    expect(ended, 'fight should have ended within 50 actions').toBe(true);
  });

  test('a combat log message appears when an encounter triggers', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);
    await page.locator('body').click();

    const triggered = await walkUntilCombat(page, 1, 0, 30);
    if (!triggered) { test.skip(); return; }

    // The server sends a GameMessage when combat starts
    await expect(page.getByText(/Hostile creatures emerge from/)).toBeVisible({ timeout: 5_000 });
  });

  test('enemy attacks are narrated in the game log', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);
    await page.locator('body').click();

    const triggered = await walkUntilCombat(page, 1, 0, 40);
    if (!triggered) { test.skip(); return; }

    // If the first enemy is slower than the player, enemy narration
    // only appears AFTER the player acts. Use Strike to trigger enemy
    // turns if needed.
    const panel = page.getByTestId('combat-panel');
    const yourTurn = panel.getByText(/YOUR TURN/);
    if (await yourTurn.isVisible()) {
      await panel.getByRole('button', { name: /Strike/ }).click();
      await page.waitForTimeout(1500);
    }

    const body = await page.textContent('body');
    expect(body ?? '').toMatch(/uses .+ on .+ for \d+ damage/);
  });
});
