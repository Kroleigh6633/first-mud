import { useState, useEffect, useRef, useCallback } from 'react';
import type { RecipeInfo, InventoryItem, StorageItem, CraftingCompleteEvent } from '../types/game';
import { SoundEffects } from '../audio/SoundEffects';

interface Props {
  recipes: RecipeInfo[];
  inventoryItems: InventoryItem[];
  storageItems: StorageItem[];
  craftingSkill: number;
  lastCraftResult: CraftingCompleteEvent | null;
  onCraft: (recipeId: string, componentIds: string[], taperId: string | null) => void;
  onSalvage: (itemId: string) => void;
  onRequestRecipes: () => void;
  onClose: () => void;
}

// ── Styles ────────────────────────────────────────────────────────────────────

const overlayStyle: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  background: 'rgba(0, 0, 0, 0.75)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  zIndex: 120,
};

const panelStyle: React.CSSProperties = {
  background: '#0d0d0d',
  border: '1px solid #00ff41',
  fontFamily: 'monospace',
  fontSize: '13px',
  color: '#00ff41',
  width: '640px',
  maxHeight: '85vh',
  display: 'flex',
  flexDirection: 'column',
  boxShadow: '0 0 40px rgba(0, 255, 65, 0.2)',
};

const headerStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
  padding: '10px 14px',
  borderBottom: '1px solid #1a3a1a',
  letterSpacing: '0.12em',
  flexShrink: 0,
};

const bodyStyle: React.CSSProperties = {
  display: 'grid',
  gridTemplateColumns: '220px 1fr',
  overflow: 'hidden',
  flex: 1,
};

const listStyle: React.CSSProperties = {
  borderRight: '1px solid #1a3a1a',
  overflowY: 'auto',
};

const detailStyle: React.CSSProperties = {
  overflowY: 'auto',
  padding: '12px 14px',
};

const btnBase: React.CSSProperties = {
  fontFamily: 'monospace',
  fontSize: '12px',
  cursor: 'pointer',
  padding: '3px 10px',
};

const closeBtnStyle: React.CSSProperties = {
  ...btnBase,
  background: 'none',
  border: '1px solid #00ff41',
  color: '#00ff41',
};

const craftBtnStyle: React.CSSProperties = {
  ...btnBase,
  background: '#003300',
  border: '1px solid #00aa33',
  color: '#00ff41',
  marginTop: '12px',
  padding: '5px 18px',
  letterSpacing: '0.08em',
};

const craftAllBtnStyle: React.CSSProperties = {
  ...craftBtnStyle,
  marginLeft: '8px',
  background: '#002200',
  border: '1px solid #006622',
  color: '#44cc66',
};

const cancelBtnStyle: React.CSSProperties = {
  ...btnBase,
  background: '#220000',
  border: '1px solid #aa2200',
  color: '#ff4444',
  marginTop: '12px',
  marginLeft: '8px',
  padding: '5px 18px',
};

const sectionLabel: React.CSSProperties = {
  color: '#888',
  fontSize: '11px',
  letterSpacing: '0.12em',
  textTransform: 'uppercase',
  marginBottom: '4px',
};

// ── Helpers ───────────────────────────────────────────────────────────────────

function outcomeColor(outcome: string): string {
  if (outcome === 'Success' || outcome === 'Discovery') return '#00ff41';
  if (outcome === 'NearMiss') return '#ffaa00';
  if (outcome === 'ComponentLoss') return '#ff4400';
  if (outcome === 'UnexpectedResult') return '#ff9900';
  return '#cccccc';
}

/**
 * Color and label for a recipe based on how it compares to the player's current skill.
 *
 *  skillDiff = recipe.requiredCraftingSkill − playerCraftingSkill
 *
 *  skillDiff ≤ −50  →  grey      (#666)     "no skill-up"
 *  skillDiff ≤ −20  →  green     (#44aa44)  "small skill-up"
 *  skillDiff ≤  −5  →  yellow    (#cccc44)  "good skill-up"
 *  skillDiff ≤   0  →  orange    (#cc8844)  "max skill-up"
 *  skillDiff ≤   5  →  red       (#cc4444)  "challenging"
 *  skillDiff >   5  →  dark red  (#661111)  "locked"
 */
function recipeSkillColor(requiredSkill: number, playerSkill: number): string {
  const diff = requiredSkill - playerSkill;
  if (diff <= -50) return '#666666';
  if (diff <= -20) return '#44aa44';
  if (diff <= -5)  return '#cccc44';
  if (diff <= 0)   return '#cc8844';
  if (diff <= 5)   return '#cc4444';
  return '#661111';
}

function recipeSkillLabel(requiredSkill: number, playerSkill: number): string {
  const diff = requiredSkill - playerSkill;
  if (diff <= -50) return '(no skill-up)';
  if (diff <= -20) return '(small skill-up)';
  if (diff <= -5)  return '(good skill-up)';
  if (diff <= 0)   return '(max skill-up)';
  if (diff <= 5)   return '(challenging)';
  return `(locked — need Skill ${requiredSkill})`;
}

/**
 * Returns the client-side crafting duration in milliseconds based on required skill level.
 *
 *  Skill 1     →  3 s
 *  Skill 2–3   →  5 s
 *  Skill 4–5   →  8 s
 *  Skill 5+    → 12 s
 */
function getCraftDurationMs(requiredSkill: number): number {
  if (requiredSkill <= 1) return 3_000;
  if (requiredSkill <= 3) return 5_000;
  if (requiredSkill <= 5) return 8_000;
  return 12_000;
}

// ── Component ─────────────────────────────────────────────────────────────────

export default function CraftingPanel({
  recipes,
  inventoryItems,
  storageItems: _storageItems,
  craftingSkill,
  lastCraftResult,
  onCraft,
  onSalvage,
  onRequestRecipes,
  onClose,
}: Props) {
  const [selectedRecipeId, setSelectedRecipeId] = useState<string | null>(null);
  const [selectedTaperId, setSelectedTaperId] = useState<string | null>(null);
  const [statusMsg, setStatusMsg] = useState<string | null>(null);
  const [statusColor, setStatusColor] = useState('#cccccc');
  const [categoryTab, setCategoryTab] = useState<string>('ALL');

  // Timed crafting progress bar state
  const [crafting, setCrafting] = useState(false);
  const [craftProgress, setCraftProgress] = useState(0); // 0–100

  // Craft All state
  const [craftAllActive, setCraftAllActive] = useState(false);
  const [craftAllCount, setCraftAllCount] = useState(0);
  const craftAllRef = useRef(false); // mutable flag to cancel the loop
  const craftAllCountRef = useRef(0);

  // Auto-salvage option for Craft All
  const [autoSalvageGrind, setAutoSalvageGrind] = useState(false);

  // Pending item id to salvage after craft (used by auto-salvage grind mode)
  const pendingSalvageRef = useRef<string | null>(null);

  // Refresh recipes when panel opens
  useEffect(() => {
    onRequestRecipes();
  }, [onRequestRecipes]);

  // Show crafting result + handle Craft All loop
  useEffect(() => {
    if (!lastCraftResult) return;

    setStatusMsg(lastCraftResult.message);
    setStatusColor(outcomeColor(lastCraftResult.outcome));

    // Clear selection after success/discovery (single craft mode)
    if (!craftAllRef.current) {
      if (lastCraftResult.outcome === 'Success' || lastCraftResult.outcome === 'Discovery') {
        setSelectedTaperId(null);
      }
    }

    // Auto-salvage grind: salvage the produced item immediately
    if (
      autoSalvageGrind &&
      (lastCraftResult.outcome === 'Success' || lastCraftResult.outcome === 'Discovery') &&
      lastCraftResult.itemId
    ) {
      pendingSalvageRef.current = lastCraftResult.itemId;
      onSalvage(lastCraftResult.itemId);
    }

    // If Craft All is running, continue or stop
    if (craftAllRef.current) {
      const didSucceed =
        lastCraftResult.outcome === 'Success' || lastCraftResult.outcome === 'Discovery';
      const materialsFailed = lastCraftResult.outcome === 'NearMiss';

      if (materialsFailed) {
        // Stop — likely ran out of materials
        craftAllRef.current = false;
        setCraftAllActive(false);
        setStatusMsg(`Craft All stopped: materials exhausted after ${craftAllCountRef.current} craft(s).`);
        setStatusColor('#ffaa00');
        return;
      }

      if (didSucceed) {
        craftAllCountRef.current += 1;
        setCraftAllCount(craftAllCountRef.current);
      }

      // Schedule next craft (brief pause for UX)
      setTimeout(() => {
        if (craftAllRef.current) {
          triggerTimedCraft(true);
        }
      }, 400);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [lastCraftResult]);

  const selectedRecipe = recipes.find(r => r.recipeId === selectedRecipeId) ?? null;

  /** Map tab label → resultCategory values to include */
  const tabCategoryMap: Record<string, string[]> = {
    ALL:         [],
    WEAPONS:     ['Weapon'],
    ARMOR:       ['Armor'],
    CONSUMABLES: ['Consumable'],
    MATERIALS:   ['Component', 'Reagent'],
    ACCESSORIES: ['Accessory'],
  };

  const filteredRecipes = categoryTab === 'ALL'
    ? recipes
    : recipes.filter(r => tabCategoryMap[categoryTab]?.includes(r.resultCategory));

  const isTaper = (item: InventoryItem) =>
    item.name.toLowerCase().includes('taper') || item.category === 'Reagent';

  const tapers = inventoryItems.filter(isTaper);

  function canCraft(): boolean {
    if (!selectedRecipe) return false;
    if (crafting || craftAllActive) return false;
    for (const ing of selectedRecipe.ingredients) {
      const total = (ing.invCount ?? 0) + (ing.storageCount ?? 0);
      if (total < ing.baseQuantity) return false;
    }
    return true;
  }

  /**
   * Animates the progress bar for the craft duration, then fires the actual craft command.
   * If `isCraftAll` is true the function runs inside a Craft All loop.
   */
  const triggerTimedCraft = useCallback((isCraftAll = false) => {
    if (!selectedRecipe) return;

    const duration = getCraftDurationMs(selectedRecipe.requiredCraftingSkill);
    const tickMs = 80; // update progress bar every 80 ms
    const totalTicks = duration / tickMs;
    let tick = 0;

    setCrafting(true);
    setCraftProgress(0);

    // Component IDs are resolved server-side; send an empty array.
    const interval = setInterval(() => {
      tick++;
      const pct = Math.min(100, Math.round((tick / totalTicks) * 100));
      setCraftProgress(pct);

      if (tick >= totalTicks) {
        clearInterval(interval);
        setCrafting(false);
        setCraftProgress(0);

        if (!isCraftAll || craftAllRef.current) {
          onCraft(selectedRecipe.recipeId, [], selectedTaperId);
        }
      }
    }, tickMs);
  }, [selectedRecipe, selectedTaperId, onCraft]);

  function handleCraft() {
    if (!selectedRecipe) {
      setStatusMsg('Select a recipe first.');
      setStatusColor('#ffaa00');
      return;
    }
    if (crafting || craftAllActive) return;

    // Compute per-ingredient shortfalls from available counts
    const shortfalls: string[] = [];
    for (const ing of selectedRecipe.ingredients) {
      const total = (ing.invCount ?? 0) + (ing.storageCount ?? 0);
      if (total < ing.baseQuantity) {
        shortfalls.push(`${ing.ingredientName}: need ${ing.baseQuantity}, have ${total}`);
      }
    }
    if (shortfalls.length > 0) {
      setStatusMsg(`Cannot craft — insufficient materials: ${shortfalls.join('; ')}`);
      setStatusColor('#ff4444');
      return;
    }

    console.log(
      `CRAFT clicked: recipeId=${selectedRecipe.recipeId}, ` +
      `taperId=${selectedTaperId ?? 'null'} (components resolved server-side)`,
    );

    setStatusMsg(null);
    triggerTimedCraft(false);
  }

  function handleCraftAll() {
    if (!canCraft()) return;
    craftAllRef.current = true;
    craftAllCountRef.current = 0;
    setCraftAllActive(true);
    setCraftAllCount(0);
    setStatusMsg(null);
    triggerTimedCraft(true);
  }

  function handleCancelCraftAll() {
    craftAllRef.current = false;
    setCraftAllActive(false);
    setCrafting(false);
    setCraftProgress(0);
    setStatusMsg(`Craft All cancelled after ${craftAllCountRef.current} craft(s).`);
    setStatusColor('#ffaa00');
  }

  // ── Render ──────────────────────────────────────────────────────────────────

  // Progress bar rendering helper
  function renderProgressBar(pct: number, label: string): React.ReactNode {
    const filled = Math.round(pct / 5); // 20 blocks total
    const empty  = 20 - filled;
    const bar    = '█'.repeat(filled) + '░'.repeat(empty);
    return (
      <div style={{ marginTop: '10px', fontSize: '12px', color: '#00cc33' }}>
        {label}<br />
        <span style={{ letterSpacing: '-1px' }}>{bar}</span>
        <span style={{ marginLeft: '6px', color: '#888' }}>{pct}%</span>
      </div>
    );
  }

  return (
    <div style={overlayStyle} onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div style={panelStyle}>
        {/* Header */}
        <div style={headerStyle}>
          <span>CRAFTING — Skill {craftingSkill}</span>
          <button type="button" style={closeBtnStyle} onClick={onClose}>close [x]</button>
        </div>

        {/* Status bar */}
        {statusMsg && (
          <div style={{ padding: '6px 14px', fontSize: '12px', color: statusColor, borderBottom: '1px solid #1a3a1a', flexShrink: 0 }}>
            {statusMsg}
          </div>
        )}

        <div style={bodyStyle}>
          {/* Recipe list */}
          <div style={listStyle}>
            {/* Category tabs */}
            <div style={{
              display: 'flex',
              flexWrap: 'wrap',
              borderBottom: '1px solid #1a3a1a',
              flexShrink: 0,
            }}>
              {(['ALL', 'WEAPONS', 'ARMOR', 'CONSUMABLES', 'MATERIALS', 'ACCESSORIES'] as const).map(tab => (
                <button
                  key={tab}
                  type="button"
                  onClick={() => setCategoryTab(tab)}
                  style={{
                    fontFamily: 'monospace',
                    fontSize: '10px',
                    cursor: 'pointer',
                    padding: '4px 6px',
                    background: categoryTab === tab ? '#002200' : 'transparent',
                    border: 'none',
                    borderRight: '1px solid #1a3a1a',
                    color: categoryTab === tab ? '#00ff41' : '#556655',
                    letterSpacing: '0.06em',
                    flexShrink: 0,
                  }}
                >
                  {tab}
                </button>
              ))}
            </div>
            {filteredRecipes.length === 0 && (
              <div style={{ padding: '12px', color: '#666' }}>
                {recipes.length === 0 ? 'No recipes available.' : 'No recipes in this category.'}
              </div>
            )}
            {filteredRecipes.map(r => {
              const skillColor = recipeSkillColor(r.requiredCraftingSkill, craftingSkill);
              const skillLabel = recipeSkillLabel(r.requiredCraftingSkill, craftingSkill);
              return (
                <div
                  key={r.recipeId}
                  onClick={() => {
                    setSelectedRecipeId(r.recipeId);
                    setSelectedTaperId(null);
                    setStatusMsg(null);
                  }}
                  style={{
                    padding: '8px 12px',
                    cursor: 'pointer',
                    borderBottom: '1px solid #111',
                    background: selectedRecipeId === r.recipeId ? '#001a00' : 'transparent',
                  }}
                  title={skillLabel}
                >
                  <div style={{ fontSize: '12px', color: skillColor }}>{r.name}</div>
                  <div style={{ fontSize: '11px', color: '#555' }}>
                    → {r.resultItemName} · Skill {r.requiredCraftingSkill}
                  </div>
                  <div style={{ fontSize: '10px', color: skillColor, opacity: 0.8 }}>
                    {skillLabel}
                  </div>
                </div>
              );
            })}
          </div>

          {/* Recipe detail */}
          <div style={detailStyle}>
            {!selectedRecipe ? (
              <div style={{ color: '#555', marginTop: '20px', textAlign: 'center' }}>
                Select a recipe to view details.
              </div>
            ) : (
              <>
                {/* Recipe header with skill color */}
                <div style={{ marginBottom: '12px' }}>
                  <div style={{
                    fontSize: '14px',
                    letterSpacing: '0.08em',
                    marginBottom: '2px',
                    color: recipeSkillColor(selectedRecipe.requiredCraftingSkill, craftingSkill),
                  }}>
                    {selectedRecipe.name}
                    <span style={{ fontSize: '11px', marginLeft: '8px', opacity: 0.75 }}>
                      {recipeSkillLabel(selectedRecipe.requiredCraftingSkill, craftingSkill)}
                    </span>
                  </div>
                  <div style={{ color: '#888', fontSize: '11px' }}>
                    Produces: {selectedRecipe.resultItemName} ({selectedRecipe.resultCategory})
                  </div>
                  <div style={{ color: '#888', fontSize: '11px' }}>
                    Workmanship: W{selectedRecipe.baseWorkmanshipMin}–W{selectedRecipe.baseWorkmanshipMax}
                    {selectedRecipe.isDiscoverable && ' · discoverable variant possible'}
                  </div>
                  {selectedRecipe.requiredTaperType && (
                    <div style={{ color: '#ffaa00', fontSize: '11px' }}>
                      Requires taper: {selectedRecipe.requiredTaperType}
                    </div>
                  )}
                  <div style={{ color: '#666', fontSize: '11px', marginTop: '3px' }}>
                    Craft time: {getCraftDurationMs(selectedRecipe.requiredCraftingSkill) / 1000}s
                  </div>
                </div>

                {/* Ingredients */}
                <div style={{ marginBottom: '12px' }}>
                  <div style={sectionLabel}>Ingredients</div>
                  {selectedRecipe.ingredients.map(ing => {
                    const inv = ing.invCount ?? 0;
                    const storage = ing.storageCount ?? 0;
                    const total = inv + storage;
                    const hasEnough = total >= ing.baseQuantity;
                    const statusIcon = hasEnough ? '✓' : '✗';
                    const statusColor = hasEnough ? '#00ff41' : '#ff4444';
                    let haveLabel = `have ${total}`;
                    if (inv > 0 && storage > 0) haveLabel += ` (${inv} inv + ${storage} storage)`;
                    else if (inv > 0) haveLabel += ' (inventory)';
                    else if (storage > 0) haveLabel += ' (storage)';
                    return (
                      <div key={ing.ingredientName} style={{ marginBottom: '6px', display: 'flex', alignItems: 'baseline', gap: '6px' }}>
                        <span style={{ fontSize: '14px', color: statusColor, flexShrink: 0 }}>{statusIcon}</span>
                        <span style={{ fontSize: '12px', color: hasEnough ? '#cccccc' : '#ff8888' }}>
                          {ing.ingredientName}
                          <span style={{ color: '#888', marginLeft: '6px' }}>
                            need {ing.baseQuantity}, {haveLabel}
                          </span>
                        </span>
                      </div>
                    );
                  })}
                </div>

                {/* Taper slot */}
                <div style={{ marginBottom: '12px' }}>
                  <div style={sectionLabel}>Taper (optional)</div>
                  {tapers.length === 0 ? (
                    <div style={{ color: '#555', fontSize: '11px' }}>No tapers in inventory.</div>
                  ) : (
                    <select
                      value={selectedTaperId ?? ''}
                      onChange={e => setSelectedTaperId(e.target.value || null)}
                      style={{
                        background: '#111',
                        border: '1px solid #1a3a1a',
                        color: '#00ff41',
                        fontFamily: 'monospace',
                        fontSize: '12px',
                        padding: '3px 6px',
                        width: '100%',
                      }}
                    >
                      <option value="">— none —</option>
                      {tapers.map(t => (
                        <option key={t.id} value={t.id}>
                          {t.name} W{t.workmanship}
                        </option>
                      ))}
                    </select>
                  )}
                </div>

                {/* Auto-salvage grind checkbox */}
                <div style={{ marginBottom: '8px' }}>
                  <label style={{ fontSize: '11px', color: autoSalvageGrind ? '#ffaa44' : '#666', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px' }}>
                    <input
                      type="checkbox"
                      checked={autoSalvageGrind}
                      onChange={e => setAutoSalvageGrind(e.target.checked)}
                      style={{ accentColor: '#ffaa44' }}
                    />
                    Auto-salvage crafted items (skill grind mode)
                    {autoSalvageGrind && <span style={{ color: '#888' }}> — craft + salvage loop</span>}
                  </label>
                </div>

                {/* Progress bar during crafting */}
                {crafting && renderProgressBar(
                  craftProgress,
                  craftAllActive
                    ? `Crafting ${selectedRecipe.resultItemName}... ${craftAllCountRef.current + 1}`
                    : `Crafting ${selectedRecipe.name}...`,
                )}

                {/* Craft All running progress */}
                {craftAllActive && !crafting && (
                  <div style={{ marginTop: '8px', fontSize: '12px', color: '#44cc66' }}>
                    Crafted {craftAllCount}× {selectedRecipe.resultItemName} — waiting for result...
                  </div>
                )}

                {/* Action buttons */}
                <div style={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap' }}>
                  {/* CRAFT button — always clickable so handleCraft can show feedback */}
                  <button
                    type="button"
                    style={{
                      ...craftBtnStyle,
                      opacity: (crafting || craftAllActive) ? 0.4 : 1,
                      cursor: (crafting || craftAllActive) ? 'not-allowed' : 'pointer',
                    }}
                    onClick={handleCraft}
                    disabled={crafting || craftAllActive}
                  >
                    CRAFT
                  </button>

                  {/* CRAFT ALL button */}
                  {!craftAllActive && (
                    <button
                      type="button"
                      style={{
                        ...craftAllBtnStyle,
                        opacity: canCraft() ? 1 : 0.4,
                        cursor: canCraft() ? 'pointer' : 'not-allowed',
                      }}
                      onClick={handleCraftAll}
                      disabled={!canCraft()}
                      title="Craft repeatedly until materials run out"
                    >
                      CRAFT ALL
                    </button>
                  )}

                  {/* CANCEL button while Craft All is running */}
                  {craftAllActive && (
                    <button
                      type="button"
                      style={cancelBtnStyle}
                      onClick={handleCancelCraftAll}
                    >
                      CANCEL
                    </button>
                  )}
                </div>
              </>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

// Re-export so callers can use the sound helper
export { SoundEffects };
