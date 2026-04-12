import { useState, useEffect } from 'react';
import type { RecipeInfo, InventoryItem, CraftingCompleteEvent } from '../types/game';

interface Props {
  recipes: RecipeInfo[];
  inventoryItems: InventoryItem[];
  craftingSkill: number;
  lastCraftResult: CraftingCompleteEvent | null;
  onCraft: (recipeId: string, componentIds: string[], taperId: string | null) => void;
  onRequestRecipes: () => void;
  onClose: () => void;
}

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

const sectionLabel: React.CSSProperties = {
  color: '#888',
  fontSize: '11px',
  letterSpacing: '0.12em',
  textTransform: 'uppercase',
  marginBottom: '4px',
};

function outcomeColor(outcome: string): string {
  if (outcome === 'Success' || outcome === 'Discovery') return '#00ff41';
  if (outcome === 'NearMiss') return '#ffaa00';
  if (outcome === 'ComponentLoss') return '#ff4400';
  if (outcome === 'UnexpectedResult') return '#ff9900';
  return '#cccccc';
}

export default function CraftingPanel({
  recipes,
  inventoryItems,
  craftingSkill,
  lastCraftResult,
  onCraft,
  onRequestRecipes,
  onClose,
}: Props) {
  const [selectedRecipeId, setSelectedRecipeId] = useState<string | null>(null);
  const [selectedComponentIds, setSelectedComponentIds] = useState<Record<string, string[]>>({});
  const [selectedTaperId, setSelectedTaperId] = useState<string | null>(null);
  const [statusMsg, setStatusMsg] = useState<string | null>(null);
  const [statusColor, setStatusColor] = useState('#cccccc');

  // Refresh recipes when panel opens
  useEffect(() => {
    onRequestRecipes();
  }, [onRequestRecipes]);

  // Show crafting result
  useEffect(() => {
    if (!lastCraftResult) return;
    setStatusMsg(lastCraftResult.message);
    setStatusColor(outcomeColor(lastCraftResult.outcome));

    // Clear selection after success/discovery
    if (lastCraftResult.outcome === 'Success' || lastCraftResult.outcome === 'Discovery') {
      setSelectedComponentIds({});
      setSelectedTaperId(null);
    }
  }, [lastCraftResult]);

  const selectedRecipe = recipes.find(r => r.recipeId === selectedRecipeId) ?? null;

  // Items in inventory grouped by name
  const byName = (name: string, category?: string) =>
    inventoryItems.filter(i =>
      i.name === name &&
      (!category || (i.category ?? '').toLowerCase() === category.toLowerCase()) &&
      !i.isLocked
    );

  const isTaper = (item: InventoryItem) =>
    item.name.toLowerCase().includes('taper') || item.category === 'Reagent';

  const tapers = inventoryItems.filter(isTaper);

  function handleSelectComponent(ingredientName: string, itemId: string, checked: boolean) {
    setSelectedComponentIds(prev => {
      const current = prev[ingredientName] ?? [];
      if (checked) return { ...prev, [ingredientName]: [...current, itemId] };
      return { ...prev, [ingredientName]: current.filter(id => id !== itemId) };
    });
  }

  function canCraft(): boolean {
    if (!selectedRecipe) return false;
    for (const ing of selectedRecipe.ingredients) {
      const chosen = (selectedComponentIds[ing.ingredientName] ?? []).length;
      if (chosen < ing.baseQuantity) return false;
    }
    return true;
  }

  function handleCraft() {
    if (!selectedRecipe) return;
    const allComponentIds = Object.values(selectedComponentIds).flat();
    onCraft(selectedRecipe.recipeId, allComponentIds, selectedTaperId);
    setStatusMsg('Crafting...');
    setStatusColor('#cccccc');
  }

  return (
    <div style={overlayStyle} onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div style={panelStyle}>
        <div style={headerStyle}>
          <span>CRAFTING — Skill {craftingSkill}</span>
          <button type="button" style={closeBtnStyle} onClick={onClose}>close [x]</button>
        </div>

        {statusMsg && (
          <div style={{ padding: '6px 14px', fontSize: '12px', color: statusColor, borderBottom: '1px solid #1a3a1a', flexShrink: 0 }}>
            {statusMsg}
          </div>
        )}

        <div style={bodyStyle}>
          {/* Recipe list */}
          <div style={listStyle}>
            {recipes.length === 0 && (
              <div style={{ padding: '12px', color: '#666' }}>No recipes available.</div>
            )}
            {recipes.map(r => (
              <div
                key={r.recipeId}
                onClick={() => { setSelectedRecipeId(r.recipeId); setSelectedComponentIds({}); setSelectedTaperId(null); setStatusMsg(null); }}
                style={{
                  padding: '8px 12px',
                  cursor: 'pointer',
                  borderBottom: '1px solid #111',
                  background: selectedRecipeId === r.recipeId ? '#001a00' : 'transparent',
                  color: craftingSkill >= r.requiredCraftingSkill ? '#00ff41' : '#555',
                }}
              >
                <div style={{ fontSize: '12px' }}>{r.name}</div>
                <div style={{ fontSize: '11px', color: '#666' }}>
                  → {r.resultItemName} · Skill {r.requiredCraftingSkill}
                </div>
              </div>
            ))}
          </div>

          {/* Recipe detail */}
          <div style={detailStyle}>
            {!selectedRecipe ? (
              <div style={{ color: '#555', marginTop: '20px', textAlign: 'center' }}>
                Select a recipe to view details.
              </div>
            ) : (
              <>
                <div style={{ marginBottom: '12px' }}>
                  <div style={{ fontSize: '14px', letterSpacing: '0.08em', marginBottom: '4px' }}>
                    {selectedRecipe.name}
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
                </div>

                <div style={{ marginBottom: '12px' }}>
                  <div style={sectionLabel}>Ingredients</div>
                  {selectedRecipe.ingredients.map(ing => {
                    const available = byName(ing.ingredientName, ing.category);
                    const chosen = selectedComponentIds[ing.ingredientName] ?? [];
                    return (
                      <div key={ing.ingredientName} style={{ marginBottom: '8px' }}>
                        <div style={{ fontSize: '12px', marginBottom: '3px', color: chosen.length >= ing.baseQuantity ? '#00ff41' : '#ffaa00' }}>
                          {ing.ingredientName} — need {ing.baseQuantity}, have {available.length}
                          {chosen.length > 0 && ` (selected ${chosen.length})`}
                        </div>
                        {available.length === 0 ? (
                          <div style={{ color: '#555', fontSize: '11px', paddingLeft: '8px' }}>Not in inventory.</div>
                        ) : (
                          <div style={{ paddingLeft: '8px' }}>
                            {available.slice(0, 8).map(item => (
                              <label key={item.id} style={{ display: 'block', cursor: 'pointer', fontSize: '11px', color: '#ccc', marginBottom: '2px' }}>
                                <input
                                  type="checkbox"
                                  checked={chosen.includes(item.id)}
                                  onChange={e => handleSelectComponent(ing.ingredientName, item.id, e.target.checked)}
                                  style={{ marginRight: '6px', accentColor: '#00ff41' }}
                                />
                                {item.name} W{item.workmanship}
                              </label>
                            ))}
                          </div>
                        )}
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

                <button
                  type="button"
                  style={{ ...craftBtnStyle, opacity: canCraft() ? 1 : 0.4, cursor: canCraft() ? 'pointer' : 'not-allowed' }}
                  onClick={canCraft() ? handleCraft : undefined}
                >
                  CRAFT
                </button>
              </>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
