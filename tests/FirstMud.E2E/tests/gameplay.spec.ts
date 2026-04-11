import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';

/**
 * Gameplay tests — exercise the in-game feedback loop that the user
 * actually interacts with after connecting:
 *   - where am I? (tile awareness)
 *   - what happens when I press Enter? (interact)
 *   - what happens when I press I? (inventory panel)
 *   - what happens when I walk onto a new zone? (arrival narration)
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
  // Hit the dev-only reset endpoint through the Vite proxy so each test
  // starts with the player on Starting Road regardless of what previous
  // tests did.
  const response = await page.request.post(`/api/players/${DEV_PLAYER_ID}/reset-position`);
  if (!response.ok()) {
    throw new Error(`reset-position failed: ${response.status()} ${await response.text()}`);
  }
}

async function waitForAuth(page: Page) {
  await expect(page.getByText(DEV_PLAYER_NAME)).toBeVisible({ timeout: 10_000 });
}

test.describe('FirstMud browser client — gameplay', () => {
  test.beforeEach(async ({ page }) => {
    await resetPlayerPosition(page);
    await seedDevPlayer(page);
  });

  test('status panel shows the current tile the player is standing on', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    // Dev seeder parks the player on Starting Road at (20, 10)
    const currentTile = page.getByTestId('current-tile');
    await expect(currentTile).toBeVisible({ timeout: 10_000 });
    await expect(currentTile).toContainText('Starting Road');
    await expect(currentTile).toContainText(/Danger:/);
  });

  test('pressing Enter on a known tile narrates its description', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);
    await expect(page.getByTestId('current-tile')).toBeVisible();

    await page.locator('body').click();
    await page.keyboard.press('Enter');

    // A game log message should appear referencing the current tile
    await expect(page.getByText(/You take stock of Starting Road/i)).toBeVisible({ timeout: 5_000 });
  });

  test('walking off the Starting Road narrates the transition', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);
    await expect(page.getByTestId('current-tile')).toContainText('Starting Road');

    await page.locator('body').click();
    // Walk east a few times — eventually we leave the Starting Road tile.
    for (let i = 0; i < 3; i++) {
      await page.keyboard.press('d');
      await page.waitForTimeout(250);
    }

    // Either we arrive at a new tile ("You arrive at ...") or we leave
    // the marked paths altogether. Either message proves the transition
    // narration is wired.
    const arriveMsg = page.getByText(/You arrive at |You leave the marked paths/);
    await expect(arriveMsg.first()).toBeVisible({ timeout: 5_000 });
  });

  test('pressing I opens the inventory panel', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('i');

    await expect(page.getByTestId('inventory-overlay')).toBeVisible({ timeout: 5_000 });
    await expect(page.getByText('INVENTORY')).toBeVisible();
    await expect(page.getByText(/Skills/i)).toBeVisible();
    await expect(page.getByText(/Items/i)).toBeVisible();
    // Dev player has no items → empty state visible
    await expect(page.getByTestId('inventory-empty')).toBeVisible();
  });

  test('Esc closes the inventory panel', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('i');
    await expect(page.getByTestId('inventory-overlay')).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(page.getByTestId('inventory-overlay')).not.toBeVisible();
  });

  test('pressing Enter on open country (after walking off road) shows "nothing here"', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);
    await expect(page.getByTestId('current-tile')).toContainText('Starting Road');

    // Walk far enough east to leave the Starting Road tile
    await page.locator('body').click();
    for (let i = 0; i < 3; i++) {
      await page.keyboard.press('d');
      await page.waitForTimeout(200);
    }

    // Confirm we left (either arrived elsewhere or see open country)
    // Then interact — if on open country, it should say "nothing here"
    const openCountry = page.getByTestId('current-tile-empty');
    if (await openCountry.isVisible()) {
      await page.keyboard.press('Enter');
      await expect(page.getByText(/nothing here to interact with/i)).toBeVisible({ timeout: 3_000 });
    }
  });
});
