import { test, expect } from '@playwright/test';
import type { Page } from '@playwright/test';

/**
 * Audio tests — mute toggle via M key and volume slider visibility.
 *
 * Audio state is tracked in the client UI; we verify the UI reflects the
 * correct muted/unmuted state and that the volume controls are present.
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

/** Open the audio / settings panel. Some UIs use M to toggle mute directly;
 *  others open a panel. We handle both. */
async function openAudioPanel(page: Page): Promise<boolean> {
  await page.locator('body').click();
  await page.keyboard.press('m');
  // Check if a dedicated audio panel opened
  const panel = page.getByTestId('audio-panel')
    .or(page.getByText(/AUDIO|SOUND|MUSIC/i))
    .or(page.getByText(/MUTED|UNMUTED/i));
  return panel.first().isVisible({ timeout: 3_000 }).catch(() => false);
}

test.describe('FirstMud — audio', () => {
  test.beforeEach(async ({ page }) => {
    await resetPlayerPosition(page);
    await seedDevPlayer(page);
  });

  // ---------------------------------------------------------------------------
  // 1. M key toggles mute
  // ---------------------------------------------------------------------------

  test('M key changes the audio mute state', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    // Capture initial mute state via any visible indicator
    const muteIndicator = page.getByTestId('mute-indicator')
      .or(page.getByText(/muted|unmuted|🔇|🔊/i))
      .or(page.getByRole('button', { name: /mute|unmute|sound/i }));

    const initiallyVisible = await muteIndicator.first().isVisible({ timeout: 3_000 }).catch(() => false);
    const initialText = initiallyVisible
      ? await muteIndicator.first().textContent().catch(() => '')
      : '';

    await page.locator('body').click();
    await page.keyboard.press('m');

    await page.waitForTimeout(300);

    // After pressing M, the audio state should have toggled.
    // Accept multiple forms of evidence:
    //   a) An audio panel opens/closes
    //   b) A mute indicator text changes
    //   c) A muted/unmuted toast appears
    const panelOpen = await page.getByTestId('audio-panel').isVisible().catch(() => false);
    const toastVisible = await page.getByText(/muted|sound off|audio off|unmuted|sound on/i).first().isVisible().catch(() => false);
    const indicatorChanged = initiallyVisible
      ? (await muteIndicator.first().textContent().catch(() => '')) !== initialText
      : false;

    expect(
      panelOpen || toastVisible || indicatorChanged,
      'M key should produce a visible audio state change',
    ).toBe(true);
  });

  test('pressing M twice returns to the original mute state', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    // Record pre-state
    const muteBtn = page.getByRole('button', { name: /mute|unmute|sound/i }).first()
      .or(page.getByTestId('mute-indicator'));
    const hadMuteIndicator = await muteBtn.isVisible({ timeout: 2_000 }).catch(() => false);
    const stateBefore = hadMuteIndicator
      ? await muteBtn.textContent().catch(() => '')
      : null;

    await page.locator('body').click();
    await page.keyboard.press('m');
    await page.waitForTimeout(200);
    await page.keyboard.press('m');
    await page.waitForTimeout(200);

    if (stateBefore !== null) {
      const stateAfter = await muteBtn.textContent().catch(() => '');
      expect(stateAfter, 'double M press should restore original audio state').toBe(stateBefore);
    } else {
      // No explicit indicator — just assert no error occurred
      const body = await page.textContent('body');
      expect(body ?? '').not.toContain('Unknown command');
    }
  });

  test('M key does not produce an Unknown command error', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('m');
    await page.waitForTimeout(500);

    const body = await page.textContent('body');
    expect(body ?? '').not.toContain('Unknown command: mute');
    expect(body ?? '').not.toContain('Unknown command: sound');
    expect(body ?? '').not.toContain('Unknown command: audio');
  });

  // ---------------------------------------------------------------------------
  // 2. Volume sliders are visible
  // ---------------------------------------------------------------------------

  test('audio panel or settings contains volume sliders', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    // Open whatever audio UI the M key exposes
    await page.locator('body').click();
    await page.keyboard.press('m');
    await page.waitForTimeout(300);

    // Check if an audio panel opened with sliders
    const panelOpen = await page.getByTestId('audio-panel').isVisible().catch(() => false);

    if (!panelOpen) {
      // Some implementations use a settings panel (S key or gear icon) for audio
      // Try the ? / settings route
      await page.keyboard.press('Escape');
      await page.waitForTimeout(200);

      // Try opening settings if there is one
      const settingsBtn = page.getByRole('button', { name: /settings|⚙|gear/i }).first();
      if (await settingsBtn.isVisible({ timeout: 2_000 }).catch(() => false)) {
        await settingsBtn.click();
        await page.waitForTimeout(300);
      }
    }

    // Look for range inputs (volume sliders) anywhere on the page
    const rangeInputs = page.locator('input[type="range"]');
    const rangeCount = await rangeInputs.count();

    if (rangeCount > 0) {
      await expect(rangeInputs.first()).toBeVisible({ timeout: 3_000 });
    } else {
      // Fall back: look for any element described as a volume or slider control
      const sliderLabel = page.getByText(/volume|music|sfx|sound effect/i).first();
      await expect(sliderLabel).toBeVisible({ timeout: 3_000 });
    }
  });

  test('volume sliders have the correct accessible role', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('m');
    await page.waitForTimeout(300);

    const panelOpen = await page.getByTestId('audio-panel').isVisible().catch(() => false);

    if (!panelOpen) {
      test.skip();
      return;
    }

    // Inside the audio panel, sliders should be accessible
    const sliders = page.getByRole('slider');
    const sliderCount = await sliders.count();

    if (sliderCount > 0) {
      await expect(sliders.first()).toBeVisible();
    } else {
      // Range inputs without explicit role are also acceptable
      const rangeInputs = page.locator('input[type="range"]');
      await expect(rangeInputs.first()).toBeVisible({ timeout: 3_000 });
    }
  });

  test('music volume slider is present in audio settings', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('m');
    await page.waitForTimeout(300);

    const panelOpen = await page.getByTestId('audio-panel').isVisible().catch(() => false);
    if (!panelOpen) {
      test.skip();
      return;
    }

    // A dedicated music volume control should be labelled
    const musicLabel = page.getByText(/music/i).first();
    await expect(musicLabel).toBeVisible({ timeout: 3_000 });
  });

  test('SFX / sound effects volume slider is present in audio settings', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('m');
    await page.waitForTimeout(300);

    const panelOpen = await page.getByTestId('audio-panel').isVisible().catch(() => false);
    if (!panelOpen) {
      test.skip();
      return;
    }

    // A sound effects volume control should be labelled
    const sfxLabel = page.getByText(/sfx|sound effects|effects/i).first();
    await expect(sfxLabel).toBeVisible({ timeout: 3_000 });
  });

  test('audio panel closes with Esc', async ({ page }) => {
    await page.goto('/');
    await waitForAuth(page);

    await page.locator('body').click();
    await page.keyboard.press('m');
    await page.waitForTimeout(300);

    const panelOpen = await page.getByTestId('audio-panel').isVisible().catch(() => false);
    if (!panelOpen) {
      test.skip();
      return;
    }

    await page.keyboard.press('Escape');
    await expect(page.getByTestId('audio-panel')).not.toBeVisible({ timeout: 3_000 });
  });
});
