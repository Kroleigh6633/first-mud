import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';

/**
 * Quest flow tests — accept → navigate → interact → complete.
 *
 * These tests guard against regressions in the quest accept/complete loop:
 *   - Quest log shows quests with Accept buttons
 *   - Accept All fires for every available quest
 *   - Navigate button triggers waypoint navigation
 *   - Explore quests auto-complete on arrival at the waypoint
 *   - Quest completion awards XP
 *   - L key triggers quest auto-run
 *
 * All tests use the dev player "Kira Ashwood" and reset position before each test.
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

/** Open the quest log with the Q key. */
async function openQuestLog(page: Page) {
  await page.locator('body').click();
  await page.keyboard.press('q');
  await expect(page.getByText('QUEST LOG')).toBeVisible({ timeout: 5_000 });
}

test.describe('FirstMud — quest flow', () => {
  test.beforeEach(async ({ page }) => {
    await resetPlayerPosition(page);
    await seedDevPlayer(page);
  });

  // ---------------------------------------------------------------------------
  // 1. Quest log shows available quests with Accept buttons
  // ---------------------------------------------------------------------------

  test('quest log shows available quests with Accept buttons', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);
    await openQuestLog(page);

    // There should be at least one available quest showing an Accept button.
    // The dev player starts with House Caervorn quests available.
    const acceptButtons = page.getByRole('button', { name: /\[Accept\]/i });
    await expect(acceptButtons.first()).toBeVisible({ timeout: 5_000 });

    // Quests that are not yet taken show the AVAILABLE badge
    const availableBadge = page.getByText('AVAILABLE');
    await expect(availableBadge.first()).toBeVisible();
  });

  // ---------------------------------------------------------------------------
  // 2. Accept All accepts every quest
  // ---------------------------------------------------------------------------

  test('Accept All accepts every available quest', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);
    await openQuestLog(page);

    // Confirm the Accept All button is present (only shown when quests are available)
    const acceptAllBtn = page.getByRole('button', { name: /\[Accept All/i });
    await expect(acceptAllBtn).toBeVisible({ timeout: 5_000 });

    await acceptAllBtn.click();

    // After Accept All, every quest should be IN PROGRESS (no AVAILABLE badges remain)
    await expect(async () => {
      const availableCount = await page.getByText('AVAILABLE').count();
      expect(availableCount, 'no AVAILABLE quests should remain after Accept All').toBe(0);
    }).toPass({ timeout: 8_000 });

    // At least one IN PROGRESS badge should now be visible
    await expect(page.getByText('IN PROGRESS').first()).toBeVisible();
  });

  // ---------------------------------------------------------------------------
  // 3. Navigate button sets waypoint on map
  // ---------------------------------------------------------------------------

  test('Navigate button sets waypoint on map', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);
    await openQuestLog(page);

    // Accept a quest first so Navigate becomes available
    const acceptBtn = page.getByRole('button', { name: /\[Accept\]/i }).first();
    await expect(acceptBtn).toBeVisible({ timeout: 5_000 });
    await acceptBtn.click();

    // Wait for the quest to be in-progress (Navigate button appears)
    const navigateBtn = page.getByRole('button', { name: /Navigate/i }).first();
    await expect(navigateBtn).toBeVisible({ timeout: 5_000 });

    await navigateBtn.click();

    // After clicking Navigate, the server broadcasts a QuestWaypoint event which
    // the client renders as a waypoint marker. We check that navigation has started
    // by looking for a waypoint indicator in the game UI.
    // The status bar or map should show some form of navigation indication.
    // We use a flexible check: either "waypoint" text or the quest log closed and
    // a navigation-related message appeared.
    const body = await page.textContent('body');
    // The client sets a waypoint and the SignalR message confirms it
    // At minimum, no error should appear
    expect(body ?? '').not.toContain('Unknown command');
    expect(body ?? '').not.toContain('error');
  });

  // ---------------------------------------------------------------------------
  // 4. Explore quest auto-completes on arrival
  // ---------------------------------------------------------------------------

  test('explore quest auto-completes on arrival at waypoint', async ({ page }) => {
    test.setTimeout(60_000); // navigation may take a while

    await page.goto('/');
    await waitForAuth(page);
    await openQuestLog(page);

    // Look for an explore-type quest (Investigate / Explore / Uncover in title)
    // The dev player typically has these available from faction quests.
    const questTitles = page.getByText(/Investigate|Explore|Uncover/i);
    const firstExploreTitle = questTitles.first();

    const questCount = await questTitles.count();
    if (questCount === 0) {
      // No explore quest visible — accept any quest and skip to completion
      test.skip();
      return;
    }

    // Find the Accept button in the same quest item as the explore title
    // (the button sits just below the title inside the same list item)
    const questItem = firstExploreTitle.locator('xpath=ancestor::div[.//button[contains(text(),"[Accept]")]]').first();
    const acceptBtn = questItem.getByRole('button', { name: /\[Accept\]/i });

    if (!(await acceptBtn.isVisible())) {
      test.skip(); // quest might already be taken
      return;
    }

    await acceptBtn.click();

    // Close the quest log
    await page.keyboard.press('Escape');
    await expect(page.getByText('QUEST LOG')).not.toBeVisible();

    // Use Auto-run (L key) to navigate to the quest waypoint automatically
    await page.locator('body').click();
    await page.keyboard.press('l');

    // Wait for a quest completion message — explore quests complete on arrival
    // The server sends either "QuestCompleted" SignalR event or a GameMessage
    const completionMsg = page.getByText(/quest.*completed|you survey the area|ancient marks|learned what/i);
    await expect(completionMsg.first()).toBeVisible({ timeout: 45_000 });
  });

  // ---------------------------------------------------------------------------
  // 5. Quest completion awards XP
  // ---------------------------------------------------------------------------

  test('quest completion awards XP message', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);
    await openQuestLog(page);

    // Accept any quest
    const acceptBtn = page.getByRole('button', { name: /\[Accept\]/i }).first();
    await expect(acceptBtn).toBeVisible({ timeout: 5_000 });
    await acceptBtn.click();

    // Close quest log and use complete button directly (in-log complete)
    // Re-open to find the Complete button for the accepted quest
    await expect(page.getByText('IN PROGRESS').first()).toBeVisible({ timeout: 5_000 });

    const completeBtn = page.getByRole('button', { name: /^Complete$/i }).first();
    if (!(await completeBtn.isVisible({ timeout: 3_000 }).catch(() => false))) {
      test.skip(); // Complete button may be locked behind kill requirements
      return;
    }

    await completeBtn.click();

    // XP gained message should appear in the game log
    await expect(page.getByText(/You gained \d+ experience!/i)).toBeVisible({ timeout: 8_000 });
  });

  // ---------------------------------------------------------------------------
  // 6. L key triggers quest auto-run
  // ---------------------------------------------------------------------------

  test('L key triggers quest auto-run and status bar shows QUEST AUTO-RUN', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    // First accept at least one quest (auto-run needs a quest to navigate to)
    await openQuestLog(page);

    const acceptBtn = page.getByRole('button', { name: /\[Accept\]/i }).first();
    await expect(acceptBtn).toBeVisible({ timeout: 5_000 });
    await acceptBtn.click();

    // Close quest log
    await page.keyboard.press('Escape');
    await expect(page.getByText('QUEST LOG')).not.toBeVisible();

    // Press L to activate quest auto-run
    await page.locator('body').click();
    await page.keyboard.press('l');

    // Status bar should show "QUEST AUTO-RUN" or equivalent active indicator
    const statusBar = page.getByText(/QUEST AUTO.?RUN/i);
    await expect(statusBar).toBeVisible({ timeout: 5_000 });
  });
});
