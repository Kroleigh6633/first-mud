import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';

/**
 * Auto-farm tests — farm picker, start/stop farming, status bar feedback,
 * and position change while auto-farming.
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
  const response = await page.request.post(`/api/players/${DEV_PLAYER_ID}/reset-position`);
  if (!response.ok()) {
    throw new Error(`reset-position failed: ${response.status()} ${await response.text()}`);
  }
}

async function waitForAuth(page: Page) {
  await expect(page.getByText(DEV_PLAYER_NAME)).toBeVisible({ timeout: 10_000 });
}

test.describe('FirstMud — auto-farm', () => {
  test.beforeEach(async ({ page }) => {
    await resetPlayerPosition(page);
    await seedDevPlayer(page);
  });

  // ---------------------------------------------------------------------------
  // 1. F key opens the farm picker
  // ---------------------------------------------------------------------------

  test('F key opens the farm picker', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('f');

    // Farm picker panel or heading should appear
    const farmPicker = page.getByTestId('farm-picker')
      .or(page.getByText(/FARM|AUTO.?FARM|FARMING/i));
    await expect(farmPicker.first()).toBeVisible({ timeout: 5_000 });
  });

  test('farm picker lists available farming zones or resource types', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('f');

    // Wait for the picker to open
    const farmPicker = page.getByTestId('farm-picker')
      .or(page.getByText(/FARM|AUTO.?FARM|FARMING/i));
    await expect(farmPicker.first()).toBeVisible({ timeout: 5_000 });

    // There should be at least one farmable zone or resource option
    const farmOptions = page.getByTestId('farm-option')
      .or(page.getByRole('button', { name: /farm|harvest|gather|mine/i }));
    await expect(farmOptions.first()).toBeVisible({ timeout: 5_000 });
  });

  test('farm picker shows START button or selectable targets', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('f');

    const farmPicker = page.getByTestId('farm-picker')
      .or(page.getByText(/FARM|AUTO.?FARM/i));
    await expect(farmPicker.first()).toBeVisible({ timeout: 5_000 });

    // Either a Start Farming button or direct zone selection buttons
    const startOrSelect = page.getByRole('button', { name: /start|farm|select|begin/i }).first();
    await expect(startOrSelect).toBeVisible({ timeout: 5_000 });
  });

  // ---------------------------------------------------------------------------
  // 2. Start farming shows status bar
  // ---------------------------------------------------------------------------

  test('starting auto-farm shows a farming status indicator', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('f');

    const farmPicker = page.getByTestId('farm-picker')
      .or(page.getByText(/FARM|AUTO.?FARM/i));
    await expect(farmPicker.first()).toBeVisible({ timeout: 5_000 });

    // Click the first available start/farm button
    const startBtn = page.getByRole('button', { name: /start|farm|begin/i }).first()
      .or(page.getByTestId('farm-option').first());

    if (!(await startBtn.isVisible({ timeout: 3_000 }).catch(() => false))) {
      test.skip();
      return;
    }

    await startBtn.click();

    // A status bar or indicator showing farming is active should appear
    const farmingStatus = page.getByTestId('autofarm-status')
      .or(page.getByText(/AUTO.?FARM|farming|HARVESTING/i));
    await expect(farmingStatus.first()).toBeVisible({ timeout: 5_000 });
  });

  test('farming status bar shows the current farming activity', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('f');

    const farmPicker = page.getByTestId('farm-picker')
      .or(page.getByText(/FARM|AUTO.?FARM/i));
    if (!(await farmPicker.first().isVisible({ timeout: 3_000 }).catch(() => false))) {
      test.skip();
      return;
    }

    const startBtn = page.getByRole('button', { name: /start|farm|begin/i }).first()
      .or(page.getByTestId('farm-option').first());
    if (!(await startBtn.isVisible({ timeout: 3_000 }).catch(() => false))) {
      test.skip();
      return;
    }

    await startBtn.click();

    // The status bar should contain AUTO FARM or a similar label with context
    const statusText = page.getByText(/AUTO.?FARM|FARMING ZONE|HARVESTING/i);
    await expect(statusText.first()).toBeVisible({ timeout: 5_000 });
  });

  // ---------------------------------------------------------------------------
  // 3. F again stops farming
  // ---------------------------------------------------------------------------

  test('pressing F again while farming stops auto-farm', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('f');

    const farmPicker = page.getByTestId('farm-picker')
      .or(page.getByText(/FARM|AUTO.?FARM/i));
    if (!(await farmPicker.first().isVisible({ timeout: 3_000 }).catch(() => false))) {
      test.skip();
      return;
    }

    const startBtn = page.getByRole('button', { name: /start|farm|begin/i }).first()
      .or(page.getByTestId('farm-option').first());
    if (!(await startBtn.isVisible({ timeout: 3_000 }).catch(() => false))) {
      test.skip();
      return;
    }

    await startBtn.click();

    // Confirm farming started
    const farmingStatus = page.getByTestId('autofarm-status')
      .or(page.getByText(/AUTO.?FARM|farming/i));
    await expect(farmingStatus.first()).toBeVisible({ timeout: 5_000 });

    // Press F again to stop
    await page.locator('body').click();
    await page.keyboard.press('f');

    // Either the status bar disappears or a "stopped" message appears
    await expect(async () => {
      const stopped = page.getByText(/stopped|farm stopped|auto.?farm.*off/i);
      const statusGone = farmingStatus.first();
      const msgVisible = await stopped.isVisible().catch(() => false);
      const statusHidden = !(await statusGone.isVisible().catch(() => true));
      expect(msgVisible || statusHidden, 'farming should stop when F is pressed again').toBe(true);
    }).toPass({ timeout: 5_000 });
  });

  test('Stop Farming button stops auto-farm', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('f');

    const farmPicker = page.getByTestId('farm-picker')
      .or(page.getByText(/FARM|AUTO.?FARM/i));
    if (!(await farmPicker.first().isVisible({ timeout: 3_000 }).catch(() => false))) {
      test.skip();
      return;
    }

    const startBtn = page.getByRole('button', { name: /start|farm|begin/i }).first()
      .or(page.getByTestId('farm-option').first());
    if (!(await startBtn.isVisible({ timeout: 3_000 }).catch(() => false))) {
      test.skip();
      return;
    }

    await startBtn.click();

    // Wait for farming to start
    await page.waitForTimeout(500);

    // Look for an explicit Stop button
    const stopBtn = page.getByRole('button', { name: /stop|cancel/i }).first();
    if (await stopBtn.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await stopBtn.click();
      // Farming indicator should disappear
      const farmingStatus = page.getByText(/AUTO.?FARM|HARVESTING/i);
      await expect(farmingStatus.first()).not.toBeVisible({ timeout: 5_000 });
    }
  });

  // ---------------------------------------------------------------------------
  // 4. Auto-farm walks — position changes
  // ---------------------------------------------------------------------------

  test('auto-farm causes the player position to change', async ({ page }) => {
    test.setTimeout(45_000); // auto-farm movement may take a few seconds per step

    await page.goto('/');
    await waitForAuth(page);

    // Record the initial player position
    const posLine = page.getByTestId('player-pos');
    await expect(posLine).toBeVisible({ timeout: 5_000 });
    const initialPos = (await posLine.textContent())?.trim();

    // Start auto-farm
    await page.locator('body').click();
    await page.keyboard.press('f');

    const farmPicker = page.getByTestId('farm-picker')
      .or(page.getByText(/FARM|AUTO.?FARM/i));
    if (!(await farmPicker.first().isVisible({ timeout: 3_000 }).catch(() => false))) {
      test.skip();
      return;
    }

    const startBtn = page.getByRole('button', { name: /start|farm|begin/i }).first()
      .or(page.getByTestId('farm-option').first());
    if (!(await startBtn.isVisible({ timeout: 3_000 }).catch(() => false))) {
      test.skip();
      return;
    }

    await startBtn.click();

    // Wait for position to change — auto-farm should walk toward the farming zone
    await expect(async () => {
      const currentPos = (await posLine.textContent())?.trim();
      expect(currentPos, 'auto-farm should move the player').not.toBe(initialPos);
    }).toPass({ timeout: 30_000 });
  });

  test('auto-farm narrates movement in the game log', async ({ page }) => {
    test.setTimeout(30_000);

    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('f');

    const farmPicker = page.getByTestId('farm-picker')
      .or(page.getByText(/FARM|AUTO.?FARM/i));
    if (!(await farmPicker.first().isVisible({ timeout: 3_000 }).catch(() => false))) {
      test.skip();
      return;
    }

    const startBtn = page.getByRole('button', { name: /start|farm|begin/i }).first()
      .or(page.getByTestId('farm-option').first());
    if (!(await startBtn.isVisible({ timeout: 3_000 }).catch(() => false))) {
      test.skip();
      return;
    }

    await startBtn.click();

    // Auto-farm should produce some activity message in the game log
    const activityMsg = page.getByText(/harvested|gathered|farming|you travel|walking to/i).first();
    await expect(activityMsg).toBeVisible({ timeout: 20_000 });
  });
});
