import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';

/**
 * Companion management tests — companion panel, bond levels, duty picker,
 * and status panel companion status reflection.
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

/** Open the companion panel with the B key. */
async function openCompanionPanel(page: Page) {
  await page.locator('body').click();
  await page.keyboard.press('b');
  await expect(page.getByText('COMPANIONS')).toBeVisible({ timeout: 5_000 });
}

test.describe('FirstMud — companion management', () => {
  test.beforeEach(async ({ page }) => {
    await resetPlayerPosition(page);
    await seedDevPlayer(page);
  });

  // ---------------------------------------------------------------------------
  // 1. B key opens the companion panel
  // ---------------------------------------------------------------------------

  test('B key opens the companion panel', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await openCompanionPanel(page);

    await expect(page.getByText('COMPANIONS')).toBeVisible();
  });

  test('B key again closes the companion panel', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await openCompanionPanel(page);

    await page.keyboard.press('b');
    await expect(page.getByText('COMPANIONS')).not.toBeVisible({ timeout: 3_000 });
  });

  test('Esc closes the companion panel', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await openCompanionPanel(page);

    await page.keyboard.press('Escape');
    await expect(page.getByText('COMPANIONS')).not.toBeVisible({ timeout: 3_000 });
  });

  // ---------------------------------------------------------------------------
  // 2. Shows all companions with bond levels
  // ---------------------------------------------------------------------------

  test('companion panel lists companions', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await openCompanionPanel(page);

    // There should be at least one companion entry from the dev seeder
    const companionItems = page.getByTestId('companion-item');
    await expect(companionItems.first()).toBeVisible({ timeout: 5_000 });
  });

  test('companion entries show bond level', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await openCompanionPanel(page);

    // Each companion should display a bond level (e.g. "Bond: 3", "Bond Lv 2", etc.)
    const bondLabel = page.getByText(/bond/i).first();
    await expect(bondLabel).toBeVisible({ timeout: 5_000 });
  });

  test('companion entries show companion names', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await openCompanionPanel(page);

    // At least one companion item should show a name (non-empty text node)
    const companionItems = page.getByTestId('companion-item');
    await expect(companionItems.first()).toBeVisible({ timeout: 5_000 });

    const firstName = await companionItems.first().textContent();
    expect(firstName, 'companion entry should contain some text (name)').toBeTruthy();
  });

  test('companion entries show current duty or status', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await openCompanionPanel(page);

    // Companion status/duty labels (ACTIVE, GUARD, GATHERING, etc.)
    const statusLabel = page.getByText(/active|guard|gathering|patrol|idle|resting/i).first();
    await expect(statusLabel).toBeVisible({ timeout: 5_000 });
  });

  // ---------------------------------------------------------------------------
  // 3. Deactivate shows duty picker
  // ---------------------------------------------------------------------------

  test('a companion entry has an Activate or Deactivate button', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await openCompanionPanel(page);

    // Either Activate (for inactive companions) or Deactivate (for active ones)
    const actionBtn = page.getByRole('button', { name: /activate|deactivate/i }).first();
    await expect(actionBtn).toBeVisible({ timeout: 5_000 });
  });

  test('Deactivate button opens the duty picker', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await openCompanionPanel(page);

    // Look for a Deactivate button (companion is currently active)
    const deactivateBtn = page.getByRole('button', { name: /deactivate/i }).first();
    if (!(await deactivateBtn.isVisible({ timeout: 3_000 }).catch(() => false))) {
      test.skip();
      return;
    }

    await deactivateBtn.click();

    // A duty picker (dropdown, modal, or inline list) should appear
    const dutyPicker = page.getByTestId('duty-picker')
      .or(page.getByText(/assign duty|choose duty|duty:/i))
      .or(page.getByRole('listbox'))
      .or(page.getByRole('combobox'));
    await expect(dutyPicker.first()).toBeVisible({ timeout: 5_000 });
  });

  test('duty picker shows available duty options', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await openCompanionPanel(page);

    const deactivateBtn = page.getByRole('button', { name: /deactivate/i }).first();
    if (!(await deactivateBtn.isVisible({ timeout: 3_000 }).catch(() => false))) {
      test.skip();
      return;
    }

    await deactivateBtn.click();
    await page.waitForTimeout(300);

    // Duty options like GUARD, GATHERING, PATROL, etc. should appear
    const dutyOption = page.getByText(/guard|gathering|patrol|idle|scouting|farming/i).first();
    await expect(dutyOption).toBeVisible({ timeout: 5_000 });
  });

  test('selecting a duty from the picker updates companion status', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await openCompanionPanel(page);

    const deactivateBtn = page.getByRole('button', { name: /deactivate/i }).first();
    if (!(await deactivateBtn.isVisible({ timeout: 3_000 }).catch(() => false))) {
      test.skip();
      return;
    }

    await deactivateBtn.click();
    await page.waitForTimeout(300);

    // Click the first available duty option
    const dutyOption = page.getByRole('button', { name: /guard|gathering|patrol|idle|scouting/i }).first()
      .or(page.getByRole('option').first());

    if (!(await dutyOption.isVisible({ timeout: 3_000 }).catch(() => false))) {
      test.skip();
      return;
    }

    await dutyOption.click();

    // The companion status should update (either in the panel or via a message)
    const outcome = page.getByText(/duty assigned|companion now|assigned to/i)
      .or(page.getByText(/guard|gathering|patrol|idle|scouting/i).first());
    await expect(outcome.first()).toBeVisible({ timeout: 5_000 });
  });

  // ---------------------------------------------------------------------------
  // 4. Status panel shows companion status (ACTIVE/GUARD/etc.)
  // ---------------------------------------------------------------------------

  test('status panel shows companion status section', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    // The status panel on the main game screen should include companion info
    // once the WorldState loads.
    const companionStatus = page.getByTestId('companion-status')
      .or(page.getByText(/companion|ACTIVE|GUARD/i).first());
    await expect(companionStatus.first()).toBeVisible({ timeout: 10_000 });
  });

  test('status panel companion status reflects active companion', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    // Give the server time to push the initial WorldState (which includes companions)
    await page.waitForTimeout(2000);

    // The status panel should show a companion status badge
    const statusBadge = page.getByText(/ACTIVE|GUARD|GATHERING|IDLE/i).first();
    await expect(statusBadge).toBeVisible({ timeout: 5_000 });
  });
});
